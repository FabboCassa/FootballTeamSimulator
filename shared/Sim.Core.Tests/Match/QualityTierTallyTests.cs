using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R3 quality-tier comparison (real-match spec). Its pass and sequence readings are
    /// <see cref="ShotPassAnalyzer"/>'s, pooled per tier; the scripted streams pin that the tier
    /// figures follow the §1.5 definitions of docs/research/football-reference.md. The streams run
    /// at 120 frames a minute, so one frame is half a second.
    /// </summary>
    [TestFixture]
    public class QualityTierTallyTests
    {
        private const int CY = Pitch.CenterY;

        [Test]
        public void Tier_CountsAPassBlockedByAnOpponent_AsIncomplete_EvenWhenATeammatePicksItUp()
        {
            PositionStream s = NewStream(40);
            Hold(s, 0, 2, true, 10, 500, CY); Act(s, 3, BallActionKind.Pass, true, 10, 8);
            Act(s, 5, BallActionKind.Block, false, 3);          // the opponent blocks it, owning nothing
            Hold(s, 6, 8, true, 8, 560, CY);                    // a teammate collects: still incomplete
            Act(s, 9, BallActionKind.Pass, true, 8, 9);
            Hold(s, 10, 12, true, 9, 600, CY);                  // received: completed

            QualityTierTally t = Tier(s, goals: 0);

            Assert.That(t.PassAccuracyPercent, Is.EqualTo(50.0));
        }

        [Test]
        public void Tier_ReadsPassesAndSequences_ByTheSection15Definitions()
        {
            PositionStream s = NewStream(40);
            Hold(s, 0, 2, true, 10, 500, CY); Act(s, 3, BallActionKind.LongBall, true, 10, 8);
            Act(s, 5, BallActionKind.Block, false, 3);          // blocked: incomplete
            Hold(s, 6, 8, true, 8, 560, CY); Act(s, 9, BallActionKind.Pass, true, 8, 9);
            Act(s, 11, BallActionKind.Clearance, false, 4);     // deflected off a man who never held it: incomplete
            Hold(s, 12, 14, true, 9, 600, CY); Act(s, 15, BallActionKind.Pass, true, 9, 7);
            Hold(s, 17, 18, true, 7, 650, CY);                  // received: completed
            Act(s, 19, BallActionKind.Clearance, true, 7);      // he held it, so his clearance is a pass
            Hold(s, 21, 22, false, 5, 700, CY);                 // to an opponent: incomplete

            QualityTierTally t = Tier(s, goals: 3);

            Assert.That(t.Matches, Is.EqualTo(1));
            Assert.That(t.GoalsPerMatch, Is.EqualTo(3.0));
            Assert.That(t.PassAccuracyPercent, Is.EqualTo(25.0), "1 completed of 4 attempted");
            Assert.That(t.LongBallSharePercent, Is.EqualTo(25.0), "1 long ball of 4 attempted");
            // home (1 pass), the block (0), home (1), the deflection (0), home (2), away to the end (0).
            Assert.That(t.PassesPerSequence, Is.EqualTo(4.0 / 6).Within(1e-12));
            Assert.That(t.BallInPlayMinutes, Is.EqualTo(40 / 120.0).Within(1e-12));
        }

        [Test]
        public void Add_RejectsANullReading_AndNegativeGoals()
        {
            var t = new QualityTierTally();

            Assert.Throws<ArgumentNullException>(() => t.Add(null!, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Add(new ShotPassMetrics(), -1));
        }

        private static QualityTierTally Tier(PositionStream s, int goals)
        {
            var t = new QualityTierTally();
            t.Add(ShotPassAnalyzer.Analyze(new MatchReport { Positions = s })!, goals);
            return t;
        }

        // ------------------------------------------------------------------ the tally and R3

        private static void Add(QualityTierTally t, int goals, int attempted, int completed, int longBalls,
            double ballInPlay, int sequences, int sequencePasses) => t.Add(new ShotPassMetrics
        {
            PassesAttempted = attempted, PassesCompleted = completed, LongBalls = longBalls,
            BallInPlayMinutes = ballInPlay, OpenPlaySequences = sequences, OpenPlaySequencePasses = sequencePasses
        }, goals);

        [Test]
        public void Tally_PoolsRatios_AndAveragesPerMatchFigures()
        {
            var t = new QualityTierTally();
            Add(t, 2, 800, 640, 80, 55, 200, 700);
            Add(t, 3, 1000, 850, 140, 57, 300, 900);

            Assert.That(t.Matches, Is.EqualTo(2));
            Assert.That(t.GoalsPerMatch, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(t.BallInPlayMinutes, Is.EqualTo(56.0).Within(1e-12));
            Assert.That(t.PassAccuracyPercent, Is.EqualTo(100.0 * 1490 / 1800).Within(1e-12));
            Assert.That(t.LongBallSharePercent, Is.EqualTo(100.0 * 220 / 1800).Within(1e-12));
            Assert.That(t.PassesPerSequence, Is.EqualTo(1600.0 / 500).Within(1e-12));
        }

        [Test]
        public void EmptyTally_ReadsZero()
        {
            var t = new QualityTierTally();

            Assert.That(t.GoalsPerMatch, Is.EqualTo(0));
            Assert.That(t.BallInPlayMinutes, Is.EqualTo(0));
            Assert.That(t.PassAccuracyPercent, Is.EqualTo(0));
            Assert.That(t.LongBallSharePercent, Is.EqualTo(0));
            Assert.That(t.PassesPerSequence, Is.EqualTo(0));
        }

        [Test]
        public void Compare_MarksEachR3GapMetOrMissed()
        {
            var top = new QualityTierTally();
            Add(top, 3, 1000, 820, 120, 56.0, 250, 1000);   // acc 82%, long 12%, 4.0/seq
            var second = new QualityTierTally();
            Add(second, 2, 1000, 800, 150, 55.0, 250, 1050); // acc 80%, long 15%, 4.2/seq

            var rows = QualityTierTally.Compare(top, second).ToDictionary(r => r.Name);

            AssertGap(rows["ball in play min"], 56.0, 55.0, -1.0, false);       // needs <= -1.5
            AssertGap(rows["pass accuracy %"], 82.0, 80.0, -2.0, false);        // needs <= -3
            AssertGap(rows["long-ball share %"], 12.0, 15.0, 3.0, true);        // needs >= +2
            AssertGap(rows["passes per open-play sequence"], 4.0, 4.2, 0.2, false);   // needs lower
            AssertGap(rows["goals/match"], 3.0, 2.0, -1.0, false);              // second tier 2.3-2.9
        }

        [Test]
        public void Compare_MeetsEveryR3Gap_WhenTheSecondTierIsClearlyWorse()
        {
            var top = new QualityTierTally();
            Add(top, 3, 1000, 830, 110, 56.0, 250, 1000);
            var second = new QualityTierTally();
            Add(second, 3, 1000, 790, 140, 54.0, 250, 900);   // with the next: 2.5 goals a match
            Add(second, 2, 1000, 790, 140, 54.4, 250, 900);

            Assert.That(QualityTierTally.Compare(top, second).All(r => r.Met), Is.True);
        }

        [Test]
        public void Format_PrintsBothTiers_TheDeltas_AndTheVerdicts()
        {
            var top = new QualityTierTally();
            Add(top, 3, 1000, 820, 120, 56.0, 250, 1000);
            var second = new QualityTierTally();
            Add(second, 2, 1000, 800, 150, 55.0, 250, 1050);

            string text = QualityTierTally.Format("V11", top, second);

            Assert.That(text, Does.Contain("V11"));
            Assert.That(text, Does.Contain("R3"));
            Assert.That(text, Does.Contain("pass accuracy %"));
            Assert.That(text, Does.Contain("82.00"));
            Assert.That(text, Does.Contain("-2.00"));
            Assert.That(text, Does.Contain("MET"));
            Assert.That(text, Does.Contain("MISSED"));
        }

        private static void AssertGap(TierGap gap, double top, double second, double delta, bool met)
        {
            Assert.That(gap.Top, Is.EqualTo(top).Within(1e-9), gap.Name);
            Assert.That(gap.Second, Is.EqualTo(second).Within(1e-9), gap.Name);
            Assert.That(gap.Delta, Is.EqualTo(delta).Within(1e-9), gap.Name);
            Assert.That(gap.Met, Is.EqualTo(met), gap.Name);
        }
    }
}
