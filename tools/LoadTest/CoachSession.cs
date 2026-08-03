using System.Text.Json;

namespace Fts.LoadTest;

/// <summary>
/// One virtual coach's session (Phase 9.6). Access tokens live 15 minutes; the first thousand-coach run
/// lasted longer than that and the tail of it quietly became a flood of 401s — which the generator counted
/// as ordinary refused-4xx responses and, worse, timed as if they were real work (an unauthenticated request
/// never touches the database, so the reported p95 was flattering).
///
/// So a session renews itself the way a real client does: BEFORE the token is due to expire, using the
/// refresh token the seeding handed over. Any 401 that still slips through is counted in its own bucket and
/// fails the run, because after this it means something is actually wrong rather than simply late.
/// </summary>
public sealed class CoachSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _accessToken;
    private string _refreshToken;
    private DateTime _renewAtUtc;

    public CoachSession(string accessToken, string refreshToken, int expiresInSeconds)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _renewAtUtc = RenewalTime(expiresInSeconds);
    }

    public string AccessToken => Volatile.Read(ref _accessToken);

    /// <summary>Number of sessions that could not be renewed — a hard failure for the run.</summary>
    public bool Broken { get; private set; }

    /// <summary>Renews the token when it is close to expiring. Called before every request; the common path
    /// is a single clock comparison.</summary>
    public async Task EnsureFreshAsync(LoadClient client, CancellationToken ct)
    {
        if (DateTime.UtcNow < _renewAtUtc || Broken) return;

        await _gate.WaitAsync(ct);
        try
        {
            // Another of this coach's in-flight calls may have renewed while we queued.
            if (DateTime.UtcNow < _renewAtUtc || Broken) return;

            string body = JsonSerializer.Serialize(new { refreshToken = _refreshToken });
            var res = await client.PostAsync(
                "/auth/refresh", null, body, "POST /auth/refresh", null, ct, timeoutSeconds: 30);

            if (res.IsAborted) return;
            if (!res.IsOk)
            {
                Broken = true;
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(res.Body);
                var root = doc.RootElement;
                string? access = root.TryGetProperty("accessToken", out var a) ? a.GetString() : null;
                string? refresh = root.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                int expires = root.TryGetProperty("expiresInSeconds", out var e) ? e.GetInt32() : 900;

                if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
                {
                    Broken = true;
                    return;
                }

                Volatile.Write(ref _accessToken, access);
                _refreshToken = refresh;
                _renewAtUtc = RenewalTime(expires);
            }
            catch (JsonException)
            {
                Broken = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Renew a minute before the token actually dies, and never less than 30 seconds out, so a slow
    /// phase cannot slide past the expiry between two checks.</summary>
    private static DateTime RenewalTime(int expiresInSeconds)
    {
        int lead = Math.Max(30, Math.Min(60, expiresInSeconds / 4));
        return DateTime.UtcNow.AddSeconds(Math.Max(5, expiresInSeconds - lead));
    }
}
