using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The long-pass share, high-pass share and aerial duels of free-ball spec R2/R4 (doc §11.3) on
    /// hand-built streams at two ticks a frame: passes struck on frame 2 (tick 3) and frame 10 (tick
    /// 19), each met by the next touch; the ball peaks on frame 5 at the height given.
    /// </summary>
    [TestFixture]
    public class AerialPassAnalyzerTests
    {
        private const int TicksPerFrame = 2;
        private const int Frames = 20;

        private static BallEvent Touch(int tick, int side, int slot, int x, int y, bool aerial = false) =>
            new BallEvent { Tick = tick, Kind = BallEventKind.Touch, Side = side, Slot = slot, XDm = x, YDm = y, AerialDuel = aerial };

        private static AerialPassMetrics Measure(int peakDm, int firstPassToX, bool restart = false)
        {
            PositionStream s = NewStream(Frames);
            s.TicksPerMinute = 300;
            s.BallZ = new int[Frames];
            s.BallZ[5] = peakDm;
            if (restart) Act(s, 0, BallActionKind.GoalKick, true, 0);
            Act(s, 2, BallActionKind.Pass, true, 4, 9);
            Act(s, 10, BallActionKind.Pass, false, 6, 7);
            var events = new List<BallEvent>
            {
                Touch(3, 0, 4, 200, 340),
                Touch(13, 0, 9, firstPassToX, 340),
                Touch(15, 1, 3, 600, 340, aerial: true),       // a header won in a duel: not a pass
                Touch(19, 1, 6, 600, 300),
                Touch(25, 1, 7, 650, 300),
            };
            return AerialPassAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(AerialPassAnalyzer.Analyze(new MatchReport(), new List<BallEvent>(), TicksPerFrame), Is.Null);
        }

        [Test]
        public void APassOver30m_IsLong_AndOneThatPeaksAt2m_IsHigh()
        {
            AerialPassMetrics m = Measure(peakDm: 20, firstPassToX: 501);

            Assert.That(m.Passes, Is.EqualTo(2));
            Assert.That(m.LongPasses, Is.EqualTo(1), "30.1 m is long, 5 m is not");
            Assert.That(m.HighPasses, Is.EqualTo(1), "the first peaks at 2 m on its way");
            Assert.That(m.LongPercent, Is.EqualTo(50).Within(1e-9));
            Assert.That(m.HighPercent, Is.EqualTo(50).Within(1e-9));
        }

        [Test]
        public void A30mPass_UnderTwoMetres_IsNeitherLongNorHigh_EvenAfterARestart()
        {
            AerialPassMetrics m = Measure(peakDm: 19, firstPassToX: 500, restart: true);

            Assert.That(m.Passes, Is.EqualTo(2), "a restart's kick is a pass too");
            Assert.That(m.LongPasses, Is.Zero);
            Assert.That(m.HighPasses, Is.Zero);
        }

        [Test]
        public void EveryHeaderThatWonADuel_IsOneAerialDuel()
        {
            Assert.That(Measure(peakDm: 0, firstPassToX: 300).AerialDuels, Is.EqualTo(1));

            var tally = new AerialPassTally();
            tally.Add(Measure(peakDm: 0, firstPassToX: 300));
            tally.Add(new AerialPassMetrics { Passes = 10, LongPasses = 1, HighPasses = 3, AerialDuels = 3 });
            IReadOnlyList<ShotPassRow> rows = tally.Rows();
            Assert.That(rows[3].Name, Is.EqualTo(RealismReference.Events.AerialDuelsPerTeamMatch.Name));
            Assert.That(rows[3].Value, Is.EqualTo(2).Within(1e-9), "a team-match's duels are the match's");
            Assert.That(rows[1].Value, Is.EqualTo(5).Within(1e-9), "the long share is the mean of the matches'");
            Assert.That(rows[0].Value, Is.EqualTo(3).Within(1e-9), "passes per team-match");
        }
    }
}
