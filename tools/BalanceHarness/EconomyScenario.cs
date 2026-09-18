using Fts.Infrastructure.Ranked;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Fts.BalanceHarness;

/// <summary>
/// "Market price sanity": what things cost, what clubs can pay, whether the two are in the same
/// universe, and whether either drifts as seasons pass. Runs the real career loop (WorldLab), so the
/// numbers include development, ageing, contract decay, wages and the AI market spending itself down.
///
/// It also answers the question phase 9.5 handed forward: a ranked club is given a flat 25M kitty while
/// the world it plays in prices its best player above 200M, which means direct coach-to-coach offers can
/// only ever reach the lower and middle of a squad. That is measured here, in one line, rather than
/// remembered as an anecdote.
/// </summary>
internal static class EconomyScenario
{
    public static void Run(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        Console.WriteLine();
        Console.WriteLine("=== ECONOMY ===");

        var lab = new WorldLab(cfg);
        var finance = new FinanceProgressor(cfg);

        var firstSeasonValues = new List<long>();
        var lastSeasonValues = new List<long>();
        var seededBudgets = new List<long>();
        var fees = new List<long>();

        long leagueIncome = 0, leagueWages = 0;
        int insolvent = 0, clubSeasons = 0;
        int transfers = 0, clubsWithoutSignings = 0;
        long minValue = long.MaxValue, maxValue = 0;
        var divisions = new Dictionary<int, DivisionStats>();

        for (int w = 0; w < opt.EconomyWorlds; w++)
        {
            ulong seed = opt.Seed + 100_000 + (ulong)w;
            LabWorld world = lab.NewWorld(seed, divisions: 2);

            for (int s = 0; s < opt.EconomySeasons; s++)
            {
                // The kitties the season is about to be played with. RunSeason re-seeds them identically
                // at window 0 (same finances, same deterministic model), so reading them here is honest.
                finance.SeedTransferBudgets(world.Leagues);
                foreach (Club club in AllClubs(world)) seededBudgets.Add(club.TransferBudget);

                SeasonResult result = lab.RunSeason(world);

                transfers += result.Transfers.Count;
                foreach (TransferRecord t in result.Transfers) fees.Add(t.Fee);

                var buyers = new HashSet<int>();
                foreach (TransferRecord t in result.Transfers) buyers.Add(t.ToClubId);
                foreach (League lg in world.Leagues)
                {
                    foreach (Club club in lg.Clubs)
                    {
                        clubSeasons++;
                        if (!buyers.Contains(club.Id)) clubsWithoutSignings++;
                        if (club.Finances.Balance < 0) insolvent++;
                        leagueIncome += club.Finances.SeasonIncome;
                        leagueWages += club.Finances.SeasonWageExpense;

                        // Per division, because a real second tier is NOT a smaller first tier: the English
                        // Championship spends more than it earns chasing promotion, while Serie B and the
                        // 2. Bundesliga run on a tenth of the money. A career passes through both.
                        if (!divisions.TryGetValue(lg.Division, out DivisionStats? stats))
                        {
                            stats = new DivisionStats();
                            divisions[lg.Division] = stats;
                        }

                        long squadValue = 0;
                        foreach (Player p in club.Squad.Players) squadValue += p.MarketValue;

                        stats.Income.Add(club.Finances.SeasonIncome);
                        stats.Wages.Add(club.Finances.SeasonWageExpense);
                        stats.SquadValue.Add(squadValue);
                        stats.Budget.Add(club.TransferBudget);
                    }
                }

                foreach (Player p in AllPlayers(world))
                {
                    if (s == 0) firstSeasonValues.Add(p.MarketValue);
                    if (s == opt.EconomySeasons - 1) lastSeasonValues.Add(p.MarketValue);
                    if (p.MarketValue < minValue) minValue = p.MarketValue;
                    if (p.MarketValue > maxValue) maxValue = p.MarketValue;
                }

                if (s < opt.EconomySeasons - 1) lab.Rollover(world);
            }
        }

        firstSeasonValues.Sort();
        lastSeasonValues.Sort();
        seededBudgets.Sort();
        fees.Sort();

        long p50First = Fmt.Percentile(firstSeasonValues, 0.50);
        long p50Last = Fmt.Percentile(lastSeasonValues, 0.50);
        long p90 = Fmt.Percentile(lastSeasonValues, 0.90);
        long p99 = Fmt.Percentile(lastSeasonValues, 0.99);
        long medianBudget = Fmt.Percentile(seededBudgets, 0.50);
        long topBudget = Fmt.Percentile(seededBudgets, 0.99);

        Console.WriteLine(
            $"[balance-values] {lastSeasonValues.Count} players (last season): min {Fmt.Money(minValue)}  " +
            $"p50 {Fmt.Money(p50Last)}  p90 {Fmt.Money(p90)}  p99 {Fmt.Money(p99)}  max {Fmt.Money(maxValue)}");
        Console.WriteLine(
            $"[balance-values] drift across {opt.EconomySeasons} seasons: p50 {Fmt.Money(p50First)} -> {Fmt.Money(p50Last)} " +
            $"({Fmt.N(Ratio(p50Last, p50First), 2)}x)");
        Console.WriteLine(
            $"[balance-budget] club kitties: p50 {Fmt.Money(medianBudget)}  p99 {Fmt.Money(topBudget)}; " +
            $"a median kitty buys {Fmt.Pct(ShareUnder(lastSeasonValues, medianBudget))} of the world's players, " +
            $"a top kitty {Fmt.Pct(ShareUnder(lastSeasonValues, topBudget))}");
        Console.WriteLine(
            $"[balance-market] {Fmt.N(transfers / (double)Math.Max(1, opt.EconomyWorlds * opt.EconomySeasons), 1)} AI transfers/season " +
            $"(two windows); fee p50 {Fmt.Money(Fmt.Percentile(fees, 0.50))}  p90 {Fmt.Money(Fmt.Percentile(fees, 0.90))}  " +
            $"max {Fmt.Money(fees.Count > 0 ? fees[^1] : 0)}; " +
            $"{Fmt.Pct(clubsWithoutSignings / (double)Math.Max(1, clubSeasons))} of club-seasons signed nobody");
        Console.WriteLine(
            $"[balance-finance] league income {Fmt.Money(leagueIncome)} vs wages {Fmt.Money(leagueWages)} = " +
            $"{Fmt.Pct(Ratio(leagueWages, leagueIncome))} of income; {insolvent}/{clubSeasons} club-seasons insolvent");

        // --- per division, and against the real world ---------------------------------------------
        // Career mode is what this measures: a coach starts somewhere, gets promoted, gets sacked, moves
        // club. So the two divisions have to make sense SEPARATELY, and both have to make sense next to a
        // club's own squad. Real anchors (2024/25): wages take 54% of revenue in the Bundesliga, 60% in
        // LaLiga, 64% across the big five, 67% Europe-wide and 80% in Ligue 1, while the English second
        // tier runs past 100%. And a real squad is worth roughly TWO years of revenue (Arsenal: a 1.33bn
        // squad on about 700m of income), which is the ratio that decides whether a club can shop.
        var divisionKeys = new List<int>(divisions.Keys);
        divisionKeys.Sort();
        foreach (int division in divisionKeys)
        {
            DivisionStats stats = divisions[division];
            long income = stats.Median(stats.Income);
            long wages = stats.Median(stats.Wages);
            long squad = stats.Median(stats.SquadValue);
            long budget = stats.Median(stats.Budget);

            Console.WriteLine(
                $"[balance-division] division {division}: median income {Fmt.Money(income)}, wages {Fmt.Money(wages)} " +
                $"({Fmt.Pct(Ratio(wages, income))} of income), squad worth {Fmt.Money(squad)} " +
                $"= {Fmt.N(Ratio(squad, income), 1)}x a season's income (real football sits near 2x), " +
                $"kitty {Fmt.Money(budget)} ({Fmt.Pct(Ratio(budget, income))} of income); " +
                $"wages are {Fmt.Pct(Ratio(wages, squad))} of squad value (real football lands near 30%: " +
                "wages take about 65% of revenue and a squad is worth about two years of it)");
        }

        long topIncome = divisions.Count > 0 ? divisions[divisionKeys[0]].Median(divisions[divisionKeys[0]].Income) : 0;
        long medianFee = Fmt.Percentile(fees, 0.50);
        Console.WriteLine(
            $"[balance-division] a median transfer ({Fmt.Money(medianFee)}) costs a top-division club " +
            $"{Fmt.Pct(Ratio(medianFee, topIncome))} of a season's income - a real top-league signing is nearer 10%, " +
            "which is why real clubs sign five to ten players a season and ours sign one");

        // --- the ranked economy question carried forward from 9.5 ----------------------------------
        var ranked = new RankedOptions();
        League rankedWorld = new LeagueGenerator(
            new LeagueGenerationOptions { ClubCount = ranked.GroupSize }, cfg).Generate(new Pcg32(opt.Seed + 777));
        new ValuationProgressor(cfg).Reprice(new List<League> { rankedWorld });

        var rankedValues = new List<long>();
        foreach (Club club in rankedWorld.Clubs)
            foreach (Player p in club.Squad.Players)
                rankedValues.Add(p.MarketValue);
        rankedValues.Sort();

        long kitty = ranked.StartingTransferBudget;
        double affordable = ShareUnder(rankedValues, kitty);
        long rankedTop = rankedValues.Count > 0 ? rankedValues[^1] : 0;
        Console.WriteLine(
            $"[balance-ranked-economy] a ranked world ({ranked.GroupSize} clubs): top player {Fmt.Money(rankedTop)}, " +
            $"p50 {Fmt.Money(Fmt.Percentile(rankedValues, 0.50))}, flat kitty {Fmt.Money(kitty)} " +
            $"-> {Fmt.Pct(affordable)} of the ladder's players are within reach of a direct offer " +
            $"(top player costs {Fmt.N(Ratio(rankedTop, kitty), 1)}x the kitty)");

        // --- nation & division wealth (R1-R3) --------------------------------------------------
        RunNationDivisionWealth(opt, cfg, checks);

        // --- checks -------------------------------------------------------------------------------
        checks.Check(
            "no absurd prices",
            minValue >= cfg.Market.MinValue && maxValue <= cfg.Market.MaxValue && minValue > 0,
            $"every value inside [{Fmt.Money(cfg.Market.MinValue)}, {Fmt.Money(cfg.Market.MaxValue)}]");

        checks.Check(
            "prices do not run away across seasons",
            Ratio(p50Last, p50First) is > 0.4 and < 2.5,
            $"median value moved {Fmt.N(Ratio(p50Last, p50First), 2)}x over {opt.EconomySeasons} seasons (band 0.4x-2.5x)");

        checks.Check(
            "wages stay in a realistic share of income",
            Ratio(leagueWages, leagueIncome) is >= 0.45 and <= 0.85,
            $"{Fmt.Pct(Ratio(leagueWages, leagueIncome))} of income (band 45%-85%, the 5.5 accepted figure was 56%)");

        checks.Check(
            "nobody goes bankrupt",
            insolvent == 0,
            $"{insolvent} insolvent club-seasons (the board floors every balance at zero)");

        double perSeason = transfers / (double)Math.Max(1, opt.EconomyWorlds * opt.EconomySeasons);
        checks.Check(
            "the market moves, without a frenzy",
            perSeason is >= 40.0 and <= 300.0,
            $"{Fmt.N(perSeason, 1)} transfers/season across two divisions (band 40-300)");

        checks.Check(
            "a median club can shop",
            ShareUnder(lastSeasonValues, medianBudget) > 0.30,
            $"a median kitty reaches {Fmt.Pct(ShareUnder(lastSeasonValues, medianBudget))} of the players (floor 30%)");

        checks.Check(
            "the ranked kitty is not dead money",
            affordable > 0.20,
            $"{Fmt.Pct(affordable)} of a ranked world's players are affordable at {Fmt.Money(kitty)} (floor 20%)");

        checks.Info(
            $"the ranked kitty ({Fmt.Money(kitty)}) is flat while the ladder's best player is worth {Fmt.Money(rankedTop)} " +
            "- direct offers structurally reach only the lower/middle of a squad (the 9.5 note). Raising " +
            "RankedOptions.StartingTransferBudget or trimming the elite tail are the two levers.");
        checks.Info(
            $"{Fmt.Pct(clubsWithoutSignings / (double)Math.Max(1, clubSeasons))} of club-seasons signed nobody - " +
            "high means budgets or needs are the throttle, low means the squads churn every year.");

        // The one number that ties the two calibrations together. Values were tuned against Transfermarkt
        // (task 5.1) and revenues against club accounts (task 5.5), separately, and never against EACH
        // OTHER - so this is the first time they are asked to agree.
        double squadToIncome = 0;
        if (divisions.Count > 0)
        {
            DivisionStats top = divisions[divisionKeys[0]];
            squadToIncome = Ratio(top.Median(top.SquadValue), top.Median(top.Income));
        }

        checks.Check(
            "a squad is worth a believable number of seasons of income",
            squadToIncome is >= 1.0 and <= 4.0,
            $"top division: {Fmt.N(squadToIncome, 1)}x (real football sits near 2x; far above it means clubs " +
            "hold assets they could never buy, which is what starves the transfer market)");
    }

