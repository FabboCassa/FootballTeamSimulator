using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;
using Sim.Core.Scouting;

namespace Fts.BalanceHarness;

/// <summary>
/// Task 11.1's measurement bench: what each database preset actually COSTS.
///
/// The roadmap is explicit that the expanded league database is a generation and performance
/// problem, not a data problem, and that the default preset has to come from a measurement rather
/// than a guess. So this scenario builds the world at every preset and prints, per preset: how long
/// generation takes, how many clubs and players exist, how much managed heap the graph holds, how
/// big the save is as raw JSON and as the gzip the client actually writes, and how long a whole
/// world season takes to play (the player's own divisions through the real match engine, everything
/// else through the cheap resolver).
///
/// A desktop number is only half the answer — the binding constraint is WebGL and mid-range Android
/// — but it is the half that can be measured automatically, and it is what tells us whether a preset
/// is even in the right order of magnitude before it is put in front of a phone.
///
/// It also re-measures the cheap resolver against the real engine, because a background league whose
/// tables do not look like football would show up the moment a scout reports from one (task 11.2).
/// </summary>
internal static class WorldScenario
{
    /// <summary>Save-size budget for the biggest preset, gzipped, as the client stores it.</summary>
    private const long LargeSaveBudgetBytes = 8L * 1024 * 1024;

    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== WORLD (task 11.1: expanded league database) ===");
        Console.WriteLine($"playable nation {opt.WorldNation} to tier {opt.WorldTiers}");

        // Warm the JIT on the whole path first, or the first preset measured pays for compiling the
        // engine and reads slower than the biggest one - which makes the table read like nonsense.
        Measure(DatabaseSize.Small, opt, cfg);

        var sizes = new[] { DatabaseSize.Small, DatabaseSize.Medium, DatabaseSize.Large };
        var measured = new List<Measurement>();

        foreach (DatabaseSize size in sizes)
        {
            Measurement m = Measure(size, opt, cfg);
            measured.Add(m);

            Console.WriteLine(
                $"[world-preset] {size,-6} nations={m.Nations,3} leagues P/B/D={m.Playable}/{m.Background}/{m.DataOnly,3} " +
                $"clubs={m.Clubs,5} players={m.Players,6} gen={Fmt.N(m.GenerationMs, 0)}ms heap={Fmt.N(m.HeapBytes / 1048576.0, 1)}MB " +
                $"json={Fmt.N(m.JsonBytes / 1048576.0, 1)}MB save(gz)={Fmt.N(m.GzipBytes / 1024.0, 0)}KB " +
                $"season={Fmt.N(m.SeasonMs, 0)}ms ({m.EngineMatches} engine in {Fmt.N(m.EngineMs, 0)}ms + {m.QuickMatches} quick in {Fmt.N(m.BackgroundMs, 0)}ms)");
        }

        Console.WriteLine();
        foreach (Measurement m in measured)
        {
            Console.WriteLine(
                $"[world-search] {m.Players,6} players: index {Fmt.N(m.IndexMs, 1)}ms (best of 3) / {Fmt.N(m.IndexBytes / 1048576.0, 1)}MB · " +
                $"whole world {Fmt.N(m.WorldSearchMs, 1)}ms ({m.SearchTotal} hits) · " +
                $"young strikers {Fmt.N(m.FilteredSearchMs, 1)}ms ({m.FilteredTotal} hits) · " +
                $"continental scan walk {Fmt.N(m.WalkScanMs, 2)}ms vs index {Fmt.N(m.IndexScanMs, 2)}ms (avg of 20) · " +
                $"publicly known {m.PubliclyKnown} ({Fmt.Pct(m.Players == 0 ? 0 : (double)m.PubliclyKnown / m.Players)})");
        }

        Measurement large = measured[^1];
        Measurement small = measured[0];

        checks.Info($"a Large world is {large.Players} players in {Fmt.N(large.GzipBytes / 1024.0, 0)}KB of save - the gzip JSON format still holds, a binary/chunked save is not needed yet");
        checks.Info($"generation cost per 1,000 players: {Fmt.N(large.GenerationMs * 1000.0 / Math.Max(1, large.Players), 1)}ms on this machine - WebGL is roughly 3-6x slower, mid-range Android 2-4x");
        double quickCost = large.QuickMatches == 0 ? 0 : large.BackgroundMs / large.QuickMatches;
        double engineCost = large.EngineMatches == 0 ? 0 : large.EngineMs / large.EngineMatches;
        checks.Info($"cost per match: {Fmt.N(engineCost, 3)}ms through the engine against {Fmt.N(quickCost, 3)}ms through the background resolver ({Fmt.N(engineCost / Math.Max(0.0001, quickCost), 0)}x) - this ratio is the whole reason the detail levels exist");

