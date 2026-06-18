using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Condition
{
    /// <summary>
    /// Task 4.2 refinement — within-match fatigue (engine flag <c>applyMatchFatigue</c>):
    /// each side's rating fades as the match wears on, scaled by its on-pitch XI's average
    /// stamina, with a small half-time recovery. Both sides tiring equally is scale-invariant,
    /// so the effect is RELATIVE: the higher-stamina (fresher) side gains the edge, especially
    /// late. Guards: flag-off identity, determinism, and the relative-edge direction.
    /// </summary>
    [TestFixture]
    public class MatchFatigueTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        /// <summary>Two clubs with a deliberately extreme stamina gap (the only thing we vary).</summary>
        private static (Club high, Club low) TwoClubs()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(31415));
            Club high = league.Clubs[0];
            Club low = league.Clubs[1];
            SetStamina(high, 95);
            SetStamina(low, 10);
            return (high, low);
        }

        [Test]
        public void FatigueOff_IsByteIdenticalToTheBaseEngine()
        {
            (Club high, Club low) = TwoClubs();
            Lineup h = LineupSelector.BestEleven(high);
            Lineup a = LineupSelector.BestEleven(low);

            string baseEngine = JsonSerializer.Serialize(new MatchEngine(Cfg).Simulate(h, a, new Pcg32(7)));
            string fatigueOff = JsonSerializer.Serialize(
                new MatchEngine(Cfg, applyCondition: false, applyMatchFatigue: false).Simulate(h, a, new Pcg32(7)));

            Assert.That(fatigueOff, Is.EqualTo(baseEngine),
                "With fatigue off the per-minute rating recompute must be byte-identical to the base engine.");
        }

        [Test]
        public void Fatigue_IsDeterministic_PerSeed()
        {
            (Club high, Club low) = TwoClubs();
            Lineup h = LineupSelector.BestEleven(high);
            Lineup a = LineupSelector.BestEleven(low);
            var engine = new MatchEngine(Cfg, applyMatchFatigue: true);

            string first = JsonSerializer.Serialize(engine.Simulate(h, a, new Pcg32(99)));
            string second = JsonSerializer.Serialize(engine.Simulate(h, a, new Pcg32(99)));
            Assert.That(second, Is.EqualTo(first), "Within-match fatigue must be a pure function of (seed, inputs).");
        }

        [Test]
        public void WithinMatchFatigue_TiltsResultsTowardTheHigherStaminaSide()
        {
            // Same lineups and seeds; the ONLY difference is the fatigue flag. The base
            // rating gap from stamina is present in both runs, so it cancels in the delta —
            // what remains is the fatigue contribution. The low-stamina side fades much more,
            // so the high-stamina side's net (GF-GA) must improve with fatigue on.
            // (Statistical harness: a large stamina gap over many seeds, robust but not exact.)
            (Club high, Club low) = TwoClubs();
            Lineup h = LineupSelector.BestEleven(high);
            Lineup a = LineupSelector.BestEleven(low);

            var off = new MatchEngine(Cfg);                          // no fatigue (baseline)
            var on = new MatchEngine(Cfg, applyMatchFatigue: true);  // within-match fatigue

            int offNet = 0, onNet = 0;
            const int matches = 400;
            for (ulong s = 0; s < matches; s++)
            {
                MatchReport ro = off.Simulate(h, a, new Pcg32(5000 + s));
                MatchReport rn = on.Simulate(h, a, new Pcg32(5000 + s));
                offNet += ro.HomeGoals - ro.AwayGoals;   // high-stamina side is home
                onNet += rn.HomeGoals - rn.AwayGoals;
            }

            TestContext.Out.WriteLine(
                $"[match-fatigue] high-stamina net GF-GA over {matches}: fatigue off {offNet}, fatigue on {onNet}");
            Assert.That(onNet, Is.GreaterThan(offNet),
                "Within-match fatigue must tilt results toward the higher-stamina side (the low-stamina side fades more).");
        }

        private static void SetStamina(Club club, int stamina)
        {
            foreach (Player p in club.Squad.Players)
                p.Attributes.Stamina = stamina;
        }
    }
}