    /// <summary>
    /// Nation and division wealth (R1-R3 of the realistic-club-economy spec): prints mean tier-1
    /// club revenue per nation (relative to England and in absolute terms) and the tier2/tier1,
    /// tier3/tier1 division ratios, then checks every acceptance band. A synthetic single-tier
    /// league is built per (nation, tier) from the SAME seed each time (only EconomicReputation and
    /// Division vary), so club generation and every match result are byte-identical across the
    /// sweep and only the finance multiplier moves - a precise read rather than a noisy one.
    /// </summary>
    private static void RunNationDivisionWealth(HarnessOptions opt, BalanceConfig cfg, CheckList checks)
    {
        List<NationProfile> atlas = NationDatabase.BuiltIn();
        NationProfile england = FindNation(atlas, "ENG");
        int englandMult = FinanceModel.NationMultiplierPermille(england.EconomicReputation, cfg.Finance);

        Console.WriteLine();
        string[] headline = { "ENG", "ESP", "GER", "ITA", "FRA", "POR", "NED", "BRA", "SCO", "SUI" };
        var tier1Revenue = new Dictionary<string, long>();
        foreach (string code in headline)
        {
            NationProfile nation = FindNation(atlas, code);
            long revenue = MeanClubRevenueOverASeason(opt.Seed, nation.EconomicReputation, tier: 1, cfg);
            tier1Revenue[code] = revenue;
            double ratio = FinanceModel.NationMultiplierPermille(nation.EconomicReputation, cfg.Finance) / (double)englandMult;
            Console.WriteLine(
                $"[balance-nation] {code} tier1: econ-rep {nation.EconomicReputation}, mean revenue {Fmt.Money(revenue)} " +
                $"({Fmt.Pct(ratio)} of England)");
        }

        checks.Check(
            "England tier-1 revenue lands in the real-football band",
            tier1Revenue["ENG"] is >= 340_000_000L and <= 460_000_000L,
            $"{Fmt.Money(tier1Revenue["ENG"])} (band 340M-460M)");

        CheckRelativeBand(checks, tier1Revenue, "ESP", 0.55, 0.75);
        CheckRelativeBand(checks, tier1Revenue, "GER", 0.55, 0.75);
        CheckRelativeBand(checks, tier1Revenue, "ITA", 0.42, 0.58);
        CheckRelativeBand(checks, tier1Revenue, "FRA", 0.38, 0.52);
        CheckRelativeBand(checks, tier1Revenue, "POR", 0.18, 0.32);
        CheckRelativeBand(checks, tier1Revenue, "NED", 0.18, 0.32);
        CheckRelativeBand(checks, tier1Revenue, "BRA", 0.18, 0.32);
        CheckRelativeBand(checks, tier1Revenue, "SCO", 0.08, 0.16);
        CheckRelativeBand(checks, tier1Revenue, "SUI", 0.08, 0.16);

        double econRep50Ratio = FinanceModel.NationMultiplierPermille(50, cfg.Finance) / (double)englandMult;
        checks.Check(
            "a nation with economic reputation 50 lands at 2-6% of England",
            econRep50Ratio is >= 0.02 and <= 0.06,
            $"{Fmt.Pct(econRep50Ratio)}");

        NationProfile italy = FindNation(atlas, "ITA");
        long italyTier1 = tier1Revenue["ITA"];
        long italyTier2 = MeanClubRevenueOverASeason(opt.Seed, italy.EconomicReputation, tier: 2, cfg);
        long italyTier3 = MeanClubRevenueOverASeason(opt.Seed, italy.EconomicReputation, tier: 3, cfg);
        Console.WriteLine(
            $"[balance-nation] ITA tier2: mean revenue {Fmt.Money(italyTier2)} ({Fmt.Pct(Ratio(italyTier2, italyTier1))} of tier1)");
        Console.WriteLine(
            $"[balance-nation] ITA tier3: mean revenue {Fmt.Money(italyTier3)} ({Fmt.Pct(Ratio(italyTier3, italyTier1))} of tier1)");

        checks.Check("Italy tier-1 revenue lands in the real-football band",
            italyTier1 is >= 170_000_000L and <= 230_000_000L, $"{Fmt.Money(italyTier1)} (band 170M-230M)");
        checks.Check("Italy tier-2 revenue lands in the real-football band",
            italyTier2 is >= 55_000_000L and <= 85_000_000L, $"{Fmt.Money(italyTier2)} (band 55M-85M)");
        checks.Check("Italy tier-3 revenue lands in the real-football band",
            italyTier3 is >= 18_000_000L and <= 30_000_000L, $"{Fmt.Money(italyTier3)} (band 18M-30M)");

        // R3: every nation with >=2 tiers keeps the SAME division ratio (it is nation-independent by
        // construction), checked on a couple more nations to confirm that structurally.
        foreach (string code in new[] { "ENG", "GER" })
        {
            NationProfile nation = FindNation(atlas, code);
            long t1 = tier1Revenue[code];
            long t2 = MeanClubRevenueOverASeason(opt.Seed, nation.EconomicReputation, tier: 2, cfg);
            double ratio2 = Ratio(t2, t1);
            Console.WriteLine($"[balance-nation] {code} tier2/tier1 = {Fmt.Pct(ratio2)}");
            checks.Check($"{code} tier2/tier1 revenue ratio is in band",
                ratio2 is >= 0.28 and <= 0.42, $"{Fmt.Pct(ratio2)} (band 28%-42%)");

            if (nation.Divisions.Count < 3) continue;
            long t3 = MeanClubRevenueOverASeason(opt.Seed, nation.EconomicReputation, tier: 3, cfg);
            double ratio3 = Ratio(t3, t1);
            Console.WriteLine($"[balance-nation] {code} tier3/tier1 = {Fmt.Pct(ratio3)}");
            checks.Check($"{code} tier3/tier1 revenue ratio is in band",
                ratio3 is >= 0.09 and <= 0.15, $"{Fmt.Pct(ratio3)} (band 9%-15%)");
        }
    }

