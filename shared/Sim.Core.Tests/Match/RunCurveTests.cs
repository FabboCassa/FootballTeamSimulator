using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R7, issue #186: a run bends round an opponent within 2 m of its straight line.
    /// Scripted geometry, in decimetres; no match is played.
    /// </summary>
    [TestFixture]
    public class RunCurveTests
    {
        private MatchBalance _cfg = null!;

        [SetUp]
        public void Fresh() => _cfg = new BalanceConfig().Match;

        private bool Bend(int toX, int toY, int[] foeX, int[] foeY, out int viaX, out int viaY) =>
            RunCurve.Bend(100, 300, toX, toY, foeX, foeY, foeX.Length, _cfg.V11CurveTriggerDm, _cfg.V11CurveClearanceDm,
                out viaX, out viaY);

        [Test]
        public void TheTwoMetreTrigger_IsTheConfiguredOne()
        {
            Assert.That(_cfg.V11CurveTriggerDm, Is.EqualTo(20));
            Assert.That(_cfg.V11CurveClearanceDm, Is.GreaterThan(_cfg.V11CurveTriggerDm));
        }

        [Test]
        public void AnOpponentOnTheLine_BendsTheRunRoundHim()
        {
            int[] x = { 250 }, y = { 310 };
            Assert.That(Bend(400, 300, x, y, out int viaX, out int viaY), Is.True);

            Assert.That(Math.Sqrt(Sq(viaX - 250, viaY - 310)), Is.EqualTo(_cfg.V11CurveClearanceDm).Within(1.5),
                "he passes the opponent at the clearance");
            Assert.That(viaY, Is.LessThan(300), "on the other side of the line from him");
            Assert.That(viaX, Is.EqualTo(250).Within(1), "level with him, so the run still heads for the target");
            Assert.That(Math.Sqrt(SegmentSq(250, 310, 100, 300, viaX, viaY)), Is.GreaterThanOrEqualTo(_cfg.V11CurveTriggerDm),
                "the first leg of the bent run does not go through him");
            Assert.That(Math.Sqrt(SegmentSq(250, 310, viaX, viaY, 400, 300)), Is.GreaterThanOrEqualTo(_cfg.V11CurveTriggerDm),
                "nor does the second");
        }

        [Test]
        public void AnOpponentRightOfTheLine_IsPassedOnTheLeft()
        {
            int[] x = { 250 }, y = { 292 };
            Assert.That(Bend(400, 300, x, y, out _, out int viaY), Is.True);
            Assert.That(viaY, Is.GreaterThan(300));
        }

        [Test]
        public void AnOpponentDeadOnTheLine_IsPassedOnTheLeft_EveryTime()
        {
            int[] x = { 250 }, y = { 300 };
            Assert.That(Bend(400, 300, x, y, out _, out int first), Is.True);
            Assert.That(Bend(400, 300, x, y, out _, out int second), Is.True);
            Assert.That(first, Is.EqualTo(second).And.GreaterThan(300));
        }

        [Test]
        public void AClearLine_IsRunStraight()
        {
            Assert.That(Bend(400, 300, new[] { 250 }, new[] { 330 }, out int viaX, out int viaY), Is.False,
                "three metres off the line is not in the way");
            Assert.That((viaX, viaY), Is.EqualTo((400, 300)));
            Assert.That(Bend(400, 300, new[] { 80 }, new[] { 300 }, out _, out _), Is.False, "behind him");
            Assert.That(Bend(400, 300, new[] { 450 }, new[] { 300 }, out _, out _), Is.False, "beyond the target");
            Assert.That(Bend(400, 300, new[] { 390 }, new[] { 305 }, out _, out _), Is.False,
                "standing on the target: the run ends at him");
            Assert.That(Bend(140, 300, new[] { 120 }, new[] { 300 }, out _, out _), Is.False, "a step too short to bend");
            Assert.That(Bend(400, 300, new[] { 115 }, new[] { 305 }, out _, out _), Is.False,
                "already alongside him: the run is not turned more than 45 degrees");
        }

        [Test]
        public void TheFirstOpponentAlongTheRun_IsTheOneBentRound()
        {
            int[] x = { 330, 200 }, y = { 305, 295 };
            Assert.That(Bend(400, 300, x, y, out int viaX, out _), Is.True);
            Assert.That(viaX, Is.EqualTo(200).Within(1));
        }

        private static double Sq(int dx, int dy) => (double)dx * dx + (double)dy * dy;

        private static double SegmentSq(int px, int py, int ax, int ay, int bx, int by)
        {
            double dx = bx - ax, dy = by - ay;
            double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
            double cx = ax + t * dx - px, cy = ay + t * dy - py;
            return cx * cx + cy * cy;
        }
    }
}
