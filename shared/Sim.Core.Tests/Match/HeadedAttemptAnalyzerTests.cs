using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Headed attempts off the ball's height at the strike (free-ball spec R5, issue #183), on
    /// hand-built streams at two ticks a frame: a home team-mate's ball on frame 2, the strike filed
    /// on frame 6 (ticks 12-13).
    /// </summary>
    [TestFixture]
    public class HeadedAttemptAnalyzerTests
    {
        private const int TicksPerFrame = 2;
        private const int Winger = 7;
        private const int Striker = 9;

        private static HeadedAttemptMetrics Measure(BallActionKind before, int strikeZDm, int strikerSlot = Striker)
        {
            PositionStream s = NewStream(20);
            Act(s, 2, before, true, Winger, strikerSlot);
            Act(s, 6, BallActionKind.Shot, true, strikerSlot);
            var events = new List<BallEvent>
            {
                new BallEvent { Tick = 4, Kind = BallEventKind.Touch, Side = 0, Slot = Winger, ZDm = 0 },
                new BallEvent { Tick = 12, Kind = BallEventKind.Touch, Side = 0, Slot = strikerSlot, ZDm = 18 },
                new BallEvent { Tick = 13, Kind = BallEventKind.Touch, Side = 0, Slot = strikerSlot, ZDm = strikeZDm },
            };
            return HeadedAttemptAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(HeadedAttemptAnalyzer.Analyze(new MatchReport(), new List<BallEvent>(), TicksPerFrame), Is.Null);
        }

        [Test]
        public void AStrikeAboveTheKnee_OffATeamMatesCross_IsAHeaderOffACross()
        {
            HeadedAttemptMetrics m = Measure(BallActionKind.Cross, HeadedAttemptAnalyzer.HeadedFromDm);
            Assert.That(m.Shots, Is.EqualTo(1));
            Assert.That(m.Headed, Is.EqualTo(1));
            Assert.That(m.HeadedFromCross, Is.EqualTo(1));
            Assert.That(m.Stray, Is.Empty);
        }

        [Test]
        public void TheStrikeIsTheStrikersLastTouchInItsFrame_NotTheGatherBeforeIt()
        {
            HeadedAttemptMetrics m = Measure(BallActionKind.Cross, HeadedAttemptAnalyzer.HeadedFromDm - 1);
            Assert.That(m.Shots, Is.EqualTo(1));
            Assert.That(m.Headed, Is.Zero, "met at head height, struck off the knee: a volley");
        }

        [Test]
        public void AStrikeBlocked_AndTouchedAgainInItsFrame_IsReadAtTheStrike()
        {
            PositionStream s = NewStream(20);
            Act(s, 2, BallActionKind.Pass, true, Winger, Striker);
            Act(s, 6, BallActionKind.Shot, true, Striker);
            var events = new List<BallEvent>
            {
                new BallEvent { Tick = 12, Kind = BallEventKind.Touch, Side = 0, Slot = Striker, ZDm = 4 },
                new BallEvent { Tick = 12, Kind = BallEventKind.Deflection, Side = 1, Slot = 4, ZDm = 12 },
                new BallEvent { Tick = 13, Kind = BallEventKind.Touch, Side = 0, Slot = Striker, ZDm = 12 },
            };
            HeadedAttemptMetrics m = HeadedAttemptAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;
            Assert.That(m.Headed, Is.Zero, "struck off the ground, blocked, and the ball came back off him in the air");
        }

        [Test]
        public void AHeaderOffAnythingButACross_IsListedAsStray()
        {
            HeadedAttemptMetrics m = Measure(BallActionKind.Pass, 15);
            Assert.That(m.Headed, Is.EqualTo(1));
            Assert.That(m.HeadedFromCross, Is.Zero);
            Assert.That(m.Stray, Is.EqualTo(new[] { 6 }));
        }

        [Test]
        public void AHeaderOffHisOwnCross_IsNotOffATeamMatesCross()
        {
            HeadedAttemptMetrics m = Measure(BallActionKind.Cross, 15, strikerSlot: Winger);
            Assert.That(m.Headed, Is.EqualTo(1));
            Assert.That(m.HeadedFromCross, Is.Zero);
        }
    }
}
