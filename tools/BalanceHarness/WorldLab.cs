using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Fts.BalanceHarness;

/// <summary>A world the harness owns for the length of a run: the same graph a career save holds.</summary>
internal sealed class LabWorld
{
    public List<League> Leagues { get; set; } = new();
    public Season Season { get; set; } = new();
    public ulong Seed { get; set; }

    /// <summary>Mirror of CareerState.TransferWindowsRun - a window fires at most once per season.</summary>
    public int WindowsRun { get; set; }

    /// <summary>Mirror of CareerState.LastTrainingWeek - the season-local development/finance week.</summary>
    public int LastTrainingWeek { get; set; }
}

/// <summary>One finished season, from the top division's point of view.</summary>
internal sealed class SeasonResult
{
    public int Year { get; set; }
    public List<LeagueTableRow> Table { get; set; } = new();
    public int UserPoints { get; set; }
    public int UserPosition { get; set; }
    public List<TransferRecord> Transfers { get; set; } = new();
}

/// <summary>
/// Runs whole seasons exactly the way the single-player client runs them (Services/LocalClock +
/// LocalMarketService): transfer windows, the weekly development/finance/valuation tick, condition-aware
/// matches with within-match fatigue and free positioning, gate receipts, prize money, rollover.
///
/// The point of mirroring the client rather than calling the engine directly is that a balance number
/// measured here is a number the shipped game produces - not a number a laboratory setup produces.
/// </summary>
internal sealed class WorldLab
{
    /// <summary>Same stride the client uses to decorrelate the per-week development RNG across seasons.</summary>
    private const int SeasonWeekStride = 100;
    private const int DayCap = 800;

    /// <summary>Sequence ids of the generation/fixture RNG streams - identical to CareerFactory's, so a
    /// harness world is the world a career of the same seed would have.</summary>
    private const ulong Div2GenSequence = 55;
    private const ulong FixtureSequence = 777;
    private const ulong Div2FixtureSequence = 778;
    private const int Div2FirstClubId = 101;
    private const int Div2FirstPlayerId = 5001;

    private readonly BalanceConfig _cfg;
    private readonly SeasonProgressor _progressor;
    private readonly DevelopmentProgressor _development;
    private readonly ValuationProgressor _valuation;
    private readonly FinanceProgressor _finance;
    private readonly TransferMarket? _transferMarket;
    private readonly SeasonRollover _rollover;
    private readonly bool _liveCondition;

    /// <summary>
    /// <paramref name="liveCondition"/> and <paramref name="market"/> default to the shipped client's
    /// behaviour (both on). Turning one off is how a scenario ISOLATES a lever: a measurement that moves
    /// when the market is switched off was never about the thing it claimed to measure.
    /// </summary>
    public WorldLab(BalanceConfig cfg, bool liveCondition = true, bool market = true)
    {
        _cfg = cfg;
        _liveCondition = liveCondition;
        // Free positioning is always on and always inert here: nothing in the harness drags a player off
        // his formation anchor, and a clean preset is the engine's identity.
        _progressor = new SeasonProgressor(cfg, liveCondition, liveCondition, applyPositioning: true);
        _development = new DevelopmentProgressor(cfg.Development);
        _valuation = new ValuationProgressor(cfg);
        _finance = new FinanceProgressor(cfg);
        _transferMarket = market ? new TransferMarket(cfg) : null;
        _rollover = new SeasonRollover(cfg);
    }

    public BalanceConfig Config => _cfg;

