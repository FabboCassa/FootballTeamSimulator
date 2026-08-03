namespace Fts.LoadTest;

/// <summary>
/// Command-line options for the ranked load generator (Phase 9.6).
///
/// Everything is parameterised for one reason: on a development machine the generator, the API, PostgreSQL
/// and Redis all share the same CPU, so a thousand virtual coaches is a deliberate, occasional run — the
/// default is a smaller cohort you can fire off any time and still compare against yesterday's numbers.
/// </summary>
public sealed class LoadOptions
{
    public string BaseUrl { get; private set; } = "http://localhost:8080";

    /// <summary>Concurrent virtual coaches. Each one is a real seeded account with its own access token.</summary>
    public int Users { get; private set; } = 200;

    /// <summary>Accounts to seed (defaults to <see cref="Users"/>; a bigger cohort makes fuller groups).</summary>
    public int Coaches { get; private set; }

    /// <summary>Calendar ticks to run right after seeding, so seasons are under way and window 0 is open.</summary>
    public int SeedTicks { get; private set; } = 1;

    /// <summary>Duration of the steady-state daily-loop phase.</summary>
    public int Seconds { get; private set; } = 60;

    /// <summary>Duration of the auction bidding spike.</summary>
    public int AuctionSeconds { get; private set; } = 30;

    /// <summary>Pause between a virtual coach's loops in the steady phase (real clients are not stampedes).</summary>
    public int ThinkMs { get; private set; } = 800;

    /// <summary>Pause between bids in the auction spike. Contention comes from the eight coaches who share a
    /// group all seeing the same lots, NOT from one account hammering: the 9.5 limiter allows 10 bids per 10
    /// seconds per account, so a shorter pause would just measure the rate limiter answering 429.</summary>
    public int BidThinkMs { get; private set; } = 1000;

    /// <summary>The pass threshold from the roadmap: p95 API latency under load, in milliseconds.</summary>
    public double P95BudgetMs { get; private set; } = 300;

    /// <summary>How long the matchday resolution job may take. The recurring job runs every minute, so a
    /// tick that outlasts a minute would start overlapping itself — that is the real deadline.</summary>
    public int MatchdayBudgetSeconds { get; private set; } = 60;

    /// <summary>all | baseline | auction | matchday | tick.</summary>
    public string Scenario { get; private set; } = "all";

    public bool RunBaseline => Scenario is "all" or "baseline";
    public bool RunAuction => Scenario is "all" or "auction";
    public bool RunMatchday => Scenario is "all" or "matchday";

    /// <summary>The isolation run: seed the cohort, then time the matchday job with nothing else running.
    /// Deliberately NOT part of "all" — it answers a diagnostic question ("is the job slow, or is the box
    /// full?"), and it would move the calendar before the other phases had their turn.</summary>
    public bool RunTickOnly => Scenario is "tick";

    public static bool TryParse(string[] args, out LoadOptions options, out string? error)
    {
        var o = new LoadOptions();
        error = null;

        try
        {
            ParseInto(o, args, ref error);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
        }

        if (error is not null)
        {
            options = o;
            return false;
        }

        o.BaseUrl = o.BaseUrl.TrimEnd('/');
        if (o.Users < 1) o.Users = 1;
        if (o.Coaches < o.Users) o.Coaches = o.Users;
        if (o.Seconds < 1) o.Seconds = 1;
        if (o.AuctionSeconds < 1) o.AuctionSeconds = 1;
        if (o.ThinkMs < 0) o.ThinkMs = 0;
        if (o.BidThinkMs < 0) o.BidThinkMs = 0;
        if (o.SeedTicks < 0) o.SeedTicks = 0;

        if (o.Scenario is not ("all" or "baseline" or "auction" or "matchday" or "tick"))
        {
            error = $"Unknown scenario '{o.Scenario}' (expected all | baseline | auction | matchday | tick).";
            options = o;
            return false;
        }

        options = o;
        return true;
    }

    private static void ParseInto(LoadOptions o, string[] args, ref string? error)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            string? value = i + 1 < args.Length ? args[i + 1] : null;

            switch (key)
            {
                case "--url": o.BaseUrl = Next(ref i, value, "--url"); break;
                case "--users": o.Users = Int(ref i, value, "--users"); break;
                case "--coaches": o.Coaches = Int(ref i, value, "--coaches"); break;
                case "--seed-ticks": o.SeedTicks = Int(ref i, value, "--seed-ticks"); break;
                case "--seconds": o.Seconds = Int(ref i, value, "--seconds"); break;
                case "--auction-seconds": o.AuctionSeconds = Int(ref i, value, "--auction-seconds"); break;
                case "--think-ms": o.ThinkMs = Int(ref i, value, "--think-ms"); break;
                case "--bid-think-ms": o.BidThinkMs = Int(ref i, value, "--bid-think-ms"); break;
                case "--p95": o.P95BudgetMs = Int(ref i, value, "--p95"); break;
                case "--matchday-budget": o.MatchdayBudgetSeconds = Int(ref i, value, "--matchday-budget"); break;
                case "--scenario": o.Scenario = Next(ref i, value, "--scenario").ToLowerInvariant(); break;
                case "--help":
                case "-h":
                    error = Usage;
                    return;
                default:
                    error = $"Unknown argument '{key}'.\n\n{Usage}";
                    return;
            }
        }

        static string Next(ref int i, string? value, string name)
        {
            if (value is null) throw new ArgumentException($"{name} needs a value.");
            i++;
            return value;
        }

        static int Int(ref int i, string? value, string name)
        {
            string raw = Next(ref i, value, name);
            if (!int.TryParse(raw, out int parsed))
                throw new ArgumentException($"{name} needs a number (got '{raw}').");
            return parsed;
        }
    }

    public const string Usage = """
        fts-loadtest -- ranked ladder load generator (Phase 9.6)

          --url <url>               server root (default http://localhost:8080)
          --users <n>               concurrent virtual coaches (default 200)
          --coaches <n>             accounts to seed (default: same as --users)
          --seed-ticks <n>          calendar ticks right after seeding (default 1)
          --seconds <n>             steady daily-loop phase, seconds (default 60)
          --auction-seconds <n>     auction spike, seconds (default 30)
          --think-ms <n>            pause between loops per coach (default 800)
          --bid-think-ms <n>        pause between bids per coach (default 1000)
          --p95 <ms>                p95 latency budget (default 300)
          --matchday-budget <s>     matchday job budget, seconds (default 60)
          --scenario <name>         all | baseline | auction | matchday | tick (default all)
                                    'tick' times the matchday job on an idle server

        The server must be running with the dev gates on (Dev:ExposeSeedEndpoints and
        Ranked:ExposeInternalEndpoints) -- docker compose already sets both.
        """;

    public void Print()
    {
        Console.WriteLine($"  server           {BaseUrl}");
        Console.WriteLine($"  scenario         {Scenario}");
        Console.WriteLine($"  coaches / users  {Coaches} / {Users}");
        Console.WriteLine($"  steady phase     {Seconds}s (think {ThinkMs}ms)");
        Console.WriteLine($"  auction spike    {AuctionSeconds}s (think {BidThinkMs}ms)");
        Console.WriteLine($"  budgets          p95 <= {P95BudgetMs:F0}ms, matchday job <= {MatchdayBudgetSeconds}s");
    }
}
