using System;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The shot, keeper and passing readings of the real-match spec (R2, R7, R9, R10), pinned
    /// against hand-built streams. Every expected value is worked out on paper from the harness
    /// definitions of docs/research/football-reference.md §1.5 and <see cref="ShotPassAnalyzer"/>.
    ///
    /// The streams run at 120 frames a minute, so one frame is half a second. A kick is read
    /// where the ball was the frame before it is filed (the stream files a kick on the first
    /// frame after it), a shot on its own frame.
    /// </summary>
    [TestFixture]
    public class ShotPassAnalyzerTests
    {
        private const int CY = Pitch.CenterY;

        private static ShotPassMetrics Measure(PositionStream s) =>
            ShotPassAnalyzer.Analyze(new MatchReport { Positions = s })!;

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(ShotPassAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ shots

        [Test]
        public void Shots_SplitIntoOnTargetBlockedInsideBox_AndSaveRates()
        {
            PositionStream s = NewStream(60);
            Ball(s, 10, 950, CY); Act(s, 10, BallActionKind.Shot, true, 9); Act(s, 12, BallActionKind.Save, false, Keeper);
            Ball(s, 20, 800, CY); Act(s, 20, BallActionKind.Shot, true, 9); Act(s, 22, BallActionKind.Goal, true, 9);
            Ball(s, 30, 100, CY); Act(s, 30, BallActionKind.Shot, false, 9); Act(s, 31, BallActionKind.Block, true, 3);
            Ball(s, 40, 100, CY); Act(s, 40, BallActionKind.Shot, false, 9); Act(s, 42, BallActionKind.Miss, false, 9);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.Shots, Is.EqualTo(4));
            Assert.That(m.ShotsOnTarget, Is.EqualTo(2), "goal + save");
            Assert.That(m.ShotsBlocked, Is.EqualTo(1));
            Assert.That(m.ShotsInsideBox, Is.EqualTo(3), "950 and both 100s; 800 is outside");
            Assert.That(m.OnTargetPercent, Is.EqualTo(50.0));
            Assert.That(m.BlockedPercent, Is.EqualTo(25.0));
            Assert.That(m.InsideBoxPercent, Is.EqualTo(75.0));
            Assert.That(m.Saves, Is.EqualTo(1));
            Assert.That(m.ShotGoals, Is.EqualTo(1));
            Assert.That(m.SaveRatePercent, Is.EqualTo(50.0));
            Assert.That(m.SaveRateInsideBoxPercent, Is.EqualTo(100.0), "the save was inside");
            Assert.That(m.SaveRateOutsideBoxPercent, Is.EqualTo(0.0), "the goal was outside");
        }

        [Test]
        public void Shots_OnTheBoxLine_AreInside()
        {
            PositionStream s = NewStream(40);
            Ball(s, 10, Pitch.LengthDm - 165, CY + 201); Act(s, 10, BallActionKind.Shot, true, 9);   // corner of the area
            Ball(s, 20, Pitch.LengthDm - 166, CY); Act(s, 20, BallActionKind.Shot, true, 9);         // a decimetre short
            Ball(s, 30, Pitch.LengthDm - 165, CY + 202); Act(s, 30, BallActionKind.Shot, true, 9);   // a decimetre wide

            Assert.That(Measure(s).ShotsInsideBox, Is.EqualTo(1));
        }

        [Test]
        public void SaveRate_CountsPenalties_AndLeavesOwnGoalsOut()
        {
            PositionStream s = NewStream(40);
            Act(s, 5, BallActionKind.Penalty, true, 9);
            Hold(s, 7, 7, true, 9, 940, CY);
            Ball(s, 8, 940, CY); Act(s, 8, BallActionKind.Shot, true, 9); Act(s, 9, BallActionKind.Goal, true, 9);
            Act(s, 20, BallActionKind.Goal, false, 4);   // nobody struck it: an own goal

            ShotPassMetrics m = Measure(s);

            Assert.That(m.Shots, Is.EqualTo(1));
            Assert.That(m.ShotGoals, Is.EqualTo(1));
            Assert.That(m.SaveRatePercent, Is.EqualTo(0.0));
        }

        [Test]
        public void Headed_IsAFirstTimeStrikeOffATeammatesCross()
        {
            PositionStream s = NewStream(60);
            // Headed: he has it one frame (0.5 s) when he strikes.
            Hold(s, 8, 9, true, 7, 800, 60); Act(s, 10, BallActionKind.Cross, true, 7, 9);
            Hold(s, 12, 12, true, 9, 960, CY); Ball(s, 13, 960, CY); Act(s, 13, BallActionKind.Shot, true, 9);
            // Not headed: three frames (1.5 s) on the ball first.
            Hold(s, 20, 21, true, 7, 800, 60); Act(s, 22, BallActionKind.Cross, true, 7, 9);
            Hold(s, 24, 27, true, 9, 960, CY); Act(s, 27, BallActionKind.Shot, true, 9);
            // Not headed: he beats a man first.
            Hold(s, 30, 31, true, 7, 800, 60); Act(s, 32, BallActionKind.Cross, true, 7, 9);
            Hold(s, 34, 34, true, 9, 960, CY); Act(s, 35, BallActionKind.Dribble, true, 9); Act(s, 35, BallActionKind.Shot, true, 9);

            Assert.That(Measure(s).HeadedShots, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ R7: keeper reach

        [Test]
        public void KeeperReach_ReadsTheCrossingPointAndTheBallsPath()
        {
            PositionStream s = NewStream(60);
            // The first four strikes run straight up the pitch at y = CY + 20: they cross the line at (1050, CY + 20).
            Strike(s, 10);
            Put(s, false, 13, Keeper, 1040, CY + 20);   // 1.0 m from the crossing point: reached
            Act(s, 13, BallActionKind.Save, false, Keeper);
            Strike(s, 20);
            Put(s, false, 23, Keeper, 1030, CY - 20);   // 4.47 m from it, 4 m off the path: not reached
            Act(s, 23, BallActionKind.Save, false, Keeper);
            Strike(s, 30);
            Put(s, false, 33, Keeper, 950, CY + 30);    // 10.05 m from it, 1 m off the path: met it
            Act(s, 33, BallActionKind.Save, false, Keeper);
            Strike(s, 40);
            Hold(s, 41, 41, false, Keeper, 1040, CY);         // caught at once, never seen free: unmeasured
            Act(s, 41, BallActionKind.Save, false, Keeper);
            // Struck from his boot at (800, CY + 20) on the frame before; one frame on, the ball is
            // at (850, CY + 40), so the path crosses the line at (1050, CY + 120).
            Hold(s, 49, 49, true, 9, 800, CY + 20);
            Ball(s, 50, 850, CY + 40); Act(s, 50, BallActionKind.Shot, true, 9);
            Ball(s, 51, 900, CY + 40);
            Put(s, false, 53, Keeper, 1040, CY + 120);  // 1.0 m from that crossing point: reached
            Act(s, 53, BallActionKind.Save, false, Keeper);
            // Parried on the very next frame: the free ball there is already off his hands, not on the path.
            Hold(s, 55, 55, true, 9, 850, CY + 20); Act(s, 55, BallActionKind.Shot, true, 9);
            Ball(s, 56, 980, 100); Act(s, 56, BallActionKind.Save, false, Keeper);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.Saves, Is.EqualTo(6));
            Assert.That(m.SavesMeasured, Is.EqualTo(4));
            Assert.That(m.SavesReached, Is.EqualTo(3));
            Assert.That(m.KeeperReachPercent, Is.EqualTo(75.0).Within(1e-9));
            double expected = (1.0 + Math.Sqrt(20 * 20 + 40 * 40) / 10 + Math.Sqrt(100 * 100 + 10 * 10) / 10 + 1.0) / 4;
            Assert.That(m.MeanKeeperToCrossingM, Is.EqualTo(expected).Within(1e-9));
        }

        // ------------------------------------------------------------------ passes

        [Test]
        public void Pass_IsCompleted_OnlyWhenATeammateTouchesItNext()
        {
            PositionStream s = NewStream(40);
            Hold(s, 5, 6, true, 2, 300, CY); Act(s, 7, BallActionKind.Pass, true, 2, 3);
            Hold(s, 9, 10, true, 3, 350, CY);                                       // received: completed
            Act(s, 11, BallActionKind.Pass, true, 3, 4); Hold(s, 13, 14, false, 4, 400, CY);   // cut out
            Act(s, 15, BallActionKind.Pass, false, 4, 5); Act(s, 17, BallActionKind.ThrowIn, true, 2);   // out
            Hold(s, 19, 20, true, 2, 300, 5); Act(s, 21, BallActionKind.Pass, true, 2, 3);
            Hold(s, 23, 24, true, 2, 300, 5);                                       // only he got it back

            ShotPassMetrics m = Measure(s);

            Assert.That(m.PassesAttempted, Is.EqualTo(4));
            Assert.That(m.PassesCompleted, Is.EqualTo(1));
            Assert.That(m.PassAccuracyPercent, Is.EqualTo(25.0));
        }

        [Test]
        public void Clearance_IsAPass_WhenHeHadTheBall_AndADeflectionIsNot()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 6, false, 5, 900, CY); Act(s, 7, BallActionKind.Clearance, false, 5);   // he cleared it
            Act(s, 12, BallActionKind.Clearance, true, 6);                                   // it hit him
            Hold(s, 14, 15, true, 7, 700, CY);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.PassesAttempted, Is.EqualTo(1));
            Assert.That(m.PassesCompleted, Is.EqualTo(0), "the next touch was the opponent's deflection");
        }

        [Test]
        public void LongBallShare_IsLongBallsOverAttemptedPasses()
        {
            PositionStream s = NewStream(30);
            Hold(s, 2, 2, true, 2, 300, CY); Act(s, 3, BallActionKind.LongBall, true, 2, 9);
            Hold(s, 5, 5, true, 9, 700, CY); Act(s, 6, BallActionKind.Pass, true, 9, 8);
            Hold(s, 8, 8, true, 8, 700, CY); Act(s, 9, BallActionKind.Pass, true, 8, 9);
            Hold(s, 11, 11, true, 9, 700, CY); Act(s, 12, BallActionKind.LongBall, true, 9, 2);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.LongBalls, Is.EqualTo(2));
            Assert.That(m.LongBallSharePercent, Is.EqualTo(50.0));
        }

        [Test]
        public void Crosses_StartWideInTheLastThird_AndEndInTheBox_SetPiecesExcepted()
        {
            PositionStream s = NewStream(80);
            // 1. Into the box: a cross.
            Hold(s, 5, 6, true, 7, 800, 60); Act(s, 7, BallActionKind.Cross, true, 7, 9); Hold(s, 9, 10, true, 9, 980, CY);
            // 2. Pulled back short of the box: not one.
            Hold(s, 15, 16, true, 7, 800, 60); Act(s, 17, BallActionKind.Cross, true, 7, 9); Hold(s, 19, 20, true, 9, 850, CY);
            // 3. Filed as a pass, cut out in the box: still a cross.
            Hold(s, 25, 26, true, 7, 800, 620); Act(s, 27, BallActionKind.Pass, true, 7, 9); Hold(s, 29, 30, false, 3, 1000, CY);
            // 4. From inside the width of the area: not one.
            Hold(s, 35, 36, true, 7, 800, CY + 100); Act(s, 37, BallActionKind.Cross, true, 7, 9); Hold(s, 39, 40, true, 9, 980, CY);
            // 5. A corner kick: not one.
            Act(s, 45, BallActionKind.Corner, true, 7);
            Hold(s, 48, 49, true, 7, 1045, 5); Act(s, 50, BallActionKind.Cross, true, 7, 9); Hold(s, 52, 53, true, 9, 990, CY);
            // 6. Short of the last third: not one.
            Hold(s, 60, 61, true, 7, 650, 60); Act(s, 62, BallActionKind.Cross, true, 7, 9); Hold(s, 64, 65, true, 9, 980, CY);
            // 7. Runs out of play after crossing the box: ends where it last was in play, inside.
            Hold(s, 70, 71, true, 7, 800, 60); Act(s, 72, BallActionKind.Cross, true, 7, 9);
            Ball(s, 74, 1000, 500); Act(s, 75, BallActionKind.GoalKick, false, Keeper);

            Assert.That(Measure(s).Crosses, Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ sequences

        [Test]
        public void Sequences_OpenPlayOnly_CountTheirPasses_IncludingTheThrowInAndTheLostOne()
        {
            PositionStream s = NewStream(40);
            Act(s, 0, BallActionKind.Kickoff, true, 10); Hold(s, 0, 1, true, 10, 525, CY);   // set piece: left out
            Act(s, 2, BallActionKind.Pass, true, 10, 5); Hold(s, 4, 5, true, 5, 450, CY);
            Act(s, 6, BallActionKind.Pass, true, 5, 6); Hold(s, 8, 9, false, 6, 500, CY);     // away win it
            Act(s, 10, BallActionKind.Pass, false, 6, 7); Hold(s, 12, 13, false, 7, 450, CY);
            Act(s, 14, BallActionKind.Pass, false, 7, 8); Act(s, 16, BallActionKind.ThrowIn, true, 2);
            Hold(s, 20, 21, true, 2, 600, 5); Act(s, 22, BallActionKind.Pass, true, 2, 3);     // throw-in: open play
            Hold(s, 24, 25, true, 3, 700, CY); Ball(s, 26, 700, CY); Act(s, 26, BallActionKind.Shot, true, 3);
            Act(s, 27, BallActionKind.Miss, true, 3);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.OpenPlaySequences, Is.EqualTo(2), "away's two passes, home's throw-in");
            Assert.That(m.OpenPlaySequencePasses, Is.EqualTo(3));
            Assert.That(m.PassesPerSequence, Is.EqualTo(1.5));
            Assert.That(m.OpenPlayShots, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsZeroToOne, Is.EqualTo(1));
        }

        [Test]
        public void TenPlusSequences_NeedTenAttemptedPasses()
        {
            PositionStream s = NewStream(80);
            int next = Chain(s, 2, true, 10);
            next = Chain(s, next, false, 9);
            Hold(s, next, next, true, 5, 500, CY);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.OpenPlaySequences, Is.EqualTo(3));
            Assert.That(m.OpenPlaySequencePasses, Is.EqualTo(19));
            Assert.That(m.TenPlusSequences, Is.EqualTo(1));
        }

        [Test]
        public void DirectSpeed_IsTheMeanOfSignedProgressOverDuration_OverSequencesOfASecondOrMore()
        {
            PositionStream s = NewStream(50);
            Hold(s, 10, 10, true, 5, 300, CY); Hold(s, 11, 20, true, 5, 500, CY);       // +20 m in 5 s: 4 m/s
            Hold(s, 30, 30, false, 5, 600, CY); Hold(s, 31, 34, false, 5, 700, CY);     // away go back 10 m in 2 s: -5 m/s
            Hold(s, 40, 41, true, 5, 500, CY);                                         // half a second: left out

            ShotPassMetrics m = Measure(s);

            Assert.That(m.DirectSpeedSequences, Is.EqualTo(2));
            Assert.That(m.DirectSpeed, Is.EqualTo(-0.5).Within(1e-9));
        }

        [Test]
        public void Ppda_CountsOpenPlayPassesAndDefensiveActionsInsideThePassersOwnSixtyPercent()
        {
            PositionStream s = NewStream(60);
            // Away in open play, in their own 60% (x >= 420): three passes from 700, one from 300.
            Hold(s, 5, 5, false, 2, 700, CY); Act(s, 6, BallActionKind.Pass, false, 2, 3);
            Hold(s, 7, 7, false, 3, 700, CY); Act(s, 8, BallActionKind.Pass, false, 3, 4);
            Hold(s, 9, 9, false, 4, 700, CY); Act(s, 10, BallActionKind.Pass, false, 4, 5);
            Hold(s, 11, 11, false, 5, 300, CY); Act(s, 12, BallActionKind.Pass, false, 5, 6);
            Hold(s, 13, 13, false, 6, 600, CY);
            Act(s, 14, BallActionKind.Interception, true, 3); Hold(s, 14, 15, true, 3, 600, CY);   // in the zone: counts
            Act(s, 16, BallActionKind.Pass, true, 3, 4);                                            // home's own 60%: counts
            Hold(s, 18, 18, false, 7, 300, CY);
            Ball(s, 19, 300, CY); Act(s, 19, BallActionKind.Foul, true, 8);                         // outside: does not
            Act(s, 19, BallActionKind.FreeKick, false, 7);
            Hold(s, 22, 22, false, 7, 700, CY); Act(s, 23, BallActionKind.Pass, false, 7, 8);       // set piece: neither
            Hold(s, 24, 24, false, 8, 700, CY);
            Act(s, 25, BallActionKind.Recovery, true, 9); Hold(s, 25, 26, true, 9, 700, CY);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.PpdaPasses, Is.EqualTo(4));
            Assert.That(m.PpdaActions, Is.EqualTo(1));
            Assert.That(m.Ppda, Is.EqualTo(4.0));
        }

        [Test]
        public void HighTurnovers_StartInOpenPlayWithinFortyMetresOfTheGoal()
        {
            PositionStream s = NewStream(40);
            Hold(s, 5, 6, true, 9, 800, CY);                                         // 25 m out: one
            Hold(s, 10, 11, false, 4, 500, CY);                                      // 50 m out: not
            Act(s, 15, BallActionKind.ThrowIn, true, 2); Hold(s, 18, 19, true, 2, 900, 5);   // a restart: not
            Hold(s, 25, 26, false, 4, 300, CY);                                      // 30 m out: one

            Assert.That(Measure(s).HighTurnovers, Is.EqualTo(2));
        }

        [Test]
        public void BallInPlay_RunsFromEachRestartsFirstTouchToTheNextStoppage()
        {
            PositionStream s = NewStream(100);
            Act(s, 0, BallActionKind.Kickoff, true, 10); Hold(s, 0, 9, true, 10, 525, CY);
            Act(s, 10, BallActionKind.ThrowIn, false, 2); Hold(s, 20, 29, false, 2, 500, 5);   // dead 10-19
            Act(s, 40, BallActionKind.Goal, false, 2);
            Act(s, 60, BallActionKind.Kickoff, true, 10); Hold(s, 60, 61, true, 10, 525, CY);  // dead 40-59

            Assert.That(Measure(s).BallInPlayMinutes, Is.EqualTo(70.0 / Tpm).Within(1e-9));
        }

        // ------------------------------------------------------------------ R9 / R10

        [Test]
        public void OpenPlayShots_ByPassesBefore_WithReboundsAndHighTurnoversInTheirOwnColumn()
        {
            PositionStream s = NewStream(80);
            // 1. One pass, 55 m out at the start: 0-1.
            Hold(s, 5, 5, true, 2, 500, CY); Act(s, 6, BallActionKind.Pass, true, 2, 9);
            Hold(s, 7, 7, true, 9, 500, CY); Ball(s, 8, 900, CY); Act(s, 8, BallActionKind.Shot, true, 9);
            Act(s, 10, BallActionKind.Save, false, Keeper);                          // parried
            // 2. The rebound: 0-1, a rebound.
            Hold(s, 12, 12, true, 9, 1000, CY); Act(s, 13, BallActionKind.Shot, true, 9);
            Act(s, 14, BallActionKind.Miss, true, 9); Act(s, 15, BallActionKind.GoalKick, false, Keeper);
            // 3. Won 25 m out: 0-1, a high turnover.
            Hold(s, 18, 18, false, Keeper, 1000, CY); Act(s, 19, BallActionKind.Pass, false, Keeper, 4);
            Hold(s, 21, 21, true, 5, 800, CY); Act(s, 22, BallActionKind.Shot, true, 5);
            Act(s, 23, BallActionKind.Save, false, Keeper); Hold(s, 24, 25, false, Keeper, 1040, CY);
            // 4. Three passes from a throw-in: 3+.
            Act(s, 30, BallActionKind.ThrowIn, true, 2);
            Hold(s, 32, 32, true, 2, 400, 5); Act(s, 33, BallActionKind.Pass, true, 2, 3);
            Hold(s, 34, 34, true, 3, 500, CY); Act(s, 35, BallActionKind.Pass, true, 3, 4);
            Hold(s, 36, 36, true, 4, 600, CY); Act(s, 37, BallActionKind.Pass, true, 4, 9);
            Hold(s, 38, 38, true, 9, 900, CY); Act(s, 39, BallActionKind.Shot, true, 9);
            Act(s, 40, BallActionKind.Miss, true, 9); Act(s, 41, BallActionKind.GoalKick, false, Keeper);
            // 5. Two passes from a throw-in: 2.
            Act(s, 45, BallActionKind.ThrowIn, true, 2);
            Hold(s, 47, 47, true, 2, 400, 5); Act(s, 48, BallActionKind.Pass, true, 2, 3);
            Hold(s, 49, 49, true, 3, 500, CY); Act(s, 50, BallActionKind.Pass, true, 3, 9);
            Hold(s, 51, 51, true, 9, 900, CY); Act(s, 52, BallActionKind.Shot, true, 9);
            Act(s, 53, BallActionKind.Miss, true, 9); Act(s, 54, BallActionKind.GoalKick, false, Keeper);
            // 6. A penalty: not open play.
            Act(s, 60, BallActionKind.Penalty, true, 9);
            Hold(s, 62, 62, true, 9, 940, CY); Act(s, 63, BallActionKind.Shot, true, 9);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.Shots, Is.EqualTo(6));
            Assert.That(m.OpenPlayShots, Is.EqualTo(5));
            Assert.That(m.OpenPlayShotsZeroToOne, Is.EqualTo(3));
            Assert.That(m.OpenPlayShotsRebound, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsHighTurnover, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsZeroToOneReboundOrHighTurnover, Is.EqualTo(2));
            Assert.That(m.OpenPlayShotsTwo, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsThreePlus, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsZeroToOnePercent, Is.EqualTo(60.0));
            Assert.That(m.OpenPlayShotsZeroToOneExclReboundsPercent, Is.EqualTo(100.0 / 3).Within(1e-9), "1 of the 3 shots left");
            Assert.That(m.OpenPlayShotsTwoPercent, Is.EqualTo(20.0));
            Assert.That(m.OpenPlayShotsThreePlusPercent, Is.EqualTo(20.0));
        }

        [Test]
        public void StrikersOwnFrame_OnTheStrikeFrame_OpensNoSequence_AndLeavesTheReboundToTheRebound()
        {
            // The stream files a strike on the frame BEFORE an odd-tick kick, so the striker can
            // still hold the ball on his strike's frame. That is the strike, not a new touch.
            PositionStream s = NewStream(30);
            Hold(s, 5, 8, true, 9, 600, CY); Act(s, 8, BallActionKind.Shot, true, 9);      // 45 m out, held on frame 8
            Act(s, 10, BallActionKind.Save, false, Keeper);                                   // parried
            Hold(s, 12, 13, true, 7, 1000, CY); Act(s, 13, BallActionKind.Shot, true, 7);   // the rebound, 5 m out, held on frame 13
            Act(s, 14, BallActionKind.Miss, true, 7); Act(s, 15, BallActionKind.GoalKick, false, Keeper);

            ShotPassMetrics m = Measure(s);

            Assert.That(m.OpenPlaySequences, Is.EqualTo(3), "home's move, the keeper's parry, home's rebound");
            Assert.That(m.OpenPlayShots, Is.EqualTo(2));
            Assert.That(m.OpenPlayShotsRebound, Is.EqualTo(1));
            Assert.That(m.OpenPlayShotsHighTurnover, Is.EqualTo(0));
            Assert.That(m.HighTurnovers, Is.EqualTo(0), "a rebound is not a high turnover");
            Assert.That(m.OpenPlayShotsZeroToOneReboundOrHighTurnover, Is.EqualTo(1));
        }

        [Test]
        public void OpenGoalShortcut_IsAShotStruckWithTheLaneOpen()
        {
            PositionStream s = NewStream(40);
            Ball(s, 10, 950, CY); Act(s, 10, BallActionKind.Shot, true, 9);          // only the keeper ahead: open
            Ball(s, 20, 950, CY); Put(s, false, 20, 3, 1000, CY); Act(s, 20, BallActionKind.Shot, true, 9);   // a defender in it
            Ball(s, 30, 800, CY); Act(s, 30, BallActionKind.Shot, true, 9);          // 25 m: out of range

            ShotPassMetrics m = Measure(s);

            Assert.That(m.OpenGoalShortcutShots, Is.EqualTo(1));
            Assert.That(m.OpenGoalShortcutPercent, Is.EqualTo(100.0 / 3).Within(1e-9));
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A home strike from (850, CY + 20) straight up the pitch: 5 m along it one frame later.</summary>
        private static void Strike(PositionStream s, int frame)
        {
            Ball(s, frame, 850, CY + 20);
            Ball(s, frame + 1, 900, CY + 20);
            Act(s, frame, BallActionKind.Shot, true, 9);
        }

        /// <summary>
        /// One side passing between slots 2 and 3 in open play: a frame on the ball, then the
        /// pass, <paramref name="passes"/> times. Returns the first frame after the last pass.
        /// </summary>
        private static int Chain(PositionStream s, int from, bool home, int passes)
        {
            int f = from;
            for (int i = 0; i < passes; i++)
            {
                int slot = 2 + i % 2;
                Hold(s, f, f, home, slot, 500, CY);
                Act(s, f + 1, BallActionKind.Pass, home, slot, 5 - slot);
                f += 2;
            }

            return f;
        }
    }
}
