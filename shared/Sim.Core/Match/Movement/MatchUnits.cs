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

        public static int ClampX(int x) => x < 0 ? 0 : (x > LengthU ? LengthU : x);
        public static int ClampY(int y) => y < 0 ? 0 : (y > WidthU ? WidthU : y);

        /// <summary>Length of a vector, in the same units.</summary>
        public static int Length(int dx, int dy)
        {
            long sq = (long)dx * dx + (long)dy * dy;
            return MovementGeometry.Sqrt(sq > int.MaxValue ? int.MaxValue : (int)sq);
        }

        public static int Distance(int ax, int ay, int bx, int by) => Length(ax - bx, ay - by);

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
            if (Length(dx, dy) <= maxLength) return;
            Scaled(dx, dy, maxLength, out dx, out dy);
        }
    }
}
