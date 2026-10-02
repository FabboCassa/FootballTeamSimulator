using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The shape, movement, pitch-bounds and keeper readings of the real-match spec (R4, R5, R6,
    /// R8), pinned against streams built by hand. Every expected value is worked out on paper from
    /// the definitions in <see cref="ShapeMovementAnalyzer"/>, not recorded from a run.
    ///
    /// The streams run at 120 frames a minute, so one frame is half a second.
    /// </summary>
    [TestFixture]
    public class ShapeMovementAnalyzerTests
    {
        private const int CY = Pitch.CenterY;

        private static ShapeMovementMetrics Measure(PositionStream s) =>
            ShapeMovementAnalyzer.Analyze(new MatchReport { Positions = s })!;

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(ShapeMovementAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ R4: team shape

        /// <summary>
        /// Home defends with its outfielders 20-50 m up the pitch (30 m deep) and 16.5-51.5 m across
        /// (35 m wide); away attacks with men 5 m and 4 m off the two touchlines (59 m wide).
        /// </summary>
        private static void DefendingBlock(PositionStream s, int from, int to, int highestX)
        {
            for (int t = from; t <= to; t++)
            {
                for (int i = 1; i < Players; i++) Put(s, true, t, i, 350, CY);
                Put(s, true, t, 1, 200, CY - 175);
                Put(s, true, t, 10, highestX, CY + 175);
                Put(s, false, t, 1, AwayStackX, 50);
                Put(s, false, t, 2, AwayStackX, 640);
            }
        }

        [Test]
        public void Shape_ADefensivePhaseInOwnHalf_PinsBlockLengthAndWidth()
        {
            PositionStream s = NewStream(20);
            DefendingBlock(s, 0, 19, 500);
            Hold(s, 0, 19, false, 9, 300, CY);        // away on the ball in the home half

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.DefendingSamples, Is.EqualTo(20));
            Assert.That(m.OwnHalfBlockLengthM, Is.EqualTo(30.0));
            Assert.That(m.BlockLengthM, Is.EqualTo(30.0));
            Assert.That(m.BlockWidthM, Is.EqualTo(35.0));
            Assert.That(m.AttackingWidthM, Is.EqualTo(59.0));
            Assert.That(m.BothTouchlinesPercent, Is.EqualTo(100.0));
        }

        [Test]
        public void Shape_BallInTheOtherHalf_CountsOnlyAnywhereAndNotAttackingWidth()
        {
            PositionStream s = NewStream(20);
            DefendingBlock(s, 0, 9, 500);
            DefendingBlock(s, 10, 19, 700);           // 50 m deep while the ball is in away's half
            Hold(s, 0, 9, false, 9, 300, CY);
            Hold(s, 10, 19, false, 9, 800, CY);

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.OwnHalfDefendingSamples, Is.EqualTo(10));
            Assert.That(m.OwnHalfBlockLengthM, Is.EqualTo(30.0));
            Assert.That(m.BlockLengthM, Is.EqualTo(40.0));
            Assert.That(m.AttackingSamples, Is.EqualTo(10));
        }

        [Test]
        public void Shape_OneTouchlineZoneEmpty_HalvesTheOccupancy()
        {
            PositionStream s = NewStream(20);
            DefendingBlock(s, 0, 19, 500);
            for (int t = 10; t < 20; t++) Put(s, false, t, 2, AwayStackX, 500);   // 18 m off the line
            Hold(s, 0, 19, false, 9, 300, CY);

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.AttackingWidthM, Is.EqualTo((59.0 + 45.0) / 2));
            Assert.That(m.BothTouchlinesPercent, Is.EqualTo(50.0));
        }

        [Test]
        public void OpenPlay_ARestartIsDeadUntilTheTakerHasTheBall()
        {
            PositionStream s = NewStream(20);
            Hold(s, 0, 9, false, 9, 300, CY);
            Act(s, 10, BallActionKind.ThrowIn, false, 3);
            Hold(s, 14, 19, false, 3, 300, 5);

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.OpenPlayFrames, Is.EqualTo(16));
            Assert.That(m.DefendingSamples, Is.EqualTo(16));
        }

        [Test]
        public void OpenPlay_AGoalIsDeadUntilTheKickoffIsTaken()
        {
            PositionStream s = NewStream(20);
            Hold(s, 0, 4, true, 9, 1000, CY);
            Act(s, 5, BallActionKind.Goal, true, 9);
            Hold(s, 6, 7, true, 9, 1049, CY);         // an owner during the celebration is still dead
            Act(s, 8, BallActionKind.Kickoff, false, 1);
            Hold(s, 12, 19, false, 1, Pitch.CenterX, CY);

            Assert.That(Measure(s).OpenPlayFrames, Is.EqualTo(13));
        }

        [Test]
        public void OffTarget_IsTheMedianOverTheTwentyOutfielders()
        {
            // RealismAnalyzerTests' scenario: eleven men off for ten seconds each, nine never.
            PositionStream s = NewStream(350);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int r = 0; r < 11; r++)
            {
                bool home = r < 10;
                int slot = home ? r + 1 : 1;
                for (int t = 10 + 30 * r; t < 30 + 30 * r; t++)
                    Put(s, home, t, slot, home ? HomeStackX : AwayStackX, home ? CY + 300 : CY - 300);
            }

            Assert.That(Measure(s).MedianOffTargetSeconds, Is.EqualTo(10.0));
        }

        // ------------------------------------------------------------------ R8: movement

        [Test]
        public void StandStill_UnderPointTwoMetresASecond_IsStandingStill()
        {
            PositionStream s = NewStream(11);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int t = 0; t < 11; t++)
            {
                Put(s, true, t, 5, HomeStackX + 2 * t, CY);   // 0.4 m/s
                Put(s, true, t, 6, HomeStackX + t, CY);       // exactly 0.2 m/s: moving
            }

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.OutfieldOpenPlayFrames, Is.EqualTo(200));
            Assert.That(m.StandStillFrames, Is.EqualTo(180));
            Assert.That(m.StandStillPercent, Is.EqualTo(90.0));
        }

        [Test]
        public void StandStill_DeadFramesAreLeftOut()
        {
            PositionStream s = NewStream(11);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            Act(s, 1, BallActionKind.ThrowIn, true, 2);
            Hold(s, 6, 10, true, 2, Pitch.CenterX, 5);

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.OutfieldOpenPlayFrames, Is.EqualTo(100));
            Assert.That(m.StandStillFrames, Is.EqualTo(100));
        }

        [Test]
        public void Distance_IsTheOutfieldTotalOverTen_KeepersLeftOut()
        {
            PositionStream s = NewStream(11);
            for (int t = 0; t < 11; t++)
            {
                Put(s, true, t, 5, HomeStackX + 10 * t, CY);  // 10 m in all
                Put(s, true, t, Keeper, 40 + 10 * t, CY);     // the keeper's run is not counted
                Put(s, false, t, 5, AwayStackX, CY - 30 * t); // 30 m in all
            }

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.DistancePerOutfieldPlayerKm(home: true), Is.EqualTo(0.001).Within(1e-12));
            Assert.That(m.DistancePerOutfieldPlayerKm(home: false), Is.EqualTo(0.003).Within(1e-12));
        }

        // ------------------------------------------------------------------ R5: pitch bounds

        [Test]
        public void OffPitch_MoreThanOneMetreOut_IsCountedPerFrame()
        {
            PositionStream s = NewStream(20);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int t = 2; t <= 9; t++)
            {
                Put(s, true, t, 5, HomeStackX, -10);              // exactly 1 m out: allowed
                Put(s, true, t, 6, HomeStackX, -11);              // 8 frames
            }

            for (int t = 2; t <= 4; t++) Put(s, false, t, 5, Pitch.LengthDm + 11, CY);   // 3 frames, no ball

            Assert.That(Measure(s).OffPitchFrames, Is.EqualTo(11));
        }

        [Test]
        public void OffPitch_ABallCarryUnderTwoSeconds_IsExempt()
        {
            PositionStream s = NewStream(20);
            Hold(s, 3, 3, true, 7, 300, Pitch.WidthDm - 5);
            for (int t = 4; t <= 6; t++) Put(s, true, t, 7, 300, Pitch.WidthDm + 20);    // 1.5 s: exempt
            Hold(s, 10, 10, true, 8, 300, Pitch.WidthDm - 5);
            for (int t = 11; t <= 14; t++) Put(s, true, t, 8, 300, Pitch.WidthDm + 20);  // 2 s: counted

            Assert.That(Measure(s).OffPitchFrames, Is.EqualTo(4));
        }

        [Test]
        public void OffPitch_TheThrowInTaker_IsExempt_ABystanderIsNot()
        {
            PositionStream s = NewStream(20);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            Act(s, 5, BallActionKind.ThrowIn, true, 3);
            for (int t = 5; t <= 12; t++) Put(s, true, t, 3, 300, -15);    // before, at and after the throw
            Hold(s, 9, 9, true, 3, 300, 5);
            for (int t = 5; t <= 8; t++) Put(s, true, t, 4, 400, -15);     // 4 frames

            Assert.That(Measure(s).OffPitchFrames, Is.EqualTo(4));
        }

        [Test]
        public void OffPitch_AManSentOff_IsNoLongerCounted()
        {
            PositionStream s = NewStream(20);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            Act(s, 5, BallActionKind.RedCard, false, 2);
            for (int t = 6; t < 20; t++) Put(s, false, t, 2, AwayStackX, -50);

            Assert.That(Measure(s).OffPitchFrames, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ R6: keeper depth

        [Test]
        public void Keeper_DepthRule_SixMetresNearHisGoal_EighteenOtherwise()
        {
            PositionStream s = NewStream(40);
            Hold(s, 0, 19, false, 9, 200, CY);        // 20 m from the home goal
            Hold(s, 20, 39, false, 9, 600, CY);       // 60 m from it
            for (int t = 0; t < 40; t++)
            {
                int x = t < 10 ? 70 : t < 20 ? 60 : t < 30 ? 190 : 180;
                Put(s, true, t, Keeper, x, CY);
            }

            ShapeMovementMetrics m = Measure(s);

            Assert.That(m.KeeperOpenPlayFrames, Is.EqualTo(80));
            Assert.That(m.KeeperDepthBreakFrames, Is.EqualTo(20));
            Assert.That(m.KeeperDepthBreakPercent, Is.EqualTo(25.0));
        }

        [TestCase(false, 0)]    // he is the nearest man to the loose ball: claiming it
        [TestCase(true, 5)]     // a defender is nearer: he is simply off his line
        public void Keeper_ClaimingTheBall_IsExempt(bool defenderNearer, int breaks)
        {
            PositionStream s = NewStream(10);
            for (int t = 0; t < 10; t++) Put(s, true, t, Keeper, 150, CY);
            Hold(s, 0, 4, true, Keeper, 150, CY);     // he holds it 15 m out
            for (int t = 5; t < 10; t++)
            {
                Ball(s, t, 165, CY);
                if (defenderNearer) Put(s, true, t, 2, 160, CY);
            }

            Assert.That(Measure(s).KeeperDepthBreakFrames, Is.EqualTo(breaks));
        }

        // ------------------------------------------------------------------ the printed report

        [Test]
        public void Tally_PoolsTheReadingsAndMarksEachTarget()
        {
            var match = new ShapeMovementMetrics
            {
                OpenPlayFrames = 100,
                DefendingSamples = 100, DefendingLengthDm = 100 * 420, DefendingWidthDm = 100 * 380,
                OwnHalfDefendingSamples = 50, OwnHalfLengthDm = 50 * 350,
                AttackingSamples = 40, AttackingWidthDm = 40 * 400, BothTouchlinesSamples = 10,
                MedianOffTargetSeconds = 4,
                OutfieldOpenPlayFrames = 2000, StandStillFrames = 400,
                HomeOutfieldDistanceDm = 1_000_000, AwayOutfieldDistanceDm = 1_100_000,
                OffPitchFrames = 3,
                KeeperOpenPlayFrames = 200, KeeperDepthBreakFrames = 1
            };
            var tally = new ShapeMovementTally();
            tally.Add(match);
            tally.Add(match);

            var rows = new System.Collections.Generic.Dictionary<string, ShapeMovementRow>();
            foreach (ShapeMovementRow row in tally.Rows()) rows[row.Name] = row;

            AssertRow(rows[ShapeMovementTargets.OwnHalfBlockLengthM.Name], 35.0, true);
            AssertRow(rows[ShapeMovementTargets.BlockLengthM.Name], 42.0, true);
            AssertRow(rows[ShapeMovementTargets.BlockWidthM.Name], 38.0, true);
            AssertRow(rows[ShapeMovementTargets.AttackingWidthM.Name], 40.0, false);
            AssertRow(rows[ShapeMovementTargets.BothTouchlinesName], 25.0, null);
            AssertRow(rows[ShapeMovementTargets.MedianOffTargetSeconds.Name], 4.0, true);
            AssertRow(rows[ShapeMovementTargets.StandStillPercent.Name], 20.0, false);
            AssertRow(rows[RealismReference.DistancePerOutfieldPlayerKm.Name], 10.5, true);
            AssertRow(rows[ShapeMovementTargets.OffPitchFrames.Name], 6.0, false);
            AssertRow(rows[ShapeMovementTargets.KeeperDepthBreakPercent.Name], 0.5, true);

            string text = tally.Format("V11");
            Assert.That(text, Does.Contain("2 matches"));
            Assert.That(text, Does.Contain("spec R4"));
            Assert.That(text, Does.Contain("RealismReference"));
            Assert.That(text, Does.Contain("report only"));
            Assert.That(text, Does.Contain("OUT"));
        }

        private static void AssertRow(ShapeMovementRow row, double value, bool? inBand)
        {
            Assert.That(row.Value, Is.EqualTo(value).Within(1e-9), row.Name);
            Assert.That(row.InBand, Is.EqualTo(inBand), row.Name);
        }
    }
}