        // --- checks ---------------------------------------------------------------------------

        checks.Check("world/deterministic",
            large.Deterministic,
            "the same seed and scope generate a byte-identical world");

        checks.Check("world/unique-ids",
            large.UniqueIds,
            "every club and player id in the world is unique");

        checks.Check("world/presets-grow",
            small.Players < measured[1].Players && measured[1].Players < large.Players,
            $"Small {small.Players} < Medium {measured[1].Players} < Large {large.Players} players");

        checks.Check("world/season-completes",
            large.SeasonComplete,
            "every fixture, playable and background, is played by the end of the season");

        checks.Check("world/save-size",
            large.GzipBytes <= LargeSaveBudgetBytes,
            $"Large save is {Fmt.N(large.GzipBytes / 1048576.0, 2)}MB gzipped against a {LargeSaveBudgetBytes / 1048576}MB budget");

        // --- task 11.3: searching the big database ----------------------------------------------

        checks.Check("world/search-fast",
            large.WorldSearchMs < 250,
            $"a query across the whole {large.Players}-player world returns in {Fmt.N(large.WorldSearchMs, 1)}ms (avg of 20) (budget 250ms, the \u2705 asks for under a second)");

        checks.Check("world/search-index-agrees",
            large.ScanAgrees,
            "the indexed discovery scan returns exactly what the task 11.2 walk returned, in the same order");

        checks.Info($"the search index costs {Fmt.N(large.IndexMs, 1)}ms and {Fmt.N(large.IndexBytes / 1048576.0, 1)}MB on a Large world - paid once, when the search tab is first opened");
        checks.Info($"a continental scouting scan costs {Fmt.N(large.WalkScanMs, 2)}ms walking the world against {Fmt.N(large.IndexScanMs, 2)}ms through the index (average of 20 warmed runs) - that is the weekly tick, once per scout");
        checks.Info($"public knowledge (task 11.3): {Fmt.Pct(large.Players == 0 ? 0 : (double)large.PubliclyKnown / large.Players)} of a Large world is famous enough to read without a scout at all");

        // --- the cheap resolver against the real engine -----------------------------------------

        ResolverFidelity fidelity = CompareResolverWithEngine(opt, cfg);

        Console.WriteLine(
            $"[world-resolver] engine goals={Fmt.N(fidelity.EngineGoals)} home={Fmt.Pct(fidelity.EngineHome)} draw={Fmt.Pct(fidelity.EngineDraw)} | " +
            $"quick goals={Fmt.N(fidelity.QuickGoals)} home={Fmt.Pct(fidelity.QuickHome)} draw={Fmt.Pct(fidelity.QuickDraw)}");

        checks.Check("world/resolver-goals",
            Math.Abs(fidelity.QuickGoals - fidelity.EngineGoals) <= 0.35,
            $"background leagues score {Fmt.N(fidelity.QuickGoals)} goals a game against the engine's {Fmt.N(fidelity.EngineGoals)}");

