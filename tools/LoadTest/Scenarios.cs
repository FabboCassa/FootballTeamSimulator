using System.Diagnostics;
using System.Text.Json;

namespace Fts.LoadTest;

/// <summary>
/// The three load shapes the roadmap asks for (Phase 9.6):
///
/// <list type="bullet">
/// <item><b>baseline</b> - every coach walking the real daily loop (digest, season, offers, and every third
/// pass the one-tap confirm). This is "1,000 concurrent users" as they actually behave: with think time,
/// not as a stampede.</item>
/// <item><b>auction</b> - the market spike. Coaches sharing a group all see the same free-agent lots, so
/// contention is real and the accepted bids are audited afterwards for losses.</item>
/// <item><b>matchday</b> - the resolution spike: the whole ladder's matchday is resolved by one calendar
/// call while everyone is refreshing, so we see both the job's duration and what the refreshes cost.</item>
/// </list>
/// </summary>
public static class Scenarios
{
    // --- baseline ---------------------------------------------------------------------------------

    public static async Task<PhaseReport> RunBaselineAsync(
        LoadClient client, IReadOnlyList<CoachSession> sessions, LoadOptions options)
    {
        var metrics = new Metrics();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds));

        var clock = Stopwatch.StartNew();
        var workers = new Task[sessions.Count];
        for (int i = 0; i < sessions.Count; i++)
        {
            CoachSession session = sessions[i];
            int seed = i;
            workers[i] = Task.Run(() => DailyLoopAsync(client, session, seed, metrics, options, cts.Token));
        }

        await Task.WhenAll(workers);
        clock.Stop();
        return metrics.Build("baseline (daily loop)", clock.Elapsed);
    }

    private static async Task DailyLoopAsync(
        LoadClient client, CoachSession session, int seed, Metrics metrics, LoadOptions options, CancellationToken ct)
    {
        var rnd = new Random(seed * 7919 + 13);

        // Ramp in over one think-time so a thousand coaches do not all knock at the same millisecond.
        if (!await DelayAsync(rnd.Next(0, Math.Max(1, options.ThinkMs)), ct)) return;

        for (int pass = 0; !ct.IsCancellationRequested; pass++)
        {
            if ((await client.GetAuthedAsync("/ranked/today", session, "GET /ranked/today", metrics, ct)).IsAborted) return;
            if ((await client.GetAuthedAsync("/ranked/season", session, "GET /ranked/season", metrics, ct)).IsAborted) return;
            if ((await client.GetAuthedAsync("/ranked/offers", session, "GET /ranked/offers", metrics, ct)).IsAborted) return;

            // The write side of the daily loop: seeds/repairs the stored inputs and stamps the matchday.
            if (pass % 3 == 0)
            {
                var confirm = await client.PostAuthedAsync(
                    "/ranked/today/confirm", session, null, "POST /ranked/today/confirm", metrics, ct);
                if (confirm.IsAborted) return;
            }

            if (!await DelayAsync(options.ThinkMs, ct)) return;
        }
    }

    // --- auction spike ----------------------------------------------------------------------------

    public static async Task<(PhaseReport Report, BidAudit Audit, int CoachesWithNoLots)> RunAuctionAsync(
        LoadClient client, IReadOnlyList<CoachSession> sessions, LoadOptions options)
    {
        var metrics = new Metrics();
        var ledger = new BidLedger();
        int noLots = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.AuctionSeconds));

        var clock = Stopwatch.StartNew();
        var workers = new Task[sessions.Count];
        for (int i = 0; i < sessions.Count; i++)
        {
            CoachSession session = sessions[i];
            int seed = i;
            workers[i] = Task.Run(async () =>
            {
                bool sawLot = await BidLoopAsync(client, session, seed, metrics, ledger, options, cts.Token);
                if (!sawLot) Interlocked.Increment(ref noLots);
            });
        }

        await Task.WhenAll(workers);
        clock.Stop();

        // Verification pass, deliberately outside the measured window: every coach re-reads their lots so
        // the ledger can compare what the server accepted with what it now reports.
        using var verifyCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        foreach (var chunk in Chunk(sessions, 64))
        {
            var reads = new List<Task>(chunk.Count);
            foreach (CoachSession s in chunk)
                reads.Add(VerifyLotsAsync(client, s, ledger, verifyCts.Token));
            await Task.WhenAll(reads);
        }

        return (metrics.Build("auction spike", clock.Elapsed), ledger.Audit(), noLots);
    }

    /// <summary>Returns true if this coach ever saw an open lot (so "nobody had anything to bid on" is
    /// reported as a setup problem instead of silently passing).</summary>
    private static async Task<bool> BidLoopAsync(
        LoadClient client, CoachSession session, int seed, Metrics metrics, BidLedger ledger,
        LoadOptions options, CancellationToken ct)
    {
        var rnd = new Random(seed * 104729 + 7);
        bool sawLot = false;

        if (!await DelayAsync(rnd.Next(0, Math.Max(1, options.BidThinkMs)), ct)) return sawLot;

        while (!ct.IsCancellationRequested)
        {
            var view = await client.GetAuthedAsync("/ranked/auctions", session, "GET /ranked/auctions", metrics, ct);
            if (view.IsAborted) return sawLot;

            if (!view.IsOk)
            {
                if (!await DelayAsync(Math.Max(250, options.BidThinkMs), ct)) return sawLot;
                continue;
            }

            var lots = ParseBiddableLots(view.Body);
            if (lots.Count == 0)
            {
                if (!await DelayAsync(1000, ct)) return sawLot;
                continue;
            }

            sawLot = true;

            // Spread the coaches across the lots they can see; several coaches share a group, so they are
            // competing for the same rows either way - that is the point of the spike.
            var lot = lots[rnd.Next(lots.Count)];
            string body = "{\"amount\":" + lot.MinNextBid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

            var bid = await client.PostAuthedAsync(
                $"/ranked/auctions/{lot.Id}/bid", session, body, "POST /ranked/auctions/{id}/bid", metrics, ct);
            if (bid.IsAborted) return sawLot;

            if (bid.IsOk && TryReadHighBid(bid.Body, out long reportedHigh))
                ledger.RecordAccepted(lot.Id, lot.MinNextBid, reportedHigh);

            if (!await DelayAsync(options.BidThinkMs, ct)) return sawLot;
        }

        return sawLot;
    }

    private static async Task VerifyLotsAsync(
        LoadClient client, CoachSession session, BidLedger ledger, CancellationToken ct)
    {
        var view = await client.GetAuthedAsync("/ranked/auctions", session, "verify", null, ct);
        if (!view.IsOk) return;

        try
        {
            using var doc = JsonDocument.Parse(view.Body);
            if (!doc.RootElement.TryGetProperty("lots", out var lots)) return;

            foreach (var lot in lots.EnumerateArray())
            {
                string? id = lot.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (id is null) continue;
                long high = lot.TryGetProperty("highBid", out var highProp) ? highProp.GetInt64() : 0;
                ledger.RecordFinal(id, high);
            }
        }
        catch (JsonException)
        {
            // A malformed body is already visible as a failed request; nothing to audit here.
        }
    }

    private readonly record struct Lot(string Id, long MinNextBid);

    private static List<Lot> ParseBiddableLots(string body)
    {
        var result = new List<Lot>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("windowOpen", out var open)
                && open.ValueKind == JsonValueKind.False) return result;
            if (!root.TryGetProperty("lots", out var lots)) return result;

            foreach (var lot in lots.EnumerateArray())
            {
                // Status is a number on the wire (the API configures no string-enum converter): 0 = Open.
                if (lot.TryGetProperty("status", out var status)
                    && status.ValueKind == JsonValueKind.Number && status.GetInt32() != 0) continue;
                if (lot.TryGetProperty("youAreLeading", out var leading)
                    && leading.ValueKind == JsonValueKind.True) continue;

                string? id = lot.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (id is null) continue;
                long min = lot.TryGetProperty("minNextBid", out var minProp) ? minProp.GetInt64() : 0;
                if (min <= 0) continue;

                result.Add(new Lot(id, min));
            }
        }
        catch (JsonException)
        {
            // Ignore - the request itself is already accounted for in the metrics.
        }

        return result;
    }

    private static bool TryReadHighBid(string body, out long highBid)
    {
        highBid = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("lot", out var lot)) return false;
            if (!lot.TryGetProperty("highBid", out var high)) return false;
            highBid = high.GetInt64();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // --- matchday spike ---------------------------------------------------------------------------

    public static async Task<(PhaseReport Report, TickResult Tick)> RunMatchdayAsync(
        LoadClient client, IReadOnlyList<CoachSession> sessions, LoadOptions options)
    {
        var metrics = new Metrics();
        using var cts = new CancellationTokenSource();

        var clock = Stopwatch.StartNew();
        var pollers = new Task[sessions.Count];
        for (int i = 0; i < sessions.Count; i++)
        {
            CoachSession session = sessions[i];
            int seed = i;
            pollers[i] = Task.Run(() => RefreshLoopAsync(client, session, seed, metrics, options, cts.Token));
        }

        // The spike itself: one calendar call resolves every due matchday across the whole ladder. This is
        // what the minutely Hangfire job does in production, so its duration is the job's real deadline.
        var tick = await AdvanceCalendarAsync(client, options);

        // Let the refreshes ride a little past the resolution, which is when clients pile in for results.
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, Math.Max(2, options.Seconds / 6))));
        cts.Cancel();
        await Task.WhenAll(pollers);
        clock.Stop();

        return (metrics.Build("matchday spike", clock.Elapsed), tick);
    }

    /// <summary>
    /// The refresh a coach does around kickoff. It uses the SAME think time as the daily loop: a thousand
    /// clients checking for results is a spike, but it is not an uninterrupted siege — and the first
    /// thousand-coach run made it exactly that, hammering at a fixed 500ms for the sixteen minutes the tick
    /// lasted, which measured a saturated queue rather than the spike.
    /// </summary>
    private static async Task RefreshLoopAsync(
        LoadClient client, CoachSession session, int seed, Metrics metrics, LoadOptions options,
        CancellationToken ct)
    {
        var rnd = new Random(seed * 15485863 + 3);
        if (!await DelayAsync(rnd.Next(0, Math.Max(1, options.ThinkMs)), ct)) return;

        while (!ct.IsCancellationRequested)
        {
            if ((await client.GetAuthedAsync("/ranked/today", session, "GET /ranked/today", metrics, ct)).IsAborted) return;
            if ((await client.GetAuthedAsync("/ranked/season", session, "GET /ranked/season", metrics, ct)).IsAborted) return;

            // A touch keener than the daily loop (results are landing), but the same order of magnitude.
            int think = Math.Max(200, options.ThinkMs / 2);
            if (!await DelayAsync(think + rnd.Next(0, Math.Max(1, think / 2)), ct)) return;
        }
    }

    /// <summary>
    /// The matchday job with NOTHING else running (Phase 9.6). The spike scenario measures the job while a
    /// thousand coaches refresh, which on a single development machine means the tick is competing with the
    /// API for the same cores — so a slow result there cannot tell "the job is slow" apart from "the box is
    /// full". This runs the same call on an idle server, and the two numbers together answer the question.
    /// </summary>
    public static async Task<TickResult> RunTickOnlyAsync(LoadClient client, LoadOptions options) =>
        await AdvanceCalendarAsync(client, options);

    private static async Task<TickResult> AdvanceCalendarAsync(LoadClient client, LoadOptions options)
    {
        var clock = Stopwatch.StartNew();
        // Fast-forward rather than a plain tick: with the real one-matchday-a-day spacing nothing would be
        // due yet, and we want the resolution work itself measured, not skipped.
        var res = await client.PostAsync(
            "/internal/ranked/fast-forward?matchdays=1", null, null, "fast-forward", null,
            CancellationToken.None, timeoutSeconds: 1800);
        clock.Stop();

        int matchdays = 0, fixtures = 0, seasons = 0;
        try
        {
            using var doc = JsonDocument.Parse(res.Body);
            var root = doc.RootElement;
            if (root.TryGetProperty("matchdaysResolved", out var m)) matchdays = m.GetInt32();
            if (root.TryGetProperty("fixturesResolved", out var f)) fixtures = f.GetInt32();
            if (root.TryGetProperty("seasonsStarted", out var s)) seasons = s.GetInt32();
        }
        catch (JsonException)
        {
            // Reported below as a zero-work tick, which the checks treat as a failure.
        }

        return new TickResult(res.Status, clock.Elapsed, matchdays, fixtures, seasons, options.MatchdayBudgetSeconds);
    }

    // --- helpers ----------------------------------------------------------------------------------

    private static async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        if (ms <= 0) return true;
        try
        {
            await Task.Delay(ms, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static IEnumerable<List<CoachSession>> Chunk(IReadOnlyList<CoachSession> items, int size)
    {
        var batch = new List<CoachSession>(size);
        foreach (CoachSession item in items)
        {
            batch.Add(item);
            if (batch.Count < size) continue;
            yield return batch;
            batch = new List<CoachSession>(size);
        }
        if (batch.Count > 0) yield return batch;
    }
}

public sealed record TickResult(
    int Status, TimeSpan Duration, int MatchdaysResolved, int FixturesResolved, int SeasonsStarted,
    int BudgetSeconds)
{
    public bool WithinBudget => Duration.TotalSeconds <= BudgetSeconds;

    /// <summary>Cost per fixture — the figure that actually compares across runs of different sizes, and the
    /// one that exposed the scaling problem in the first place (44ms with 25 groups, 1.16s with 125).</summary>
    public double MsPerFixture => FixturesResolved <= 0 ? 0 : Duration.TotalMilliseconds / FixturesResolved;

    public void Print(string title = "matchday job")
    {
        Console.WriteLine();
        Console.WriteLine($"-- {title}: HTTP {Status} in {Duration.TotalSeconds:F1}s "
                          + $"(budget {BudgetSeconds}s)");
        Console.WriteLine($"   resolved {MatchdaysResolved} matchdays / {FixturesResolved} fixtures, "
                          + $"started {SeasonsStarted} seasons  ->  {MsPerFixture:F0}ms per fixture");
    }
}
