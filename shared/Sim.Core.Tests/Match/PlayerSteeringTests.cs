using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R8 of docs/specs/real-match-and-playing-styles.md: realistic acceleration, and a man who
    /// eases into his spot instead of stopping three metres short of it.
    /// </summary>
    [TestFixture]
    public class PlayerSteeringTests
    {
        [Test]
        public void BaseAcceleration_IsBetweenFiveAndSixMetresPerSecondSquared()
        {
            int accel = new MatchBalance().PlayerAccelDmPerSecond2;

            Assert.That(accel, Is.InRange(50, 60));
        }

        /// <summary>
        /// A man jogging to his place, 2 m off it and standing still, walks onto it: he moves in
        /// the first second, never passes the spot or turns back, and ends standing exactly on it.
        /// </summary>
        [Test]
        public void APlayerTwoMetresFromHisSpot_EasesOntoIt_WithoutOscillating()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int jog = PlayerSteering.SpeedPerTick(cfg.PlayerTopSpeedDmPerSecond * cfg.PlayerCruisePercent / 100, cfg);
            int target = 20 * PlayerSteering.UnitsPerDm;
            int x = 0, y = 0, vx = 0, vy = 0;
            int movedAfterOneSecond = 0;

            for (int tick = 1; tick <= 30 * cfg.TicksPerSecond; tick++)
            {
                int top = legs.Desired(target - x, -y, jog, sprint: false, out int wx, out int wy);
                PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top);
                x += vx;
                y += vy;

                Assert.That(x, Is.LessThanOrEqualTo(target), $"overshot the spot at tick {tick}");
                Assert.That(vx, Is.GreaterThanOrEqualTo(0), $"turned back at tick {tick}");
                if (tick == cfg.TicksPerSecond) movedAfterOneSecond = x;
            }

            Assert.That(movedAfterOneSecond, Is.GreaterThan(0), "a man 2 m from his spot must move toward it");
            Assert.That(x, Is.EqualTo(target), "he settles on the spot");
            Assert.That(y, Is.EqualTo(0));
            Assert.That(vx, Is.EqualTo(0), "and stands still there");
        }
    }
}
