using System;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R9 of free-ball-real-movement (issue #187): the renderer draws the ball between stream frames
    /// along the path the simulation gave it (<see cref="BallPath"/>), and its size and shadow read
    /// its height (<see cref="BallLook"/>) so a cross reads as in the air at 1x. Read allocation-free.
    /// </summary>
    [TestFixture]
    public class ReadableBallTests
    {
        private const int Fpm = 300; // the 5 fps stream
        private const double Eps = 0.01;

        private static PositionStream Stream(int frames) => new PositionStream
        {
            TicksPerMinute = Fpm,
            PlayerCount = 11,
            LastTick = frames - 1,
            BallXY = new int[frames * 2],
            BallZ = new int[frames],
            Owner = new int[frames]
        };

        private static void Put(PositionStream s, int frame, int x, int y)
        {
            s.BallXY[frame * 2] = x;
            s.BallXY[frame * 2 + 1] = y;
        }

        /// <summary>The ball's ground velocity just before or just after <paramref name="position"/>, in dm per frame.</summary>
        private static (double X, double Y) Velocity(BallPath path, double position, bool after)
        {
            double from = after ? position : position - Eps, to = after ? position + Eps : position;
            path.GroundAt(from, out float x0, out float y0);
            path.GroundAt(to, out float x1, out float y1);
            return ((x1 - x0) / Eps, (y1 - y0) / Eps);
        }

        private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);

        /// <summary>A free ball struck at <paramref name="speedDm"/> dm per frame, slowed by friction every frame.</summary>
        private static PositionStream Rolling(int frames, double speedDm, float kept)
        {
            PositionStream s = Stream(frames);
            double x = 100, y = 80, v = speedDm;
            for (int t = 0; t < frames; t++)
            {
                Put(s, t, (int)Math.Round(x), (int)Math.Round(y));
                x += v * 0.8;
                y += v * 0.6;
                v *= kept;
            }

            return s;
        }

        // ------------------------------------------------------------- on the grass

        [Test]
        public void AFreeBall_KeepsTheFrictionsShareOfItsSpeed_OverEachFrame()
        {
            var fps5 = new BallPath(Stream(4));
            Assert.That(fps5.FrameKept, Is.EqualTo(0.9416).Within(0.006),
                "0.74 kept a second is 0.74^0.2 a 0.2 s frame");

            var old = new BallPath(new PositionStream { TicksPerMinute = 120, BallXY = new int[8], Owner = new int[4] });
            Assert.That(old.FrameKept, Is.EqualTo(0.860).Within(0.006), "a 2 fps replay's half-second frame");
        }

        [Test]
        public void AFreeBall_RunsOnAtItsOwnPace_AcrossEveryFrameBoundary_WhereALerpLurches()
        {
            var probe = new BallPath(Stream(2));
            PositionStream s = Rolling(14, 60, probe.FrameKept);
            var path = new BallPath(s);

            double pathJumps = 0, lerpJumps = 0;
            for (int b = 1; b < 12; b++)
            {
                path.GroundAt(b, out float x, out float y);
                Assert.That(x, Is.EqualTo(s.BallXY[b * 2]), $"frame {b} is drawn where it is");
                Assert.That(y, Is.EqualTo(s.BallXY[b * 2 + 1]));

                (double X, double Y) before = Velocity(path, b, false), after = Velocity(path, b, true);
                double speed = Length(before.X, before.Y);
                double jump = Length(after.X - before.X, after.Y - before.Y);
                double lerp = Length(
                    s.BallXY[b * 2 + 2] - 2 * s.BallXY[b * 2] + s.BallXY[b * 2 - 2],
                    s.BallXY[b * 2 + 3] - 2 * s.BallXY[b * 2 + 1] + s.BallXY[b * 2 - 1]);
                TestContext.Out.WriteLine($"[ball-path] frame {b}: speed {speed:F1}, path jump {jump:F2}, lerp jump {lerp:F2}");
                Assert.That(jump, Is.LessThan(0.01 * speed), $"frame {b}: the drawn ball changes pace on the boundary");
                pathJumps += jump;
                lerpJumps += lerp;
            }

            Assert.That(pathJumps, Is.LessThan(lerpJumps / 10), "a straight lerp steps the pace down at every frame");

            path.GroundAt(3.5, out float mx, out _);
            float lerpMid = (s.BallXY[6] + s.BallXY[8]) / 2f;
            Assert.That(mx, Is.GreaterThan(lerpMid), "slowing down, it covers more of the frame in its first half");
        }

        [Test]
        public void ABallAtAMansFeet_MovesAsHeDoes_Linearly()
        {
            PositionStream s = Stream(4);
            Put(s, 0, 100, 100);
            Put(s, 1, 130, 120);
            Put(s, 2, 140, 125);
            Put(s, 3, 160, 130);
            s.Owner[1] = 4;
            var path = new BallPath(s);

            path.GroundAt(1.25, out float x, out float y);
            Assert.That(x, Is.EqualTo(132.5f).Within(1e-3f));
            Assert.That(y, Is.EqualTo(121.25f).Within(1e-3f));
        }

        [Test]
        public void ADeadBallCarriedToItsSpot_KeepsItsConstantPace()
        {
            PositionStream s = Stream(6);
            for (int t = 0; t < 6; t++)
                Put(s, t, 300 + 4 * t, 200 - 3 * t);
            var path = new BallPath(s);

            for (double p = 1; p < 4; p += 0.125)
            {
                path.GroundAt(p, out float x, out float y);
                Assert.That(x, Is.EqualTo(300 + 4 * p).Within(1e-3), $"position {p}");
                Assert.That(y, Is.EqualTo(200 - 3 * p).Within(1e-3), $"position {p}");
            }
        }

        [Test]
        public void TheBall_NeverRunsPastEitherFrame_KickedFromRest_OrStopping()
        {
            PositionStream s = Stream(6);
            Put(s, 0, 500, 300);
            Put(s, 1, 500, 300);
            Put(s, 2, 560, 300); // struck
            Put(s, 3, 562, 300); // and stopped dead inside the next frame
            Put(s, 4, 562, 300);
            Put(s, 5, 480, 300); // struck back
            var path = new BallPath(s);

            float previous = 500f;
            for (double p = 0; p <= 4; p += 0.05)
            {
                path.GroundAt(p, out float x, out _);
                Assert.That(x, Is.InRange(500f, 562f), $"position {p}");
                Assert.That(x, Is.GreaterThanOrEqualTo(previous - 1e-3f), $"position {p}: the ball ran backwards");
                previous = x;
            }

            path.GroundAt(4.5, out float back, out _);
            Assert.That(back, Is.InRange(480f, 562f));
            path.GroundAt(99, out float end, out _);
            Assert.That(end, Is.EqualTo(480f), "past the end: the last frame");
        }

        // ------------------------------------------------------------- in the air

        [Test]
        public void ALoftedBall_RisesAndFallsOnGravitysArc_NotAPolyline()
        {
            const int frames = 12;
            PositionStream s = Stream(frames);
            const double g = 98.1 * 0.2 * 0.2; // dm per frame per frame
            double v0 = g * (frames - 1) / 2;
            for (int n = 0; n < frames; n++)
                s.BallZ[n] = (int)Math.Round(v0 * n - g * n * n / 2);
            var path = new BallPath(s);

            double pathJumps = 0, lerpJumps = 0;
            for (int b = 1; b < frames - 1; b++)
            {
                Assert.That(path.HeightAt(b), Is.EqualTo(s.BallZ[b]), $"frame {b} is drawn at its height");
                double before = (path.HeightAt(b) - path.HeightAt(b - Eps)) / Eps;
                double after = (path.HeightAt(b + Eps) - path.HeightAt(b)) / Eps;
                pathJumps += Math.Abs(after - before);
                lerpJumps += Math.Abs(s.BallZ[b + 1] - 2 * s.BallZ[b] + s.BallZ[b - 1]);
            }

            TestContext.Out.WriteLine($"[ball-path] height kinks: path {pathJumps:F2}, lerp {lerpJumps:F2}");
            Assert.That(pathJumps, Is.LessThan(lerpJumps / 3), "a lerp kinks the arc at every frame");

            float top = path.HeightAt(5.5);
            Assert.That(top, Is.GreaterThan((s.BallZ[5] + s.BallZ[6]) / 2f), "the arc bows up between frames");
            Assert.That(path.HeightAt(frames - 1), Is.EqualTo(0f), "landed");
        }

        [Test]
        public void ABallOnTheGrass_StaysOnIt_AndAnOldReplayHasNoHeight()
        {
            PositionStream s = Stream(3);
            var path = new BallPath(s);
            for (double p = 0; p <= 2; p += 0.1)
                Assert.That(path.HeightAt(p), Is.EqualTo(0f), $"position {p}");

            var old = new BallPath(new PositionStream { TicksPerMinute = Fpm, BallXY = new int[6], Owner = new int[3] });
            Assert.That(old.HeightAt(1.5), Is.EqualTo(0f));
        }

        [Test]
        public void AHeadHighCross_ReadsAsInTheAir_AndAHoofReadsHigher()
        {
            BallLook head = BallLook.At(18f), hoof = BallLook.At(60f);

            Assert.That(head.Scale, Is.GreaterThanOrEqualTo(1.25f), "a cross at head height reads as off the grass");
            Assert.That(head.LiftDm, Is.GreaterThanOrEqualTo(BallLook.MaxLiftDm * 0.3f));
            Assert.That(hoof.Scale, Is.GreaterThan(head.Scale + 0.25f), "and a hoof as much higher");

            BallLook previous = BallLook.At(0f);
            for (float h = 5f; h <= BallLook.FullHeightDm; h += 5f)
            {
                BallLook look = BallLook.At(h);
                Assert.That(look.Scale, Is.GreaterThan(previous.Scale), $"{h} dm");
                Assert.That(look.LiftDm, Is.GreaterThan(previous.LiftDm), $"{h} dm");
                Assert.That(look.ShadowAlpha, Is.LessThan(previous.ShadowAlpha), $"{h} dm");
                Assert.That(look.ShadowScale, Is.LessThan(previous.ShadowScale), $"{h} dm");
                previous = look;
            }
        }

        // ------------------------------------------------------------- a real match

        [Test]
        public void ARealMatch_DrawsEveryFreeBall_WithFewerPaceLurchesThanALerp()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            MatchReport report = new MatchEngine().Simulate(
                LineupSelector.BestEleven(league.Clubs[9]),
                LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(14_800));
            PositionStream s = report.Positions!.Unpack();
            var path = new BallPath(s);
            int[] xy = s.BallXY;

            double pathJumps = 0, lerpJumps = 0;
            int boundaries = 0;
            for (int b = 1; b + 1 < s.TickCount; b++)
            {
                if (s.Owner[b - 1] != PositionStream.NoOwner || s.Owner[b] != PositionStream.NoOwner
                    || s.Owner[b + 1] != PositionStream.NoOwner)
                    continue;

                int ix = xy[b * 2] - xy[b * 2 - 2], iy = xy[b * 2 + 1] - xy[b * 2 - 1];
                int ox = xy[b * 2 + 2] - xy[b * 2], oy = xy[b * 2 + 3] - xy[b * 2 + 1];
                if (Length(ix, iy) < 10 || ix * ox + iy * oy <= 0)
                    continue; // resting, or met and turned: not one run of a free ball

                (double X, double Y) before = Velocity(path, b, false), after = Velocity(path, b, true);
                pathJumps += Length(after.X - before.X, after.Y - before.Y);
                lerpJumps += Length(ox - ix, oy - iy);
                boundaries++;
            }

            TestContext.Out.WriteLine($"[ball-path] {boundaries} free-ball boundaries: path jumps {pathJumps:F0}, lerp jumps {lerpJumps:F0}");
            Assert.That(boundaries, Is.GreaterThan(100));
            Assert.That(pathJumps, Is.LessThan(lerpJumps * 0.6));
        }

        // ------------------------------------------------------------- per frame

        [Test]
        public void ReadingTheBallsPathEveryFrame_AllocatesNothing()
        {
            const int frames = 90 * Fpm;
            PositionStream s = Stream(frames);
            for (int t = 0; t < frames; t++)
            {
                Put(s, t, t * 7 % 1000, t * 3 % 680);
                s.BallZ[t] = t % 97;
                s.Owner[t] = t % 13 == 0 ? 3 : PositionStream.NoOwner;
            }

            var path = new BallPath(s);
            path.GroundAt(0.5, out _, out _);
            path.HeightAt(0.5);

            long before = GC.GetAllocatedBytesForCurrentThread();
            float sum = 0f;
            for (double p = 0; p < frames; p += 0.37)
            {
                path.GroundAt(p, out float x, out float y);
                sum += x + y + BallLook.At(path.HeightAt(p)).Scale;
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0), "the renderer reads this sixty times a second");
            Assert.That(sum, Is.GreaterThan(0f));
        }
    }
}
