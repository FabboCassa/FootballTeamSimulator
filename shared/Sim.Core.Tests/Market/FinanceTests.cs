using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Task 5.5 acceptance: club facilities &amp; finances.
    ///   - THE ✅ (dev speed): upgrading the training ground measurably speeds development;
    ///   - finances balance correctly over a season (income and wages are the same order of
    ///     magnitude, every club stays solvent) — a printed read-out to judge the calibration;
    ///   - bankruptcy is impossible (the board floors the balance) yet overspending shrinks the
    ///     transfer kitty so a skint club can't keep signing;
    ///   - the facility tier→effect mappings, wage drivers and whole-world progressor are pure
    ///     and deterministic.
    ///
    /// All of this is opt-in (the match engine never calls it), so the golden master and the
    /// existing tests are unaffected — proven elsewhere by those tests staying green.
    /// </summary>
    [TestFixture]
    public class FinanceTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static FinanceBalance F => Cfg.Finance;
        private const ulong WorldSeed = 5_005_005;
        private const int Days = 7 * 38; // full double round-robin for 20 clubs

        // ============================================================ THE ✅: training → dev speed

        [Test]
        public void TrainingGround_Upgrade_SpeedsDevelopment()
        {
            // Two identical young squads (same seed == same save), lots of headroom to grow.
            Club basic = MakeYoungSquad(seed: 5_500_001, clubIndex: 5, startAge: 18, headroom: 25);
            Club elite = MakeYoungSquad(seed: 5_500_001, clubIndex: 5, startAge: 18, headroom: 25);

            int start = SkillTotal(basic);
            Assert.That(SkillTotal(elite), Is.EqualTo(start), "Same seed must start identical");

            // Tier 1 = the neutral facility level (4.4 baseline); a maxed training ground lifts it.
            int basicLevel = FacilityEffects.TrainingFacilityLevel(1, Cfg);
            int eliteLevel = FacilityEffects.TrainingFacilityLevel(F.MaxFacilityTier, Cfg);
            Assert.That(basicLevel, Is.EqualTo(Cfg.Development.FacilityNeutralLevel),
                "Training tier 1 must map to the development model's neutral facility level (4.4 baseline)");
            Assert.That(eliteLevel, Is.GreaterThan(basicLevel), "A maxed training ground must raise the facility level");

            var basicCtx = new DevelopmentContext(playingSharePercent: 100, basicLevel, Cfg.Development.PerformanceNeutralRating);
            var eliteCtx = new DevelopmentContext(playingSharePercent: 100, eliteLevel, Cfg.Development.PerformanceNeutralRating);

            // Same weekly RNG stream for both → only the training facility differs.
            DevelopSeason(basic, basicCtx, baseSeed: 4242);
            DevelopSeason(elite, eliteCtx, baseSeed: 4242);

            int basicTotal = SkillTotal(basic);
            int eliteTotal = SkillTotal(elite);
            TestContext.Out.WriteLine(
                $"[finance-training] facility level {basicLevel} -> +{basicTotal - start} skill pts vs " +
                $"level {eliteLevel} -> +{eliteTotal - start} (one season, identical squads & RNG)");

            Assert.That(basicTotal, Is.GreaterThan(start), "Even a basic training ground still develops youth");
            Assert.That(eliteTotal, Is.GreaterThan(basicTotal),
                "Upgrading the training ground must measurably speed development (the ✅)");
        }

        // ============================================================ finances balance over a season

        [Test]
        public void Finances_BalanceCorrectlyOverASeason()
        {
            (League league, Season season) = NewWorld();
            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league }); // wages read MarketValue

            var p = new SeasonProgressor();
            for (int d = 0; d < Days; d++)
            {
                List<MatchOutcome> outcomes = p.AdvanceDay(league, season, WorldSeed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes); // gate to home clubs
                fin.AccrueWeek(new[] { league }, season);       // sponsors + wages (≈ one week / matchday)
            }
            fin.AwardPrizeMoney(new[] { league }, season);

            long totalIncome = 0, totalWages = 0;
            int insolvent = 0;
            foreach (Club c in league.Clubs)
            {
                totalIncome += c.Finances.SeasonIncome;
                totalWages += c.Finances.SeasonExpense;
                if (c.Finances.Balance < F.MinBalance) insolvent++;
            }

            // Sample read-out: champion, a mid club, the bottom club.
            List<LeagueTableRow> table = LeagueTable.Compute(league, season, Cfg.Season);
            PrintClub("champion", league, table[0].ClubId);
            PrintClub("mid", league, table[table.Count / 2].ClubId);
            PrintClub("bottom", league, table[table.Count - 1].ClubId);

            double wageShare = (double)totalWages / totalIncome;
            TestContext.Out.WriteLine(
                $"[finance-season] league totals: income {Money(totalIncome)}, wages {Money(totalWages)} " +
                $"({wageShare * 100:F0}% of income), insolvent clubs {insolvent}/{league.Clubs.Count}");

            Assert.That(insolvent, Is.Zero, "No club may end the season below the operating floor (bankruptcy is impossible)");
            Assert.That(totalIncome, Is.GreaterThan(0), "Clubs must earn income across a season");
            Assert.That(totalWages, Is.GreaterThan(0), "Clubs must pay wages across a season");
            Assert.That(wageShare, Is.InRange(0.45, 0.85),
                "League wages should sit in the realistic ~63% band (real Premier League average), leaving clubs profitable enough to do transfers");
        }

        // ============================================================ bankruptcy impossible

        [Test]
        public void Bankruptcy_IsImpossible_EvenWithNoIncomeAndHeavyWages()
        {
            (League league, Season season) = NewWorld();
            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league });

            // Zero every club's cash, then charge many weeks of wages with NO matches (no gate).
            foreach (Club c in league.Clubs) c.Finances.Balance = 0;

            for (int week = 0; week < 60; week++)
            {
                fin.AccrueWeek(new[] { league }, season);
                foreach (Club c in league.Clubs)
                    Assert.That(c.Finances.Balance, Is.GreaterThanOrEqualTo(F.MinBalance),
                        $"Club {c.Id} fell below the operating floor — the board must absorb the shortfall");
            }
        }

        // ============================================================ overspending blocks signings

        [Test]
        public void Overspending_ShrinksTheTransferBudget()
        {
            (League league, _) = NewWorld();
            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });

            Club club = league.Clubs[0];
            long flush = FinanceModel.SeasonTransferBudget(club, league.Division, Cfg);

            // Simulate a club that has blown its reserves on transfers.
            club.Finances.Balance = 0;
            long broke = FinanceModel.SeasonTransferBudget(club, league.Division, Cfg);

            TestContext.Out.WriteLine(
                $"[finance-budget] cash-flush budget {Money(flush)} vs spent-out budget {Money(broke)} (board grant only)");

            Assert.That(broke, Is.LessThan(flush),
                "A club that has spent its cash must get a smaller transfer kitty (overspending blocks signings)");
            Assert.That(broke, Is.GreaterThanOrEqualTo(F.MinTransferBudget),
                "But the board floor keeps a minimal kitty so the club isn't frozen out entirely");
        }

        // ============================================================ facility tier → effect mappings

        [Test]
        public void FacilityEffects_MapTiers_MonotonicallyAndBounded()
        {
            // Training level: tier 1 neutral, strictly rising, never above the skill ceiling.
            Assert.That(FacilityEffects.TrainingFacilityLevel(1, Cfg), Is.EqualTo(Cfg.Development.FacilityNeutralLevel));
            Assert.That(FacilityEffects.TrainingFacilityLevel(3, Cfg),
                Is.GreaterThan(FacilityEffects.TrainingFacilityLevel(2, Cfg)));
            Assert.That(FacilityEffects.TrainingFacilityLevel(F.MaxFacilityTier, Cfg), Is.LessThanOrEqualTo(100));

            // Stadium capacity rises with tier.
            Assert.That(FacilityEffects.StadiumCapacity(1, F), Is.EqualTo(F.StadiumBaseCapacity));
            Assert.That(FacilityEffects.StadiumCapacity(5, F),
                Is.GreaterThan(FacilityEffects.StadiumCapacity(4, F)));

            // Scout level = tier, clamped to the scouting max; tier 1 = base.
            Assert.That(FacilityEffects.ScoutLevel(1, Cfg), Is.EqualTo(1));
            Assert.That(FacilityEffects.ScoutLevel(99, Cfg), Is.EqualTo(Cfg.Scouting.MaxScoutLevel));

            // Academy rating rises and is bounded.
            Assert.That(FacilityEffects.AcademyRating(1, F), Is.EqualTo(F.AcademyRatingBase));
            Assert.That(FacilityEffects.AcademyRating(5, F), Is.GreaterThan(FacilityEffects.AcademyRating(1, F)));

            // Upgrade cost rises with tier and is zero at the cap.
            Assert.That(FacilityEffects.UpgradeCost(2, F), Is.GreaterThan(FacilityEffects.UpgradeCost(1, F)));
            Assert.That(FacilityEffects.UpgradeCost(F.MaxFacilityTier, F), Is.Zero);

            // Bigger clubs start with bigger grounds.
            Assert.That(FacilityEffects.SuggestedStadiumTier(70, F),
                Is.GreaterThan(FacilityEffects.SuggestedStadiumTier(52, F)));
        }

        // ============================================================ wage drivers

        [Test]
        public void Wages_RiseWithValue_AndWithSeasonResults()
        {
            long lowValueWage = WageModel.WeeklyWage(5_000_000, 1000, F);
            long highValueWage = WageModel.WeeklyWage(50_000_000, 1000, F);
            Assert.That(highValueWage, Is.GreaterThan(lowValueWage), "A dearer player earns more");

            long midTable = WageModel.WeeklyWage(20_000_000, 1000, F);
            long champions = WageModel.WeeklyWage(20_000_000, F.WageResultCeilPermille, F);
            long strugglers = WageModel.WeeklyWage(20_000_000, F.WageResultFloorPermille, F);
            Assert.That(champions, Is.GreaterThan(midTable), "A winning season lifts the wage bill (bonuses/renewals)");
            Assert.That(strugglers, Is.LessThan(midTable), "A poor season trims the wage bill");

            // The standings → result-permille mapping: leader earns the ceiling, last the floor.
            Assert.That(FinanceModel.ResultPermilleForPosition(1, 20, Cfg), Is.EqualTo(F.WageResultCeilPermille));
            Assert.That(FinanceModel.ResultPermilleForPosition(20, 20, Cfg), Is.EqualTo(F.WageResultFloorPermille));
            Assert.That(FinanceModel.ResultPermilleForPosition(1, 20, Cfg),
                Is.GreaterThan(FinanceModel.ResultPermilleForPosition(20, 20, Cfg)));
        }

        // ============================================================ deterministic, whole-world

        [Test]
        public void Finance_IsDeterministic_WholeWorld()
        {
            (League a, Season seasonA) = NewWorld();
            (League b, Season seasonB) = NewWorld();

            RunFinanceSeason(a, seasonA);
            RunFinanceSeason(b, seasonB);

            for (int c = 0; c < a.Clubs.Count; c++)
            {
                Assert.That(b.Clubs[c].Finances.Balance, Is.EqualTo(a.Clubs[c].Finances.Balance),
                    $"Club {a.Clubs[c].Id} ended with a different balance across identical runs (non-deterministic finances)");
                Assert.That(b.Clubs[c].TransferBudget, Is.EqualTo(a.Clubs[c].TransferBudget),
                    $"Club {a.Clubs[c].Id} got a different transfer budget across identical runs");
            }
        }

        // ============================================================ helpers

        private static (League, Season) NewWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            return (league, season);
        }

        private static void RunFinanceSeason(League league, Season season)
        {
            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league });

            var p = new SeasonProgressor();
            for (int d = 0; d < Days; d++)
            {
                List<MatchOutcome> outcomes = p.AdvanceDay(league, season, WorldSeed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);
            fin.SeedTransferBudgets(new[] { league });
        }

        private static Club MakeYoungSquad(int seed, int clubIndex, int startAge, int headroom)
        {
            Club club = new LeagueGenerator().Generate(new Pcg32((ulong)seed)).Clubs[clubIndex];
            foreach (Player p in club.Squad.Players)
            {
                p.Age = startAge;
                p.Development.Potential = AttributeScale.ClampSkill(PlayerRating.Overall(p) + headroom);
            }
            return club;
        }

        /// <summary>One season of weekly development ticks with a fixed context and RNG stream.</summary>
        private static void DevelopSeason(Club club, DevelopmentContext ctx, int baseSeed)
        {
            for (int week = 0; week < 38; week++)
            {
                var rng = new Pcg32((ulong)baseSeed, (ulong)week);
                foreach (Player p in club.Squad.Players)
                    DevelopmentModel.ApplyDevelopmentWeek(
                        p, TeamTrainingFocus.Balanced, IndividualTrainingFocus.None, ctx, rng, Cfg.Development);
            }
        }

        private static int SkillTotal(Club club)
        {
            int sum = 0;
            foreach (Player p in club.Squad.Players)
                for (int s = 0; s < PlayerAttributes.SkillCount; s++) sum += p.Attributes[s];
            return sum;
        }

        private static void PrintClub(string label, League league, int clubId)
        {
            Club? c = league.FindClub(clubId);
            if (c == null) return;
            Finances f = c.Finances;
            TestContext.Out.WriteLine(
                $"    {label} {c.ShortName}: gate {Money(f.SeasonGateIncome)}, sponsor {Money(f.SeasonSponsorIncome)}, " +
                $"prize {Money(f.SeasonPrizeIncome)}, wages {Money(f.SeasonWageExpense)} -> net {Money(f.SeasonNet)}, " +
                $"balance {Money(f.Balance)}");
        }

        private static string Money(long v)
        {
            if (v >= 1_000_000 || v <= -1_000_000) return $"€{v / 1_000_000.0:F1}M";
            if (v >= 1_000 || v <= -1_000) return $"€{v / 1_000.0:F0}k";
            return $"€{v}";
        }
    }
}
