using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R6 structured set pieces (watchable-match spec, task 8), on scripted inputs: no match is
    /// played. The V11 brain asks these pure functions what a free kick, a corner and a restart
    /// should be; the stream tests in <see cref="V11SetPieceTests"/> check it does what they say.
    /// </summary>
    [TestFixture]
    public class SetPiecePlanTests
    {
        /// <summary>The penalty area, 16.5 m out from the goal line.</summary>
        private const int BoxDepthDm = 165;

        private MatchBalance _cfg = null!;

        [SetUp]
        public void Fresh() => _cfg = new BalanceConfig().Match;

        [Test]
        public void Tunables_DefaultTo35mAndLeaveV10sShootingRangeAlone()
        {
            Assert.That(_cfg.SetPieceRangeDm, Is.EqualTo(350));
            Assert.That(_cfg.MaxShootRangeDm, Is.EqualTo(320), "V10's wall range is its shooting range");
            Assert.That(_cfg.FreeKickCrossOddsPermille, Is.InRange(1, 999));
            Assert.That(_cfg.PenaltyRunUpDm, Is.GreaterThan(_cfg.KickRangeDm * 2),
                "a run-up shorter than two kicking reaches is not visible");
        }

        // ------------------------------------------------------------ free kicks

        [Test]
        public void FreeKick_CentralAndClose_IsShotDirectly_ByAGoodStriker()
        {
            FreeKickOption choice = SetPiecePlan.ChooseFreeKick(200, 0, 80, 80, _cfg, out int direct, out int cross);

            Assert.That(choice, Is.EqualTo(FreeKickOption.DirectShot));
            Assert.That(direct, Is.GreaterThan(cross), "chosen by value");
        }

        [Test]
        public void FreeKick_WideOfTheBox_IsCrossed()
        {
            FreeKickOption choice = SetPiecePlan.ChooseFreeKick(250, 200, 80, 80, _cfg, out int direct, out int cross);

            Assert.That(choice, Is.EqualTo(FreeKickOption.Cross));
            Assert.That(cross, Is.GreaterThan(direct), "chosen by value");
        }

        [Test]
        public void FreeKick_TheSameSpot_IsShotByTheBetterStrikerOnly()
        {
            // At the margin the taker's own foot decides: a poor striker crosses what a good one shoots.
            int distance = 0;
            FreeKickOption good = FreeKickOption.Cross, poor = FreeKickOption.Cross;
            for (int d = 150; d <= 340 && !(good == FreeKickOption.DirectShot && poor == FreeKickOption.Cross); d += 5)
            {
                distance = d;
                good = SetPiecePlan.ChooseFreeKick(d, 0, 95, 95, _cfg, out _, out _);
                poor = SetPiecePlan.ChooseFreeKick(d, 0, 10, 10, _cfg, out _, out _);
            }

            Assert.That(good, Is.EqualTo(FreeKickOption.DirectShot), $"at {distance} dm");
            Assert.That(poor, Is.EqualTo(FreeKickOption.Cross), $"at {distance} dm");
        }

        [Test]
        public void FreeKick_Beyond35m_IsNoSetPiece()
        {
            Assert.That(SetPiecePlan.ChooseFreeKick(351, 0, 99, 99, _cfg, out _, out _), Is.EqualTo(FreeKickOption.None));
            Assert.That(SetPiecePlan.ChooseFreeKick(349, 0, 99, 99, _cfg, out _, out _), Is.Not.EqualTo(FreeKickOption.None));
        }

        // ------------------------------------------------------------ corners

        [Test]
        public void CornerRoles_NearFarAndEdge_GoToDistinctEligibleMen()
        {
            //                   0   1   2   3   4   5   6   7
            int[] aerial =    { 99, 60, 90, 40, 85, 50, 30, 20 };
            int[] finishing = { 10, 20, 30, 40, 50, 95, 70, 99 };
            bool[] eligible = { false, true, true, true, true, true, true, false }; // 0 keeper, 7 taker
            var roles = new CornerRole[8];

            CornerRoles.Assign(aerial, finishing, eligible, roles);

            Assert.That(roles[2], Is.EqualTo(CornerRole.FarPost), "best in the air attacks the far post");
            Assert.That(roles[4], Is.EqualTo(CornerRole.NearPost), "second best in the air attacks the near post");
            Assert.That(roles[5], Is.EqualTo(CornerRole.EdgeOfBox), "best finisher left waits on the edge");
            Assert.That(roles[0], Is.EqualTo(CornerRole.None), "the keeper has no role");
            Assert.That(roles[7], Is.EqualTo(CornerRole.None), "the taker has no role");
            foreach (int i in new[] { 1, 3, 6 }) Assert.That(roles[i], Is.EqualTo(CornerRole.None));
        }

        [Test]
        public void CornerRoles_WithTooFewMen_FillWhatTheyCan()
        {
            var roles = new CornerRole[3];
            CornerRoles.Assign(new[] { 50, 60, 70 }, new[] { 50, 50, 50 }, new[] { true, false, false }, roles);

            Assert.That(roles, Is.EqualTo(new[] { CornerRole.FarPost, CornerRole.None, CornerRole.None }));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void CornerSpots_NearPostOnTheCornersSide_FarPostBeyond_EdgeOutsideTheBox(bool home, bool lowY)
        {
            int goalX = home ? Pitch.LengthDm : 0;
            int cornerY = lowY ? 0 : Pitch.WidthDm;

            CornerRoles.Spot(CornerRole.NearPost, home, lowY, _cfg, out int nx, out int ny);
            CornerRoles.Spot(CornerRole.FarPost, home, lowY, _cfg, out int fx, out int fy);
            CornerRoles.Spot(CornerRole.EdgeOfBox, home, lowY, _cfg, out int ex, out int ey);

            Assert.That(System.Math.Abs(ny - cornerY), Is.LessThan(System.Math.Abs(fy - cornerY)),
                "the near post is the one nearer the corner");
            Assert.That(System.Math.Abs(nx - goalX), Is.LessThan(BoxDepthDm), "near post is in the box");
            Assert.That(System.Math.Abs(fx - goalX), Is.LessThan(BoxDepthDm), "far post is in the box");
            Assert.That(System.Math.Abs(ex - goalX), Is.GreaterThan(BoxDepthDm), "edge is outside the box");
            Assert.That(ey, Is.EqualTo(Pitch.CenterY));
        }

        // ------------------------------------------------------------ throw-ins and goal kicks

        [Test]
        public void Restarts_FollowTheBuildUpInstruction()
        {
            Assert.That(SetPiecePlan.LongRestart(BallActionKind.GoalKick, Tempo.Slow, _cfg), Is.False);
            Assert.That(SetPiecePlan.LongRestart(BallActionKind.GoalKick, Tempo.Fast, _cfg), Is.True);
            Assert.That(SetPiecePlan.LongRestart(BallActionKind.ThrowIn, Tempo.Slow, _cfg), Is.False);
            Assert.That(SetPiecePlan.LongRestart(BallActionKind.ThrowIn, Tempo.Fast, _cfg), Is.True);
        }

        [Test]
        public void RestartTarget_ShortIsTheNearestMan_LongIsTheMostAdvancedInReach()
        {
            // Home attacks +x. Taker at x = 50.
            int[] xs = { 50, 150, 250, 450, 700, 900 };
            int[] ys = { 340, 300, 400, 340, 200, 340 };
            bool[] eligible = { false, true, true, true, true, true };

            int shortTarget = SetPiecePlan.PickRestartTarget(false, 50, 340, 1, xs, ys, eligible, 50, 600);
            int longTarget = SetPiecePlan.PickRestartTarget(true, 50, 340, 1, xs, ys, eligible, 50, 700);

            Assert.That(shortTarget, Is.EqualTo(1), "short: the nearest team-mate");
            Assert.That(longTarget, Is.EqualTo(4), "long: the furthest forward still within reach");
        }

        [Test]
        public void RestartTarget_NobodyInReach_IsNoTarget()
        {
            int target = SetPiecePlan.PickRestartTarget(
                true, 0, 0, 1, new[] { 0, 900 }, new[] { 0, 0 }, new[] { false, true }, 50, 300);

            Assert.That(target, Is.EqualTo(-1));
        }
    }
}
