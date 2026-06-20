using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Task 5.1 acceptance: the valuation model. THE ✅s — prices correlate with ability and
    /// the age curve, young stars cost more than equal-ability 30-year-olds, and there are no
    /// negative/absurd prices across 10k generated players. Plus the supporting guarantees:
    /// the model is deterministic with no RNG, an expiring contract and a lower division both
    /// discount the fee, form nudges the price only slightly, and the whole-world re-pricing
    /// pass writes a stable MarketValue.
    /// </summary>
    [TestFixture]
    public class ValuationTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static MarketBalance M => Cfg.Market;

        // ----------------------------------------------------------- correlates with ability

        [Test]
        public void Value_RisesStrictlyWithOverall_AllElseEqual()
        {
            long previous = -1;
            for (int overall = 40; overall <= 90; overall += 5)
            {
                // Uniform attributes ⇒ Overall == the skill value exactly (every role row sums to 100).
                Player p = Uniform(PositionRole.FullBack, skill: overall, age: 25,
                                   potential: overall, seasons: M.ContractFullSeasons, form: 50);
                long v = ValuationModel.Value(p, Cfg);
                Assert.That(v, Is.GreaterThan(previous), $"Value must rise with overall (at {overall})");
                previous = v;
            }
        }

        [Test]
        public void Value_FollowsAgeCurve_ForEqualAbility()
        {
            // Equal ability, no potential headroom ⇒ only the age-value multiplier moves the price.
            long v24 = ValueAtAge(24);
            long v28 = ValueAtAge(28);
            long v32 = ValueAtAge(32);
            long v35 = ValueAtAge(35);

            TestContext.Out.WriteLine($"[valuation-age] 24={v24:N0} 28={v28:N0} 32={v32:N0} 35={v35:N0}");

            Assert.That(v24, Is.EqualTo(v28), "Below the value-decline onset, age does not change value");
            Assert.That(v32, Is.LessThan(v28), "Value declines past the onset age");
            Assert.That(v35, Is.LessThan(v32), "Value keeps declining with age");
        }

        // ----------------------------------------------------------- THE ✅: young star vs veteran

        [Test]
        public void YoungStar_IsWorthMoreThan_EqualAbilityThirtySomething()
        {
            // Same overall (78), same role/contract/league/form — only age + potential differ.
            Player youngStar = Uniform(PositionRole.Striker, skill: 78, age: 19,
                                       potential: 92, seasons: M.ContractFullSeasons, form: 50);
            Player veteran = Uniform(PositionRole.Striker, skill: 78, age: 30,
                                     potential: 78, seasons: M.ContractFullSeasons, form: 50);

            long young = ValuationModel.Value(youngStar, Cfg);
            long old = ValuationModel.Value(veteran, Cfg);
            TestContext.Out.WriteLine($"[valuation-youth] 19yo pot92 = {young:N0} vs 30yo pot78 = {old:N0}");

            Assert.That(young, Is.GreaterThan(old),
                "A young star must cost more than an equal-ability 30-year-old (the ✅)");
        }

        // ----------------------------------------------------------- the fat top tail: phenoms

        [Test]
        public void ElitePhenom_CommandsAHeadlineFee_FarAboveAGoodPlayer()
        {
            // A young, high-overall, high-potential talent — the kind that fetches €100M+ in the
            // real market — must cost an order of magnitude more than a solid mid-table starter.
            Player phenom = Uniform(PositionRole.Striker, skill: 78, age: 18,
                                    potential: 95, seasons: M.ContractFullSeasons, form: 50);
            Player goodStarter = Uniform(PositionRole.Striker, skill: 70, age: 25,
                                         potential: 70, seasons: M.ContractFullSeasons, form: 50);

            long elite = ValuationModel.Value(phenom, Cfg);
            long good = ValuationModel.Value(goodStarter, Cfg);
            TestContext.Out.WriteLine($"[valuation-elite] phenom = {elite:N0} vs good starter = {good:N0}");

            Assert.That(elite, Is.GreaterThan(100_000_000),
                "A young phenom commands a headline fee (the fat top tail)");
            Assert.That(elite, Is.GreaterThan(good * 4),
                "A phenom is worth multiples of a merely good player");
            Assert.That(elite, Is.LessThanOrEqualTo(M.MaxValue), "Still bounded by the absurdity cap");
        }

        // ----------------------------------------------------------- THE ✅: no absurd prices

        [Test]
        public void NoNegativeOrAbsurdPrices_Across10kGeneratedPlayers()
        {
            var values = new List<long>(11_000);
            int leagues = 0;

            // ~23 leagues × (20 clubs × 22) ≈ 10k players, spread across 5 division levels.
            for (int i = 0; values.Count < 10_000; i++)
            {
                int division = (i % 5) + 1;
                var options = new LeagueGenerationOptions { Division = division };
                League league = new LeagueGenerator(options).Generate(new Pcg32((ulong)(5_010_000 + i)));
                leagues++;

                foreach (Club club in league.Clubs)
                    foreach (Player p in club.Squad.Players)
                        values.Add(ValuationModel.Value(p, division, Cfg));
            }

            values.Sort();
            long min = values[0];
            long max = values[values.Count - 1];
            long avg = Sum(values) / values.Count;
            TestContext.Out.WriteLine(
                $"[valuation-10k] {values.Count} players across {leagues} leagues — " +
                $"min {min:N0}, p50 {Percentile(values, 50):N0}, p90 {Percentile(values, 90):N0}, " +
                $"p99 {Percentile(values, 99):N0}, max {max:N0}, avg {avg:N0}");

            foreach (long v in values)
            {
                Assert.That(v, Is.GreaterThan(0), "No price may be zero or negative");
                Assert.That(v, Is.GreaterThanOrEqualTo(M.MinValue), "Every price respects the floor");
                Assert.That(v, Is.LessThanOrEqualTo(M.MaxValue), "No price may exceed the absurdity cap");
            }
        }

        [Test]
        public void WeakestPlayers_FloorAtMinValue_NeverBelow()
        {
            // A poor, old, out-of-contract player in the lowest division: the curve bottoms out,
            // but the floor keeps the price positive and tidy.
            Player p = Uniform(PositionRole.CentreBack, skill: AttributeScale.MinSkill, age: 36,
                               potential: AttributeScale.MinSkill, seasons: 0, form: 0);
            long v = ValuationModel.Value(p, leagueLevel: 5, Cfg);
            Assert.That(v, Is.EqualTo(M.MinValue), "The weakest player is floored exactly at MinValue");
        }

        // ----------------------------------------------------------- determinism (no RNG)

        [Test]
        public void Valuation_IsDeterministic()
        {
            Player p = Uniform(PositionRole.CentralMidfielder, skill: 71, age: 23,
                               potential: 84, seasons: 2, form: 57);
            long a = ValuationModel.Value(p, leagueLevel: 2, Cfg);
            long b = ValuationModel.Value(p, leagueLevel: 2, Cfg);
            Assert.That(b, Is.EqualTo(a), "Valuation is a pure function of state — identical every call");
        }

        // ----------------------------------------------------------- contract & league discounts

        [Test]
        public void ExpiringContract_DiscountsValue()
        {
            Player expiring = Uniform(PositionRole.Winger, 76, 25, 76, seasons: 0, form: 50);
            Player locked = Uniform(PositionRole.Winger, 76, 25, 76, seasons: 4, form: 50);
            Assert.That(ValuationModel.Value(expiring, Cfg),
                Is.LessThan(ValuationModel.Value(locked, Cfg)),
                "A player in his final months fetches less than one on a long deal");
        }

        [Test]
        public void LowerDivision_DiscountsValue()
        {
            Player p = Uniform(PositionRole.Striker, 80, 26, 80, seasons: 3, form: 50);
            Assert.That(ValuationModel.Value(p, leagueLevel: 3, Cfg),
                Is.LessThan(ValuationModel.Value(p, leagueLevel: 1, Cfg)),
                "The same player is priced lower in a lower division");
        }

        // ----------------------------------------------------------- form: present but small

        [Test]
        public void Form_NudgesValue_OnlySlightly()
        {
            Player hot = Uniform(PositionRole.AttackingMidfielder, 75, 25, 75, seasons: 3, form: 90);
            Player neutral = Uniform(PositionRole.AttackingMidfielder, 75, 25, 75, seasons: 3, form: 50);
            Player cold = Uniform(PositionRole.AttackingMidfielder, 75, 25, 75, seasons: 3, form: 10);

            long h = ValuationModel.Value(hot, Cfg);
            long n = ValuationModel.Value(neutral, Cfg);
            long c = ValuationModel.Value(cold, Cfg);

            Assert.That(h, Is.GreaterThan(n), "Hot form lifts value");
            Assert.That(n, Is.GreaterThan(c), "Cold form trims value");
            Assert.That(h, Is.LessThan(n * 11 / 10), "Form's effect stays within a small band (≤10%)");
        }

        // ----------------------------------------------------------- whole-world re-pricing

        [Test]
        public void Progressor_RepricesWholeWorld_IntoMarketValue_Deterministically()
        {
            League div1 = new LeagueGenerator(new LeagueGenerationOptions { Division = 1 })
                .Generate(new Pcg32(5_010_777));
            League div2 = new LeagueGenerator(new LeagueGenerationOptions { Division = 2 })
                .Generate(new Pcg32(5_010_778));
            var world = new[] { div1, div2 };

            var progressor = new ValuationProgressor(Cfg);
            progressor.Reprice(world);

            // Every value written, positive, and equal to a direct valuation at the league level.
            foreach (League league in world)
                foreach (Club club in league.Clubs)
                    foreach (Player p in club.Squad.Players)
                    {
                        Assert.That(p.MarketValue, Is.GreaterThan(0), "Re-pricing writes a positive value");
                        Assert.That(p.MarketValue, Is.EqualTo(ValuationModel.Value(p, league.Division, Cfg)),
                            "Stored value matches the model at the league level");
                    }

            // Re-running on a fresh identical world yields identical values (deterministic, order-independent).
            League div1b = new LeagueGenerator(new LeagueGenerationOptions { Division = 1 })
                .Generate(new Pcg32(5_010_777));
            League div2b = new LeagueGenerator(new LeagueGenerationOptions { Division = 2 })
                .Generate(new Pcg32(5_010_778));
            new ValuationProgressor(Cfg).Reprice(new[] { div1b, div2b });

            for (int l = 0; l < world.Length; l++)
            {
                League a = world[l];
                League b = l == 0 ? div1b : div2b;
                for (int c = 0; c < a.Clubs.Count; c++)
                    for (int i = 0; i < a.Clubs[c].Squad.Players.Count; i++)
                        Assert.That(b.Clubs[c].Squad.Players[i].MarketValue,
                            Is.EqualTo(a.Clubs[c].Squad.Players[i].MarketValue),
                            "Whole-world re-pricing is deterministic");
            }
        }

        // ----------------------------------------------------------- helpers

        private static long ValueAtAge(int age)
        {
            Player p = Uniform(PositionRole.FullBack, skill: 75, age: age,
                               potential: 75, seasons: M.ContractFullSeasons, form: 50);
            return ValuationModel.Value(p, Cfg);
        }

        /// <summary>
        /// A player with uniform attributes, so <see cref="PlayerRating.Overall"/> equals
        /// <paramref name="skill"/> exactly (every role weight row sums to 100) — lets a test
        /// pin overall, age, potential, contract and form independently.
        /// </summary>
        private static Player Uniform(PositionRole role, int skill, int age, int potential, int seasons, int form)
        {
            var p = new Player { Id = 1, Age = age, Role = role };
            for (int s = 0; s < PlayerAttributes.SkillCount; s++) p.Attributes[s] = skill;
            p.Development.Potential = potential;
            p.Contract.SeasonsRemaining = seasons;
            p.Condition.Form = form;
            return p;
        }

        private static long Sum(List<long> values)
        {
            long sum = 0;
            foreach (long v in values) sum += v;
            return sum;
        }

        private static long Percentile(List<long> sorted, int percentile)
        {
            int index = (sorted.Count - 1) * percentile / 100;
            return sorted[index];
        }
    }
}
