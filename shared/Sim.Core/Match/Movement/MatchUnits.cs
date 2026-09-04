using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The number space the visual simulation runs in (task 13.2).
    ///
    /// Positions and velocities are integers in SIXTEENTHS OF A DECIMETRE. The extra
    /// precision is what lets a ball decelerate smoothly and a player accelerate instead
    /// of snapping between whole decimetres, while keeping every value an integer — which
    /// is what makes the stream bit-identical on .NET, Mono and IL2CPP. Only the frames
    /// written into the <see cref="PositionStream"/> are rounded back to decimetres.
    /// </summary>
    internal static class U
    {
        /// <summary>Sub-units per decimetre.</summary>
        public const int Scale = 16;

        public static readonly int LengthU = Pitch.LengthDm * Scale;
        public static readonly int WidthU = Pitch.WidthDm * Scale;
        public static readonly int CenterXU = Pitch.CenterX * Scale;
        public static readonly int CenterYU = Pitch.CenterY * Scale;

        public static int Dm(int units) => units / Scale;
        public static int Units(int dm) => dm * Scale;

        /// <summary>
        /// A speed written in decimetres per second as units per tick — the one place the
        /// length unit and the time base meet (engine phase 1). Every speed in the config is
        /// physical and per-second; nothing in the model is allowed to be "per tick" by hand,
        /// which is how the old model ended up with players sprinting at 0.7 m/s.
        /// </summary>
        public static int PerTick(int dmPerSecond, MatchBalance cfg)
        {
            int tps = cfg.TicksPerSecond < 1 ? 1 : cfg.TicksPerSecond;
            return dmPerSecond * Scale / tps;
        }

        /// <summary>An acceleration written in decimetres per second per second, as units per tick per tick.</summary>
        public static int PerTickPerTick(int dmPerSecond2, MatchBalance cfg)
        {
            int tps = cfg.TicksPerSecond < 1 ? 1 : cfg.TicksPerSecond;
            int square = tps * tps;
            return (dmPerSecond2 * Scale + square / 2) / square;
        }

        public static int ClampX(int x) => x < 0 ? 0 : (x > LengthU ? LengthU : x);
        public static int ClampY(int y) => y < 0 ? 0 : (y > WidthU ? WidthU : y);

        /// <summary>Length of a vector, in the same units.</summary>
        public static int Length(int dx, int dy)
        {
            long sq = (long)dx * dx + (long)dy * dy;
            return MovementGeometry.Sqrt(sq > int.MaxValue ? int.MaxValue : (int)sq);
        }

        public static int Distance(int ax, int ay, int bx, int by) => Length(ax - bx, ay - by);

        /// <summary>
        /// The SQUARE of a distance. Anything that only wants to know which of two things is
        /// nearer, or whether something is inside a radius, compares these and never takes a
        /// root — which at 10 Hz is the difference between a match that costs tens of
        /// milliseconds and one that costs hundreds.
        /// </summary>
        public static long DistanceSq(int ax, int ay, int bx, int by)
        {
            long dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }

        /// <summary>Rescales a vector to the given length (zero-safe).</summary>
        public static void Scaled(int dx, int dy, int length, out int x, out int y)
        {
            int current = Length(dx, dy);
            if (current <= 0)
            {
                x = 0;
                y = 0;
                return;
            }

            x = (int)((long)dx * length / current);
            y = (int)((long)dy * length / current);
        }

        /// <summary>Caps a vector's length, leaving its direction alone.</summary>
        public static void Cap(ref int dx, ref int dy, int maxLength)
        {
            long sq = (long)dx * dx + (long)dy * dy;
            if (sq <= (long)maxLength * maxLength) return;   // the common case, with no root taken
            Scaled(dx, dy, maxLength, out dx, out dy);
        }
    }
}
