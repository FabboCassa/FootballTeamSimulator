using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The ball-freedom, crowding, height, pass-flight and loose-ball readings of the
    /// free-ball-real-movement spec (R2, doc §10.3), pinned against hand-built streams at the
    /// engine's 300 frames a minute (0.2 s a frame). The play happens around (850, 100), far from
    /// both keepers and both outfield stacks, so nobody is near the ball unless a test puts him there.
    /// </summary>
    [TestFixture]
    public class BallFreedomAnalyzerTests
    {
        private const int Nine = 9;
        private const int Ten = 10;
        private const int Mate = 1;

        private static PositionStream Stream(int ticks, int ticksPerMinute = 300)
        {
            PositionStream s = NewStream(ticks);
            s.TicksPerMinute = ticksPerMinute;
            for (int t = 0; t < ticks; t++) Ball(s, t, 850, 100);
            return s;
        }

        private static BallFreedomMetrics Measure(PositionStream s) =>
            BallFreedomAnalyzer.Analyze(new MatchReport { Positions = s })!;

        /// <summary>The man stands at (x, y) from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
        private static void Stay(PositionStream s, int from, int to, bool home, int slot, int x, int y)
        {
            for (int t = from; t <= to; t++) Put(s, home, t, slot, x, y);
        }

        /// <summary>The man stands at (x, y) with the ball at his feet.</summary>
        private static void Carry(PositionStream s, int from, int to, bool home, int slot, int x, int y)
        {
            Stay(s, from, to, home, slot, x, y);
            for (int t = from; t <= to; t++) Ball(s, t, x, y);
        }

        private static void BallAt(PositionStream s, int from, int to, int x, int y)
        {
            for (int t = from; t <= to; t++) Ball(s, t, x, y);
        }

        /// <summary>
        /// Home 9 at (700, 100) and home 10 at (900, 100) swap the ball: each holds it for
        /// <paramref name="hold"/> frames on his spot, then it runs free at (800, 100), 10 m from
        /// both, for each gap's frames. A pass's flight is its gap plus one frame.
        /// </summary>
        private static PositionStream Exchange(int hold, params int[] gaps)
        {
            int ticks = hold * (gaps.Length + 1) + gaps.Sum() + 1;
            PositionStream s = Stream(ticks);
            Stay(s, 0, ticks - 1, true, Nine, 700, 100);
            Stay(s, 0, ticks - 1, true, Ten, 900, 100);
            BallAt(s, 0, ticks - 1, 800, 100);
            int t = 0;
            for (int i = 0; i <= gaps.Length; i++)
            {
                BallAt(s, t, t + hold - 1, i % 2 == 0 ? 700 : 900, 100);
                t += hold + (i < gaps.Length ? gaps[i] : 0);
            }

            return s;
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(BallFreedomAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ freedom, crowding, height

        [Test]
        public void BallFree_NobodyWithin15mAnd3m_StrictlyFurther()
        {
            PositionStream s = Stream(10);
            Stay(s, 0, 1, true, Nine, 865, 100);   // 1.5 m: within reach
            Stay(s, 2, 3, true, Nine, 866, 100);   // 1.6 m
            Stay(s, 4, 5, true, Nine, 880, 100);   // 3.0 m: within 3 m
            Stay(s, 6, 9, true, Nine, 881, 100);   // 3.1 m

            BallFreedomMetrics m = Measure(s);

            Assert.That(m.InPlayFrames, Is.EqualTo(10));
            Assert.That(m.Free15Percent, Is.EqualTo(80.0).Within(1e-9));
            Assert.That(m.Free3Percent, Is.EqualTo(40.0).Within(1e-9));
        }

        [Test]
        public void PlayersWithin5mAnd10m_CountBothSidesAndKeepers_StrictlyCloser()
        {
            PositionStream s = Stream(10);
            Stay(s, 0, 9, true, Nine, 899, 100);     // 4.9 m
            Stay(s, 0, 9, false, Nine, 850, 150);    // 5.0 m: within 10 only
            Stay(s, 0, 9, true, Keeper, 850, 199);   // 9.9 m
            Stay(s, 0, 9, false, Ten, 750, 100);     // 10.0 m: neither

            BallFreedomMetrics m = Measure(s);

            Assert.That(m.Within5, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(m.Within10, Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void BallAbove1m_ReadsBallZ_OverTenDecimetres_AndAStreamWithoutHeightIsOnTheGrass()
        {
            PositionStream s = Stream(10);
            s.BallZ = new int[10];
            for (int t = 0; t < 5; t++) s.BallZ[t] = 11;
            s.BallZ[5] = 10;

            Assert.That(Measure(s).Above1mPercent, Is.EqualTo(50.0).Within(1e-9));
            s.BallZ = new int[0];
            Assert.That(Measure(s).Above1mPercent, Is.EqualTo(0.0));
        }

        [Test]
        public void DeadBall_FromTheWhistleUntilTheRestartIsTaken_IsNotMeasured()
        {
            // The throw-in is whistled on frame 5; the taker stands with the ball from frame 8 and
            // throws it on frame 12 (the ball status goes live on the kick, not on the pick-up).
            PositionStream s = Stream(20);
            Act(s, 5, BallActionKind.ThrowIn, true, Nine);
            Hold(s, 8, 19, true, Nine, 850, 100);
            Stay(s, 8, 19, true, Nine, 850, 100);
            Act(s, 9, BallActionKind.YellowCard, false, Nine);
            Act(s, 12, BallActionKind.Pass, true, Nine);

            BallFreedomMetrics m = Measure(s);

            Assert.That(m.InPlayFrames, Is.EqualTo(13), "frames 0-4 and 12-19; a card does not restart play");
            Assert.That(m.Free15Frames, Is.EqualTo(5));
        }

        [Test]
        public void SentOffMan_NoLongerCounts()
        {
            PositionStream s = Stream(10);
            Stay(s, 0, 9, false, Nine, 850, 100);
            Act(s, 3, BallActionKind.RedCard, false, Nine);

            BallFreedomMetrics m = Measure(s);

            Assert.That(m.Free15Frames, Is.EqualTo(7));
            Assert.That(m.Within5Sum, Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ passes

        [Test]
        public void Pass_FlightIsReleaseToArrival_AndTheReceiverFarFromTheArrivalPointMakesItIntoSpace()
        {
            // Home 9 holds frames 0-2; the ball runs free 3-7; home 10, at (880, 130) on release,
            // takes it at (875, 100) on frame 8, 3.04 m from where he stood.
            PositionStream s = Stream(20);
            Carry(s, 0, 2, true, Nine, 800, 100);
            Stay(s, 3, 19, true, Nine, 800, 100);
            for (int i = 0; i < 5; i++) Ball(s, 3 + i, 820 + 10 * i, 100);
            Stay(s, 0, 7, true, Ten, 880, 130);
            Carry(s, 8, 10, true, Ten, 875, 100);
            Stay(s, 11, 19, true, Ten, 875, 100);
            BallAt(s, 11, 19, 875, 200);

            BallFreedomMetrics m = Measure(s);

            Assert.That(m.Passes, Is.EqualTo(1));
            Assert.That(m.FlightP50Seconds, Is.EqualTo(1.2).Within(1e-9));
            Assert.That(m.FlightP90Seconds, Is.EqualTo(1.2).Within(1e-9));
            Assert.That(m.ReceiverToArrivalP50Meters, Is.EqualTo(System.Math.Sqrt(925) / 10).Within(1e-9));
            Assert.That(m.IntoSpacePercent, Is.EqualTo(100.0));
        }

        [Test]
        public void PassPercentiles_AreNumpysLinearOnes_AndAPassToTheMansFeetIsNotIntoSpace()
        {
            // Flights of 1, 2, 3, 4 and 10 frames: 0.2-0.8 s and 2.0 s.
            BallFreedomMetrics m = Measure(Exchange(3, 0, 1, 2, 3, 9));

            Assert.That(m.Passes, Is.EqualTo(5));
            Assert.That(m.FlightP50Seconds, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(m.FlightP90Seconds, Is.EqualTo(0.8 + 0.6 * 1.2).Within(1e-9));
            Assert.That(m.ReceiverToArrivalP50Meters, Is.EqualTo(0.0));
            Assert.That(m.IntoSpacePercent, Is.EqualTo(0.0));
        }

        [Test]
        public void Pass_LongerThan6s_IsDropped_AndExactly6sIsKept()
        {
            Assert.That(Measure(Exchange(3, 29)).Passes, Is.EqualTo(1), "30 frames = 6.0 s");
            Assert.That(Measure(Exchange(3, 30)).Passes, Is.EqualTo(0), "31 frames = 6.2 s");
        }

        [Test]
        public void Pass_ToAnOpponent_OrBackToHimself_IsNot()
        {
            PositionStream s = Exchange(3, 2, 2);
            Stay(s, 0, s.TickCount - 1, true, Ten, 600, 300);
            Stay(s, 0, s.TickCount - 1, false, Ten, 900, 100);   // the opponent takes the first ball
            Assert.That(Measure(s).Passes, Is.EqualTo(0), "9 -> away 10 -> 9");

            PositionStream dribble = Exchange(3, 2, 2);
            Stay(dribble, 0, dribble.TickCount - 1, true, Ten, 700, 100);
            Assert.That(Measure(dribble).Passes, Is.EqualTo(0), "the ball comes back to 9 alone");
        }

        [Test]
        public void Pass_ThroughADeadBall_IsDropped()
        {
            PositionStream s = Exchange(3, 4);
            Assert.That(Measure(s).Passes, Is.EqualTo(1));

            // Whistled mid-flight on frame 4; home 10 takes the free kick from his spot on frame 7.
            Act(s, 4, BallActionKind.FreeKick, true, Ten);
            Act(s, 7, BallActionKind.Pass, true, Ten);
            Assert.That(Measure(s).Passes, Is.EqualTo(0));
        }

        [Test]
        public void Pass_WithAShortTouchInBetween_IsDropped_AtTenFramesASecond()
        {
            // At 10 fps a holder needs 2 frames; team-mate 1's single frame on the ball drops 9 -> 10.
            PositionStream s = Exchange(3, 5);
            s.TicksPerMinute = 600;
            Stay(s, 0, s.TickCount - 1, true, Mate, 600, 300);
            Carry(s, 5, 5, true, Mate, 800, 100);
            Assert.That(Measure(s).Passes, Is.EqualTo(0));

            s.TicksPerMinute = 300;
            Assert.That(Measure(s).Passes, Is.EqualTo(2), "at 5 fps one frame is a run: 9 -> 1 -> 10");
        }

        // ------------------------------------------------------------------ loose balls

        /// <summary>
        /// Home 9 holds frames 0-2 at (800, 100); the ball runs 2 frames at (820, 100); on frame 5 away 9
        /// touches it at (840, 100) with home 9 at (850, 100) — within reach only when
        /// <paramref name="duel"/>; then it is free at (900, 200) from frame 6 for <paramref name="freeFrames"/>.
        /// </summary>
        private static PositionStream Touch(bool duel, int freeFrames, int awayTouchFrames = 1)
        {
            int last = 5 + awayTouchFrames - 1 + freeFrames;
            PositionStream s = Stream(last + 1);
            Carry(s, 0, 2, true, Nine, 800, 100);
            Stay(s, 3, last, true, Nine, duel ? 850 : 800, 100);
            Stay(s, 3, 4, true, Nine, 800, 100);
            BallAt(s, 3, 4, 820, 100);
            Stay(s, 0, last, false, Nine, 840, 105);
            BallAt(s, 5, 5 + awayTouchFrames - 1, 840, 100);
            BallAt(s, 5 + awayTouchFrames, last, 900, 200);
            return s;
        }

        [Test]
        public void LooseBall_AfterAShortTouchInADuel_IsAnEpisode()
        {
            BallFreedomMetrics m = Measure(Touch(duel: true, freeFrames: 5));

            Assert.That(m.LooseDuels, Is.EqualTo(1));
            Assert.That(m.LooseDeflections, Is.EqualTo(0));
        }

        [Test]
        public void LooseBall_AfterTheOtherTeamsShortTouch_IsADeflection()
        {
            BallFreedomMetrics m = Measure(Touch(duel: false, freeFrames: 5));

            Assert.That(m.LooseDuels, Is.EqualTo(0));
            Assert.That(m.LooseDeflections, Is.EqualTo(1));
            Assert.That(m.LooseBalls, Is.EqualTo(1));
        }

        [Test]
        public void LooseBall_NeedsAFreeSecond_AndATouchUnder04s()
        {
            Assert.That(Measure(Touch(duel: true, freeFrames: 4)).LooseBalls, Is.EqualTo(0), "0.8 s free");
            Assert.That(Measure(Touch(duel: true, freeFrames: 5, awayTouchFrames: 2)).LooseBalls, Is.EqualTo(0),
                "0.4 s of control is a release");
        }

        [Test]
        public void LooseBall_OffATeamMatesTouch_OutsideADuel_IsNot()
        {
            PositionStream s = Touch(duel: false, freeFrames: 5);
            Stay(s, 0, s.TickCount - 1, false, Nine, 600, 300);
            Stay(s, 0, s.TickCount - 1, true, Ten, 840, 105);

            Assert.That(Measure(s).LooseBalls, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ the tally

        [Test]
        public void Tally_AveragesTheMatches_PassRowsOverMatchesWithPasses_AndPrintsTheBands()
        {
            var a = new BallFreedomMetrics
            {
                InPlayFrames = 100, Free15Frames = 40, Free3Frames = 20, Above1mFrames = 30,
                Within5Sum = 160, Within10Sum = 350, Passes = 10, PassesIntoSpace = 4,
                FlightP50Seconds = 1.0, FlightP90Seconds = 2.0, ReceiverToArrivalP50Meters = 2.5,
                LooseDuels = 100, LooseDeflections = 20,
            };
            var b = new BallFreedomMetrics { InPlayFrames = 200, Free15Frames = 40, Free3Frames = 20, LooseDuels = 60 };

            var tally = new BallFreedomTally();
            tally.Add(a);
            tally.Add(b);
            var rows = tally.Rows().ToDictionary(row => row.Name);

            Assert.That(tally.Matches, Is.EqualTo(2));
            Assert.That(rows[RealismReference.Tracking.BallFree15Percent.Name].Value, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.BallFree15Percent.Name].InBand, Is.False);
            Assert.That(rows[RealismReference.Tracking.BallFree3Percent.Name].Value, Is.EqualTo(15.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.PlayersWithin5m.Name].Value, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.PlayersWithin10m.Name].Value, Is.EqualTo(1.75).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.BallAbove1mPercent.Name].Value, Is.EqualTo(15.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.PassFlightP50Seconds.Name].Value, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.PassFlightP50Seconds.Name].InBand, Is.True);
            Assert.That(rows[RealismReference.Tracking.PassFlightP90Seconds.Name].Value, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.ReceiverToArrivalP50Meters.Name].Value, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.PassIntoSpacePercent.Name].Value, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(rows[BallFreedomTally.PassesName].Value, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.LooseBallsPerMatch.Name].Value, Is.EqualTo(90.0).Within(1e-9));
            Assert.That(rows[RealismReference.Tracking.LooseBallsPerMatch.Name].InBand, Is.True);
            Assert.That(rows[BallFreedomTally.LooseDuelsName].Value, Is.EqualTo(80.0).Within(1e-9));
            Assert.That(rows[BallFreedomTally.LooseDeflectionsName].Value, Is.EqualTo(10.0).Within(1e-9));

            string text = tally.Format("pitch");
            Assert.That(text, Does.Contain("(2 matches)"));
            Assert.That(text, Does.Contain(RealismReference.Tracking.BallFree15Percent.Name));
            Assert.That(text, Does.Contain(RealismReference.Tracking.BallFree15Percent.Describe()));
            Assert.That(text, Does.Contain("OUT"));
        }
    }
}
