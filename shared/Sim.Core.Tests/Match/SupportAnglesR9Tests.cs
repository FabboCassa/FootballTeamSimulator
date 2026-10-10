using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Real-match spec R9, scripted: the short option a man near the ball offers (angle and
    /// distance), and the pressed carrier who uses it. Home attacks toward x = Pitch.LengthDm.
    /// The in-match reading is <see cref="SupportAnglesMatchTests"/>, the passing-chain bands the
    /// realism harness's.
    /// </summary>
    [TestFixture]
    public class SupportAnglesR9Tests
    {
        private const int TopSpeed = 75;   // dm/s
        private const int BallX = 300;
        private const int BallY = 340;
        private const int PresserDxDm = 10, PresserDyDm = -6;   // a metre and a bit from the ball
        private const int PresserVxDm = 50, PresserVyDm = -30;  // dm/s, carried on past it

        private MatchBalance _cfg = null!;
        private V11SupportAngles _angles = null!;
        private V11ActionValuation _valuation = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _angles = new V11SupportAngles(_cfg);
            _valuation = new V11ActionValuation(_cfg);
        }

        private static double Length(int ax, int ay, int bx, int by) =>
            Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));

        private static double ToSegment(int px, int py, int ax, int ay, int bx, int by)
        {
            double dx = bx - ax, dy = by - ay;
            double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
            return Length(px, py, (int)Math.Round(ax + t * dx), (int)Math.Round(ay + t * dy));
        }

        private (int X, int Y) Offer(int across, int manX, int manY, int[] foeX, int[] foeY)
        {
            int angle = _angles.BestAngle(true, BallX, BallY, across, manX, manY, foeX, foeY, foeX.Length);
            Assert.That(angle, Is.Not.EqualTo(V11SupportAngles.NoAngle), "a man in midfield always has a spot");
            Assert.That(_angles.Spot(true, BallX, BallY, across, angle, out int x, out int y), Is.True);
            return (x, y);
        }

        [Test]
        public void TheShortOption_IsAShortPassAway_OnHisSideOfTheBall_WithTheLaneAndTheSpotFree()
        {
            // The carrier closed down from in front; a midfielder screens the forward diagonal on
            // the man's side, another stands square of the ball on it.
            int[] foeX = { BallX + 25, BallX + 70, BallX - 5, BallX + 200 };
            int[] foeY = { BallY, BallY + 110, BallY + 130, BallY - 60 };
            int manX = BallX - 40, manY = BallY + 160;

            Assert.That(V11SupportAngles.SideOf(BallY, manY), Is.EqualTo(1));
            var spot = Offer(1, manX, manY, foeX, foeY);

            double distance = Length(spot.X, spot.Y, BallX, BallY);
            Assert.That(distance, Is.InRange(_cfg.MinPassDm, 180), "a short pass, not a long one");
            Assert.That(spot.Y, Is.GreaterThan(BallY), "on his own side of the ball");
            Assert.That(Length(foeX[0], foeY[0], spot.X, spot.Y), Is.GreaterThanOrEqualTo(50), "not behind the presser");
            for (int j = 0; j < foeX.Length; j++)
            {
                if (Length(foeX[j], foeY[j], BallX, BallY) < _cfg.PressureRadiusDm) continue;   // on the ball, not on the lane
                Assert.That(ToSegment(foeX[j], foeY[j], BallX, BallY, spot.X, spot.Y), Is.GreaterThanOrEqualTo(30),
                    $"opponent {j} is off the lane");
                Assert.That(Length(foeX[j], foeY[j], spot.X, spot.Y), Is.GreaterThanOrEqualTo(50),
                    $"opponent {j} is not on top of him");
            }
        }

        [Test]
        public void WithNobodyAbout_HeOffersAhead_AndAManInTheLaneTurnsTheAngle()
        {
            int[] none = Array.Empty<int>();
            int manX = BallX, manY = BallY + 150;
            var free = Offer(1, manX, manY, none, none);
            Assert.That(free.X, Is.GreaterThan(BallX), "an empty pitch: the angle that gains ground");

            // Now an opponent stands on that lane: the angle moves off it.
            int[] foeX = { (BallX + free.X) / 2 };
            int[] foeY = { (BallY + free.Y) / 2 };
            var turned = Offer(1, manX, manY, foeX, foeY);
            Assert.That(turned, Is.Not.EqualTo(free));
            Assert.That(ToSegment(foeX[0], foeY[0], BallX, BallY, turned.X, turned.Y), Is.GreaterThanOrEqualTo(30));
        }

        [Test]
        public void NoSpotOffThePitch_AtTheTouchlineTheRingHasNoOutsideSide()
        {
            int[] none = Array.Empty<int>();
            int angle = _angles.BestAngle(true, BallX, 30, -1, BallX, 10, none, none, 0);
            Assert.That(angle, Is.EqualTo(V11SupportAngles.NoAngle));
        }

        [Test]
        public void ThePressedCarrier_WithAFreeManAtASafeAngle_PassesToHim_EvenInsideHisHold()
        {
            V11Scene s = PressedScene();
            int free = s.AddMate(new PitchActor(BallX - 60, BallY + 120, 0, 0, TopSpeed), keeper: false);
            s.AddMate(new PitchActor(BallX + 250, BallY - 40, 0, 0, TopSpeed), keeper: false);
            s.AddFoe(new PitchActor(BallX + 260, BallY - 30, 0, 0, TopSpeed), keeper: false);   // on the far man

            V11Choice choice = _valuation.Choose(s, holding: true);

            Assert.That(choice.Kind, Is.EqualTo(V11ActionKind.Pass), "pressed, he lets it go to the free man");
            Assert.That(choice.Mate, Is.EqualTo(free));
        }

        [Test]
        public void ThePressedCarrier_WithNobodyFree_KeepsHisHold_AndTheUnpressedOneKeepsItToo()
        {
            V11Scene marked = PressedScene();
            marked.AddMate(new PitchActor(BallX - 60, BallY + 120, 0, 0, TopSpeed), keeper: false);
            marked.AddFoe(new PitchActor(BallX - 50, BallY + 112, 0, 0, TopSpeed), keeper: false);   // tight on him
            marked.AddFoe(new PitchActor(BallX - 30, BallY + 60, 0, 0, TopSpeed), keeper: false);    // on the lane
            Assert.That(_valuation.Choose(marked, holding: true).Kind, Is.EqualTo(V11ActionKind.Hold));

            V11Scene calm = Calm(PressedScene());
            calm.AddMate(new PitchActor(BallX - 60, BallY + 120, 0, 0, TopSpeed), keeper: false);
            Assert.That(_valuation.Choose(calm, holding: true).Kind, Is.EqualTo(V11ActionKind.Hold));
        }

        [Test]
        public void ThePressedCarrier_PassesToTheFreeMan_RatherThanRunAtThePresser()
        {
            // Midfield, the free man a ball back and across: given room, running at them is worth more.
            int midfield = Pitch.LengthDm / 2;
            V11Scene s = PressedScene(midfield);
            s.CarryKeepPermille = 1000;
            int free = s.AddMate(new PitchActor(midfield - 100, BallY + 100, 0, 0, TopSpeed), keeper: false);
            V11Scene room = Calm(PressedScene(midfield));
            room.CarryKeepPermille = 1000;
            room.AddMate(new PitchActor(midfield - 100, BallY + 100, 0, 0, TopSpeed), keeper: false);

            V11Choice unpressed = _valuation.Choose(room, holding: false);
            V11Choice pressed = _valuation.Choose(s, holding: false);

            Assert.That(unpressed.Kind, Is.EqualTo(V11ActionKind.Carry), "with room he runs with it rather than play it back");
            Assert.That(pressed.Kind, Is.EqualTo(V11ActionKind.Pass), "pressed, the free man gets it");
            Assert.That(pressed.Mate, Is.EqualTo(free));
        }

        [Test]
        public void InHisOwnThird_TheReleaseMustBeSurer_ThanFurtherUp()
        {
            _cfg.V11PressedReleaseDeepSafetyPermille = 1001;   // no ball is that sure
            _valuation = new V11ActionValuation(_cfg);
            int midfield = Pitch.LengthDm / 2;

            V11Scene deep = PressedScene();
            deep.AddMate(new PitchActor(BallX - 60, BallY + 120, 0, 0, TopSpeed), keeper: false);
            V11Scene up = PressedScene(midfield);
            up.AddMate(new PitchActor(midfield - 60, BallY + 120, 0, 0, TopSpeed), keeper: false);

            Assert.That(_valuation.Choose(deep, holding: true).Kind, Is.EqualTo(V11ActionKind.Hold),
                "near his own goal a ball that is not sure enough is no release");
            Assert.That(_valuation.Choose(up, holding: true).Kind, Is.EqualTo(V11ActionKind.Pass),
                "the same ball in midfield is");
        }

        private static V11Scene Calm(V11Scene s)
        {
            s.PressurePermille = 0;
            return s;
        }

        /// <summary>
        /// His own third, the man on him a metre and a bit away and committed: he has lunged, and
        /// his momentum carries him on past the ball on the near side. Pressed, and the far side
        /// is open — the moment a footballer plays it round the press.
        /// </summary>
        private V11Scene PressedScene(int ballX = BallX)
        {
            int gap = (int)Math.Round(Length(0, 0, PresserDxDm, PresserDyDm));
            var s = new V11Scene
            {
                AttacksHighX = true,
                BallXDm = ballX,
                BallYDm = BallY,
                Shooting = 60,
                Technique = 60,
                Passing = 60,
                VisionPercent = 100,
                CarryTouchDm = 40,
                CarryKeepPermille = 500,
                PressurePermille = 1000 - 1000 * gap / _cfg.PressureRadiusDm,
                MaxPassDm = 600,
                OffsideLineXDm = Pitch.LengthDm,
                Instructions = TacticInstructions.Neutral
            };
            Assert.That(_valuation.Pressed(s), Is.True, "the scene is a pressed carrier");
            s.AddFoe(new PitchActor(Pitch.LengthDm - 5, Pitch.CenterY, 0, 0, TopSpeed), keeper: true);
            s.AddFoe(new PitchActor(ballX + PresserDxDm, BallY + PresserDyDm, PresserVxDm, PresserVyDm, TopSpeed), keeper: false);
            return s;
        }
    }
}
