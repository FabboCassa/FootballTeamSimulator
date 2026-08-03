using System.Collections.Concurrent;

namespace Fts.LoadTest;

/// <summary>How a single request ended. A 429 is the rate limiter doing its job (Phase 9.5) and a 4xx is the
/// API refusing something on purpose — both are answers, and both belong in the latency picture rather than
/// in an error rate. <see cref="ServerError"/>, <see cref="Network"/> and <see cref="Unauthorized"/> are the
/// failures: the last one has its own bucket because an expired session used to hide among the ordinary 4xx
/// while quietly making the load fake (an unauthenticated request never reaches the database).</summary>
public enum Outcome
{
    Ok,
    RateLimited,
    ClientError,
    ServerError,
    Network,
    Unauthorized,
}

public readonly record struct Sample(string Endpoint, double Ms, int Status, Outcome Outcome);

/// <summary>Latency + outcome collection for one phase. Workers record from many threads; percentiles are
/// computed once at the end, so nothing is aggregated (and no lock is taken) on the hot path.</summary>
public sealed class Metrics
{
    private readonly ConcurrentQueue<Sample> _samples = new();

    public void Record(string endpoint, double ms, int status, Outcome outcome) =>
        _samples.Enqueue(new Sample(endpoint, ms, status, outcome));

    public PhaseReport Build(string name, TimeSpan duration)
    {
        var all = _samples.ToArray();
        var overall = Summarise("(all endpoints)", all);

        var byEndpoint = new List<EndpointReport>();
        foreach (var grouping in all.GroupBy(s => s.Endpoint).OrderBy(g => g.Key, StringComparer.Ordinal))
            byEndpoint.Add(Summarise(grouping.Key, grouping.ToArray()));

        double seconds = Math.Max(0.001, duration.TotalSeconds);
        return new PhaseReport(name, duration, all.Length / seconds, overall, byEndpoint);
    }

    private static EndpointReport Summarise(string endpoint, Sample[] samples)
    {
        var times = new double[samples.Length];
        int ok = 0, limited = 0, client = 0, server = 0, network = 0, unauthorized = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            times[i] = samples[i].Ms;
            switch (samples[i].Outcome)
            {
                case Outcome.Ok: ok++; break;
                case Outcome.RateLimited: limited++; break;
                case Outcome.ClientError: client++; break;
                case Outcome.ServerError: server++; break;
                case Outcome.Unauthorized: unauthorized++; break;
                default: network++; break;
            }
        }

        Array.Sort(times);
        return new EndpointReport(
            endpoint, samples.Length, ok, limited, client, server, network, unauthorized,
            Percentile(times, 50), Percentile(times, 90), Percentile(times, 95), Percentile(times, 99),
            times.Length == 0 ? 0 : times[^1]);
    }

    /// <summary>Linear-interpolated percentile over the sorted latencies.</summary>
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];

        double rank = p / 100.0 * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}

public sealed record EndpointReport(
    string Endpoint,
    int Count,
    int Ok,
    int RateLimited,
    int ClientErrors,
    int ServerErrors,
    int NetworkErrors,
    int Unauthorized,
    double P50,
    double P90,
    double P95,
    double P99,
    double Max)
{
    public int Failures => ServerErrors + NetworkErrors + Unauthorized;
}

public sealed record PhaseReport(
    string Name,
    TimeSpan Duration,
    double RequestsPerSecond,
    EndpointReport Overall,
    IReadOnlyList<EndpointReport> Endpoints)
{
    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine($"-- {Name}: {Overall.Count} requests in {Duration.TotalSeconds:F1}s "
                          + $"({RequestsPerSecond:F0} req/s)");
        Console.WriteLine($"   ok {Overall.Ok}  rate-limited {Overall.RateLimited}  "
                          + $"refused-4xx {Overall.ClientErrors}  unauthorized-401 {Overall.Unauthorized}  "
                          + $"server-5xx {Overall.ServerErrors}  network {Overall.NetworkErrors}");
        Console.WriteLine($"   latency ms  p50 {Overall.P50:F0}  p90 {Overall.P90:F0}  "
                          + $"p95 {Overall.P95:F0}  p99 {Overall.P99:F0}  max {Overall.Max:F0}");

        foreach (var e in Endpoints)
            Console.WriteLine($"     {e.Endpoint,-34} n={e.Count,-7} p50={e.P50,6:F0}  p95={e.P95,6:F0}  "
                              + $"max={e.Max,6:F0}  429={e.RateLimited,-5} 4xx={e.ClientErrors,-5} "
                              + $"fail={e.Failures}");
    }
}
