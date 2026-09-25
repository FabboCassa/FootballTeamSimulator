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
    /// default and is what the golden master pins; V11 plays any match, and since it positions
    /// its own men (task 6) its match is its own.
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

        [TestCase(0, 5, 424242UL)]
        [TestCase(9, 10, 777UL)]
        public void V11_PlaysAWholeMatch_OnItsOwnPositioning_AndTheV10PathIsUntouched(int home, int away, ulong seed)
        {
            MatchReport v10 = Play(MatchBrainVersion.V10, home, away, seed);
            MatchReport v11 = Play(MatchBrainVersion.V11, home, away, seed);

            Assert.That(v11.Positions, Is.Not.Null, "A match on V11 must be played on the pitch.");
            Assert.That(v11.Positions!.LastTick, Is.EqualTo(v10.Positions!.LastTick), "the whole ninety minutes");
            Assert.That(v11.Positions.Actions, Is.Not.Empty);
            Assert.That(MatchReportHasher.Hash(Play(MatchBrainVersion.V11, home, away, seed)),
                Is.EqualTo(MatchReportHasher.Hash(v11)), "V11 is deterministic: same seed, same match");

            // Since task 6 V11 moves its own men (R2), so its match is no longer V10's: the seam
            // really switches brain. V10 itself is pinned to the golden master elsewhere
            // (WorldEconomyDeterminismTests, server SimulationDeterminismTests); here the default
            // config must still be the V10 path.
            Assert.That(MatchReportHasher.Hash(v11), Is.Not.EqualTo(MatchReportHasher.Hash(v10)));
            Assert.That(MatchReportHasher.Hash(PlayDefault(home, away, seed)), Is.EqualTo(MatchReportHasher.Hash(v10)));
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

        private static MatchReport PlayDefault(int home, int away, ulong seed) =>
            new MatchEngine(new BalanceConfig()).Simulate(
                LineupSelector.BestEleven(_league.Clubs[home]),
                LineupSelector.BestEleven(_league.Clubs[away]),
                new Pcg32(seed));

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
