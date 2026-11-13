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

        /// <summary>
        /// Free-ball spec R7 (oscillation trace §1): a man running at 4.9 m/s whose new target lands
        /// 3 m ahead wants to walk, but his legs brake at the bounded rate rather than dropping to a
        /// walk in one tick. Without the brake, the old cap stops him dead.
        /// </summary>
        [Test]
        public void ARunningManWhoseTargetLandsClose_BrakesAtTheBoundedRate_InsteadOfStoppingDead()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int jog = PlayerSteering.SpeedPerTick(cfg.PlayerTopSpeedDmPerSecond * cfg.PlayerCruisePercent / 100, cfg);
            int running = PlayerSteering.SpeedPerTick(49, cfg);
            int ahead = 30 * PlayerSteering.UnitsPerDm;

            int top = legs.Desired(ahead, 0, jog, sprint: false, out int wx, out int wy);
            Assert.That(top, Is.LessThan(running - legs.Brake), "the eased pace is far below his speed");

            int vx = running, vy = 0;
            PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top, legs.Brake);
            Assert.That(vx, Is.EqualTo(running - legs.Brake), "one tick of braking, not a stop");

            int oldX = running, oldY = 0;
            PlayerSteering.Accelerate(ref oldX, ref oldY, wx, wy, legs.Accel, top);
            Assert.That(oldX, Is.LessThanOrEqualTo(top), "with no brake the cap drops him to the eased pace at once");
        }

        [Test]
        public void TheBrake_IsAtLeastTheAcceleration_AndReadsTheConfig()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            Assert.That(legs.Brake, Is.GreaterThanOrEqualTo(legs.Accel));
            cfg.PlayerBrakeDmPerSecond2 = 100;
            Assert.That(new PlayerSteering(cfg).Brake, Is.GreaterThan(legs.Brake));
        }

        /// <summary>A man jogging onto a spot 5 m ahead, with the brake, still settles on it without passing it.</summary>
        [Test]
        public void WithTheBrake_AJoggingManStillSettlesOnHisSpot_WithoutOvershooting()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int jog = PlayerSteering.SpeedPerTick(cfg.PlayerTopSpeedDmPerSecond * cfg.PlayerCruisePercent / 100, cfg);
            int target = 50 * PlayerSteering.UnitsPerDm;
            int x = 0, y = 0, vx = jog, vy = 0;

            for (int tick = 1; tick <= 30 * cfg.TicksPerSecond; tick++)
            {
                int top = legs.Desired(target - x, -y, jog, sprint: false, out int wx, out int wy);
                PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top, legs.Brake);
                x += vx;
                y += vy;
                Assert.That(x, Is.LessThanOrEqualTo(target), $"overshot the spot at tick {tick}");
            }

            Assert.That(x, Is.EqualTo(target));
            Assert.That(vx, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ issue #215: choosing and reaching speed

        /// <summary>The fastest gain an acceleration of 2 m/s^2 allows in one tick, in units: a hard effort in the tracking data.</summary>
        private static double EffortPerTick(MatchBalance cfg) => 20.0 * PlayerSteering.UnitsPerDm / (cfg.TicksPerSecond * cfg.TicksPerSecond);

        /// <summary>
        /// Flat out from standing, a 7 m/s man bursts at his whole acceleration up to the knee of his
        /// top speed, so the first second is as sharp as before; above it his gain fades, and the
        /// last stretch to 90% of his top speed takes over half as long again as the old constant
        /// acceleration took (3.1% of frames above 7 m/s became 0.64, reference 0.54-0.94).
        /// </summary>
        [Test]
        public void ASprinter_BurstsAtHisWholeAcceleration_ButTakesALongRunToReachTopSpeed()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int max = PlayerSteering.SpeedPerTick(70, cfg);
            int knee = max * cfg.PlayerSprintFadeKneePercent / 100;
            int fast = max * 9 / 10;
            int vx = 0, vy = 0, toKnee = 0, toFast = 0;

            for (int tick = 1; tick <= 20 * cfg.TicksPerSecond && toFast == 0; tick++)
            {
                int before = vx;
                int top = legs.Desired(1000 * PlayerSteering.UnitsPerDm, 0, max, sprint: true, out int wx, out int wy);
                PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top, legs.Brake,
                    legs.Gain(sprint: true, run: false, before, max), before);
                if (before < knee) Assert.That(vx - before, Is.EqualTo(System.Math.Min(legs.Accel, max - before)), $"tick {tick}: below the knee the burst is whole");
                if (toKnee == 0 && vx >= knee) toKnee = tick;
                if (vx >= fast) toFast = tick;
            }

            int constantTicks = (fast - knee + legs.Accel - 1) / legs.Accel;
            Assert.That(toKnee, Is.EqualTo((knee + legs.Accel - 1) / legs.Accel));
            Assert.That(toFast, Is.GreaterThan(0), "he still gets there");
            Assert.That(toFast - toKnee, Is.GreaterThan(constantTicks * 3 / 2), "the last stretch fades");
        }

        /// <summary>
        /// A jog, and a positional run back into the shape, pick up pace under 2 m/s^2, so neither is
        /// a hard acceleration in the tracking sense: 6.8 a player-minute became 2.4 (reference
        /// 1.25-2.70). The run is held to its pace, below the man's top speed.
        /// </summary>
        [Test]
        public void AJogAndAPositionalRun_PickUpPaceUnderAHardEffort()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int max = PlayerSteering.SpeedPerTick(70, cfg);

            Assert.That(legs.Gain(sprint: false, run: false, 0, max), Is.LessThan(EffortPerTick(cfg)), "jog");
            Assert.That(legs.Gain(sprint: true, run: true, 0, max), Is.LessThan(EffortPerTick(cfg)), "run");
            Assert.That(legs.Gain(sprint: true, run: false, 0, max), Is.GreaterThan(EffortPerTick(cfg)), "a sprint is still an effort");
            Assert.That(cfg.PlayerRunPacePercent, Is.InRange(cfg.PlayerCruisePercent + 1, 99));

            int vx = 0, vy = 0;
            for (int tick = 1; tick <= cfg.TicksPerSecond; tick++)
            {
                int before = vx;
                int top = legs.Desired(1000 * PlayerSteering.UnitsPerDm, 0, max * cfg.PlayerRunPacePercent / 100, sprint: true, out int wx, out int wy);
                PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top, legs.Brake,
                    legs.Gain(sprint: true, run: true, before, max), before);
            }

            Assert.That(vx, Is.LessThan(EffortPerTick(cfg) * cfg.TicksPerSecond), "a second into a run he has gained under 2 m/s");
        }

        /// <summary>
        /// A man who has set off for a place 8 m away jogs it instead of easing from the first metre,
        /// brakes for it, settles once he is near it and stands on it without passing it. Easing
        /// all the way in, as before, he would still be crawling.
        /// </summary>
        [Test]
        public void AManWhoHasSetOff_JogsToHisPlace_ThenSettlesOnIt_WithoutOvershooting()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int jog = PlayerSteering.SpeedPerTick(cfg.PlayerTopSpeedDmPerSecond * cfg.PlayerCruisePercent / 100, cfg);
            int target = 80 * PlayerSteering.UnitsPerDm;
            int stop = cfg.PlayerJogStopDm * PlayerSteering.UnitsPerDm;

            int jogging = legs.Desired(target, 0, jog, sprint: false, out _, out _, jog: true);
            int easing = legs.Desired(target, 0, jog, sprint: false, out _, out _, jog: false);
            Assert.That(jogging, Is.EqualTo(jog), "8 m out he jogs");
            Assert.That(easing, Is.LessThan(jog), "the old easing was already slowing him");

            int x = 0, y = 0, vx = 0, vy = 0;
            bool setOff = true;
            for (int tick = 1; tick <= 30 * cfg.TicksPerSecond; tick++)
            {
                if (target - x < stop) setOff = false;
                int top = legs.Desired(target - x, -y, jog, sprint: false, out int wx, out int wy, setOff);
                int before = System.Math.Abs(vx);
                PlayerSteering.Accelerate(ref vx, ref vy, wx, wy, legs.Accel, top, legs.Brake,
                    legs.Gain(sprint: false, run: false, before, jog), before);
                x += vx;
                y += vy;
                Assert.That(x, Is.LessThanOrEqualTo(target), $"overshot the spot at tick {tick}");
            }

            Assert.That(x, Is.EqualTo(target));
            Assert.That(vx, Is.EqualTo(0));
        }

        /// <summary>A settled man a metre from his place walks onto it at the gentle-stop pace, not the old crawl.</summary>
        [Test]
        public void ASettledMan_WalksTheLastMetre_InsteadOfCrawling()
        {
            var cfg = new MatchBalance();
            var legs = new PlayerSteering(cfg);
            int jog = PlayerSteering.SpeedPerTick(cfg.PlayerTopSpeedDmPerSecond * cfg.PlayerCruisePercent / 100, cfg);
            int metre = 10 * PlayerSteering.UnitsPerDm;

            int walk = legs.Desired(metre, 0, jog, sprint: false, out _, out _);
            int crawl = jog * metre / (cfg.PlayerApproachDm * PlayerSteering.UnitsPerDm);

            Assert.That(walk, Is.GreaterThan(crawl));
        }
    }
}
