namespace Sim.Core.Match
{
    /// <summary>
    /// Pitch geometry shared by the engine and the renderer.
    /// Coordinates are integers in decimetres (dm): standard 105m x 68m pitch.
    /// Integer math keeps the position stream bit-identical across runtimes.
    /// The home team attacks toward X = LengthDm; the away team toward X = 0.
    /// </summary>
    public static class Pitch
    {
        public const int LengthDm = 1050;
        public const int WidthDm = 680;
        public const int CenterX = LengthDm / 2;
        public const int CenterY = WidthDm / 2;

        public static int ClampX(int x) => x < 0 ? 0 : (x > LengthDm ? LengthDm : x);
        public static int ClampY(int y) => y < 0 ? 0 : (y > WidthDm ? WidthDm : y);
    }

    /// <summary>A point on the pitch in decimetres. Serializable.</summary>
    public struct PitchPoint
    {
        public int X { get; set; }
        public int Y { get; set; }

        public PitchPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
