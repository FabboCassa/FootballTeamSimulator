using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The cross label by geometry (free-ball-real-movement spec R2, doc §11.3) on hand-built
    /// streams at two ticks a frame: a pass struck on frame 2 (tick 3), met on frame 8 (tick 15).
    /// The home side attacks x = 1050; its final third starts at x = 700, its wide channel is
    /// y &lt;= 138 or y &gt;= 542 (13.84 m from a touchline), and the box is x &gt;= 885, 139 &lt;= y &lt;= 541.
    /// </summary>
    [TestFixture]
    public class GeometricCrossAnalyzerTests
    {
        private const int TicksPerFrame = 2;
        private const int Frames = 20;
        private const int PassFrame = 2;
        private const int KickTick = 3;
        private const int MeetTick = 15;
        private const int Winger = 7;
        private const int Striker = 9;

        private static PositionStream Stream(int peakDm)
        {
            PositionStream s = NewStream(Frames);
            s.TicksPerMinute = 300;
            s.BallZ = new int[Frames];
            s.BallZ[5] = peakDm;
            return s;
        }

        private static BallEvent Touch(int tick, int side, int slot, int x, int y) =>
            new BallEvent { Tick = tick, Kind = BallEventKind.Touch, Side = side, Slot = slot, XDm = x, YDm = y };

        /// <summary>A home pass from (fromX, fromY) met by a team-mate at (toX, toY), with the ball peaking at <paramref name="peakDm"/>.</summary>
        private static GeometricCrossMetrics Pass(int fromX, int fromY, int toX, int toY, int peakDm = 25,
            BallActionKind label = BallActionKind.Cross)
        {
            PositionStream s = Stream(peakDm);
            Act(s, PassFrame, label, true, Winger, Striker);
            var events = new List<BallEvent>
            {
                Touch(KickTick, 0, Winger, fromX, fromY),
                new BallEvent { Tick = 9, Kind = BallEventKind.Bounce, Side = 0, Slot = Winger, XDm = 950, YDm = 120 },
                Touch(MeetTick, 0, Striker, toX, toY),
            };
            return GeometricCrossAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(GeometricCrossAnalyzer.Analyze(new MatchReport(), new List<BallEvent>(), TicksPerFrame), Is.Null);
        }

        [Test]
        public void AHighCross_FromTheWideFinalThirdIntoTheBox_PeaksAt2m()
        {
            GeometricCrossMetrics m = Pass(900, 60, 980, 300, peakDm: 20);

            Assert.That(m.HomeHigh, Is.EqualTo(1));
            Assert.That(m.HomeLow + m.AwayHigh + m.AwayLow, Is.EqualTo(0));
            Assert.That(m.HomeLabelled, Is.EqualTo(1), "the engine's own label is counted beside it");
        }

        [Test]
        public void ALowCross_OrCutBack_PeaksUnder2m_WhateverTheEngineCalledIt()
        {
            GeometricCrossMetrics m = Pass(1000, 620, 960, 400, peakDm: 19, label: BallActionKind.Pass);

            Assert.That(m.HomeLow, Is.EqualTo(1));
            Assert.That(m.High, Is.EqualTo(0));
            Assert.That(m.Labelled, Is.EqualTo(0));
        }

        [Test]
        public void AWidePass_ThatIsNotACross_IsNotCounted()
        {
            Assert.That(Total(Pass(900, 60, 980, 130)), Is.EqualTo(0), "it stays outside the box's side line (13.0 m in)");
            Assert.That(Total(Pass(900, 139, 980, 300)), Is.EqualTo(0), "struck 13.9 m in: not the wide channel");
            Assert.That(Total(Pass(690, 60, 980, 300)), Is.EqualTo(0), "struck before the final third");
            Assert.That(Total(Pass(900, 60, 880, 300)), Is.EqualTo(0), "met 17 m out: outside the box");
            Assert.That(Total(Pass(900, 138, 885, 139)), Is.EqualTo(1), "the edges: 13.8 m in, met on the box lines");
        }

        [Test]
        public void ARestartsKick_IsNotOpenPlay()
        {
            PositionStream s = Stream(25);
            Act(s, 0, BallActionKind.Corner, true, Winger);
            Act(s, PassFrame, BallActionKind.Cross, true, Winger, Striker);
            var events = new List<BallEvent> { Touch(KickTick, 0, Winger, 1040, 10), Touch(MeetTick, 0, Striker, 980, 300) };

            GeometricCrossMetrics m = GeometricCrossAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;

            Assert.That(Total(m), Is.EqualTo(0));
        }

        [Test]
        public void ABallThatGoesDeadBeforeAnyTouch_ArrivesWhereItWasOnTheLastFrameInPlay_AndTheAwaySideAttacksX0()
        {
            PositionStream s = Stream(5);
            Act(s, PassFrame, BallActionKind.Cross, false, Winger, Striker);
            Ball(s, 9, 40, 300);                               // in the home box, on the last frame in play
            Act(s, 10, BallActionKind.GoalKick, true, 0);
            var events = new List<BallEvent> { Touch(KickTick, 1, Winger, 200, 600), Touch(25, 0, 0, 40, 300) };

            GeometricCrossMetrics m = GeometricCrossAnalyzer.Analyze(new MatchReport { Positions = s }, events, TicksPerFrame)!;

            Assert.That(m.AwayLow, Is.EqualTo(1));
            Assert.That(m.HomeHigh + m.HomeLow + m.AwayHigh, Is.EqualTo(0));
        }

        private static int Total(GeometricCrossMetrics m) => m.High + m.Low;
    }
}
