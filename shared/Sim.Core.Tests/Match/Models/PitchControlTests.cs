using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match.Movement.Models;

namespace Sim.Core.Tests.Match.Models
{
    /// <summary>
    /// Simplified Spearman pitch control (watchable-match-engine R3): time-to-intercept per
    /// player, and from it how safe a pass lane and its receiver are.
    /// </summary>
    [TestFixture]
    public class PitchControlTests
    {
        private static readonly ActionModelBalance Cfg = new MatchBalance().ActionModels;

        private const int BallSpeed = 150;
        private const int Speed = 70;

        private static PitchActor Still(int x, int y) => new PitchActor(x, y, 0, 0, Speed);

        [Test]
        public void TimeToIntercept_GrowsWithDistance_AndShrinksWithSpeed()
        {
            int near = PitchControl.TimeToReachMs(Still(500, 300), 550, 300, Cfg);
            int far = PitchControl.TimeToReachMs(Still(500, 300), 700, 300, Cfg);
            int quick = PitchControl.TimeToReachMs(new PitchActor(500, 300, 0, 0, 90), 700, 300, Cfg);

            Assert.That(far, Is.GreaterThan(near));
            Assert.That(quick, Is.LessThan(far));
            Assert.That(near, Is.GreaterThanOrEqualTo(Cfg.PitchControlReactionMs));
        }

        [Test]
        public void TimeToIntercept_CountsTheMomentumHeCarries()
        {
            int toward = PitchControl.TimeToReachMs(new PitchActor(500, 300, 60, 0, Speed), 700, 300, Cfg);
            int away = PitchControl.TimeToReachMs(new PitchActor(500, 300, -60, 0, Speed), 700, 300, Cfg);
            Assert.That(toward, Is.LessThan(away));
        }

        [Test]
        public void FastestMs_IsTheBestPlacedPlayer_AndNobodyReachesItWhenThereIsNobody()
        {
            var players = new[] { Still(100, 100), Still(480, 300), Still(900, 600) };
            Assert.That(PitchControl.FastestMs(players, 500, 300, Cfg),
                Is.EqualTo(PitchControl.TimeToReachMs(players[1], 500, 300, Cfg)));
            Assert.That(PitchControl.FastestMs(ReadOnlySpan<PitchActor>.Empty, 500, 300, Cfg), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ALaneBlockedByADefender_ScoresLower_ThanAClearLane()
        {
            var inTheLane = new[] { Still(500, 340) };
            var outOfIt = new[] { Still(500, 600) };

            int blocked = PitchControl.LaneSafetyPermille(400, 340, 600, 340, BallSpeed, inTheLane, Cfg);
            int clear = PitchControl.LaneSafetyPermille(400, 340, 600, 340, BallSpeed, outOfIt, Cfg);

            Assert.That(blocked, Is.LessThan(clear));
            Assert.That(blocked, Is.LessThan(300), "a man standing on the line cuts it out");
            Assert.That(clear, Is.EqualTo(1000));
        }

        [Test]
        public void AFasterBall_BeatsADefenderClosingTheLane()
        {
            var closing = new[] { Still(500, 400) };
            int slow = PitchControl.LaneSafetyPermille(400, 340, 600, 340, 80, closing, Cfg);
            int fast = PitchControl.LaneSafetyPermille(400, 340, 600, 340, 240, closing, Cfg);
            Assert.That(fast, Is.GreaterThan(slow));
        }

        [Test]
        public void ALaneWithNoDefenders_IsSafe()
        {
            Assert.That(PitchControl.LaneSafetyPermille(400, 340, 600, 340, BallSpeed,
                ReadOnlySpan<PitchActor>.Empty, Cfg), Is.EqualTo(1000));
        }

        [Test]
        public void AMarkedReceiver_IsLessSafe_ThanAFreeOne()
        {
            PitchActor receiver = Still(600, 340);
            var marked = new[] { Still(610, 340) };
            var free = new[] { Still(600, 520) };

            int tight = PitchControl.ReceiverSafetyPermille(receiver, marked, 600, 340, Cfg);
            int open = PitchControl.ReceiverSafetyPermille(receiver, free, 600, 340, Cfg);

            Assert.That(tight, Is.LessThan(open));
            Assert.That(open, Is.EqualTo(1000));
            Assert.That(tight, Is.InRange(1, 999));
        }

        [Test]
        public void ReceiverSafety_IsEven_WhenBothArriveTogether()
        {
            PitchActor receiver = Still(560, 340);
            var defenders = new[] { Still(640, 340) };
            Assert.That(PitchControl.ReceiverSafetyPermille(receiver, defenders, 600, 340, Cfg), Is.EqualTo(500));
        }

        [Test]
        public void SameInputs_SameAnswer()
        {
            var defenders = new[] { Still(510, 360), new PitchActor(700, 200, -30, 40, 80) };
            int a = PitchControl.LaneSafetyPermille(420, 330, 760, 300, 180, defenders, Cfg);
            int b = PitchControl.LaneSafetyPermille(420, 330, 760, 300, 180, defenders, Cfg);
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void LaneAndReceiver_DoNotAllocate()
        {
            var defenders = new[] { Still(500, 360), Still(620, 300), Still(300, 500) };
            PitchActor receiver = Still(600, 340);
            PitchControl.LaneSafetyPermille(400, 340, 600, 340, BallSpeed, defenders, Cfg);
            PitchControl.ReceiverSafetyPermille(receiver, defenders, 600, 340, Cfg);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += PitchControl.LaneSafetyPermille(400, 340, 600 + i % 50, 340, BallSpeed, defenders, Cfg);
                sum += PitchControl.ReceiverSafetyPermille(receiver, defenders, 600, 340 + i % 30, Cfg);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0));
            Assert.That(sum, Is.GreaterThan(0));
        }
    }
}
