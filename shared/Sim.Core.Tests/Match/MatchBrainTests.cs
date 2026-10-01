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
    /// Engine v11 has one brain (watchable-match spec, task 19): there is no selector any more, and
    /// a balance document stored while there was one still loads and plays that brain; knobs only
    /// engine v10 read (the passer's offside judgement) are ignored on load.
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

        [TestCase("{\"Match\":{\"Brain\":0}}")]
        [TestCase("{\"Match\":{\"Brain\":1}}")]
        [TestCase("{\"Match\":{\"HomeAdvantagePercent\":6}}")]
        [TestCase("{\"Match\":{\"Brain\":0,\"OffsideJudgementDm\":40,\"OffsideJudgementFloorDm\":30}}")]
        public void AnOlderDocument_NamingABrainOrNot_LoadsAndPlaysTheOneBrain(string json)
        {
            BalanceConfig older = JsonSerializer.Deserialize<BalanceConfig>(json)!;

            Assert.That(MatchReportHasher.Hash(Play(older, 0, 5, 424242UL)),
                Is.EqualTo(MatchReportHasher.Hash(Play(new BalanceConfig(), 0, 5, 424242UL))));
        }

        [TestCase(0, 5, 424242UL)]
        [TestCase(9, 10, 777UL)]
        public void AMatch_IsWholeValidAndReproducible(int home, int away, ulong seed)
        {
            MatchReport report = Play(new BalanceConfig(), home, away, seed);
            MatchReport again = Play(new BalanceConfig(), home, away, seed);

            Assert.That(report.EngineVersion, Is.EqualTo(11));
            Assert.That(report.Positions, Is.Not.Null, "A watched match must be played on the pitch.");
            Assert.That(report.Positions!.LastTick, Is.EqualTo(90 * report.Positions.TicksPerMinute), "the whole ninety minutes");
            Assert.That(report.Positions.Actions, Is.Not.Empty);
            int homeGoals = 0, awayGoals = 0;
            foreach (BallAction a in report.Positions.Actions)
                if (a.Kind == BallActionKind.Goal) { if (a.Home) homeGoals++; else awayGoals++; }
            Assert.That(report.HomeGoals, Is.EqualTo(homeGoals));
            Assert.That(report.AwayGoals, Is.EqualTo(awayGoals));
            Assert.That(MatchReportHasher.Hash(again), Is.EqualTo(MatchReportHasher.Hash(report)),
                "the same seed is the same match");
        }

        private static MatchReport Play(BalanceConfig cfg, int home, int away, ulong seed) =>
            new MatchEngine(cfg).Simulate(
                LineupSelector.BestEleven(_league.Clubs[home]),
                LineupSelector.BestEleven(_league.Clubs[away]),
                new Pcg32(seed));
    }
}
