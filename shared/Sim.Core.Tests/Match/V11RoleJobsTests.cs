using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R7 (believable-match R4), issue #186: the role jobs out of possession, each on
    /// a scripted scene. No match is played. Home (side 0) defends the goal at x = 0.
    /// </summary>
    [TestFixture]
    public class V11RoleJobsTests
    {
        // Formations.Roles(F433): GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST.
        private const int FullBackA = 3;
        private const int FullBackB = 4;
        private const int Holding = 5;
        private const int Midfielder = 6;

        private static readonly PositionRole[] Shape = Formations.Roles(Formation.F433);

        private MatchBalance _cfg = null!;
        private V11RoleJobs _jobs = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _jobs = new V11RoleJobs(_cfg);
        }

        private V11Slot Man(int slot) => V11Slot.InFormation(Shape, slot, _cfg);

        /// <summary>Which side of the centre (pitch y) a home man's flank is.</summary>
        private int Flank(int slot) => Math.Sign(Man(slot).BaseYPermille - 500);

        private (int Near, int Far) FullBacks(int flank) => Flank(FullBackA) == flank ? (FullBackA, FullBackB) : (FullBackB, FullBackA);

        [Test]
        public void TheFormation_HasOneFullBackOnEachFlank()
        {
            Assert.That(Flank(FullBackA) * Flank(FullBackB), Is.EqualTo(-1));
        }

        [Test]
        public void NearSideFullBack_StepsToTheWideCarrier_FromTheGoalSide()
        {
            const int flank = 1;
            (int near, _) = FullBacks(flank);
            int spotX = 250, spotY = Pitch.CenterY + flank * 220;
            int carrierX = 320, carrierY = Pitch.CenterY + flank * 270;

            Assert.That(_jobs.Step(Man(near), true, spotX, spotY, carrierX, carrierY, out int x, out int y), Is.True);

            int gap = (int)Math.Round(Math.Sqrt(U2(x - carrierX, y - carrierY)));
            Assert.That(gap, Is.InRange(_cfg.V11StepStandOffDm - 1, _cfg.V11StepStandOffDm + 1), "a stand-off from the carrier");
            Assert.That(x, Is.LessThan(carrierX), "on the goal side of him");
            Assert.That(Math.Abs(y - Pitch.CenterY), Is.LessThan(Math.Abs(carrierY - Pitch.CenterY)), "and inside him, toward the goal");
        }

        [Test]
        public void FarSideFullBack_DoesNotStep_HeTucksIn()
        {
            const int flank = 1;
            (_, int far) = FullBacks(flank);
            int spotX = 250, spotY = Pitch.CenterY - flank * 220;
            int carrierX = 320, carrierY = Pitch.CenterY + flank * 270;

            Assert.That(_jobs.Step(Man(far), true, spotX, spotY, carrierX, carrierY, out _, out _), Is.False,
                "the carrier is on the other flank");
            Assert.That(_jobs.Tuck(Man(far), true, spotY, carrierY, out int y), Is.True);
            Assert.That(Math.Abs(spotY - Pitch.CenterY) - Math.Abs(y - Pitch.CenterY), Is.EqualTo(_cfg.V11TuckDm),
                "he comes in toward the centre-backs");
        }

        [Test]
        public void NearSideFullBack_DoesNotTuck_AndNobodyTucksPastTheCentre()
        {
            const int flank = -1;
            (int near, int far) = FullBacks(flank);
            int ballY = Pitch.CenterY + flank * 250;

            Assert.That(_jobs.Tuck(Man(near), true, Pitch.CenterY + flank * 220, ballY, out _), Is.False, "the ball is on his side");
            Assert.That(_jobs.Tuck(Man(far), true, Pitch.CenterY - flank * 30, ballY, out int y), Is.True);
            Assert.That(y, Is.EqualTo(Pitch.CenterY), "a full-back three metres off the centre stops on it");
        }

        [Test]
        public void FullBack_DoesNotStep_ToACentralCarrier_OrOneOutOfReach()
        {
            const int flank = 1;
            (int near, _) = FullBacks(flank);
            int spotX = 250, spotY = Pitch.CenterY + flank * 220;

            Assert.That(_jobs.Step(Man(near), true, spotX, spotY, 300, Pitch.CenterY + flank * 100, out _, out _), Is.False,
                "a carrier in the middle is the centre's job");
            Assert.That(_jobs.Step(Man(near), true, spotX, spotY, spotX + _cfg.V11StepReachDm + 50, Pitch.CenterY + flank * 270,
                out _, out _), Is.False, "a carrier twenty-five metres upfield is not his to step to");
        }

        [Test]
        public void AwayFullBack_StepsToo_TheSceneMirrored()
        {
            // Away defends x = Pitch.LengthDm; its flanks are the home flanks mirrored.
            int flank = -Flank(FullBackA);
            int spotX = Pitch.LengthDm - 250, spotY = Pitch.CenterY + flank * 220;
            int carrierX = Pitch.LengthDm - 320, carrierY = Pitch.CenterY + flank * 270;

            Assert.That(_jobs.Step(Man(FullBackA), false, spotX, spotY, carrierX, carrierY, out int x, out _), Is.True);
            Assert.That(x, Is.GreaterThan(carrierX), "goal side, toward x = Pitch.LengthDm");
        }

        [Test]
        public void HoldingMidfielder_ScreensTheLaneToTheStriker()
        {
            int ballX = 520, ballY = 300, forwardX = 260, forwardY = 360;
            int spotX = 330, spotY = Pitch.CenterY;

            Assert.That(_jobs.Screen(Man(Holding), true, spotX, spotY, ballX, ballY, forwardX, forwardY, out int x, out int y), Is.True);

            double lane = Math.Sqrt(SegmentDistanceSq(x, y, ballX, ballY, forwardX, forwardY));
            Assert.That(lane, Is.LessThanOrEqualTo(1.5), "he stands on the passing lane");
            Assert.That(Math.Sqrt(U2(x - forwardX, y - forwardY)), Is.EqualTo(_cfg.V11ScreenFromForwardDm).Within(1.5),
                "a few metres in front of the striker");
        }

        [Test]
        public void TheScreen_StaysOnALeashFromHisPlace()
        {
            int spotX = 450, spotY = Pitch.CenterY - 150;
            Assert.That(_jobs.Screen(Man(Holding), true, spotX, spotY, 600, 500, 200, 500, out int x, out int y), Is.True);
            Assert.That(Math.Sqrt(U2(x - spotX, y - spotY)), Is.LessThanOrEqualTo(_cfg.V11ScreenLeashDm + 1));
        }

        [Test]
        public void TheScreen_IsTheHoldingMidfieldersJob_WithTheBallInFrontOfTheStriker()
        {
            Assert.That(_jobs.Screen(Man(Midfielder), true, 330, 340, 520, 300, 260, 360, out _, out _), Is.False,
                "a central midfielder is not the holding one");
            Assert.That(_jobs.Screen(Man(Holding), true, 330, 340, 200, 300, 260, 360, out _, out _), Is.False,
                "a ball already behind the striker has no lane to screen");
            Assert.That(_jobs.Screen(Man(Holding), true, 330, 340, 300, 360, 260, 360, out _, out _), Is.False,
                "a lane of four metres is a pass nobody can stand in");
        }

        [Test]
        public void TheMostAdvancedOpponent_IsTheOneNearestOurGoal()
        {
            int[] x = { 900, 300, 520, 410 };
            Assert.That(V11RoleJobs.MostAdvanced(true, x, x.Length), Is.EqualTo(1));
            Assert.That(V11RoleJobs.MostAdvanced(false, x, x.Length), Is.EqualTo(0));
            Assert.That(V11RoleJobs.MostAdvanced(true, x, 0), Is.EqualTo(-1));
        }

        private static double U2(int dx, int dy) => (double)dx * dx + (double)dy * dy;

        private static double SegmentDistanceSq(int px, int py, int ax, int ay, int bx, int by)
        {
            double dx = bx - ax, dy = by - ay;
            double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
            return U2((int)Math.Round(ax + t * dx) - px, (int)Math.Round(ay + t * dy) - py);
        }
    }
}