    private static void CheckRelativeBand(CheckList checks, Dictionary<string, long> tier1Revenue, string code, double lo, double hi)
    {
        double ratio = Ratio(tier1Revenue[code], tier1Revenue["ENG"]);
        checks.Check($"{code} tier-1 revenue relative to England is in band",
            ratio >= lo && ratio <= hi, $"{Fmt.Pct(ratio)} (band {lo:P0}-{hi:P0})");
    }

    private static NationProfile FindNation(List<NationProfile> atlas, string code)
    {
        NationProfile? found = NationDatabase.Find(atlas, code);
        return found ?? throw new InvalidOperationException($"Nation '{code}' is missing from the built-in atlas.");
    }

    /// <summary>
    /// A full season's mean club revenue for a synthetic single-tier, 20-club league carrying the
    /// given nation wealth and division tier. Deliberately independent of <see cref="WorldLab"/>
    /// (no market/condition/difficulty) so the read is about revenue alone.
    /// </summary>
    private static long MeanClubRevenueOverASeason(ulong seed, int economicReputation, int tier, BalanceConfig cfg)
    {
        const int clubCount = 20;
        League league = new LeagueGenerator(new LeagueGenerationOptions
        {
            LeagueId = 9_000 + tier,
            LeagueName = "Nation Wealth Reading",
            Division = tier,
            ClubCount = clubCount,
            FirstClubId = 1,
            FirstPlayerId = 1
        }, cfg).Generate(new Pcg32(seed));
        league.EconomicReputation = economicReputation;

        var season = new Season { Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777)) };

        var fin = new FinanceProgressor(cfg);
        fin.SeedWorld(new List<League> { league });
        new ValuationProgressor(cfg).Reprice(new List<League> { league });

        var progressor = new SeasonProgressor(cfg);
        int days = 2 * (clubCount - 1) * cfg.Season.DaysBetweenRounds;
        for (int d = 0; d < days; d++)
        {
            List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, seed);
            if (outcomes.Count == 0) continue;
            fin.AccrueMatchday(new List<League> { league }, outcomes);
            fin.AccrueWeek(new List<League> { league }, season);
        }
        fin.AwardPrizeMoney(new List<League> { league }, season);

        long total = 0;
        foreach (Club c in league.Clubs) total += c.Finances.SeasonIncome;
        return total / league.Clubs.Count;
    }

    /// <summary>What one division looked like across every club-season of the run.</summary>
    private sealed class DivisionStats
    {
        public List<long> Income { get; } = new();
        public List<long> Wages { get; } = new();
        public List<long> SquadValue { get; } = new();
        public List<long> Budget { get; } = new();

        public long Median(List<long> values)
        {
            if (values.Count == 0) return 0;
            var copy = new List<long>(values);
            copy.Sort();
            return Fmt.Percentile(copy, 0.50);
        }
    }

    private static IEnumerable<Club> AllClubs(LabWorld world)
    {
        foreach (League league in world.Leagues)
            foreach (Club club in league.Clubs)
                yield return club;
    }

    private static IEnumerable<Player> AllPlayers(LabWorld world)
    {
        foreach (Club club in AllClubs(world))
            foreach (Player p in club.Squad.Players)
                yield return p;
    }

    /// <summary>Share of a sorted value list at or below a ceiling.</summary>
    private static double ShareUnder(IReadOnlyList<long> sortedValues, long ceiling)
    {
        if (sortedValues.Count == 0) return 0;
        int n = 0;
        foreach (long v in sortedValues) if (v <= ceiling) n++;
        return n / (double)sortedValues.Count;
    }

    private static double Ratio(long a, long b) => b == 0 ? 0 : a / (double)b;
}
