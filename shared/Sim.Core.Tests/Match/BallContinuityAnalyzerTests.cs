using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The ball-continuity, shot-flight and keeper readings of the believable-match spec (R2),
    /// pinned against hand-built streams at the engine's 300 frames a minute (0.2 s a frame, so a
    /// step of 2 dm is 1 m/s). The home side attacks the goal at x = 1050 dm; the away keeper
    /// (slot 0) stands 4 m off it.
    /// </summary>
    [TestFixture]
    public class BallContinuityAnalyzerTests
    {
        private const int CY = Pitch.CenterY;
        private const int Striker = 9;
        private const int AwayKeeperX = Pitch.LengthDm - 40;

        private static PositionStream Stream(int ticks = 80)
        {
            PositionStream s = NewStream(ticks);
            s.TicksPerMinute = 300;
            return s;
        }

        private static BallContinuityMetrics Measure(PositionStream s) =>
            BallContinuityAnalyzer.Analyze(new MatchReport { Positions = s })!;

        /// <summary>The ball from frame <paramref name="from"/> on, one point a frame.</summary>
        private static void Path(PositionStream s, int from, params (int X, int Y)[] points)
        {
            for (int i = 0; i < points.Length; i++) Ball(s, from + i, points[i].X, points[i].Y);
        }

        /// <summary>The ball rests at (x, y) from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static void Rest(PositionStream s, int from, int to, int x, int y)
        {
            for (int t = from; t <= to; t++) Ball(s, t, x, y);
        }

        /// <summary>The home striker holds the ball at (900, 340) on frames 10-12; his strike is filed on 13.</summary>
        private static PositionStream Strike(int ticks = 80)
        {
            PositionStream s = Stream(ticks);
            Hold(s, 10, 12, true, Striker, 900, CY);
            Act(s, 13, BallActionKind.Shot, true, Striker);
            return s;
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(BallContinuityAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ jumps

        [Test]
        public void Teleport_Over8mInOneFrame_IsAJump_AndExactly8mIsNot()
        {
            PositionStream s = Stream();
            Rest(s, 0, 9, 300, CY);
            Rest(s, 10, 19, 381, CY);   // 8.1 m
            Rest(s, 20, 79, 461, CY);   // 8.0 m

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.Jumps, Is.EqualTo(1));
            Assert.That(m.JumpsAtRestarts, Is.EqualTo(0), "no restart near it");
        }

        [Test]
        public void Jump_OnARestartFrame_IsCountedAlsoAsARestartJump()
        {
            PositionStream s = Stream();
            Rest(s, 0, 29, 1017, 144);
            Rest(s, 30, 79, 995, CY);
            Act(s, 30, BallActionKind.GoalKick, false, Keeper);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.Jumps, Is.EqualTo(1));
            Assert.That(m.JumpsAtRestarts, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ restarts

        [Test]
        public void Restart_WithTheBallAppearingOnTheSpot_IsPlaced()
        {
            PositionStream s = Stream();
            Rest(s, 0, 29, 1017, 144);
            Rest(s, 30, 79, 995, CY);
            Act(s, 30, BallActionKind.GoalKick, false, Keeper);
            Act(s, 50, BallActionKind.Pass, false, Keeper, 4);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.Restarts, Is.EqualTo(1));
            Assert.That(m.PlacedRestarts, Is.EqualTo(1));
        }

        [Test]
        public void Restart_WithTheBallCarriedToTheSpotAt8mps_IsNotPlaced()
        {
            // 16 dm a frame is 8 m/s: the ball walks from (995, 180) to the spot in ten frames.
            PositionStream s = Stream();
            Rest(s, 0, 19, 995, 180);
            for (int i = 1; i <= 10; i++) Hold(s, 19 + i, 19 + i, false, Keeper, 995, 180 + 16 * i);
            Rest(s, 30, 79, 995, CY);
            Act(s, 30, BallActionKind.GoalKick, false, Keeper);
            Act(s, 50, BallActionKind.Pass, false, Keeper, 4);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.Restarts, Is.EqualTo(1));
            Assert.That(m.PlacedRestarts, Is.EqualTo(0));
        }

        [Test]
        public void KickoffOnTheFirstFrame_IsNotARestartToTransport()
        {
            PositionStream s = Stream();
            Act(s, 0, BallActionKind.Kickoff, true, Striker);

            Assert.That(Measure(s).Restarts, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ ball speed

        [Test]
        public void PassAndShotSpeeds_AreReadPerKind_OverTheFlightFrames()
        {
            // Pass: kicked from (500, 340) on frame 20, 40 dm a frame (20 m/s) until held on 24.
            PositionStream s = Stream();
            Hold(s, 18, 19, true, 5, 500, CY);
            Act(s, 20, BallActionKind.Pass, true, 5, Striker);
            Path(s, 20, (540, CY), (580, CY), (620, CY), (660, CY));
            Hold(s, 24, 30, true, Striker, 670, CY);
            // Shot: 60 dm a frame (30 m/s) from (670, 340) until the save.
            Act(s, 31, BallActionKind.Shot, true, Striker);
            Path(s, 31, (730, CY), (790, CY));
            Act(s, 33, BallActionKind.Save, false, Keeper);
            Hold(s, 33, 40, false, Keeper, 800, CY);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.SpeedFrames(shot: false), Is.EqualTo(4), "pass frames 20-23; the snap into 24 is the end");
            Assert.That(m.SpeedPercentile(shot: false, 0.99), Is.EqualTo(20.0).Within(0.06));
            Assert.That(m.SpeedFrames(shot: true), Is.EqualTo(2), "shot frames 31-32");
            Assert.That(m.SpeedPercentile(shot: true, 0.99), Is.EqualTo(30.0).Within(0.06));
        }

        // ------------------------------------------------------------------ shot flight

        [Test]
        public void WideShot_StoppingShortOfTheLine_IsNotACrossing_AndItsGapIsRead()
        {
            PositionStream s = Strike();
            Path(s, 13, (940, 300), (980, 260), (1010, 240));
            Act(s, 16, BallActionKind.Miss, true, Striker);
            Act(s, 16, BallActionKind.GoalKick, false, Keeper);
            Rest(s, 16, 79, 995, CY);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.WideShots, Is.EqualTo(1));
            Assert.That(m.WideShotsCrossing, Is.EqualTo(0));
            Assert.That(m.WideShotShortSumM, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(m.PlacedRestarts, Is.EqualTo(1), "and it reappears on the goal-kick spot");
        }

        [Test]
        public void WideShot_BeyondTheLineBeforeTheRestart_IsACrossing()
        {
            PositionStream s = Strike();
            Path(s, 13, (950, 290), (1000, 240), (1052, 190));
            Rest(s, 16, 30, 1060, 180);
            Act(s, 16, BallActionKind.Miss, true, Striker);
            Act(s, 31, BallActionKind.GoalKick, false, Keeper);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.WideShots, Is.EqualTo(1));
            Assert.That(m.WideShotsCrossing, Is.EqualTo(1));
            Assert.That(m.WideShotShortSumM, Is.EqualTo(0));
        }

        [Test]
        public void Goal_EndingOnTheLine_IsNotBehindIt_But1mInBetweenThePostsIs()
        {
            PositionStream s = Strike(120);
            StrandKeeper(s, false, 0, 119);
            Path(s, 13, (980, CY), (1050, CY));
            Act(s, 14, BallActionKind.Goal, true, Striker);
            Rest(s, 14, 39, 1050, CY);
            Act(s, 40, BallActionKind.Kickoff, false, Striker);
            Rest(s, 40, 59, Pitch.CenterX, CY);

            Hold(s, 60, 62, true, Striker, 900, CY);
            Act(s, 63, BallActionKind.Shot, true, Striker);
            Path(s, 63, (980, CY), (1050, CY + 10), (1060, CY + 12));
            Act(s, 64, BallActionKind.Goal, true, Striker);
            Rest(s, 66, 119, 1060, CY + 12);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.Goals, Is.EqualTo(2));
            Assert.That(m.GoalsBehindLine, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ keeper

        [Test]
        public void StaticKeeper_OnTheShotsPath_MovesNothing_AndIsNear()
        {
            PositionStream s = Strike();
            Path(s, 13, (940, CY), (980, CY));
            Act(s, 15, BallActionKind.Save, false, Keeper);
            Hold(s, 15, 30, false, Keeper, AwayKeeperX, CY);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.SaveDisplacements, Is.EqualTo(1));
            Assert.That(m.SaveDisplacementSumM, Is.EqualTo(0));
            Assert.That(m.SavesRead, Is.EqualTo(1));
            Assert.That(m.SavesNear, Is.EqualTo(1));
        }

        [Test]
        public void StaticKeeper_3mOffTheShotsPath_IsNotNear_EvenWithTheBallSnappedToHim()
        {
            // The shot runs along y = 340 into the line; the keeper stands still 3 m to the side.
            PositionStream s = Strike();
            for (int t = 0; t < 80; t++) Put(s, false, t, Keeper, AwayKeeperX, CY + 30);
            Path(s, 13, (940, CY), (980, CY));
            Act(s, 15, BallActionKind.Save, false, Keeper);
            Hold(s, 15, 30, false, Keeper, AwayKeeperX, CY + 30);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.SaveDisplacementSumM, Is.EqualTo(0));
            Assert.That(m.SavesRead, Is.EqualTo(1));
            Assert.That(m.SavesNear, Is.EqualTo(0));
        }

        [Test]
        public void KeeperDisplacement_RunsFromReleaseToTheFrameTheBallReachesTheLine()
        {
            // Released on frame 12; the ball is on the line on frame 15, the goal filed on 16. The
            // keeper steps 1 m a frame from frame 13 on: 3 m by frame 15, more after it.
            PositionStream s = Strike();
            for (int t = 13; t < 80; t++) Put(s, false, t, Keeper, AwayKeeperX, CY + 10 * System.Math.Min(t - 12, 6));
            Path(s, 13, (950, 300), (1000, 270), (1050, 250));
            Act(s, 16, BallActionKind.Goal, true, Striker);
            Rest(s, 16, 79, 1050, 250);

            BallContinuityMetrics m = Measure(s);

            Assert.That(m.GoalDisplacements, Is.EqualTo(1));
            Assert.That(m.GoalDisplacementSumM, Is.EqualTo(3.0).Within(1e-9));
        }

        // ------------------------------------------------------------------ the tally

        [Test]
        public void Tally_PoolsMatches_AndPrintsEachReadingWithItsBand()
        {
            var a = new BallContinuityMetrics
            {
                Jumps = 30, JumpsAtRestarts = 20, Restarts = 100, PlacedRestarts = 40,
                WideShots = 8, WideShotsCrossing = 2, WideShotShortSumM = 24,
                Goals = 3, GoalsBehindLine = 0, SaveDisplacements = 5, SaveDisplacementSumM = 5,
                SavesRead = 4, SavesNear = 3,
            };
            var b = new BallContinuityMetrics { Jumps = 10, Restarts = 100, Goals = 1, GoalsBehindLine = 1, SavesRead = 4, SavesNear = 3 };
            for (int i = 0; i < 99; i++) a.AddSpeed(shot: false, 10.0);
            a.AddSpeed(shot: false, 25.0);
            b.AddSpeed(shot: true, 28.0);

            var tally = new BallContinuityTally();
            tally.Add(a);
            tally.Add(b);
            var rows = tally.Rows().ToDictionary(row => row.Name);

            Assert.That(tally.Matches, Is.EqualTo(2));
            Assert.That(rows[BallContinuityTally.JumpsName].Value, Is.EqualTo(20.0));
            Assert.That(rows[BallContinuityTally.JumpsName].InBand, Is.False, "the R5 band is zero");
            Assert.That(rows[BallContinuityTally.JumpsAtRestartsName].Value, Is.EqualTo(10.0));
            Assert.That(rows[BallContinuityTally.PlacedRestartsName].Value, Is.EqualTo(20.0));
            Assert.That(rows[BallContinuityTally.PlacedSharePercentName].Value, Is.EqualTo(20.0));
            Assert.That(rows[RealismReference.Movement.PassSpeedP99.Name].Value, Is.EqualTo(10.0).Within(0.06));
            Assert.That(rows[RealismReference.Movement.ShotSpeedP99.Name].InBand, Is.True);
            Assert.That(rows[BallContinuityTally.WideCrossingName].Value, Is.EqualTo(25.0));
            Assert.That(rows[BallContinuityTally.WideShortName].Value, Is.EqualTo(4.0));
            Assert.That(rows[BallContinuityTally.GoalsBehindName].Value, Is.EqualTo(25.0));
            Assert.That(rows[BallContinuityTally.SaveDisplacementName].Value, Is.EqualTo(1.0));
            Assert.That(rows[BallContinuityTally.KeeperNearName].Value, Is.EqualTo(75.0));
            Assert.That(rows[BallContinuityTally.KeeperNearName].InBand, Is.True);

            string text = tally.Format("V12");
            Assert.That(text, Does.Contain("V12"));
            Assert.That(text, Does.Contain("report only"));
            Assert.That(text, Does.Contain("OUT"));
        }
    }
}
