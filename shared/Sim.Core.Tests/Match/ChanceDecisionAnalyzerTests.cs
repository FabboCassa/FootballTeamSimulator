using System.Linq;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using static Sim.Core.Tests.Match.ScriptedStream;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The chance-decision table of the believable-match spec (R2), pinned against hand-built
    /// streams. The home side attacks the goal at x = 1050 dm, so a carrier at (950, 340) is 10 m
    /// from its centre, central. The away keeper stands 4 m off that goal and never spoils "free".
    /// </summary>
    [TestFixture]
    public class ChanceDecisionAnalyzerTests
    {
        private const int CY = Pitch.CenterY;
        private const int Striker = 9;
        private const int Mate = 6;
        private const int Defender = 4;

        private static ChanceDecisionMetrics Measure(PositionStream s) =>
            ChanceDecisionAnalyzer.Analyze(new MatchReport { Positions = s })!;

        /// <summary>The man holds the ball, standing on it, from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static void Carry(PositionStream s, int from, int to, bool home, int slot, int x, int y)
        {
            for (int t = from; t <= to; t++) Put(s, home, t, slot, x, y);
            Hold(s, from, to, home, slot, x, y);
        }

        /// <summary>A home striker free 10 m out, central, from frame 10 to 12; his kick is filed on 13.</summary>
        private static PositionStream FreeStriker(int ticks = 40)
        {
            PositionStream s = NewStream(ticks);
            Carry(s, 10, 12, true, Striker, 950, CY);
            return s;
        }

        private static void AssertOnly(ChanceDecisionMetrics m, bool free, bool near, ChanceAction action)
        {
            Assert.That(m.Count(free, near, action), Is.EqualTo(1), $"{free} {near} {action}");
            Assert.That(m.Total, Is.EqualTo(1), "one possession, one count");
        }

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(ChanceDecisionAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        [Test]
        public void Shot_ByAFreeCarrierInside12m_IsAFreeNearShot()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Shot, true, Striker);
            Act(s, 15, BallActionKind.Save, false, Keeper);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Shot);
        }

        [Test]
        public void Shot_StillShownHoldingOnTheStrikeFrame_CountsOnce()
        {
            PositionStream s = FreeStriker();
            Carry(s, 13, 13, true, Striker, 950, CY);
            Act(s, 13, BallActionKind.Shot, true, Striker);
            Act(s, 15, BallActionKind.Miss, true, Striker);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Shot);
        }

        [Test]
        public void Pass_EndingNoFartherThan3mFromGoalCentre_IsForwardOrSquare()
        {
            // Carrier 10 m out; the ball ends 13 m out: exactly 3 m farther is not back.
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Pass, true, Striker, Mate);
            Carry(s, 16, 20, true, Mate, 920, CY);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.PassForward);
        }

        [Test]
        public void Pass_EndingMoreThan3mFartherFromGoalCentre_IsBack()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Pass, true, Striker, Mate);
            Carry(s, 16, 20, true, Mate, 919, CY);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.PassBack);
        }

        [Test]
        public void CrossAndLongBall_ArePasses_ReadWhereTheBallEnds()
        {
            PositionStream s = NewStream(60);
            Carry(s, 10, 12, true, Striker, 950, CY);
            Act(s, 13, BallActionKind.Cross, true, Striker, Mate);
            Carry(s, 15, 18, true, Mate, 1000, CY + 30);
            Act(s, 19, BallActionKind.LongBall, true, Mate, Striker);
            Carry(s, 30, 32, true, Striker, 600, CY);

            ChanceDecisionMetrics m = Measure(s);

            Assert.That(m.Count(true, true, ChanceAction.PassForward), Is.EqualTo(1), "the cross");
            Assert.That(m.Count(true, true, ChanceAction.PassBack), Is.EqualTo(1), "the long ball back out");
            Assert.That(m.Total, Is.EqualTo(2));
        }

        [Test]
        public void Pass_CutOutBeforeAnyoneHoldsIt_EndsWhereTheBallWasBeforeTheTouch()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Pass, true, Striker, Mate);
            Ball(s, 14, 800, CY);
            Ball(s, 15, 790, CY);
            Act(s, 15, BallActionKind.Interception, false, Defender);
            Carry(s, 15, 18, false, Defender, 790, CY);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.PassBack);
        }

        [Test]
        public void Dribble_IsACarry_AndThePossessionIsNotCountedAgain()
        {
            PositionStream s = FreeStriker();
            Act(s, 12, BallActionKind.Dribble, true, Striker);
            Ball(s, 13, 960, CY);                           // knocked ahead: loose
            Carry(s, 14, 16, true, Striker, 970, CY);
            Act(s, 17, BallActionKind.Shot, true, Striker);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Carry);
        }

        [Test]
        public void Clearance_ByTheCarrier_IsAClear()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Clearance, true, Striker);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Clear);
        }

        [Test]
        public void Recovery_BeforeHeActs_IsLost()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Recovery, false, Defender);
            Carry(s, 13, 16, false, Defender, 950, CY);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Lost);
        }

        [Test]
        public void Opponent_HoldingTheBallWithNoActionFiled_IsLost()
        {
            PositionStream s = FreeStriker();
            Carry(s, 14, 16, false, Defender, 950, CY);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Lost);
        }

        [Test]
        public void Foul_OnTheCarrier_IsLost()
        {
            PositionStream s = FreeStriker();
            Act(s, 13, BallActionKind.Foul, false, Defender);
            Act(s, 13, BallActionKind.FreeKick, true, Striker);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Lost);
        }

        [Test]
        public void Defender_Within3m_MakesHimNotFree()
        {
            PositionStream s = FreeStriker();
            for (int t = 10; t <= 12; t++) Put(s, false, t, Defender, 950 + 18, CY + 24);   // exactly 3 m
            Act(s, 13, BallActionKind.Shot, true, Striker);

            AssertOnly(Measure(s), free: false, near: true, ChanceAction.Shot);
        }

        [Test]
        public void Defender_JustBeyond3m_AndTheKeeperOnHim_LeaveHimFree()
        {
            PositionStream s = FreeStriker();
            for (int t = 10; t <= 12; t++)
            {
                Put(s, false, t, Defender, 950 + 31, CY);
                Put(s, false, t, Keeper, 950 + 10, CY);
            }

            Act(s, 13, BallActionKind.Shot, true, Striker);

            AssertOnly(Measure(s), free: true, near: true, ChanceAction.Shot);
        }

        [Test]
        public void Distance_SplitsAt12m_AndTheZoneEndsAt16mAnd10mOffCentre()
        {
            PositionStream s = NewStream(80);
            Carry(s, 10, 11, true, Striker, Pitch.LengthDm - 120, CY);        // 12 m: near
            Act(s, 12, BallActionKind.Shot, true, Striker);
            Carry(s, 20, 21, true, Striker, Pitch.LengthDm - 121, CY);        // 12.1 m: far
            Act(s, 22, BallActionKind.Shot, true, Striker);
            Carry(s, 30, 31, true, Striker, Pitch.LengthDm - 120, CY + 100);  // 15.6 m, 10 m off centre: far
            Act(s, 32, BallActionKind.Shot, true, Striker);
            Carry(s, 40, 41, true, Striker, Pitch.LengthDm - 161, CY);        // 16.1 m: outside
            Act(s, 42, BallActionKind.Shot, true, Striker);
            Carry(s, 50, 51, true, Striker, Pitch.LengthDm - 50, CY + 101);   // 10.1 m off centre: outside
            Act(s, 52, BallActionKind.Shot, true, Striker);

            ChanceDecisionMetrics m = Measure(s);

            Assert.That(m.Count(true, true, ChanceAction.Shot), Is.EqualTo(1));
            Assert.That(m.Count(true, false, ChanceAction.Shot), Is.EqualTo(2));
            Assert.That(m.Total, Is.EqualTo(3));
        }

        [Test]
        public void AwaySide_AttacksTheGoalAtXZero_InBothHalves()
        {
            PositionStream s = NewStream(80);
            Carry(s, 10, 12, false, Striker, 100, CY);
            Act(s, 13, BallActionKind.Shot, false, Striker);
            Act(s, 30, BallActionKind.HalfTime, true, -1);
            Act(s, 32, BallActionKind.Kickoff, true, Striker);
            Carry(s, 50, 52, false, Striker, 100, CY);
            Act(s, 53, BallActionKind.Shot, false, Striker);
            Carry(s, 60, 62, true, Striker, 100, CY);                         // home at its own goal
            Act(s, 63, BallActionKind.Clearance, true, Striker);

            ChanceDecisionMetrics m = Measure(s);

            Assert.That(m.Count(true, true, ChanceAction.Shot), Is.EqualTo(2));
            Assert.That(m.Total, Is.EqualTo(2));
        }

        [Test]
        public void SetPiece_TakersFirstSpell_IsNotAChance()
        {
            PositionStream s = NewStream(40);
            Act(s, 5, BallActionKind.Penalty, true, Striker);
            Carry(s, 8, 9, true, Striker, Pitch.LengthDm - 110, CY);
            Act(s, 10, BallActionKind.Shot, true, Striker);

            Assert.That(Measure(s).Total, Is.EqualTo(0));
        }

        [Test]
        public void Bucket_IsReadOnTheFirstFrameInTheZone()
        {
            PositionStream s = NewStream(40);
            Carry(s, 8, 9, true, Striker, 850, CY);                           // 20 m: not yet
            Carry(s, 10, 11, true, Striker, 900, CY);                         // 15 m, free
            Carry(s, 12, 13, true, Striker, 950, CY);                         // 10 m, now marked
            for (int t = 12; t <= 13; t++) Put(s, false, t, Defender, 960, CY);
            Act(s, 14, BallActionKind.Shot, true, Striker);

            AssertOnly(Measure(s), free: true, near: false, ChanceAction.Shot);
        }

        // ------------------------------------------------------------------ the tally

        [Test]
        public void Tally_PoolsMatches_AndPrintsEveryBucketAndAction()
        {
            var a = new ChanceDecisionMetrics();
            a.Add(free: true, near: true, ChanceAction.PassForward);
            a.Add(free: true, near: true, ChanceAction.PassBack);
            var b = new ChanceDecisionMetrics();
            b.Add(free: true, near: true, ChanceAction.PassBack);
            b.Add(free: false, near: false, ChanceAction.Lost);

            var tally = new ChanceDecisionTally();
            tally.Add(a);
            tally.Add(b);

            var rows = tally.Rows().ToDictionary(row => row.Name, row => row.Value);
            Assert.That(tally.Matches, Is.EqualTo(2));
            Assert.That(rows[ChanceDecisionTally.RowName(true, true, null)], Is.EqualTo(1.5));
            Assert.That(rows[ChanceDecisionTally.RowName(true, true, ChanceAction.PassBack)], Is.EqualTo(1.0));
            Assert.That(rows[ChanceDecisionTally.RowName(true, true, ChanceAction.Shot)], Is.EqualTo(0.0));
            Assert.That(rows[ChanceDecisionTally.RowName(false, false, ChanceAction.Lost)], Is.EqualTo(0.5));
            Assert.That(tally.Rows().All(row => row.Band == null), "report only: no band, no gate");
            Assert.That(rows.Count, Is.EqualTo(4 * 7));

            string text = tally.Format("V12");
            foreach (string word in new[] { "V12", "free", "pressed", "<=12 m", "12-16 m", "shot", "fwd/square", "back", "carry", "clear", "lost" })
                Assert.That(text, Does.Contain(word));
        }
    }
}
