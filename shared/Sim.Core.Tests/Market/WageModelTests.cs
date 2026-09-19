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
    /// Wages set by the paying club (R7 of the realistic-club-economy spec):
    ///   - a player's wage is a function of his value AND the paying club's own wage structure
    ///     (nation x division x stature), not value alone;
    ///   - the SAME player earns >=2x at an Italy tier-1 club vs an Italy tier-2 club;
    ///   - moving to a richer club (higher nation wealth, higher division, higher stature) raises
    ///     the demanded wage;
    ///   - median wage/revenue ratio by tier: tier 1 55-70%, tier 2 72-88%, tier 3 78-92%;
    ///   - zero insolvent clubs over a simulated season (the board floor still applies).
    ///
    /// All of this is pure/deterministic (integer math, no RNG of its own) and the match engine
    /// never reads it, so the golden master is untouched - proven elsewhere by
    /// SimulationDeterminismTests staying green.
    /// </summary>
    [TestFixture]
    public class WageModelTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static FinanceBalance F => Cfg.Finance;

        // ============================================================ wage = f(value, club structure)

        [Test]
        public void WeeklyWage_RisesWithValue_AndWithClubWageStructure()
        {
            const int neutralResult = 1000;

            long lowValue = WageModel.WeeklyWage(5_000_000, neutralResult, 1000, F);
            long highValue = WageModel.WeeklyWage(50_000_000, neutralResult, 1000, F);
            Assert.That(highValue, Is.GreaterThan(lowValue), "A dearer player earns more, all else equal");

            long poorClub = WageModel.WeeklyWage(20_000_000, neutralResult, 500, F);
            long neutralClub = WageModel.WeeklyWage(20_000_000, neutralResult, 1000, F);
            long richClub = WageModel.WeeklyWage(20_000_000, neutralResult, 2000, F);
            Assert.That(neutralClub, Is.GreaterThan(poorClub), "A poorer club's wage structure pays the SAME player less");
            Assert.That(richClub, Is.GreaterThan(neutralClub), "A richer club's wage structure pays the SAME player more");

            // Exact formula check: value/divisor * result/1000 * structure/1000.
            long divisor = F.WageWeeklyValueDivisor;
            long expected = (20_000_000 / divisor) * neutralResult / 1000 * 1500 / 1000;
            Assert.That(WageModel.WeeklyWage(20_000_000, neutralResult, 1500, F), Is.EqualTo(expected));
        }

        [Test]
        public void ClubWageStructure_IsNationTimesDivisionTimesStatureTimesFacility()
        {
            int nation = FinanceModel.NationMultiplierPermille(70, F);
            int division = FinanceModel.WageDivisionMultiplierPermille(2, F);
            int stature = FinanceModel.WageStatureMultiplierPermille(80, F);
            int facility = FinanceModel.WageFacilityMultiplierPermille(3, F);
            long expected = (long)nation * division / 1000 * stature / 1000 * facility / 1000;

            Assert.That(FinanceModel.ClubWageStructurePermille(80, 2, 70, 3, F), Is.EqualTo(expected));
        }

        [Test]
        public void ClubWageStructure_RisesWithFacilityTier()
        {
            int poorFacility = FinanceModel.ClubWageStructurePermille(50, 1, 100, facilityTier: 1, F);
            int richFacility = FinanceModel.ClubWageStructurePermille(50, 1, 100, facilityTier: 5, F);
            Assert.That(richFacility, Is.GreaterThan(poorFacility),
                "a bigger stadium must mean a heavier wage structure, all else equal");
        }

        // ============================================================ same player, richer club

        [Test]
        public void SamePlayer_AtItalyTier1Club_EarnsAtLeastDoubleOfTier2Club()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            Player player = MakePlayer(overall: 75, potential: 75, age: 27);
            Club tier1Club = new Club { Stature = 50 };
            Club tier2Club = new Club { Stature = 50 };

            long tier1Wage = FinanceModel.DemandedWeeklyWage(player, tier1Club, 1000, leagueLevel: 1, italyRep, Cfg);
            long tier2Wage = FinanceModel.DemandedWeeklyWage(player, tier2Club, 1000, leagueLevel: 2, italyRep, Cfg);

            TestContext.Out.WriteLine($"[wage-tier] Italy tier1 {tier1Wage:N0} vs tier2 {tier2Wage:N0} (ratio {tier1Wage / (double)tier2Wage:F2}x)");

            Assert.That(tier1Wage, Is.GreaterThanOrEqualTo(tier2Wage * 2),
                "The same player must earn at least double at an Italy tier-1 club vs an Italy tier-2 club");
        }

        // ============================================================ richer club -> higher demand

        [Test]
        public void MovingToARicherClub_RaisesTheDemandedWage()
        {
            Player player = MakePlayer(overall: 72, potential: 72, age: 26);

            Club poorClub = new Club { Stature = 10 };
            Club richClub = new Club { Stature = 90 };

            // Richer stature, same nation/division.
            long poorStatureWage = FinanceModel.DemandedWeeklyWage(player, poorClub, 1000, leagueLevel: 1, economicReputation: 100, Cfg);
            long richStatureWage = FinanceModel.DemandedWeeklyWage(player, richClub, 1000, leagueLevel: 1, economicReputation: 100, Cfg);
            Assert.That(richStatureWage, Is.GreaterThan(poorStatureWage), "A higher-stature club must demand a higher wage for the same player");

            // Richer division (tier 1 vs tier 3), same club.
            Club club = new Club { Stature = 50 };
            long lowerDivisionWage = FinanceModel.DemandedWeeklyWage(player, club, 1000, leagueLevel: 3, economicReputation: 100, Cfg);
            long topDivisionWage = FinanceModel.DemandedWeeklyWage(player, club, 1000, leagueLevel: 1, economicReputation: 100, Cfg);
            Assert.That(topDivisionWage, Is.GreaterThan(lowerDivisionWage), "A top-division club must demand a higher wage than a lower-division one");

            // Richer nation (England-equivalent vs a poor nation), same tier/stature.
            long poorNationWage = FinanceModel.DemandedWeeklyWage(player, club, 1000, leagueLevel: 1, economicReputation: 20, Cfg);
            long richNationWage = FinanceModel.DemandedWeeklyWage(player, club, 1000, leagueLevel: 1, economicReputation: 100, Cfg);
            Assert.That(richNationWage, Is.GreaterThan(poorNationWage), "A wealthier nation's club must demand a higher wage than a poorer nation's");
        }

        // ============================================================ median wage/revenue by tier

        /// <summary>
        /// 24 distinct 16-club Italy-tier seeds. The wage bill charged is the SUM of the squad's
        /// structure-aware per-player <see cref="FinanceModel.DemandedWeeklyWage"/> — see
        /// <see cref="FinanceModel.WeeklyWageBill"/> — no separate revenue-share target of its own;
        /// the median landing inside each tier's band is instead a property of the CALIBRATION
        /// (<see cref="FinanceModel.ClubWageStructurePermille"/>'s nation/division/stature/facility
        /// factors and the ability-value constants in <see cref="Config.FinanceBalance"/>), so it
        /// must also hold on seeds outside this list — a wider, unfiltered self-probe run while
        /// calibrating this test is reported by the task that added it.
        /// </summary>
        private static readonly ulong[] TierSeeds =
        {
            2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000, 11000, 12000, 13000,
            14000, 15000, 16000, 17000, 18000, 19000, 20000, 21000, 22000, 23000, 24000, 25000
        };

        [Test]
        public void MedianWageRevenueRatio_PerTier_MatchesRealFootballBand()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            AssertTierBand(italyRep, tier: 1, clubCount: 16, lo: 0.55, hi: 0.70);
            AssertTierBand(italyRep, tier: 2, clubCount: 16, lo: 0.72, hi: 0.88);
            AssertTierBand(italyRep, tier: 3, clubCount: 16, lo: 0.78, hi: 0.92);
        }

        private static void AssertTierBand(int economicReputation, int tier, int clubCount, double lo, double hi)
        {
            foreach (ulong seed in TierSeeds)
            {
                double median = MedianWageRevenueRatio(economicReputation, tier, clubCount, seed);
                TestContext.Out.WriteLine($"[wage-share] tier{tier} seed {seed}: median wage/revenue = {median:P1}");
                Assert.That(median, Is.InRange(lo, hi),
                    $"tier {tier} seed {seed}: median club wage/revenue must be {lo:P0}-{hi:P0}, got {median:P1}");
            }
        }

        /// <summary>
        /// Drives a full season through the REAL <see cref="FinanceProgressor"/> path and returns the
        /// MEDIAN across clubs of each club's SeasonWageExpense/SeasonIncome.
        /// </summary>
        private static double MedianWageRevenueRatio(int economicReputation, int tier, int clubCount, ulong seed)
        {
            var league = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 7_000 + tier,
                LeagueName = "Wage Test League",
                Division = tier,
                ClubCount = clubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            }, Cfg).Generate(new Pcg32(seed));
            league.EconomicReputation = economicReputation;

            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777))
            };

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league });

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (clubCount - 1) * Cfg.Season.DaysBetweenRounds;
            for (int d = 0; d < days; d++)
            {
                List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, seed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);

            List<double> ratios = league.Clubs
                .Where(c => c.Finances.SeasonIncome > 0)
                .Select(c => (double)c.Finances.SeasonExpense / c.Finances.SeasonIncome)
                .OrderBy(r => r)
                .ToList();

            int n = ratios.Count;
            return n % 2 == 1 ? ratios[n / 2] : (ratios[n / 2 - 1] + ratios[n / 2]) / 2.0;
        }

        // ============================================================ bill = sum of per-player demands

        [Test]
        public void WeeklyWageBill_EqualsSum_OfDemandedWeeklyWage_OverTheSquad()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            Club club = new Club { Stature = 62 };
            for (int i = 0; i < 18; i++)
                club.Squad.Players.Add(MakePlayer(overall: 40 + i * 2, potential: 40 + i * 2, age: 25));

            const int resultPermille = 1050;
            const int leagueLevel = 2;

            long bill = FinanceModel.WeeklyWageBill(club, resultPermille, leagueLevel, italyRep, Cfg);
            long summed = club.Squad.Players.Sum(p => FinanceModel.DemandedWeeklyWage(p, club, resultPermille, leagueLevel, italyRep, Cfg));

            Assert.That(bill, Is.EqualTo(summed),
                "the club's aggregate wage bill must be exactly the sum of what each player individually demands from it - one formula, two callers, no separate target-share anchor");
        }

        [Test]
        public void WeeklyWageBill_DoublingSquadAbilityValue_RoughlyDoublesTheBill()
        {
            // Attempt-3's rejected design anchored the bill to a share of revenue with a clamped
            // squad-value factor (0.85-1.15x), so doubling squad value barely moved it. The current
            // bill is a plain SUM of per-player demands (see WeeklyWageBill), which is linear in each
            // player's wage-side ability value by construction - this proves there is no such clamp.
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;
            Club club = new Club { Stature = 55 };

            // overall 30 -> ability value ~88M; overall 64 -> ~176M (almost exactly double), see
            // FinanceModel.WageAbilityValue: unitPerRating*(overall-floor) + flat living wage.
            const int baselineOverall = 30;
            const int doubledOverall = 64;
            const int squadSize = 20;
            const int leagueLevel = 1;

            var baselineSquad = new List<Player>();
            var doubledSquad = new List<Player>();
            for (int i = 0; i < squadSize; i++)
            {
                baselineSquad.Add(MakePlayer(overall: baselineOverall, potential: baselineOverall, age: 26));
                doubledSquad.Add(MakePlayer(overall: doubledOverall, potential: doubledOverall, age: 26));
            }

            club.Squad.Players.Clear();
            club.Squad.Players.AddRange(baselineSquad);
            long baselineBill = FinanceModel.WeeklyWageBill(club, 1000, leagueLevel, italyRep, Cfg);

            club.Squad.Players.Clear();
            club.Squad.Players.AddRange(doubledSquad);
            long doubledBill = FinanceModel.WeeklyWageBill(club, 1000, leagueLevel, italyRep, Cfg);

            double ratio = (double)doubledBill / baselineBill;
            TestContext.Out.WriteLine($"[bill-scaling] baseline={baselineBill:N0} doubled={doubledBill:N0} ratio={ratio:F2}");
            Assert.That(ratio, Is.InRange(1.8, 2.2), "roughly doubling the squad's wage-side ability value must roughly double the bill, not be clamped/anchored away from it");
        }

        // ============================================================ zero insolvency

        [Test]
        public void ZeroInsolventClubs_OverASimulatedSeason_AcrossEveryTier()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = NationDatabase.Find(atlas, "ITA")!.EconomicReputation;

            for (int tier = 1; tier <= 3; tier++)
            {
                var league = new LeagueGenerator(new LeagueGenerationOptions
                {
                    LeagueId = 7_100 + tier,
                    LeagueName = "Insolvency Test League",
                    Division = tier,
                    ClubCount = 16,
                    FirstClubId = 1,
                    FirstPlayerId = 1
                }, Cfg).Generate(new Pcg32(9_500_000 + (ulong)tier));
                league.EconomicReputation = italyRep;

                var season = new Season
                {
                    Fixtures = new FixtureGenerator().Generate(league, new Pcg32(9_500_000 + (ulong)tier, 777))
                };

                var fin = new FinanceProgressor(Cfg);
                fin.SeedWorld(new[] { league });
                new ValuationProgressor(Cfg).Reprice(new[] { league });

                var progressor = new SeasonProgressor(Cfg);
                int days = 2 * (league.Clubs.Count - 1) * Cfg.Season.DaysBetweenRounds;
                for (int d = 0; d < days; d++)
                {
                    List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, 9_500_000 + (ulong)tier);
                    if (outcomes.Count == 0) continue;
                    fin.AccrueMatchday(new[] { league }, outcomes);
                    fin.AccrueWeek(new[] { league }, season);

                    foreach (Club c in league.Clubs)
                        Assert.That(c.Finances.Balance, Is.GreaterThanOrEqualTo(F.MinBalance),
                            $"tier {tier}, club {c.Id} fell below the operating floor mid-season");
                }
                fin.AwardPrizeMoney(new[] { league }, season);

                int insolvent = league.Clubs.Count(c => c.Finances.Balance < F.MinBalance);
                Assert.That(insolvent, Is.Zero, $"tier {tier}: no club may end the season insolvent");
            }
        }

        // ============================================================ helpers

        private static Player MakePlayer(int overall, int potential, int age)
        {
            var player = new Player
            {
                Age = age,
                Attributes = new PlayerAttributes()
            };
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
                player.Attributes[i] = overall;
            player.Development.Potential = potential;
            player.Contract.SeasonsRemaining = Cfg.Market.ContractFullSeasons;
            player.Condition.Form = 50;
            return player;
        }
    }
}
