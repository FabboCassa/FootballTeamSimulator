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
    /// Nation and division wealth multipliers (R1-R3 of the realistic-club-economy spec):
    ///   - R1 nation wealth: <see cref="NationProfile.EconomicReputation"/> defaults to
    ///     <see cref="NationProfile.Reputation"/> unless the atlas overrides it, and a strictly
    ///     monotone curve turns it into a tier-1 revenue multiplier;
    ///   - R2 absolute anchor: England's tier-1 mean club revenue and Italy's tier-1/2/3 land in
    ///     the real-football-calibrated bands;
    ///   - R3 division ratio: every nation's tier-2 revenue is 28-42% of tier 1, tier-3 is 9-15%.
    ///
    /// All of this is pure/deterministic (integer math, no RNG of its own) and the match engine
    /// never reads it, so the golden master is untouched — proven elsewhere by
    /// <c>SimulationDeterminismTests</c> staying green.
    /// </summary>
    [TestFixture]
    public class EconomyTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static FinanceBalance F => Cfg.Finance;
        private const ulong Seed = 9_009_009;
        private const int ClubCount = 20;

        // ============================================================ R1: NationProfile.EconomicReputation

        [Test]
        public void EconomicReputation_DefaultsToReputation_UnlessOverridden()
        {
            var reputationFirst = new NationProfile { Reputation = 70 };
            Assert.That(reputationFirst.EconomicReputation, Is.EqualTo(70),
                "With nothing set explicitly, EconomicReputation must fall back to the sporting Reputation");

            var overridden = new NationProfile { Reputation = 70, EconomicReputation = 40 };
            Assert.That(overridden.EconomicReputation, Is.EqualTo(40), "An explicit override must win");

            // Order of initialization must not matter (EconomicReputation set before Reputation).
            var orderIndependent = new NationProfile { EconomicReputation = 40, Reputation = 70 };
            Assert.That(orderIndependent.EconomicReputation, Is.EqualTo(40),
                "Setting EconomicReputation before Reputation in an object initializer must still stick");

            var changedReputation = new NationProfile { Reputation = 70 };
            changedReputation.Reputation = 55;
            Assert.That(changedReputation.EconomicReputation, Is.EqualTo(55),
                "The fallback must track Reputation live (no override was ever set)");
        }

        [Test]
        public void Atlas_OrdersItalyBelowGermanyAndSpain_AndDefaultsEveryoneElseToReputation()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            NationProfile eng = Find(atlas, "ENG");
            NationProfile esp = Find(atlas, "ESP");
            NationProfile ger = Find(atlas, "GER");
            NationProfile ita = Find(atlas, "ITA");

            Assert.That(ita.EconomicReputation, Is.LessThan(ger.EconomicReputation),
                "Italy must sit below Germany economically, despite a close sporting reputation");
            Assert.That(ita.EconomicReputation, Is.LessThan(esp.EconomicReputation),
                "Italy must sit below Spain economically, despite a close sporting reputation");
            Assert.That(eng.EconomicReputation, Is.EqualTo(eng.Reputation),
                "England carries no override: its EconomicReputation is its sporting Reputation");

            // A nation the atlas never overrides falls back to its own sporting reputation.
            NationProfile arg = Find(atlas, "ARG");
            Assert.That(arg.EconomicReputation, Is.EqualTo(arg.Reputation));
        }

        // ============================================================ R1: the nation wealth curve

        [Test]
        public void NationMultiplier_IsStrictlyMonotoneInEconomicReputation()
        {
            // The curve is a pure power of EconomicReputation with a hard floor
            // (NationWealthFloorPermille) so no nation earns literally nothing; several very low
            // reputations tie at that floor by design. No real nation's EconomicReputation drops
            // that low (the built-in atlas floors at 48), so strict monotonicity is asserted from
            // just above where the floor stops binding.
            int previous = FinanceModel.NationMultiplierPermille(39, F);
            for (int rep = 40; rep <= 100; rep++)
            {
                int mult = FinanceModel.NationMultiplierPermille(rep, F);
                Assert.That(mult, Is.GreaterThan(previous),
                    $"NationMultiplierPermille must strictly increase from {rep - 1} to {rep} (got {previous} -> {mult})");
                previous = mult;
            }

            // And across every distinct EconomicReputation actually used by the built-in atlas.
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            List<int> distinctReps = atlas.Select(n => n.EconomicReputation).Distinct().OrderBy(r => r).ToList();
            int lastMult = -1;
            foreach (int rep in distinctReps)
            {
                int mult = FinanceModel.NationMultiplierPermille(rep, F);
                Assert.That(mult, Is.GreaterThan(lastMult),
                    $"Two different atlas EconomicReputation values ({rep}) must never map to the same multiplier");
                lastMult = mult;
            }
        }

        [Test]
        public void NationMultiplier_RelativeBands_MatchEnglandAnchor()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int englandMult = FinanceModel.NationMultiplierPermille(Find(atlas, "ENG").EconomicReputation, F);

            AssertRelativeBand(atlas, englandMult, "ESP", 0.55, 0.75);
            AssertRelativeBand(atlas, englandMult, "GER", 0.55, 0.75);
            AssertRelativeBand(atlas, englandMult, "ITA", 0.42, 0.58);
            AssertRelativeBand(atlas, englandMult, "FRA", 0.38, 0.52);
            AssertRelativeBand(atlas, englandMult, "POR", 0.18, 0.32);
            AssertRelativeBand(atlas, englandMult, "NED", 0.18, 0.32);
            AssertRelativeBand(atlas, englandMult, "BRA", 0.18, 0.32);
            AssertRelativeBand(atlas, englandMult, "SCO", 0.08, 0.16);
            AssertRelativeBand(atlas, englandMult, "SUI", 0.08, 0.16);

            // A nation with EconomicReputation 50 (no atlas entry needed - this is a curve property).
            double genericRatio = FinanceModel.NationMultiplierPermille(50, F) / (double)englandMult;
            Assert.That(genericRatio, Is.InRange(0.02, 0.06),
                $"EconomicReputation 50 must land at 2-6% of England, got {genericRatio:P1}");
        }

        private static void AssertRelativeBand(List<NationProfile> atlas, int englandMult, string code, double lo, double hi)
        {
            NationProfile nation = Find(atlas, code);
            int mult = FinanceModel.NationMultiplierPermille(nation.EconomicReputation, F);
            double ratio = mult / (double)englandMult;
            Assert.That(ratio, Is.InRange(lo, hi),
                $"{code} (EconomicReputation {nation.EconomicReputation}) must land at {lo:P0}-{hi:P0} of England, got {ratio:P1}");
        }

        // ============================================================ R2: absolute anchor + R3: division ratio

        [Test]
        public void EnglandTier1_MeanClubRevenue_IsInTheRealFootballBand()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            long revenue = MeanClubRevenueOverASeason(Find(atlas, "ENG").EconomicReputation, tier: 1);

            TestContext.Out.WriteLine($"[economy-anchor] England tier 1 mean club revenue: {Money(revenue)}");
            Assert.That(revenue, Is.InRange(340_000_000L, 460_000_000L),
                "England tier-1 mean club revenue must land in the real-football-calibrated 340-460M band");
        }

        [Test]
        public void ItalyEveryTier_MeanClubRevenue_IsInTheRealFootballBand()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            int italyRep = Find(atlas, "ITA").EconomicReputation;

            long tier1 = MeanClubRevenueOverASeason(italyRep, tier: 1);
            long tier2 = MeanClubRevenueOverASeason(italyRep, tier: 2);
            long tier3 = MeanClubRevenueOverASeason(italyRep, tier: 3);

            TestContext.Out.WriteLine(
                $"[economy-anchor] Italy mean club revenue: tier1 {Money(tier1)}, tier2 {Money(tier2)}, tier3 {Money(tier3)}");

            Assert.That(tier1, Is.InRange(170_000_000L, 230_000_000L), "Italy tier-1 mean club revenue out of band");
            Assert.That(tier2, Is.InRange(55_000_000L, 85_000_000L), "Italy tier-2 mean club revenue out of band");
            Assert.That(tier3, Is.InRange(18_000_000L, 30_000_000L), "Italy tier-3 mean club revenue out of band");
        }

        [Test]
        public void EveryNationWithTwoOrMoreTiers_DivisionRevenueRatio_IsInBand()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();
            AssertDivisionRatios(Find(atlas, "ENG"));
            AssertDivisionRatios(Find(atlas, "ITA"));
            AssertDivisionRatios(Find(atlas, "GER"));
            AssertDivisionRatios(Find(atlas, "BRA")); // only two tiers - exercises the tier2/tier1 band alone
        }

        private static void AssertDivisionRatios(NationProfile nation)
        {
            Assert.That(nation.Divisions.Count, Is.GreaterThanOrEqualTo(2),
                $"{nation.Code} must have at least two tiers for this check to mean anything");

            long tier1 = MeanClubRevenueOverASeason(nation.EconomicReputation, tier: 1);
            long tier2 = MeanClubRevenueOverASeason(nation.EconomicReputation, tier: 2);
            double ratio2 = tier2 / (double)tier1;

            TestContext.Out.WriteLine(
                $"[economy-division] {nation.Code} tier2/tier1 = {ratio2:P1} (tier1 {Money(tier1)}, tier2 {Money(tier2)})");
            Assert.That(ratio2, Is.InRange(0.28, 0.42),
                $"{nation.Code} tier-2 revenue must be 28-42% of tier 1, got {ratio2:P1}");

            if (nation.Divisions.Count < 3) return;

            long tier3 = MeanClubRevenueOverASeason(nation.EconomicReputation, tier: 3);
            double ratio3 = tier3 / (double)tier1;

            TestContext.Out.WriteLine($"[economy-division] {nation.Code} tier3/tier1 = {ratio3:P1} (tier3 {Money(tier3)})");
            Assert.That(ratio3, Is.InRange(0.09, 0.15),
                $"{nation.Code} tier-3 revenue must be 9-15% of tier 1, got {ratio3:P1}");
        }

        // ============================================================ helpers

        private static NationProfile Find(List<NationProfile> atlas, string code)
        {
            NationProfile? found = NationDatabase.Find(atlas, code);
            Assert.That(found, Is.Not.Null, $"Nation '{code}' must exist in the built-in atlas");
            return found!;
        }

        /// <summary>
        /// A full season's mean club revenue for a synthetic single-tier league carrying the given
        /// nation wealth and division tier. Same seed for every call, so club generation and every
        /// match result are byte-identical across nations/tiers - only the finance MULTIPLIER differs
        /// - which is what makes the R1/R3 ratio checks precise rather than noisy.
        /// </summary>
        private static long MeanClubRevenueOverASeason(int economicReputation, int tier)
        {
            League league = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 9_000 + tier,
                LeagueName = "Economy Test League",
                Division = tier,
                ClubCount = ClubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            }, Cfg).Generate(new Pcg32(Seed));
            league.EconomicReputation = economicReputation;

            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(Seed, 777))
            };

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });
            new ValuationProgressor(Cfg).Reprice(new[] { league });

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (ClubCount - 1) * Cfg.Season.DaysBetweenRounds;
            for (int d = 0; d < days; d++)
            {
                List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, Seed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);

            long total = 0;
            foreach (Club c in league.Clubs) total += c.Finances.SeasonIncome;
            return total / league.Clubs.Count;
        }

        private static string Money(long v)
        {
            if (v >= 1_000_000 || v <= -1_000_000) return $"EUR {v / 1_000_000.0:F1}M";
            if (v >= 1_000 || v <= -1_000) return $"EUR {v / 1_000.0:F0}k";
            return $"EUR {v}";
        }
    }
}
