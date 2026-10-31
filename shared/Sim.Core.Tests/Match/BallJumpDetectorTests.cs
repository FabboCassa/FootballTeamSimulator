using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R9 of free-ball-real-movement (issue #188): the dev overlay reports every rendered ball jump
    /// over 2 m between rendered frames with its tick, and never one the picture hides or means — a
    /// cut under its curtain, the change of ends. Allocation-free on every frame.
    /// </summary>
    [TestFixture]
    public class BallJumpDetectorTests
    {
        private const int Fpm = 300;              // the 5 fps stream
        private const double Pump = 1.0 / 60.0;   // one renderer pump at 60 fps

        [Test]
        public void A_move_within_the_bar_is_not_a_jump_and_one_past_it_is_reported_with_its_tick()
        {
            var detector = new BallJumpDetector();

            Assert.That(detector.Observe(10.2, 100f, 100f, false, 0.0, 1, out _), Is.False, "the first frame has nothing to compare");
            Assert.That(detector.Observe(10.4, 112f, 116f, false, 0.0, 1, out _), Is.False, "exactly 2 m is not past the bar");
            Assert.That(detector.Observe(11.7, 112f, 137f, false, 0.0, 1, out BallJump jump), Is.True);

            Assert.That(jump.Tick, Is.EqualTo(11));
            Assert.That(jump.FromPosition, Is.EqualTo(10.4));
            Assert.That(jump.ToPosition, Is.EqualTo(11.7));
            Assert.That(jump.FromX, Is.EqualTo(112f));
            Assert.That(jump.FromY, Is.EqualTo(116f));
            Assert.That(jump.ToX, Is.EqualTo(112f));
            Assert.That(jump.ToY, Is.EqualTo(137f));
            Assert.That(jump.DistanceDm, Is.EqualTo(21f).Within(1e-4));
        }

        [Test]
        public void The_bar_scales_with_playback_speed_because_the_frame_step_does()
        {
            var detector = new BallJumpDetector();
            detector.Observe(5.0, 100f, 100f, false, 0.0, 4, out _);

            Assert.That(detector.Observe(5.5, 179f, 100f, false, 0.0, 4, out _), Is.False, "7.9 m is under 4 x 2 m at 4x");
            Assert.That(detector.Observe(6.0, 260f, 100f, false, 0.0, 4, out BallJump jump), Is.True, "8.1 m is over it");
            Assert.That(jump.DistanceDm, Is.EqualTo(81f).Within(1e-4));

            Assert.That(detector.Observe(6.5, 290f, 100f, false, 0.0, 1, out _), Is.True, "3 m is over the bar at 1x");
        }

        [Test]
        public void No_frame_under_the_curtain_is_compared_nor_the_first_clear_frame_after_it()
        {
            var detector = new BallJumpDetector();
            detector.Observe(19.0, 100f, 100f, false, 0.0, 1, out _);

            Assert.That(detector.Observe(19.5, 600f, 100f, false, 0.4, 1, out _), Is.False, "fading out");
            Assert.That(detector.Observe(40.0, 900f, 400f, false, 1.0, 1, out _), Is.False, "black");
            Assert.That(detector.Observe(40.1, 905f, 400f, false, 0.0, 1, out _), Is.False, "clear again: nothing before the fade counts");
            Assert.That(detector.Observe(40.2, 960f, 400f, false, 0.0, 1, out BallJump jump), Is.True, "a jump in clear play after it does");
            Assert.That(jump.Tick, Is.EqualTo(40));
        }

        [Test]
        public void The_change_of_ends_starts_a_new_run()
        {
            var detector = new BallJumpDetector();
            detector.Observe(99.9, 900f, 600f, false, 0.0, 1, out _);

            Assert.That(detector.Observe(100.1, 100f, 80f, true, 0.0, 1, out _), Is.False, "the picture mirrored, the ball did not jump");
            Assert.That(detector.Observe(100.2, 300f, 80f, true, 0.0, 1, out _), Is.True, "on the new ends a jump is a jump again");
        }

        [Test]
        public void Forget_drops_the_last_frame_so_a_seek_or_a_cut_crossed_in_one_step_is_not_a_jump()
        {
            var detector = new BallJumpDetector();
            detector.Observe(10.0, 100f, 100f, false, 0.0, 4, out _);
            detector.Forget();

            Assert.That(detector.Observe(400.0, 900f, 500f, false, 0.0, 4, out _), Is.False);
            Assert.That(detector.Observe(400.1, 990f, 500f, false, 0.0, 4, out _), Is.True, "and the run goes on from there");
        }

        /// <summary>
        /// Live [0,20), cut [20,40), live [40,60): the ball rolls slowly, is somewhere else when the
        /// picture comes back after the cut, and teleports from frame 49 to frame 50. Played the way
        /// the renderer plays it — a pump at 60 fps, the ball read on its path, the curtain and a
        /// crossed cut handed in — the only jumps reported are drawn over the teleport's frame, 49 to 50.
        /// </summary>
        [Test]
        public void Played_through_a_cut_only_the_teleport_in_open_play_is_reported()
        {
            const int frames = 60;
            var stream = new PositionStream
            {
                TicksPerMinute = Fpm,
                PlayerCount = 11,
                LastTick = frames - 1,
                BallXY = new int[frames * 2],
                BallZ = new int[frames],
                Owner = new int[frames]
            };

            for (int t = 0; t < frames; t++)
            {
                int x = t < 40 ? 100 + t : t < 50 ? 800 + t : 300 + t;
                int y = t < 40 ? 100 : 400;
                stream.BallXY[t * 2] = x;
                stream.BallXY[t * 2 + 1] = y;
            }

            var timeline = new BroadcastTimeline(Fpm, frames, frames,
                new[]
                {
                    new BroadcastSegment(0, 20, PlaybackRate.Live),
                    new BroadcastSegment(20, 40, PlaybackRate.Cut),
                    new BroadcastSegment(40, frames, PlaybackRate.Live)
                },
                Array.Empty<CutSummary>());

            var path = new BallPath(stream);
            var playback = new BroadcastPlayback(timeline);
            var detector = new BallJumpDetector();
            playback.CutCrossed += _ => detector.Forget();

            var jumps = new List<BallJump>();
            for (int pump = 0; pump < 100000 && !playback.Finished; pump++)
            {
                playback.Advance(Pump);
                path.GroundAt(playback.Position, out float x, out float y);
                if (detector.Observe(playback.Position, x, y, false, playback.Curtain, playback.Speed, out BallJump jump))
                    jumps.Add(jump);
            }

            Assert.That(playback.Finished, Is.True);
            Assert.That(jumps, Is.Not.Empty, "a 5 m teleport is drawn over a few pumps, each past 2 m");
            Assert.That(jumps.TrueForAll(j => j.ToPosition > 49 && j.FromPosition < 50), Is.True,
                "reported: " + string.Join(", ", jumps.ConvertAll(j => j.ToPosition.ToString("F3"))));
        }

        [Test]
        public void Observing_allocates_nothing()
        {
            var detector = new BallJumpDetector();
            detector.Observe(0.0, 0f, 0f, false, 0.0, 1, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int reported = 0;
            for (int i = 1; i < 10000; i++)
            {
                float x = (i % 7) * 30f;
                if (detector.Observe(i * 0.1, x, 100f, i % 1000 == 0, i % 500 == 0 ? 1.0 : 0.0, 1 + i % 4, out BallJump jump))
                    reported += jump.Tick >= 0 ? 1 : 0;
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0), "the renderer reads this sixty times a second");
            Assert.That(reported, Is.GreaterThan(0));
        }
    }
}
