using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>Readings over a minute window of a finished stream, and each shout's target metric (R10, R11).</summary>
    [TestFixture]
    public class MatchWindowTests
    {
        private const int Minutes = 3;
        private const int FarHalfX = 800;   // the away half: home attacks the far goal
        private const int NearHalfX = 300;  // the home half

        [TestCase(0, 0, ScoreState.Level)]
        [TestCase(2, 1, ScoreState.Leading)]
        [TestCase(0, 3, ScoreState.Trailing)]
        public void StateOf_ReadsTheScoreFromOneSide(int own, int against, ScoreState expected)
        {
            Assert.That(MatchWindow.StateOf(own, against), Is.EqualTo(expected));
        }

        [Test]
        public void Goals_CountTheSideInTheWindow_FromTheFirstFrameOfTheMinute()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Act(s, Tpm - 1, BallActionKind.Goal, home: true);   // last frame of minute 0
            Act(s, Tpm, BallActionKind.Goal, home: true);       // first frame of minute 1
            Act(s, 2 * Tpm + 5, BallActionKind.Goal, home: false);

            Assert.That(MatchWindow.Goals(s, true, 0, 1), Is.EqualTo(1));
            Assert.That(MatchWindow.Goals(s, true, 1, 3), Is.EqualTo(1));
            Assert.That(MatchWindow.Goals(s, false, 1, 3), Is.EqualTo(1));
            Assert.That(MatchWindow.GoalDifference(s, true, 0, 3), Is.EqualTo(1));
            Assert.That(MatchWindow.GoalDifference(s, false, 2, 3), Is.EqualTo(1));
        }

        [Test]
        public void StateAt_IsTheScoreBeforeTheMinuteStarts()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Act(s, Tpm + 10, BallActionKind.Goal, home: true);

            Assert.That(MatchWindow.StateAt(s, true, 1), Is.EqualTo(ScoreState.Level));
            Assert.That(MatchWindow.StateAt(s, true, 2), Is.EqualTo(ScoreState.Leading));
            Assert.That(MatchWindow.StateAt(s, false, 2), Is.EqualTo(ScoreState.Trailing));
        }

        [Test]
        public void HighRecoveries_AreBallsWonInTheOpponentsHalf()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Ball(s, 10, FarHalfX, Pitch.CenterY);
            Act(s, 10, BallActionKind.Recovery, home: true);
            Ball(s, 20, NearHalfX, Pitch.CenterY);
            Act(s, 20, BallActionKind.Interception, home: true);      // own half: not high
            Ball(s, 30, NearHalfX, Pitch.CenterY);
            Act(s, 30, BallActionKind.Interception, home: false);     // the home half is the away side's high
            Ball(s, 40, FarHalfX, Pitch.CenterY);
            Act(s, 40, BallActionKind.Pass, home: true);              // not a recovery at all

            Assert.That(MatchWindow.HighRecoveries(s, true, 0, 1), Is.EqualTo(1));
            Assert.That(MatchWindow.HighRecoveries(s, false, 0, 1), Is.EqualTo(1));
            Assert.That(MatchWindow.HighRecoveries(s, true, 1, 3), Is.EqualTo(0));
        }

        [Test]
        public void BallLosses_AreTheOpponentsRecoveriesAnywhere_OwnHalfLossesOnlyInOurHalf()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Ball(s, 10, FarHalfX, Pitch.CenterY);
            Act(s, 10, BallActionKind.Recovery, home: false);
            Ball(s, 20, NearHalfX, Pitch.CenterY);
            Act(s, 20, BallActionKind.Interception, home: false);
            Act(s, 30, BallActionKind.Recovery, home: true);

            Assert.That(MatchWindow.BallLosses(s, true, 0, 1), Is.EqualTo(2));
            Assert.That(MatchWindow.OwnHalfLosses(s, true, 0, 1), Is.EqualTo(1));
            Assert.That(MatchWindow.BallLosses(s, false, 0, 1), Is.EqualTo(1));
        }

        [Test]
        public void Shots_CountOnlyTheSidesStrikesInTheWindow()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Act(s, 10, BallActionKind.Shot, home: true);
            Act(s, 11, BallActionKind.Shot, home: false);
            Act(s, Tpm + 1, BallActionKind.Shot, home: true);

            Assert.That(MatchWindow.Shots(s, true, 0, 1), Is.EqualTo(1));
            Assert.That(MatchWindow.Shots(s, true, 0, 3), Is.EqualTo(2));
            Assert.That(MatchWindow.Shots(s, false, 1, 3), Is.EqualTo(0));
        }

        [Test]
        public void PossessionShare_IsOwnedFramesOverFramesAnybodyOwned()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Hold(s, 0, 59, true, 3, Pitch.CenterX, Pitch.CenterY);
            Hold(s, 60, 89, false, 3, Pitch.CenterX, Pitch.CenterY);

            Assert.That(MatchWindow.PossessionShare(s, true, 0, 1), Is.EqualTo(60.0 / 90).Within(1e-12));
            Assert.That(MatchWindow.PossessionShare(s, false, 0, 1), Is.EqualTo(30.0 / 90).Within(1e-12));
            Assert.That(MatchWindow.PossessionShare(s, true, 1, 3), Is.EqualTo(0.5), "nobody held it: an even split");
        }

        [TestCase(TouchlineShout.PressHigh, ShoutMetric.HighRecoveries, 1)]
        [TestCase(TouchlineShout.KeepBall, ShoutMetric.PossessionShare, 1)]
        [TestCase(TouchlineShout.AllForward, ShoutMetric.Shots, 1)]
        [TestCase(TouchlineShout.Encourage, ShoutMetric.BallLosses, -1)]
        [TestCase(TouchlineShout.Concentrate, ShoutMetric.OwnHalfLosses, -1)]
        [TestCase(TouchlineShout.None, ShoutMetric.None, 0)]
        public void EachShout_HasATargetMetric_AndTheWayItShouldMove(TouchlineShout shout, ShoutMetric metric, int sign)
        {
            Assert.That(ShoutTargets.MetricOf(shout), Is.EqualTo(metric));
            Assert.That(ShoutTargets.ExpectedSign(metric), Is.EqualTo(sign));
            Assert.That(ShoutTargets.Describe(metric), Is.Not.Empty);
        }

        [Test]
        public void ShoutTargets_ReadTheirMetricOffTheWindow()
        {
            PositionStream s = NewStream(Minutes * Tpm);
            Hold(s, 0, 59, true, 3, FarHalfX, Pitch.CenterY);
            Act(s, 5, BallActionKind.Recovery, home: true);
            Act(s, 6, BallActionKind.Shot, home: true);
            Act(s, 7, BallActionKind.Shot, home: true);
            Ball(s, 70, NearHalfX, Pitch.CenterY);
            Act(s, 70, BallActionKind.Interception, home: false);

            Assert.That(ShoutTargets.Read(ShoutMetric.HighRecoveries, s, true, 0, 1), Is.EqualTo(1));
            Assert.That(ShoutTargets.Read(ShoutMetric.Shots, s, true, 0, 1), Is.EqualTo(2));
            Assert.That(ShoutTargets.Read(ShoutMetric.PossessionShare, s, true, 0, 1), Is.EqualTo(1.0));
            Assert.That(ShoutTargets.Read(ShoutMetric.BallLosses, s, true, 0, 1), Is.EqualTo(1));
            Assert.That(ShoutTargets.Read(ShoutMetric.OwnHalfLosses, s, true, 0, 1), Is.EqualTo(1));
            Assert.That(ShoutTargets.Read(ShoutMetric.None, s, true, 0, 1), Is.EqualTo(0));
        }
    }
}
