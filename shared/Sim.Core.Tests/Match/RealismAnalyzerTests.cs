using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The realism readings of the watchable-match spec (R2, R4, R5, R7), pinned against streams
    /// built by hand. Every expected value is worked out on paper from the definitions in
    /// docs/specs/watchable-match-engine.md, not recorded from a run.
    ///
    /// The streams run at 120 frames a minute, so one frame is half a second.
    /// </summary>
    [TestFixture]
    public class RealismAnalyzerTests
    {
        private const int CY = Pitch.CenterY;

        private static RealismMetrics Measure(PositionStream s) =>
            RealismAnalyzer.Analyze(new MatchReport { Positions = s })!;

        // ------------------------------------------------------------------ the basics

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(RealismAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        // ------------------------------------------------------------------ R4: open goal

        [Test]
        public void OpenGoal_ShotWithinOneAndAHalfSeconds_IsTaken()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 12, true, 9, 950, CY);          // 10 m out, only the keeper in the triangle
            Act(s, 8, BallActionKind.Shot, true, 9);    // three frames after the chance opened

            RealismMetrics m = Measure(s);

            Assert.That(m.OpenGoalChances, Is.EqualTo(1));
            Assert.That(m.OpenGoalShots, Is.EqualTo(1));
            Assert.That(m.OpenGoalShotRate, Is.EqualTo(1.0));
        }

        [Test]
        public void OpenGoal_ShotAfterTheWindow_IsAChanceMissed()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 12, true, 9, 950, CY);
            Act(s, 9, BallActionKind.Shot, true, 9);    // four frames = 2 s: too late

            RealismMetrics m = Measure(s);

            Assert.That(m.OpenGoalChances, Is.EqualTo(1));
            Assert.That(m.OpenGoalShots, Is.EqualTo(0));
            Assert.That(m.OpenGoalShotRate, Is.EqualTo(0.0));
        }

        [Test]
        public void OpenGoal_TwoSpells_OneTakenOneNot_RateIsAHalf()
        {
            PositionStream s = NewStream(60);
            Hold(s, 5, 12, true, 9, 950, CY);
            Act(s, 7, BallActionKind.Shot, true, 9);
            Hold(s, 30, 40, false, 9, 100, CY);         // the away side, in front of the home goal
            Act(s, 38, BallActionKind.Shot, false, 9);  // eight frames late

            RealismMetrics m = Measure(s);

            Assert.That(m.OpenGoalChances, Is.EqualTo(2));
            Assert.That(m.OpenGoalShots, Is.EqualTo(1));
            Assert.That(m.OpenGoalShotRate, Is.EqualTo(0.5));
        }

        [Test]
        public void OpenGoal_ALooseFrameWhileHeRunsWithIt_IsStillOneChance()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 8, true, 9, 950, CY);
            Hold(s, 10, 12, true, 9, 960, CY);         // frame 9: the ball a stride ahead of him

            RealismMetrics m = Measure(s);

            Assert.That(m.OpenGoalChances, Is.EqualTo(1));
            Assert.That(m.OpenGoalShots, Is.EqualTo(0));
        }

        [Test]
        public void OpenGoal_OutfieldDefenderInTheTriangle_IsNoChance()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 12, true, 9, 950, CY);
            for (int t = 0; t < 30; t++) Put(s, false, t, 4, 1000, 330);   // inside ball-to-posts

            Assert.That(Measure(s).OpenGoalChances, Is.EqualTo(0));
        }

        [Test]
        public void OpenGoal_FurtherThanTwentyMetres_IsNoChance()
        {
            PositionStream s = NewStream(30);
            Hold(s, 5, 12, true, 9, 840, CY);          // 21 m out

            Assert.That(Measure(s).OpenGoalChances, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ R5: sterile possession

        [Test]
        public void Possession_TwentyFiveSecondsAndEightMetres_IsSterile()
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, 49, true, 1, 400, CY);
            Hold(s, 50, 50, true, 1, 480, CY);          // 50 frames = 25 s, 8 m forward
            Hold(s, 51, 60, false, 1, 480, CY);

            RealismMetrics m = Measure(s);

            Assert.That(m.Possessions, Is.EqualTo(2));
            Assert.That(m.SterilePossessions, Is.EqualTo(1));
            Assert.That(m.SterilePossessionShare, Is.EqualTo(0.5));
        }

        [Test]
        public void Possession_ElevenMetresForward_IsNotSterile()
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, 49, true, 1, 400, CY);
            Hold(s, 50, 50, true, 1, 510, CY);

            RealismMetrics m = Measure(s);

            Assert.That(m.Possessions, Is.EqualTo(1));
            Assert.That(m.SterilePossessions, Is.EqualTo(0));
        }

        [Test]
        public void Possession_AwayProgressIsTowardXZero()
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, 49, false, 1, 600, CY);
            Hold(s, 50, 50, false, 1, 690, CY);        // 9 m BACKWARDS for the away side

            Assert.That(Measure(s).SterilePossessions, Is.EqualTo(1));
        }

        [TestCase(39, 0)]   // 19.5 s
        [TestCase(40, 1)]   // 20 s
        public void Possession_TwentySeconds_IsTheThreshold(int lastFrame, int sterile)
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, lastFrame, true, 1, 400, CY);

            Assert.That(Measure(s).SterilePossessions, Is.EqualTo(sterile));
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void Possession_APassIntoTheFinalThird_IsNotSterile(bool withPass, int sterile)
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, 20, true, 1, 660, CY);
            if (withPass) Act(s, 20, BallActionKind.Pass, true, 1, 9);
            Hold(s, 25, 30, true, 9, 720, CY);          // received inside the final third
            Hold(s, 35, 50, true, 1, 690, CY);          // 25 s, 3 m net

            RealismMetrics m = Measure(s);

            Assert.That(m.Possessions, Is.EqualTo(1));
            Assert.That(m.SterilePossessions, Is.EqualTo(sterile));
        }

        [Test]
        public void Possession_ARestartEndsIt()
        {
            PositionStream s = NewStream(50);
            Hold(s, 0, 20, true, 1, 400, CY);
            Act(s, 25, BallActionKind.ThrowIn, true);
            Hold(s, 26, 40, true, 1, 400, CY);

            Assert.That(Measure(s).Possessions, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ R7: box entries

        [Test]
        public void BoxEntries_CountOpenPlayPossessionsThatReachTheBox()
        {
            PositionStream s = NewStream(70);
            Hold(s, 0, 10, true, 1, 525, CY);
            Hold(s, 11, 15, true, 9, 950, CY);          // home possession 1 is carried into the box
            Hold(s, 16, 20, false, 1, 525, CY);
            Hold(s, 21, 25, true, 9, 800, CY);          // home possession 2: in,
            Hold(s, 26, 28, true, 9, 950, CY);
            Hold(s, 29, 31, true, 9, 800, CY);          // out,
            Hold(s, 32, 34, true, 9, 950, CY);          // in again: still one entry
            Hold(s, 35, 38, false, 1, 525, CY);
            Act(s, 39, BallActionKind.Corner, true);
            Hold(s, 40, 41, true, 9, 1000, 20);         // the corner taker,
            Hold(s, 42, 45, true, 8, 950, CY);          // and the ball into the box: not open play
            Hold(s, 46, 48, false, 9, 300, CY);
            Hold(s, 49, 50, false, 9, 100, CY);         // away reaches the home box
            Hold(s, 51, 55, true, 9, 800, CY);
            Hold(s, 56, 58, true, 9, 950, CY);          // home possession 3

            RealismMetrics m = Measure(s);

            Assert.That(m.HomeBoxEntries, Is.EqualTo(3));
            Assert.That(m.AwayBoxEntries, Is.EqualTo(1));
        }

        [Test]
        public void BoxEntries_ABallWonBackInsideTheBox_WasNeverBroughtIn()
        {
            PositionStream s = NewStream(30);
            Hold(s, 0, 5, false, 1, 525, CY);
            Hold(s, 6, 12, true, 9, 950, CY);           // recovered in the box and kept there

            Assert.That(Measure(s).HomeBoxEntries, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ R2: off-target seconds

        [Test]
        public void OffTarget_ALoneRunnerThirtyMetresOut_CountsHisSeconds()
        {
            PositionStream s = NewStream(100);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int t = 10; t < 30; t++) Put(s, true, t, 5, HomeStackX, CY + 300);

            var analyzer = new RealismAnalyzer();
            RealismMetrics m = analyzer.Measure(new MatchReport { Positions = s })!;

            Assert.That(analyzer.OffTargetSeconds(true, 5), Is.EqualTo(10.0));
            Assert.That(analyzer.OffTargetSeconds(true, 4), Is.EqualTo(0.0));
            Assert.That(analyzer.OffTargetSeconds(false, 5), Is.EqualTo(0.0));
            Assert.That(m.MedianOffTargetSeconds, Is.EqualTo(0.0));
        }

        [Test]
        public void OffTarget_ChasingTheBall_IsExempt()
        {
            PositionStream s = NewStream(100);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int t = 10; t < 30; t++)
            {
                Put(s, true, t, 5, HomeStackX, CY + 300);
                Ball(s, t, HomeStackX, CY + 260);       // he is his side's nearest man to it
            }

            var analyzer = new RealismAnalyzer();
            analyzer.Measure(new MatchReport { Positions = s });

            Assert.That(analyzer.OffTargetSeconds(true, 5), Is.EqualTo(0.0));
        }

        [Test]
        public void OffTarget_MarkingAnOpponent_IsExempt()
        {
            PositionStream s = NewStream(100);
            Hold(s, 0, 0, true, 1, Pitch.CenterX, CY);
            for (int t = 10; t < 30; t++)
            {
                Put(s, true, t, 5, HomeStackX, CY + 300);
                Put(s, false, t, 6, HomeStackX + 10, CY + 300);   // a metre off him
            }

            var analyzer = new RealismAnalyzer();
            analyzer.Measure(new MatchReport { Positions = s });

            Assert.That(analyzer.OffTargetSeconds(true, 5), Is.EqualTo(0.0));
            Assert.That(analyzer.OffTargetSeconds(false, 6), Is.EqualTo(0.0));
        }

        [Test]
        public void OffTarget_MedianIsTakenOverTheTwentyOutfielders()
        {
            // Eleven men each wander thirty metres off for ten seconds, one at a time: eleven
            // readings of 10 s and nine of 0 s, so the middle pair of the twenty is 10 and 10.
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
    }
}
