using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The cause trace of fast free-ball turns (free-ball-real-movement spec R2, doc §10.3), pinned
    /// against hand-built streams at the engine's 300 frames a minute (0.2 s a frame, two ticks a
    /// frame). The ball runs at 15 m/s around (750-900, 100), far from both keepers and both stacks:
    /// east for five frames, then it turns 90 degrees north at frame 5.
    /// </summary>
    [TestFixture]
    public class FreeBallTurnAnalyzerTests
    {
        private const int TicksPerFrame = 2;
        private const int Frames = 10;
        private const int TurnFrame = 5;
        private const int Nine = 9;

        private static PositionStream Corner()
        {
            PositionStream s = NewStream(Frames);
            s.TicksPerMinute = 300;
            s.BallZ = new int[Frames];
            for (int t = 0; t < Frames; t++)
            {
                if (t <= TurnFrame) Ball(s, t, 750 + 30 * t, 100);
                else Ball(s, t, 750 + 30 * TurnFrame, 100 + 30 * (t - TurnFrame));
            }

            return s;
        }

        private static BallEvent Event(int tick, BallEventKind kind, int side = 0, int slot = Nine) =>
            new BallEvent { Tick = tick, Kind = kind, Side = side, Slot = slot, XDm = 900, YDm = 100 };

        private static FreeBallTurnMetrics Measure(PositionStream s, params BallEvent[] events) =>
            FreeBallTurnAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(FreeBallTurnAnalyzer.Analyze(new MatchReport(), new List<BallEvent>(), TicksPerFrame), Is.Null);
        }

        [Test]
        public void ATurnWithATouch_IsCountedAndCausedByTheTouch()
        {
            // Tick 11 is filed on frame 6: the second step of the turn at frame 5.
            FreeBallTurnMetrics m = Measure(Corner(), Event(11, BallEventKind.Touch));

            Assert.That(m.Steps, Is.EqualTo(Frames - 2), "every middle frame with a step either side qualifies");
            Assert.That(m.Turns.Count, Is.EqualTo(1));
            Assert.That(m.Turns[0].Frame, Is.EqualTo(TurnFrame));
            Assert.That(m.Turns[0].Cause, Is.EqualTo(BallEventKind.Touch));
            Assert.That(m.TurnPercent, Is.EqualTo(100.0 / (Frames - 2)).Within(1e-9));
            Assert.That(m.CountOf(BallEventKind.None), Is.EqualTo(0));
        }

        [Test]
        public void ATurnWithoutATouch_HasNoCause_AndTheTallyListsItAsADefect()
        {
            // The touch is two frames after the turn: it explains nothing at frame 5.
            FreeBallTurnMetrics m = Measure(Corner(), Event(15, BallEventKind.Touch));

            Assert.That(m.Turns.Count, Is.EqualTo(1));
            Assert.That(m.Turns[0].Cause, Is.EqualTo(BallEventKind.None));

            var tally = new TurnCrossTally();
            tally.Add(3, m);
            Assert.That(tally.Defects.Count, Is.EqualTo(1));
            Assert.That(tally.Defects[0].Match, Is.EqualTo(3));
            string text = tally.Format("test");
            Assert.That(text, Does.Contain("DEFECTS: 1 free-ball turns with no cause"));
            Assert.That(text, Does.Contain("DEFECT match 3 frame 5"));
        }

        [Test]
        public void ATouchOnTheFirstStepsFrame_ExplainsTheTurn_OneBeforeItDoesNot()
        {
            // Frame 5 holds ticks 9-10, frame 4 ticks 7-8 (the first step's start position).
            Assert.That(Measure(Corner(), Event(9, BallEventKind.Touch)).Turns[0].Cause, Is.EqualTo(BallEventKind.Touch));
            Assert.That(Measure(Corner(), Event(8, BallEventKind.Touch)).Turns[0].Cause, Is.EqualTo(BallEventKind.None));
        }

        [Test]
        public void ADeflection_OutranksTheTouchItCameWith_AndABounceIsACause()
        {
            FreeBallTurnMetrics both = Measure(Corner(), Event(11, BallEventKind.Deflection), Event(11, BallEventKind.Touch));
            Assert.That(both.Turns[0].Cause, Is.EqualTo(BallEventKind.Deflection));

            FreeBallTurnMetrics bounce = Measure(Corner(), Event(10, BallEventKind.Bounce, side: -1, slot: -1));
            Assert.That(bounce.Turns[0].Cause, Is.EqualTo(BallEventKind.Bounce));
        }

        [Test]
        public void TheNearestMan_IsReported_AndAManWithin2m_TakesTheFrameOut()
        {
            PositionStream s = Corner();
            Put(s, false, TurnFrame, Nine, 900, 125);   // 2.5 m from the ball at the turn
            FreeBallTurn turn = Measure(s).Turns.Single();
            Assert.That(turn.NearestHome, Is.False);
            Assert.That(turn.NearestSlot, Is.EqualTo(Nine));
            Assert.That(turn.NearestDm, Is.EqualTo(25));

            Put(s, false, TurnFrame, Nine, 900, 120);   // 2.0 m: not free
            FreeBallTurnMetrics m = Measure(s);
            Assert.That(m.Turns, Is.Empty);
            Assert.That(m.Steps, Is.EqualTo(Frames - 3));
        }

        [Test]
        public void ABallAt1m_ASlowStep_OrADeadFrame_IsNotAStep()
        {
            PositionStream high = Corner();
            high.BallZ[TurnFrame] = 10;
            Assert.That(Measure(high).Turns, Is.Empty, "1 m up at the middle frame: not a low ball");

            PositionStream slow = Corner();
            Ball(slow, TurnFrame + 1, 750 + 30 * TurnFrame, 110);   // 1.0 m in 0.2 s: 5 m/s, not faster
            Assert.That(Measure(slow).Turns, Is.Empty);

            PositionStream dead = Corner();
            Act(dead, TurnFrame + 1, BallActionKind.ThrowIn, true);
            FreeBallTurnMetrics m = Measure(dead);
            Assert.That(m.Turns, Is.Empty);
            Assert.That(m.Steps, Is.EqualTo(TurnFrame - 1), "only the windows that end before the whistle");
        }
    }
}
