using System;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The brain seam (watchable-match spec, task 2). The decisions and the positioning of the
    /// watched match sit behind one selector in <see cref="MatchBalance.Brain"/>. V10 is the
    /// default and is what the golden master pins; V11 plays any match with its own decisions.
    /// </summary>
    [TestFixture]
    public class MatchBrainTests
    {
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void Brain_DefaultsToV10_AndADocumentWithoutItStillPlaysV10()
        {
            Assert.That(new BalanceConfig().Match.Brain, Is.EqualTo(MatchBrainVersion.V10));

            BalanceConfig older = JsonSerializer.Deserialize<BalanceConfig>(
                "{\"Match\":{\"HomeAdvantagePercent\":6}}")!;
            Assert.That(older.Match.Brain, Is.EqualTo(MatchBrainVersion.V10));

            BalanceConfig pushed = JsonSerializer.Deserialize<BalanceConfig>("{\"Match\":{\"Brain\":1}}")!;
            Assert.That(pushed.Match.Brain, Is.EqualTo(MatchBrainVersion.V11));
        }

        /// <summary>
        /// V11 plays its own set pieces (task 8), so it is no longer the V10 match draw for draw.
        /// What still proves the seam: a V11 match is a whole, valid, reproducible match, with a
        /// score that is the goals on its own stream. V10 stays pinned by the golden master
        /// (SimulationDeterminismTests, WorldEconomyDeterminismTests).
        /// </summary>
        [TestCase(0, 5, 424242UL)]
        [TestCase(9, 10, 777UL)]
        public void V11_PlaysAWholeValidMatch_Reproducibly(int home, int away, ulong seed)
        {
            MatchReport v11 = Play(MatchBrainVersion.V11, home, away, seed);
            MatchReport again = Play(MatchBrainVersion.V11, home, away, seed);

            Assert.That(v11.Positions, Is.Not.Null, "A match on V11 must be played on the pitch.");
            Assert.That(v11.Positions!.LastTick, Is.GreaterThan(0));
            Assert.That(v11.Positions.Actions, Is.Not.Empty);
            int homeGoals = 0, awayGoals = 0;
            foreach (BallAction a in v11.Positions.Actions)
                if (a.Kind == BallActionKind.Goal) { if (a.Home) homeGoals++; else awayGoals++; }
            Assert.That(v11.HomeGoals, Is.EqualTo(homeGoals));
            Assert.That(v11.AwayGoals, Is.EqualTo(awayGoals));
            Assert.That(MatchReportHasher.Hash(again), Is.EqualTo(MatchReportHasher.Hash(v11)),
                "the same seed on V11 is the same match");
        }

        [Test]
        public void UnknownBrain_IsRefused()
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = (MatchBrainVersion)7;

            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchEngine(cfg).Simulate(
                LineupSelector.BestEleven(_league.Clubs[0]),
                LineupSelector.BestEleven(_league.Clubs[1]),
                new Pcg32(1)));
        }

        private static MatchReport Play(MatchBrainVersion brain, int home, int away, ulong seed)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = brain;
            return new MatchEngine(cfg).Simulate(
                LineupSelector.BestEleven(_league.Clubs[home]),
                LineupSelector.BestEleven(_league.Clubs[away]),
                new Pcg32(seed));
        }
    }
}
