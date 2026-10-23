using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Real-match spec R11 on scripted inputs: where the men stand for a goal kick played out from
    /// the back and for a throw-in. No match is played; the stream tests in
    /// <see cref="V11SetPieceTests"/> check the brain puts them there.
    /// </summary>
    [TestFixture]
    public class RestartShapeTests
    {
        private const int BoxDepthDm = 165;
        private const int BoxHalfWidthDm = 201;
        private const int CornerToleranceDm = 30;
        private const int WideDm = 80;
        private const int OfferRangeDm = 150;

        // A 4-3-3: keeper, a back four left to right, three midfielders, three forwards.
        private static readonly PositionRole[] Roles =
        {
            PositionRole.Goalkeeper,
            PositionRole.FullBack, PositionRole.CentreBack, PositionRole.CentreBack, PositionRole.FullBack,
            PositionRole.CentralMidfielder, PositionRole.DefensiveMidfielder, PositionRole.CentralMidfielder,
            PositionRole.Winger, PositionRole.Striker, PositionRole.Winger
        };

        private static readonly int[] BaseY = { 500, 120, 380, 620, 880, 300, 500, 700, 150, 500, 850 };

        private MatchBalance _cfg = null!;

        [SetUp]
        public void Fresh() => _cfg = new BalanceConfig().Match;

        // ------------------------------------------------------------ goal kicks

        [TestCase(true)]
        [TestCase(false)]
        public void GoalKick_CentreBacksSplitToTheBoxCorners(bool home)
        {
            Shape(home, AllOn(), out bool[] placed, out int[] x, out int[] y);

            int goalX = (home ? 0 : Pitch.LengthDm);
            int[] cbs = { 2, 3 };
            foreach (int cb in cbs)
            {
                Assert.That(placed[cb], Is.True, $"centre-back {cb} has a goal-kick spot");
                int cornerY = Flank(home, BaseY[cb]) < 0 ? Pitch.CenterY - BoxHalfWidthDm : Pitch.CenterY + BoxHalfWidthDm;
                double off = Dist(x[cb], y[cb], goalX + (home ? 1 : -1) * BoxDepthDm, cornerY);
                Assert.That(off, Is.LessThanOrEqualTo(CornerToleranceDm), $"centre-back {cb} on his box corner");
            }

            Assert.That(Math.Sign(y[2] - Pitch.CenterY), Is.Not.EqualTo(Math.Sign(y[3] - Pitch.CenterY)),
                "the two centre-backs split, one to each corner");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GoalKick_FullBacksGoWide_AheadOfTheCentreBacks(bool home)
        {
            Shape(home, AllOn(), out bool[] placed, out int[] x, out int[] y);

            int dir = (home ? 1 : -1);
            foreach (int fb in new[] { 1, 4 })
            {
                Assert.That(placed[fb], Is.True, $"full-back {fb} has a goal-kick spot");
                int toLine = Flank(home, BaseY[fb]) < 0 ? y[fb] : Pitch.WidthDm - y[fb];
                Assert.That(toLine, Is.InRange(_cfg.TouchlineInsetDm, WideDm), $"full-back {fb} wide on his own flank");
                Assert.That(dir * x[fb], Is.GreaterThan(dir * x[2]), $"full-back {fb} higher than the centre-backs");
            }
        }

        [Test]
        public void GoalKick_MovesOnlyTheBackLine()
        {
            Shape(true, AllOn(), out bool[] placed, out int[] _, out int[] _);

            Assert.That(placed[0], Is.False, "the keeper takes it");
            for (int i = 5; i < Roles.Length; i++) Assert.That(placed[i], Is.False, $"slot {i} keeps his own spot");
        }

        [Test]
        public void GoalKick_ThreeCentreBacks_TheOuterTwoSplit()
        {
            PositionRole[] roles = (PositionRole[])Roles.Clone();
            int[] baseY = (int[])BaseY.Clone();
            roles[6] = PositionRole.CentreBack;     // the holding man drops in between them
            baseY[6] = 500;

            bool[] placed = new bool[roles.Length];
            int[] x = new int[roles.Length], y = new int[roles.Length];
            RestartShape.GoalKick(roles, baseY, AllOn(), true, _cfg, placed, x, y);

            Assert.That(placed[2] && placed[3], Is.True);
            Assert.That(placed[6], Is.False, "the middle one keeps his spot");
        }

        [Test]
        public void GoalKick_ACentreBackSentOff_NobodySplitsAlone()
        {
            bool[] on = AllOn();
            on[3] = false;
            Shape(true, on, out bool[] placed, out int[] _, out int[] _);

            Assert.That(placed[2], Is.False, "one centre-back cannot split");
            Assert.That(placed[3], Is.False, "a man sent off has no spot");
            Assert.That(placed[1] && placed[4], Is.True, "the full-backs still go wide");
        }

        // ------------------------------------------------------------ throw-ins

        [Test]
        public void ThrowIn_TheTwoNearestTeamMatesOffer()
        {
            // Home throw on the y = 0 touchline at x = 50 m; the taker (slot 9) is on the ball.
            int bx = 500, by = _cfg.TouchlineInsetDm;
            int[] xs = { 40, 300, 300, 300, 420, 450, 350, 540, 700, 500, 650 };
            int[] ys = { 340, 100, 240, 440, 600, 150, 340, 180, 60, 8, 600 };

            int offers = Throw(bx, by, xs, ys, Eligible(taker: 9), out bool[] offering);

            Assert.That(offers, Is.EqualTo(2));
            Assert.That(offering[5] && offering[7], Is.True, "the two nearest team-mates offer");
            for (int i = 0; i < offering.Length; i++)
                if (i != 5 && i != 7) Assert.That(offering[i], Is.False, $"slot {i} is not one of them");
        }

        [Test]
        public void ThrowIn_OneTeamMateLeft_OnlyHeOffers()
        {
            int[] xs = { 40, 450, 500, 500, 500, 500, 500, 500, 500, 500, 500 };
            int[] ys = { 340, 300, 8, 8, 8, 8, 8, 8, 8, 8, 8 };
            bool[] eligible = new bool[xs.Length];
            eligible[1] = true;

            int offers = Throw(500, 8, xs, ys, eligible, out bool[] offering);

            Assert.That(offers, Is.EqualTo(1));
            Assert.That(offering[1], Is.True);
        }

        [TestCase(300, 300)]     // 36 m off, inside and behind
        [TestCase(700, 200)]     // 28 m off, ahead
        [TestCase(500, 600)]     // across the pitch
        public void OfferSpot_APlaceOutOfReach_ComesInOnTheLineToIt_NoNearerThanHeMust(int placeX, int placeY)
        {
            int bx = 500, by = _cfg.TouchlineInsetDm;

            RestartShape.OfferSpot(bx, by, placeX, placeY, _cfg, out int x, out int y);

            AssertOffer(bx, by, x, y);
            Assert.That(Dist(x, y, bx, by), Is.GreaterThanOrEqualTo(_cfg.ThrowInOfferDm - 1), "he comes no nearer than he must");
            // On the line from the ball to his place: as near his place as the reach allows.
            long cross = (long)(placeX - bx) * (y - by) - (long)(placeY - by) * (x - bx);
            Assert.That(Math.Abs(cross) / Dist(placeX, placeY, bx, by), Is.LessThanOrEqualTo(1.5), "straight towards his place");
            Assert.That(Dist(x, y, placeX, placeY), Is.EqualTo(Dist(placeX, placeY, bx, by) - Dist(x, y, bx, by)).Within(1.5),
                "between the ball and his place");
        }

        [Test]
        public void OfferSpot_APlaceWithinReach_IsHisPlace()
        {
            int bx = 500, by = _cfg.TouchlineInsetDm;

            RestartShape.OfferSpot(bx, by, 470, 80, _cfg, out int x, out int y);

            Assert.That((x, y), Is.EqualTo((470, 80)), "a man whose place is already in reach stays on it");
        }

        [Test]
        public void OfferSpot_ByTheCornerFlag_StaysOnThePitch()
        {
            // Home throw on the far touchline, a metre from the goal line it attacks.
            int bx = Pitch.LengthDm - 10, by = Pitch.WidthDm - _cfg.TouchlineInsetDm;
            int[] placesX = { 900, 950, 980, 800, 1040 };
            int[] placesY = { 600, 500, 640, 340, 400 };

            for (int i = 0; i < placesX.Length; i++)
            {
                RestartShape.OfferSpot(bx, by, placesX[i], placesY[i], _cfg, out int x, out int y);
                AssertOffer(bx, by, x, y);
                Assert.That(x, Is.InRange(_cfg.TouchlineInsetDm, Pitch.LengthDm - _cfg.TouchlineInsetDm));
            }
        }

        // ------------------------------------------------------------ helpers

        private void AssertOffer(int bx, int by, int x, int y)
        {
            double d = Dist(x, y, bx, by);
            Assert.That(d, Is.LessThanOrEqualTo(OfferRangeDm), "an offer is within 15 m of the ball");
            Assert.That(d, Is.GreaterThanOrEqualTo(_cfg.RestartMinPassDm), "and far enough to be thrown to");
            Assert.That(y, Is.InRange(_cfg.TouchlineInsetDm, Pitch.WidthDm - _cfg.TouchlineInsetDm), "on the pitch");
        }

        private void Shape(bool home, bool[] on, out bool[] placed, out int[] x, out int[] y)
        {
            placed = new bool[Roles.Length];
            x = new int[Roles.Length];
            y = new int[Roles.Length];
            RestartShape.GoalKick(Roles, BaseY, on, home, _cfg, placed, x, y);
        }

        private static int Throw(int bx, int by, int[] xs, int[] ys, bool[] eligible, out bool[] offering)
        {
            offering = new bool[xs.Length];
            return RestartShape.ThrowIn(bx, by, xs, ys, eligible, offering);
        }

        /// <summary>Everybody on the pitch but the keeper.</summary>
        private static bool[] AllOn()
        {
            var on = new bool[Roles.Length];
            for (int i = 1; i < on.Length; i++) on[i] = true;
            return on;
        }

        private static bool[] Eligible(int taker)
        {
            bool[] on = AllOn();
            on[taker] = false;
            return on;
        }

        /// <summary>Which side of the pitch a man plays on, on the stream's y axis: negative is the y = 0 side.</summary>
        private static int Flank(bool home, int baseY) => (home ? 1 : -1) * (baseY - 500);

        private static double Dist(int ax, int ay, int bx, int by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
