using System.Text.Json;

namespace Fts.LoadTest;

/// <summary>
/// Ranked ladder load generator (Phase 9.6). Seeds a cohort of real accounts through the dev endpoint, then
/// drives three load shapes against a running server and checks the roadmap's acceptance criteria:
/// p95 API latency under budget during the spikes, no lost bids, and the matchday job finishing inside its
/// window. Exit code 0 = every check passed.
///
/// Run it against docker compose (the dev gates it needs are already on there). On a development machine
/// the generator competes with the server and the database for the same cores, so treat the absolute
/// numbers as a regression baseline and re-confirm them on a real host before launch.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!LoadOptions.TryParse(args, out var options, out string? error))
        {
            Console.WriteLine(error);
            return error == LoadOptions.Usage ? 0 : 2;
        }

        Console.WriteLine("FTS ranked load test (Phase 9.6)");
        Console.WriteLine("================================");
        options.Print();

        using var client = new LoadClient(options.BaseUrl, options.Users);
        var checks = new CheckList();

        // 1. Is anything listening?
        var health = await client.GetAsync("/health", null, "health", null, CancellationToken.None, 15);
        if (!health.IsOk)
        {
            Console.WriteLine();
            Console.WriteLine($"Server not reachable at {options.BaseUrl} (HTTP {health.Status}). "
                              + "Start it with: cd server && docker compose up -d");
            return 2;
        }

        // 2. Seed the cohort (accounts + enrolment + the ticks that start their seasons).
        Console.WriteLine();
        Console.WriteLine($"Seeding {options.Coaches} coaches (this materialises a world per full group, "
                          + "so it can take a few minutes)...");

        var seed = await SeedAsync(client, options);
        if (seed is null)
        {
            Console.WriteLine("Seeding failed - is Dev:ExposeSeedEndpoints on in the running server?");
            return 2;
        }

        Console.WriteLine($"   created {seed.Created}, enrolled {seed.Enrolled} across {seed.Groups} groups "
                          + $"in {seed.ElapsedMs / 1000.0:F1}s");
        Console.WriteLine($"   ticks: {seed.SeasonsStarted} seasons started, "
                          + $"{seed.FixturesResolved} fixtures resolved, "
                          + $"{seed.MarketWindowsOpened} market windows opened");

        checks.Add("every seeded coach joined the ladder", seed.Enrolled == seed.Created,
            $"{seed.Enrolled}/{seed.Created}");

        var sessions = seed.Sessions.Take(options.Users).ToList();
        if (sessions.Count == 0)
        {
            Console.WriteLine("Seeding returned no usable tokens.");
            return 2;
        }

        // 3. The load phases, in an order that leaves the state each one needs: the market window opened by
        // the seeding ticks is still open for the auction spike, and the calendar is only moved at the end.
        var reports = new List<PhaseReport>();

        if (options.RunBaseline)
        {
            Console.WriteLine();
            Console.WriteLine($"Baseline: {sessions.Count} coaches walking the daily loop for {options.Seconds}s...");
            var report = await Scenarios.RunBaselineAsync(client, sessions, options);
            report.Print();
            reports.Add(report);
            checks.Add("baseline p95 within budget", report.Overall.P95 <= options.P95BudgetMs,
                $"{report.Overall.P95:F0}ms vs {options.P95BudgetMs:F0}ms");
        }

        if (options.RunAuction)
        {
            Console.WriteLine();
            Console.WriteLine($"Auction spike: {sessions.Count} coaches bidding for {options.AuctionSeconds}s...");
            var (report, audit, noLots) = await Scenarios.RunAuctionAsync(client, sessions, options);
            report.Print();
            audit.Print();
            reports.Add(report);

            checks.Add("auction spike p95 within budget", report.Overall.P95 <= options.P95BudgetMs,
                $"{report.Overall.P95:F0}ms vs {options.P95BudgetMs:F0}ms");
            checks.Add("the spike had lots to bid on", noLots < sessions.Count,
                $"{sessions.Count - noLots}/{sessions.Count} coaches saw an open lot");
            checks.Add("no lost bids", audit.LostBids == 0,
                $"{audit.LostBids} lost of {audit.AcceptedBids} accepted");
            checks.Add("no write/read mismatch on accepted bids", audit.Mismatches == 0,
                $"{audit.Mismatches} mismatches");
        }

        // The matchday job is measured TWICE (Phase 9.6): once on an idle server and once while every coach
        // refreshes. One number without the other cannot tell "the job is slow" apart from "the machine is
        // full" — on a development box, where the generator, the API, PostgreSQL and Redis share the same
        // cores, the loaded figure is mostly the second thing. So the CHECK is judged on the idle run (that
        // is the job's own cost against its schedule) and the loaded one is reported beside it as the price
        // of contention.
        TickResult? idleTick = null;
        if (options.RunTickOnly || options.RunMatchday)
        {
            Console.WriteLine();
            Console.WriteLine("Matchday job, idle server: timing one whole-ladder resolution on its own...");
            idleTick = await Scenarios.RunTickOnlyAsync(client, options);
            idleTick.Print("matchday job (idle server)");

            checks.Add("matchday job finished inside its window (idle server)", idleTick.WithinBudget,
                $"{idleTick.Duration.TotalSeconds:F1}s vs {idleTick.BudgetSeconds}s, "
                + $"{idleTick.MsPerFixture:F0}ms per fixture");
            checks.Add("the matchday job actually resolved fixtures", idleTick.FixturesResolved > 0,
                $"{idleTick.FixturesResolved} fixtures");
        }

        if (options.RunMatchday)
        {
            Console.WriteLine();
            Console.WriteLine("Matchday spike: resolving the ladder's matchday while everyone refreshes...");
            var (report, tick) = await Scenarios.RunMatchdayAsync(client, sessions, options);
            report.Print();
            tick.Print("matchday job (under load)");
            reports.Add(report);

            if (idleTick is { FixturesResolved: > 0 } idle && tick.FixturesResolved > 0)
            {
                double factor = idle.MsPerFixture <= 0 ? 0 : tick.MsPerFixture / idle.MsPerFixture;
                Console.WriteLine($"   contention: {tick.MsPerFixture:F0}ms vs {idle.MsPerFixture:F0}ms per "
                                  + $"fixture = {factor:F1}x slower with {sessions.Count} coaches refreshing");
            }

            checks.Add("matchday spike p95 within budget", report.Overall.P95 <= options.P95BudgetMs,
                $"{report.Overall.P95:F0}ms vs {options.P95BudgetMs:F0}ms");
        }

        // 4. Failures are counted across every phase: a single 5xx or dropped connection under load is a
        // finding, not noise. 429s are excluded on purpose - that is the rate limiter working (9.5).
        int serverErrors = reports.Sum(r => r.Overall.ServerErrors);
        int networkErrors = reports.Sum(r => r.Overall.NetworkErrors);
        checks.Add("no 5xx responses", serverErrors == 0, $"{serverErrors}");
        checks.Add("no dropped connections", networkErrors == 0, $"{networkErrors}");

        // Sessions renew themselves before their token expires, so a 401 now means the load stopped being
        // authenticated — and an unauthenticated request never reaches the database, which would quietly
        // flatter every latency number after it. The first thousand-coach run failed exactly this way.
        int unauthorized = reports.Sum(r => r.Overall.Unauthorized);
        int brokenSessions = sessions.Count(s => s.Broken);
        checks.Add("every session stayed authenticated", unauthorized == 0 && brokenSessions == 0,
            $"{unauthorized} 401s, {brokenSessions} sessions could not renew");

        int clientErrors = reports.Sum(r => r.Overall.ClientErrors);
        int rateLimited = reports.Sum(r => r.Overall.RateLimited);
        Console.WriteLine();
        Console.WriteLine($"(informational: {rateLimited} rate-limited, {clientErrors} refused-4xx responses)");

        return checks.Report();
    }

    private sealed record SeedResult(
        int Created, int Enrolled, int Groups, int SeasonsStarted, int FixturesResolved,
        int MarketWindowsOpened, long ElapsedMs, IReadOnlyList<CoachSession> Sessions);

    private static async Task<SeedResult?> SeedAsync(LoadClient client, LoadOptions options)
    {
        string path = $"/internal/dev/ranked/load-seed?coaches={options.Coaches}&ticks={options.SeedTicks}";
        var res = await client.PostAsync(
            path, null, null, "load-seed", null, CancellationToken.None, timeoutSeconds: 3600);
        if (!res.IsOk) return null;

        try
        {
            using var doc = JsonDocument.Parse(res.Body);
            var root = doc.RootElement;

            var sessions = new List<CoachSession>();
            if (root.TryGetProperty("accounts", out var accounts))
            {
                foreach (var account in accounts.EnumerateArray())
                {
                    string? token = account.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                    string? refresh = account.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                    int expires = account.TryGetProperty("expiresInSeconds", out var x) ? x.GetInt32() : 900;
                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(refresh))
                        sessions.Add(new CoachSession(token, refresh, expires));
                }
            }

            return new SeedResult(
                Int(root, "created"), Int(root, "enrolled"), Int(root, "groups"),
                Int(root, "seasonsStarted"), Int(root, "fixturesResolved"), Int(root, "marketWindowsOpened"),
                root.TryGetProperty("elapsedMs", out var e) ? e.GetInt64() : 0,
                sessions);
        }
        catch (JsonException)
        {
            return null;
        }

        static int Int(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;
    }
}

/// <summary>PASS/FAIL bookkeeping in the same shape as the tools/smoke-ranked-*.ps1 scripts, so a load run
/// reads like the other live checks and can gate a pipeline on its exit code.</summary>
public sealed class CheckList
{
    private readonly List<(string Text, bool Passed, string Detail)> _checks = new();

    public void Add(string text, bool passed, string detail) => _checks.Add((text, passed, detail));

    public int Report()
    {
        Console.WriteLine();
        Console.WriteLine("Checks");
        Console.WriteLine("------");

        int failed = 0;
        foreach (var (text, passed, detail) in _checks)
        {
            Console.WriteLine($"   {(passed ? "OK  " : "FAIL")}  {text} ({detail})");
            if (!passed) failed++;
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"ALL CHECKS PASSED ({_checks.Count})"
            : $"{failed} of {_checks.Count} CHECKS FAILED");
        return failed == 0 ? 0 : 1;
    }
}
