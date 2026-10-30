using System;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// Where the renderer draws the ball between two stream frames (spec free-ball-real-movement R9):
    /// along the path the simulation gave it, not a straight lerp at a constant pace per frame, which
    /// makes a rolling ball lurch at every frame boundary as its speed steps down by the friction.
    ///
    /// On the grass each frame is a cubic between its two frames. Where the frames either side are one
    /// run of a free ball, both chords share the ball's velocity on the frame between them, so its
    /// pace and line carry on across the boundary — a rolling ball slows smoothly, a dead ball carried
    /// to its spot keeps its constant pace. Where the run breaks (a touch, a strike, a stop) the chord
    /// slows through the frame as friction slows a free ball; a ball at a man's feet moves with him,
    /// linearly, as his token does. In the air the height follows gravity's parabola through the two
    /// frames.
    ///
    /// A frame position is the frame itself, exactly: a director's cut or the change of ends jumps
    /// to a frame and nothing here blends across it. Presentation only; reads the stream, never
    /// changes a result, and is allocation-free on every repaint.
    /// </summary>
    public sealed class BallPath
    {
        private readonly int[] _xy;
        private readonly int[] _z;
        private readonly int[] _owner;
        private readonly int _last;

        /// <summary>Two chords turning by more than this (about 25 degrees) are a touch, not one run.</summary>
        private const double MinRunCosine = 0.9;

        /// <summary>Gravity's lift over a frame's chord at its middle is a quarter of this, in decimetres.</summary>
        private readonly float _gravityBowDm;

        public BallPath(PositionStream stream, MatchBalance? cfg = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            MatchBalance m = cfg ?? new BalanceConfig().Match;
            int fpm = stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1;
            _xy = stream.BallXY ?? Array.Empty<int>();
            _z = stream.BallZ ?? Array.Empty<int>();
            _owner = stream.Owner ?? Array.Empty<int>();
            _last = _xy.Length / 2 - 1;

            // The ticks a frame holds, each slowed by the same integer retention the ball runs on.
            int ticksPerFrame = Math.Max(1, (m.TicksPerMinute + fpm / 2) / fpm);
            int perTick = MatchBall.PerTickRetention(Math.Max(1, Math.Min(999, m.BallSpeedKeptPermillePerSecond)), m.TicksPerSecond);
            double kept = 1.0;
            for (int i = 0; i < ticksPerFrame; i++)
                kept *= perTick / 1000.0;
            FrameKept = (float)kept;

            double frameSeconds = 60.0 / fpm;
            _gravityBowDm = (float)(MatchBall.GravityMilliDmPerSecond2 / 1000.0 * frameSeconds * frameSeconds / 2.0);
        }

        /// <summary>The share of its speed a free ball keeps over one frame.</summary>
        public float FrameKept { get; }

        /// <summary>The ball's point on the grass at stream position <paramref name="position"/>, in pitch decimetres.</summary>
        public void GroundAt(double position, out float x, out float y)
        {
            if (_last < 0)
            {
                x = 0f;
                y = 0f;
                return;
            }

            int a = FrameOf(position, out float f);
            int ax = _xy[a * 2], ay = _xy[a * 2 + 1];
            if (f <= 0f)
            {
                x = ax;
                y = ay;
                return;
            }

            int b = a + 1;
            double dx = _xy[b * 2] - ax, dy = _xy[b * 2 + 1] - ay;
            bool carried = Held(a);

            // Cubic Hermite on the frame's chord: the speeds it leaves and reaches its frames at.
            double sx, sy, ex, ey;
            if (carried || !SharedTangent(a, out sx, out sy))
            {
                double leave = carried ? 1.0 : 2.0 / (1.0 + FrameKept);
                sx = dx * leave;
                sy = dy * leave;
            }

            if (carried || !SharedTangent(b, out ex, out ey))
            {
                double reach = carried ? 1.0 : 2.0 * FrameKept / (1.0 + FrameKept);
                ex = dx * reach;
                ey = dy * reach;
            }

            double f2 = f * f, f3 = f2 * f;
            double toB = 3 * f2 - 2 * f3, fromStart = f3 - 2 * f2 + f, intoEnd = f3 - f2;
            x = (float)(ax + dx * toB + sx * fromStart + ex * intoEnd);
            y = (float)(ay + dy * toB + sy * fromStart + ey * intoEnd);
        }

        /// <summary>
        /// The ball's height at stream position <paramref name="position"/>, in decimetres: on gravity's
        /// arc through the two frames while it is off the grass; 0 when the stream carries no height.
        /// </summary>
        public float HeightAt(double position)
        {
            if (_z.Length == 0 || _last < 0)
                return 0f;

            int a = FrameOf(position, out float f);
            if (a + 1 >= _z.Length)
                return _z[Math.Min(a, _z.Length - 1)];

            int za = _z[a], zb = _z[a + 1];
            float h = za + (zb - za) * f;
            if (za > 0 || zb > 0)
                h += _gravityBowDm * f * (1f - f);
            return h < 0f ? 0f : h;
        }

        /// <summary>The frame <paramref name="position"/> lies in and how far through it; the last frame at its end.</summary>
        private int FrameOf(double position, out float f)
        {
            if (position <= 0 || _last == 0)
            {
                f = 0f;
                return 0;
            }

            if (position >= _last)
            {
                f = 0f;
                return _last;
            }

            int a = (int)Math.Floor(position);
            f = (float)(position - a);
            return a;
        }

        /// <summary>
        /// The ball's velocity on frame <paramref name="frame"/>, in dm per frame, when the frames
        /// either side are one run of a free ball: running on the same way, neither struck faster nor
        /// slowed harder than friction can. Both chords meeting there then share it, so the drawn ball
        /// keeps its pace and line across the boundary: the geometric mean of their lengths (a ball
        /// slowing by a constant share a frame is that fast on the boundary) along their bisector.
        /// False where the ball is held, met, turned, struck or stopped: each chord keeps its own.
        /// </summary>
        private bool SharedTangent(int frame, out double tx, out double ty)
        {
            tx = 0;
            ty = 0;
            int a = frame - 1, b = frame + 1;
            if (a < 0 || b > _last || Held(a) || Held(frame))
                return false;

            double ix = _xy[frame * 2] - _xy[a * 2], iy = _xy[frame * 2 + 1] - _xy[a * 2 + 1];
            double ox = _xy[b * 2] - _xy[frame * 2], oy = _xy[b * 2 + 1] - _xy[frame * 2 + 1];
            double li = Math.Sqrt(ix * ix + iy * iy), lo = Math.Sqrt(ox * ox + oy * oy);
            if (li <= 0 || lo <= 0)
                return false;

            double ratio = lo / li;
            if (ratio > 1.0 / FrameKept || ratio < FrameKept * FrameKept
                || (ix * ox + iy * oy) < MinRunCosine * li * lo)
                return false;

            double bx = ix / li + ox / lo, by = iy / li + oy / lo;
            double scale = Math.Sqrt(li * lo) / Math.Sqrt(bx * bx + by * by);
            tx = bx * scale;
            ty = by * scale;
            return true;
        }

        private bool Held(int frame) => frame < _owner.Length && _owner[frame] != PositionStream.NoOwner;
    }
}
