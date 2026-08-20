using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

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
