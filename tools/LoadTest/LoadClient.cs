using System.Diagnostics;
using System.Text;

namespace Fts.LoadTest;

/// <summary>One HTTP call's outcome. <see cref="Status"/> is -1 when the run was stopped mid-flight (the
/// phase's clock ran out) — those are dropped rather than recorded, so shutting the load down never shows up
/// as a wall of errors.</summary>
public readonly record struct HttpOutcome(int Status, string Body, double Ms)
{
    public static HttpOutcome Aborted => new(-1, string.Empty, 0);

    public bool IsAborted => Status == -1;
    public bool IsOk => Status is >= 200 and < 300;
}

/// <summary>
/// The generator's HTTP surface: one pooled <see cref="HttpClient"/> shared by every virtual coach, with the
/// connection cap raised to match the concurrency (the default of a handful of connections per server would
/// otherwise queue the whole load inside the generator and we would be measuring ourselves).
/// </summary>
public sealed class LoadClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public LoadClient(string baseUrl, int concurrency)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Math.Max(64, concurrency * 2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        // Generous ceiling: seeding a big cohort (every full placement group materialises its own generated
        // world) and a matchday tick that resolves hundreds of fixtures are both legitimately slow. The
        // per-request timeouts below keep the load phases honest.
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }

    public Task<HttpOutcome> GetAsync(
        string path, string? token, string label, Metrics? metrics, CancellationToken ct,
        int timeoutSeconds = 30) =>
        SendAsync(HttpMethod.Get, path, token, null, label, metrics, timeoutSeconds, ct);

    public Task<HttpOutcome> PostAsync(
        string path, string? token, string? json, string label, Metrics? metrics, CancellationToken ct,
        int timeoutSeconds = 30) =>
        SendAsync(HttpMethod.Post, path, token, json, label, metrics, timeoutSeconds, ct);

    /// <summary>A coach's call: the session renews its token first if it is close to expiring, so a long run
    /// keeps measuring real authenticated work instead of drifting into 401s (Phase 9.6).</summary>
    public async Task<HttpOutcome> GetAuthedAsync(
        string path, CoachSession session, string label, Metrics? metrics, CancellationToken ct,
        int timeoutSeconds = 30)
    {
        await session.EnsureFreshAsync(this, ct);
        return await SendAsync(HttpMethod.Get, path, session.AccessToken, null, label, metrics, timeoutSeconds, ct);
    }

    public async Task<HttpOutcome> PostAuthedAsync(
        string path, CoachSession session, string? json, string label, Metrics? metrics, CancellationToken ct,
        int timeoutSeconds = 30)
    {
        await session.EnsureFreshAsync(this, ct);
        return await SendAsync(HttpMethod.Post, path, session.AccessToken, json, label, metrics, timeoutSeconds, ct);
    }

    private async Task<HttpOutcome> SendAsync(
        HttpMethod method, string path, string? token, string? json, string label, Metrics? metrics,
        int timeoutSeconds, CancellationToken runToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cts.Token);
            string body = await response.Content.ReadAsStringAsync(cts.Token);
            sw.Stop();

            int status = (int)response.StatusCode;
            metrics?.Record(label, sw.Elapsed.TotalMilliseconds, status, Classify(status));
            return new HttpOutcome(status, body, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            // The phase ended while this call was in flight - not a server problem, so it is not a sample.
            return HttpOutcome.Aborted;
        }
        catch (Exception)
        {
            sw.Stop();
            metrics?.Record(label, sw.Elapsed.TotalMilliseconds, 0, Outcome.Network);
            return new HttpOutcome(0, string.Empty, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static Outcome Classify(int status) => status switch
    {
        429 => Outcome.RateLimited,
        401 => Outcome.Unauthorized,
        >= 500 => Outcome.ServerError,
        >= 400 => Outcome.ClientError,
        _ => Outcome.Ok,
    };

    public void Dispose() => _http.Dispose();
}