    /// <summary>A fresh world plus its first season, built the way CareerFactory builds one.</summary>
    public LabWorld NewWorld(ulong seed, int divisions = 2, int clubsPerDivision = 20)
    {
        var world = new LabWorld { Seed = seed };

        var div1 = new LeagueGenerator(new LeagueGenerationOptions
        {
            LeagueId = 1,
            Division = 1,
            LeagueName = "Division 1",
            ClubCount = clubsPerDivision,
            FirstClubId = 1,
            FirstPlayerId = 1,
        }, _cfg).Generate(new Pcg32(seed));
        world.Leagues.Add(div1);

        if (divisions > 1)
        {
            var div2 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 2,
                Division = 2,
                LeagueName = "Division 2",
                ClubCount = clubsPerDivision,
                FirstClubId = Div2FirstClubId,
                FirstPlayerId = Div2FirstPlayerId,
            }, _cfg).Generate(new Pcg32(seed, Div2GenSequence));
            world.Leagues.Add(div2);
        }

        // Whole-world finances & facilities, then the first prices (task 5.5 / 5.1).
        _finance.SeedWorld(world.Leagues);
        _valuation.Reprice(world.Leagues);
        world.Season = BuildSeason(world);
        return world;
    }

    private Season BuildSeason(LabWorld world)
    {
        var season = new Season();
        var generator = new FixtureGenerator(_cfg.Season);

        List<Fixture> first = generator.Generate(world.Leagues[0], new Pcg32(world.Seed, FixtureSequence), 1);
        season.Fixtures.AddRange(first);

        if (world.Leagues.Count > 1)
            season.Fixtures.AddRange(
                generator.Generate(world.Leagues[1], new Pcg32(world.Seed, Div2FixtureSequence), first.Count + 1));

        return season;
    }

    /// <summary>
    /// Plays one whole season. <paramref name="userClubId"/> is the club the harness is watching (and the
    /// club the AI market leaves alone, as the client does); pass -1 for a world with no human in it.
    /// <paramref name="difficulty"/> degrades the AI lineups, <paramref name="settings"/> scales the
    /// transfer kitties, <paramref name="tactics"/> gives a club its tactic - all optional.
    /// <paramref name="watchedClubTrades"/> lets the watched club buy and sell like every other club
    /// (an engaged human) instead of sitting the market out (a human who never opens the screen).
    /// </summary>
    public SeasonResult RunSeason(
        LabWorld world,
        int userClubId = -1,
        DifficultyContext? difficulty = null,
        DifficultySettings? settings = null,
        IReadOnlyDictionary<int, LineupPlan>? lineupPlans = null,
        IReadOnlyDictionary<int, TacticContext>? tactics = null,
        bool watchedClubTrades = false)
    {
        var result = new SeasonResult { Year = world.Season.Year };

        int marketExcludes = watchedClubTrades ? -1 : userClubId;

        int guard = 0;
        while (guard++ < DayCap && !AllPlayed(world.Season))
        {
            RunDueWindows(world, userClubId, marketExcludes, settings, result.Transfers);
            EvolveWeeksUpTo(world, world.Season.CurrentDay + 1);

            List<MatchOutcome> outcomes = _progressor.AdvanceDay(
                world.Leagues, world.Season, world.Seed, lineupPlans, tactics, null, difficulty);

            _finance.AccrueMatchday(world.Leagues, outcomes);

            if (_liveCondition)
                _progressor.EvolveCondition(world.Leagues, world.Season, outcomes, world.Seed, lineupPlans, difficulty);
        }

        _finance.AwardPrizeMoney(world.Leagues, world.Season);

        result.Table = LeagueTable.Compute(world.Leagues[0], world.Season, _cfg.Season);
        for (int i = 0; i < result.Table.Count; i++)
        {
            if (result.Table[i].ClubId != userClubId) continue;
            result.UserPosition = i + 1;
            result.UserPoints = result.Table[i].Points;
        }

        return result;
    }

    /// <summary>Promotion/relegation, ageing, a fresh calendar - the client's season rollover.</summary>
    public void Rollover(LabWorld world)
    {
        RolloverResult res = _rollover.EndSeason(world.Leagues, world.Season, world.Seed);
        world.Season = res.NewSeason;
        FinanceProgressor.ResetSeasonCounters(world.Leagues);
        world.WindowsRun = 0;
        world.LastTrainingWeek = 0;
    }

    // --- the client's two per-day chores -----------------------------------------------------------

    private void RunDueWindows(
        LabWorld world, int userClubId, int marketExcludes, DifficultySettings? settings, List<TransferRecord> log)
    {
        if (_transferMarket is null) return;

        if (world.WindowsRun <= 0)
        {
            // Start-of-season kitties come from each club's finances, then difficulty scales them.
            _finance.SeedTransferBudgets(world.Leagues);
            if (settings is { } s) ScaleBudgetsByDifficulty(world, userClubId, s);

            log.AddRange(_transferMarket.RunWindow(world.Leagues, world.Seed, 0, marketExcludes));
            world.WindowsRun = 1;
        }

        if (world.WindowsRun == 1 && world.Season.CurrentDay >= MidSeasonDay(world.Season))
        {
            log.AddRange(_transferMarket.RunWindow(world.Leagues, world.Seed, 1, marketExcludes));
            world.WindowsRun = 2;
        }
    }

    private void EvolveWeeksUpTo(LabWorld world, int upcomingDay)
    {
        int period = _cfg.Season.DaysBetweenRounds;
        if (period <= 0) return;

        int targetWeek = upcomingDay / period;
        if (world.LastTrainingWeek >= targetWeek) return;

        while (world.LastTrainingWeek < targetWeek)
        {
            int weekInSeason = world.LastTrainingWeek + 1;
            int rngWeek = world.Season.Year * SeasonWeekStride + weekInSeason;

            // No explicit training plans and no per-player contexts: every club (including the watched
            // one) trains the balanced AI default at neutral minutes. The harness measures the world's
            // baseline, not a particular player's training choices.
            _development.EvolveWeek(world.Leagues, null, null, world.Seed, rngWeek);
            _finance.AccrueWeek(world.Leagues, world.Season);

            world.LastTrainingWeek = weekInSeason;
        }

        _valuation.Reprice(world.Leagues);
    }

    private void ScaleBudgetsByDifficulty(LabWorld world, int userClubId, DifficultySettings s)
    {
        long minBudget = _cfg.Transfer.MinBudget;
        foreach (League league in world.Leagues)
        {
            foreach (Club club in league.Clubs)
            {
                int permille = club.Id == userClubId ? s.UserBudgetPermille : s.AiBudgetPermille;
                long scaled = club.TransferBudget * permille / 1000;
                club.TransferBudget = scaled < minBudget ? minBudget : scaled;
            }
        }
    }

    private static int MidSeasonDay(Season season)
    {
        int maxDay = 0;
        foreach (Fixture f in season.Fixtures)
            if (f.Day > maxDay) maxDay = f.Day;
        return maxDay / 2;
    }

    private static bool AllPlayed(Season season)
    {
        foreach (Fixture f in season.Fixtures)
            if (!f.Played) return false;
        return true;
    }
}