        // Both sides are a single 760-match season, so a couple of points of difference is sampling
        // noise, not drift: across four seeds the engine draws 19.9-24.2% and the resolver 22.5-28%.
        // The resolver does draw a little more often - a binomial has less spread than the engine's
        // chance-by-chance goals - and for leagues resolved without a match that is an acceptable
        // trade for 28 random numbers a game. The tolerances below are set to catch a real drift,
        // not that known few points.
        checks.Check("world/resolver-outcomes",
            Math.Abs(fidelity.QuickHome - fidelity.EngineHome) <= 0.06
            && Math.Abs(fidelity.QuickDraw - fidelity.EngineDraw) <= 0.08,
            $"home {Fmt.Pct(fidelity.QuickHome)} vs {Fmt.Pct(fidelity.EngineHome)}, draw {Fmt.Pct(fidelity.QuickDraw)} vs {Fmt.Pct(fidelity.EngineDraw)}");
    }

    private sealed class Measurement
    {
        public int Nations, Playable, Background, DataOnly, Clubs, Players;
        public int EngineMatches, QuickMatches;
        public double GenerationMs, SeasonMs, EngineMs, BackgroundMs;
        public long HeapBytes, JsonBytes, GzipBytes;
        public bool Deterministic, UniqueIds, SeasonComplete;

        // --- task 11.3: the search index -------------------------------------------------------
        public double IndexMs, WorldSearchMs, FilteredSearchMs, WalkScanMs, IndexScanMs;
        public long IndexBytes;
        public int SearchTotal, FilteredTotal, PubliclyKnown;
        public bool ScanAgrees;
    }

    private static WorldGenerationOptions Options(DatabaseSize size, HarnessOptions opt)
    {
        var scope = new WorldScope { Size = size };
        scope.Playable.Add(new PlayableNation { Code = opt.WorldNation, PlayableTiers = opt.WorldTiers });
        return new WorldGenerationOptions { Scope = scope };
    }

    private static Measurement Measure(DatabaseSize size, HarnessOptions opt, BalanceConfig cfg)
    {
        var m = new Measurement();

        long baseline = GC.GetTotalMemory(true);
        var clock = Stopwatch.StartNew();
        World world = new WorldGenerator(Options(size, opt), cfg).Generate(opt.Seed);
        clock.Stop();

        m.GenerationMs = clock.Elapsed.TotalMilliseconds;
        m.HeapBytes = Math.Max(0, GC.GetTotalMemory(true) - baseline);

        m.Nations = world.Nations.Count;
        m.Playable = world.LeaguesAt(LeagueDetailLevel.Playable).Count;
        m.Background = world.LeaguesAt(LeagueDetailLevel.Background).Count;
        m.DataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly).Count;
        m.Clubs = world.ClubCount();
        m.Players = world.PlayerCount();

        m.UniqueIds = HasUniqueIds(world);
        m.Deterministic = JsonSerializer.Serialize(new WorldGenerator(Options(size, opt), cfg).Generate(opt.Seed))
                          == JsonSerializer.Serialize(world);

        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world));
        m.JsonBytes = json.Length;
        m.GzipBytes = GzipSize(json);

        MeasureSearch(m, world, opt, cfg);

        // A whole world season: the player's divisions through the real engine, the rest cheaply.
        Season season = FirstSeason(world, opt.Seed, cfg);
        var progressor = new SeasonProgressor(cfg);
        var background = new BackgroundLeagueProgressor(cfg);
        List<League> playable = world.PlayableLeagues();

        int lastDay = Math.Max(
            season.Fixtures.Count == 0 ? 0 : season.Fixtures.Max(f => f.Day),
            BackgroundLeagueProgressor.LastDay(world));

        // Timed apart, because the two costs are what the whole detail-level split is about: the
        // engine matches are the expensive ones and the background world has to stay nearly free.
        var engineClock = new Stopwatch();
        var backgroundClock = new Stopwatch();

        clock.Restart();
        while (season.CurrentDay <= lastDay)
        {
            engineClock.Start();
            m.EngineMatches += progressor.AdvanceDay(playable, season, opt.Seed).Count;
            engineClock.Stop();

            backgroundClock.Start();
            m.QuickMatches += background.AdvanceTo(world, season.CurrentDay, opt.Seed);
            backgroundClock.Stop();
        }
        clock.Stop();

        m.SeasonMs = clock.Elapsed.TotalMilliseconds;
        m.EngineMs = engineClock.Elapsed.TotalMilliseconds;
        m.BackgroundMs = backgroundClock.Elapsed.TotalMilliseconds;
        m.SeasonComplete = season.Fixtures.All(f => f.Played) && world.BackgroundSeason.Fixtures.All(f => f.Played);

        return m;
    }

    /// <summary>
    /// Task 11.3: what it costs to SEARCH the database that task 11.1 made big.
    ///
    /// Three numbers matter here. How long the flat index takes to build and what it weighs (the
    /// client pays that once, when the search tab is first opened). How long a whole-world query
    /// takes — the ✅ says "under a second", and the honest target is a fraction of a frame, since
    /// this runs on every keystroke. And the walk-versus-index comparison on a continental scouting
    /// scan, which is the weekly cost task 11.2 left behind and 11.3 was meant to remove.
    /// </summary>
    private static void MeasureSearch(Measurement m, World world, HarnessOptions opt, BalanceConfig cfg)
    {
        ScoutingBalance scouting = cfg.Scouting;

        // Everything here runs in single-digit milliseconds, which is exactly the range where a
        // single shot measures the JIT and the garbage collector instead of the code. The first run
        // of this bench proved it: the walk-versus-index comparison came out 2.5 vs 0.1 on Small,
        // 0.1 vs 2.7 on Medium and 0.4 vs 0.3 on Large - noise, in three different directions. So
        // every timing below is warmed first and then averaged over Repeats runs, and the index
        // build - too allocation-heavy to repeat many times - is the best of three.
        const int Repeats = 20;
        const int Builds = 3;

        long baseline = GC.GetTotalMemory(true);
        WorldPlayerIndex index = WorldPlayerIndex.Build(world, scouting);
        m.IndexBytes = Math.Max(0, GC.GetTotalMemory(true) - baseline);

        double bestBuild = double.MaxValue;
        for (int i = 0; i < Builds; i++)
        {
            var buildClock = Stopwatch.StartNew();
            WorldPlayerIndex.Build(world, scouting);
            buildClock.Stop();
            bestBuild = Math.Min(bestBuild, buildClock.Elapsed.TotalMilliseconds);
        }

        m.IndexMs = bestBuild;

        for (int slot = 0; slot < index.Count; slot++)
        {
            if (index.FloorAt(slot) > 0)
                m.PubliclyKnown++;
        }

        var everyone = new PlayerSearchQuery { PageSize = 20, ExcludeOwnClub = false };
        var wonderkids = new PlayerSearchQuery
        {
            Filters = new ScoutingFilters { Role = (int)PositionRole.Striker, MaxAge = 23 },
            Sort = PlayerSearchSort.Potential,
            PageSize = 20,
            ExcludeOwnClub = false
        };

        // The weekly continental scan, both ways. Same brief, same knowledge, same everything - the
        // only difference is whether the area is reached through the index or by walking the graph.
        var knowledge = new KnowledgeStore();
        var brief = new ScoutingAssignment
        {
            ScoutId = 1,
            Area = ScoutingArea.ForContinent(Continent.Europe),
            Filters = new ScoutingFilters { Role = (int)PositionRole.Striker, MaxAge = 24 }
        };

        // Warm every path before any of them is timed.
        m.SearchTotal = index.Search(everyone, null, opt.Seed, ScoutQuality.Neutral).Total;
        m.FilteredTotal = index.Search(wonderkids, null, opt.Seed, ScoutQuality.Neutral).Total;
        List<ScoutingDiscovery.Candidate> walked =
            ScoutingDiscovery.Scan(world, brief, knowledge, opt.Seed, 0, ScoutQuality.Neutral, 10, scouting);
        List<ScoutingDiscovery.Candidate> indexed =
            ScoutingDiscovery.Scan(world, brief, knowledge, opt.Seed, 0, ScoutQuality.Neutral, 10, scouting, index);

        m.ScanAgrees = walked.Count == indexed.Count;
        for (int i = 0; m.ScanAgrees && i < walked.Count; i++)
            m.ScanAgrees = walked[i].PlayerId == indexed[i].PlayerId
                           && walked[i].EstimatedOverall == indexed[i].EstimatedOverall;

        m.WorldSearchMs = Average(Repeats, () => index.Search(everyone, null, opt.Seed, ScoutQuality.Neutral));
        m.FilteredSearchMs = Average(Repeats, () => index.Search(wonderkids, null, opt.Seed, ScoutQuality.Neutral));
        m.WalkScanMs = Average(Repeats, () =>
            ScoutingDiscovery.Scan(world, brief, knowledge, opt.Seed, 0, ScoutQuality.Neutral, 10, scouting));
        m.IndexScanMs = Average(Repeats, () =>
            ScoutingDiscovery.Scan(world, brief, knowledge, opt.Seed, 0, ScoutQuality.Neutral, 10, scouting, index));
    }

    /// <summary>Milliseconds per run, averaged over <paramref name="repeats"/> warmed runs.</summary>
    private static double Average(int repeats, Func<object> work)
    {
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < repeats; i++)
            work();

        clock.Stop();
        return clock.Elapsed.TotalMilliseconds / Math.Max(1, repeats);
    }

    /// <summary>The career season the host builds for the playable leagues (mirrors task 11.1b).</summary>
    private static Season FirstSeason(World world, ulong seed, BalanceConfig cfg)
    {
        var season = new Season { Year = 1, CurrentDay = 1 };
        var generator = new FixtureGenerator(cfg.Season);
        int nextId = 1;

        foreach (League league in world.PlayableLeagues())
        {
            List<Fixture> fixtures = generator.Generate(league, new Pcg32(seed, 20_000UL + (ulong)league.Id), nextId);
            nextId += fixtures.Count;
            season.Fixtures.AddRange(fixtures);
        }

        return season;
    }

    private static bool HasUniqueIds(World world)
    {
        var clubs = new HashSet<int>();
        var players = new HashSet<int>();

        foreach (Nation nation in world.Nations)
        {
            foreach (League league in nation.Leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (!clubs.Add(club.Id)) return false;
                    foreach (Player player in club.Squad.Players)
                        if (!players.Add(player.Id)) return false;
                }
            }
        }

        return true;
    }

    private static long GzipSize(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true))
            gzip.Write(payload, 0, payload.Length);

        return buffer.Length;
    }

    private sealed class ResolverFidelity
    {
        public double EngineGoals, EngineHome, EngineDraw;
        public double QuickGoals, QuickHome, QuickDraw;
    }

    /// <summary>
    /// Plays one real two-division season with the engine, then replays every ordered pairing of the
    /// same clubs through the cheap resolver. Same squads, same strengths — so the two distributions
    /// are directly comparable, and a drift in either shows up as a failed check rather than as a
    /// weird table someone notices six months later.
    /// </summary>
    private static ResolverFidelity CompareResolverWithEngine(HarnessOptions opt, BalanceConfig cfg)
    {
        var leagues = new List<League>
        {
            new LeagueGenerator(new LeagueGenerationOptions { LeagueId = 1, Division = 1, FirstClubId = 1, FirstPlayerId = 1 }, cfg)
                .Generate(new Pcg32(opt.Seed)),
            new LeagueGenerator(new LeagueGenerationOptions { LeagueId = 2, Division = 2, FirstClubId = 101, FirstPlayerId = 5001 }, cfg)
                .Generate(new Pcg32(opt.Seed, 55))
        };

        var season = new Season();
        var generator = new FixtureGenerator(cfg.Season);
        List<Fixture> first = generator.Generate(leagues[0], new Pcg32(opt.Seed, 777), 1);
        season.Fixtures.AddRange(first);
        season.Fixtures.AddRange(generator.Generate(leagues[1], new Pcg32(opt.Seed, 778), first.Count + 1));

        var progressor = new SeasonProgressor(cfg);
        int lastDay = season.Fixtures.Max(f => f.Day);
        while (season.CurrentDay <= lastDay)
            progressor.AdvanceDay(leagues, season, opt.Seed);

        var fidelity = new ResolverFidelity();
        List<Fixture> played = season.Fixtures.Where(f => f.Played).ToList();
        Summarise(played.Select(f => (f.HomeGoals, f.AwayGoals)).ToList(),
            out fidelity.EngineGoals, out fidelity.EngineHome, out fidelity.EngineDraw);

        var quick = new List<(int Home, int Away)>();
        int fixtureId = 1;
        foreach (League league in leagues)
        {
            foreach (Club home in league.Clubs)
            {
                foreach (Club away in league.Clubs)
                {
                    if (home.Id == away.Id) continue;

                    var fixture = new Fixture { Id = fixtureId++ };
                    QuickResultResolver.Resolve(fixture, SquadStrength(home), SquadStrength(away), opt.Seed, cfg);
                    quick.Add((fixture.HomeGoals, fixture.AwayGoals));
                }
            }
        }

        Summarise(quick, out fidelity.QuickGoals, out fidelity.QuickHome, out fidelity.QuickDraw);
        return fidelity;
    }

    private static int SquadStrength(Club club)
    {
        if (club.Squad.Players.Count == 0) return 1;

        int total = 0;
        foreach (Player player in club.Squad.Players)
            total += PlayerRating.Overall(player);

        return total / club.Squad.Players.Count;
    }

    private static void Summarise(IReadOnlyList<(int Home, int Away)> results, out double goals, out double home, out double draw)
    {
        if (results.Count == 0)
        {
            goals = home = draw = 0;
            return;
        }

        int total = 0, homeWins = 0, draws = 0;
        foreach ((int h, int a) in results)
        {
            total += h + a;
            if (h > a) homeWins++;
            else if (h == a) draws++;
        }

        goals = (double)total / results.Count;
        home = (double)homeWins / results.Count;
        draw = (double)draws / results.Count;
    }
}
