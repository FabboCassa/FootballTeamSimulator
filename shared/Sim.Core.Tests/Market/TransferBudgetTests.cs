using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Task: realistic transfer budgets (R9 of the realistic-club-economy spec):
    ///   - SeasonTransferBudget = cash share + board grant; the grant reuses nation (R1) and club
    ///     stature (R4) the same way revenue does, times its OWN steeper division curve (see
    ///     FinanceModel.BoardGrantDivisionMultiplierPermille's remarks for why);
    ///   - median Italy tier-2 season budget is &lt;= 25% of median Italy tier-1 budget;
    ///   - over a simulated season (two AI transfer windows) no tier-2 club signs a player valued
    ///     above the 90th percentile of its own nation's tier-1 player values.
    ///
    /// Pure/deterministic (integer math, no new RNG streams) and off the match-engine path, so the
    /// golden master is untouched — proven elsewhere by SimulationDeterminismTests staying green.
    /// </summary>
    [TestFixture]
    public class TransferBudgetTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static FinanceBalance F => Cfg.Finance;
        private const int ClubCount = 16;

        // ============================================================ formula: cash share + scaled grant

        [Test]
        public void SeasonTransferBudget_EqualsCashShare_PlusNationDivisionStatureScaledGrant()
        {
            Club club = new Club { Stature = 74, Finances = { Balance = 120_000_000 } };
            const int leagueLevel = 2;
            const int economicReputation = 63;

            long cashShare = club.Finances.Balance * F.TransferBudgetCashPercent / 100;
            long grant = F.BoardGrantTopFlight * FinanceModel.NationMultiplierPermille(economicReputation, F) / 1000;
            grant = grant * FinanceModel.BoardGrantDivisionMultiplierPermille(leagueLevel, F) / 1000;
            grant = grant * FinanceModel.StatureMultiplierPermille(club.Stature, F) / 1000;
            long expected = cashShare + grant;

            long actual = FinanceModel.SeasonTransferBudget(club, leagueLevel, economicReputation, Cfg);

            Assert.That(actual, Is.EqualTo(expected),
                "the budget must be exactly cash share + a grant scaled by nation x division x stature");
        }

        [Test]
        public void SeasonTransferBudget_GrantOnly_RisesWithNationAndWithStature()
        {
            // Isolate the grant: zero cash reserves.
            Club poorStature = new Club { Stature = 5 };
            Club richStature = new Club { Stature = 95 };
            long poorStatureBudget = FinanceModel.SeasonTransferBudget(poorStature, leagueLevel: 1, economicReputation: 100, Cfg);
            long richStatureBudget = FinanceModel.SeasonTransferBudget(richStature, leagueLevel: 1, economicReputation: 100, Cfg);
            Assert.That(richStatureBudget, Is.GreaterThan(poorStatureBudget),
                "a higher-stature club must get a bigger board grant, all else equal");

            Club club = new Club { Stature = 50 };
            long poorNationBudget = FinanceModel.SeasonTransferBudget(club, leagueLevel: 1, economicReputation: 20, Cfg);
            long richNationBudget = FinanceModel.SeasonTransferBudget(club, leagueLevel: 1, economicReputation: 100, Cfg);
            Assert.That(richNationBudget, Is.GreaterThan(poorNationBudget),
                "a wealthier nation's club must get a bigger board grant, all else equal");

            long tier1Budget = FinanceModel.SeasonTransferBudget(club, leagueLevel: 1, economicReputation: 100, Cfg);
            long tier3Budget = FinanceModel.SeasonTransferBudget(club, leagueLevel: 3, economicReputation: 100, Cfg);
            Assert.That(tier1Budget, Is.GreaterThan(tier3Budget),
                "a top-flight club must get a bigger board grant than a lower-division one, all else equal");
        }

        // ============================================================ median tier-2 <= 25% of tier-1 (Italy)

        /// <summary>
        /// 24 distinct 16-club-per-tier Italy seeds. The median ratio landing at/under the R9 band is a
        /// property of the CALIBRATION (BoardGrantTopFlight, TransferBudgetCashPercent and the
        /// BoardGrantDivisionWealthPermille table), not of this method, so it must also hold on seeds
        /// outside this committed list — see the self-probe below.
        /// </summary>
        private static readonly ulong[] BudgetSeeds =
        {
            31000, 32000, 33000, 34000, 35000, 36000, 37000, 38000, 39000, 40000, 41000, 42000,
            43000, 44000, 45000, 46000, 47000, 48000, 49000, 50000, 51000, 52000, 53000, 54000
        };

        [Test]
        public void MedianTier2Budget_IsAtMostAQuarter_OfMedianTier1Budget_ForItaly()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            foreach (ulong seed in BudgetSeeds)
            {
                (double tier1Median, double tier2Median) = MedianBudgetsByTier(italyRep, seed);
                double ratio = tier2Median / tier1Median;
                TestContext.Out.WriteLine(
                    $"[budget-ratio] seed {seed}: tier1 median {tier1Median:N0}, tier2 median {tier2Median:N0}, ratio {ratio:P1}");

                Assert.That(tier2Median, Is.LessThanOrEqualTo(tier1Median * 0.25),
                    $"seed {seed}: median tier-2 Italy budget must be <=25% of median tier-1 Italy budget, got {ratio:P1}");
            }
        }

        [Test]
        public void MedianTier2Budget_SelfProbe_FreshSeedsOutsideCommittedList()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            int misses = 0;
            const int probes = 50;
            for (int i = 0; i < probes; i++)
            {
                ulong seed = 900_000 + (ulong)i * 137; // disjoint from BudgetSeeds by construction
                (double tier1Median, double tier2Median) = MedianBudgetsByTier(italyRep, seed);
                double ratio = tier2Median / tier1Median;
                bool miss = tier2Median > tier1Median * 0.25;
                if (miss) misses++;
                TestContext.Out.WriteLine($"[budget-ratio-probe] seed {seed}: ratio {ratio:P1}{(miss ? " MISS" : string.Empty)}");
            }

            TestContext.Out.WriteLine($"[budget-ratio-probe] miss rate {misses}/{probes} on fresh seeds outside the committed list");
            Assert.That(misses, Is.Zero, $"fresh-seed self-probe must not miss the R9 median band (got {misses}/{probes})");
        }

        private static (double tier1Median, double tier2Median) MedianBudgetsByTier(int economicReputation, ulong seed)
        {
            League tier1 = BuildAndSimulateSeason(1, economicReputation, seed);
            League tier2 = BuildAndSimulateSeason(2, economicReputation, seed);
            return (Median(tier1.Clubs.Select(c => (double)c.TransferBudget)), Median(tier2.Clubs.Select(c => (double)c.TransferBudget)));
        }

        /// <summary>Generates a tier's league, runs a full season through the REAL FinanceProgressor path, then seeds the transfer budgets exactly like the live path does at season start.</summary>
        private static League BuildAndSimulateSeason(int tier, int economicReputation, ulong seed)
        {
            var league = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 8_000 + tier,
                LeagueName = "Budget Test League",
                Division = tier,
                ClubCount = ClubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            }, Cfg).Generate(new Pcg32(seed, (ulong)tier));
            league.EconomicReputation = economicReputation;

            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777 + (ulong)tier))
            };

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league });

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (ClubCount - 1) * Cfg.Season.DaysBetweenRounds;
            for (int d = 0; d < days; d++)
            {
                List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, seed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);
            fin.SeedTransferBudgets(new[] { league });
            return league;
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        // ============================================================ no tier-2 club out-signs the tier-1 top decile

        private static readonly ulong[] MarketSeeds =
        {
            61000, 62000, 63000, 64000, 65000, 66000, 67000, 68000, 69000, 70000, 71000, 72000,
            73000, 74000, 75000, 76000, 77000, 78000, 79000, 80000,
            // Regression: attempt 1's independent probe found a real miss here (no structural cap in
            // TransferMarket, just a budget that happened to exceed the nation's tier-1 p90 value) —
            // see fail7-1.md. Kept committed so the guard added for it never regresses silently.
            3000802
        };

        [Test]
        public void SimulatedSeason_NoTier2Signing_ExceedsTheNinetiethPercentileOfTier1NationValues()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            foreach (ulong seed in MarketSeeds)
            {
                int violations = SimulateSeasonAndCountTier2ViolationsOverP90(italyRep, seed, out int signings, out long p90);
                TestContext.Out.WriteLine(
                    $"[budget-cap] seed {seed}: tier1 p90 value {p90:N0}, {signings} tier-2 signings, {violations} over the cap");
                Assert.That(violations, Is.Zero,
                    $"seed {seed}: no tier-2 club may sign a player valued above its nation's tier-1 90th percentile");
            }
        }

        [Test]
        public void SimulatedSeason_SelfProbe_FreshSeedsOutsideCommittedList()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            int missSeeds = 0;
            const int probes = 50;
            for (int i = 0; i < probes; i++)
            {
                ulong seed = 950_000 + (ulong)i * 211; // disjoint from MarketSeeds by construction
                int violations = SimulateSeasonAndCountTier2ViolationsOverP90(italyRep, seed, out int signings, out long p90);
                bool miss = violations > 0;
                if (miss) missSeeds++;
                TestContext.Out.WriteLine(
                    $"[budget-cap-probe] seed {seed}: tier1 p90 {p90:N0}, {signings} tier-2 signings, {violations} over the cap{(miss ? " MISS" : string.Empty)}");
            }

            TestContext.Out.WriteLine($"[budget-cap-probe] miss rate {missSeeds}/{probes} on fresh seeds outside the committed list");
            Assert.That(missSeeds, Is.Zero, $"fresh-seed self-probe must not miss the R9 signing cap (got {missSeeds}/{probes})");
        }

        /// <summary>
        /// Builds an Italy tier-1 + tier-2 world, seeds finances/budgets, then runs a full season
        /// through the REAL <see cref="TransferMarket"/> (start + mid-season windows, exactly the live
        /// cadence). Returns the count of tier-2 buyer signings whose player value (captured once, at
        /// season start, before any window can change ability/age) exceeds the 90th percentile of the
        /// tier-1 league's own player values at that same point.
        /// </summary>
        private static int SimulateSeasonAndCountTier2ViolationsOverP90(int economicReputation, ulong worldSeed, out int tier2Signings, out long p90)
        {
            League tier1 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 9_001, LeagueName = "Market Test D1", Division = 1, ClubCount = ClubCount,
                FirstClubId = 1, FirstPlayerId = 1
            }, Cfg).Generate(new Pcg32(worldSeed, 1));
            tier1.EconomicReputation = economicReputation;

            League tier2 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 9_002, LeagueName = "Market Test D2", Division = 2, ClubCount = ClubCount,
                FirstClubId = 1_000, FirstPlayerId = 100_000
            }, Cfg).Generate(new Pcg32(worldSeed, 2));
            tier2.EconomicReputation = economicReputation;

            var leagues = new List<League> { tier1, tier2 };
            var tier2ClubIds = new HashSet<int>(tier2.Clubs.Select(c => c.Id));

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(leagues);
            new ValuationProgressor(Cfg).Reprice(leagues);

            // Capture player values at season start, once — the fixed reference the cap is judged against.
            long[] tier1Values = tier1.Clubs.SelectMany(c => c.Squad.Players).Select(p => p.MarketValue).OrderBy(v => v).ToArray();
            p90 = tier1Values[(tier1Values.Length - 1) * 90 / 100];
            var valueAtStart = new Dictionary<int, long>();
            foreach (Club c in tier1.Clubs.Concat(tier2.Clubs))
                foreach (Player p in c.Squad.Players)
                    valueAtStart[p.Id] = p.MarketValue;

            var season1 = new Season { Fixtures = new FixtureGenerator().Generate(tier1, new Pcg32(worldSeed, 777)) };
            var season2 = new Season { Fixtures = new FixtureGenerator().Generate(tier2, new Pcg32(worldSeed, 778)) };

            fin.SeedTransferBudgets(leagues);
            var market = new TransferMarket(Cfg);
            var allRecords = new List<TransferRecord>();
            allRecords.AddRange(market.RunWindow(leagues, worldSeed, windowIndex: 0));

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (ClubCount - 1) * Cfg.Season.DaysBetweenRounds;
            int midSeasonDay = days / 2;
            for (int d = 0; d < days; d++)
            {
                List<MatchOutcome> outcomes1 = progressor.AdvanceDay(tier1, season1, worldSeed);
                if (outcomes1.Count > 0)
                {
                    fin.AccrueMatchday(new[] { tier1 }, outcomes1);
                    fin.AccrueWeek(new[] { tier1 }, season1);
                }
                List<MatchOutcome> outcomes2 = progressor.AdvanceDay(tier2, season2, worldSeed);
                if (outcomes2.Count > 0)
                {
                    fin.AccrueMatchday(new[] { tier2 }, outcomes2);
                    fin.AccrueWeek(new[] { tier2 }, season2);
                }

                if (d == midSeasonDay)
                    allRecords.AddRange(market.RunWindow(leagues, worldSeed, windowIndex: 1));
            }

            long cap = p90;
            tier2Signings = allRecords.Count(r => tier2ClubIds.Contains(r.ToClubId));
            return allRecords.Count(r => tier2ClubIds.Contains(r.ToClubId)
                                          && valueAtStart.TryGetValue(r.PlayerId, out long v)
                                          && v > cap);
        }
    }
}
