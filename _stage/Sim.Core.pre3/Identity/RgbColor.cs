using System;

namespace Sim.Core.Identity
{
    /// <summary>
    /// A 24-bit RGB colour (task 6.1, the art pass' club-identity layer). Pure value type,
    /// platform-independent: channels are integers clamped to [0, 255] and the only colour
    /// maths here (HSV→RGB, luminance, distance) is integer arithmetic — NO transcendental
    /// functions, so the result is byte-identical on .NET / Mono / IL2CPP (determinism rule,
    /// ARCHITECTURE.md §4.1). The host (client painter2D, server) turns these into pixels /
    /// USS strings; Sim.Core only computes them.
    /// </summary>
    public readonly struct RgbColor : IEquatable<RgbColor>
    {
        public int R { get; }
        public int G { get; }
        public int B { get; }

        public RgbColor(int r, int g, int b)
        {
            R = Clamp(r);
            G = Clamp(g);
            B = Clamp(b);
        }

        private static int Clamp(int c) => c < 0 ? 0 : c > 255 ? 255 : c;

        /// <summary>The colour packed as 0xRRGGBB.</summary>
        public int Packed => (R << 16) | (G << 8) | B;

        /// <summary>Reconstructs a colour from a 0xRRGGBB packed int.</summary>
        public static RgbColor FromPacked(int packed) =>
            new RgbColor((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);

        /// <summary>
        /// Perceived brightness in [0, 255] using the integer Rec. 601 luma weights
        /// (0.299 R + 0.587 G + 0.114 B). Used to pick a legible text/emblem colour and to
        /// keep palettes readable.
        /// </summary>
        public int Luminance => (R * 299 + G * 587 + B * 114) / 1000;

        /// <summary>
        /// Cheap perceptual distance to another colour: the sum of absolute channel
        /// differences, in [0, 765]. Big enough = the two colours are visually distinct.
        /// </summary>
        public int DistanceTo(RgbColor o) =>
            Math.Abs(R - o.R) + Math.Abs(G - o.G) + Math.Abs(B - o.B);

        /// <summary>
        /// Integer HSV→RGB. <paramref name="h"/> is the hue in degrees (wrapped into [0, 360)),
        /// <paramref name="s"/> the saturation and <paramref name="v"/> the value, both in
        /// [0, 255]. Standard six-sector conversion done with integer maths only.
        /// </summary>
        public static RgbColor FromHsv(int h, int s, int v)
        {
            h = ((h % 360) + 360) % 360;
            s = s < 0 ? 0 : s > 255 ? 255 : s;
            v = v < 0 ? 0 : v > 255 ? 255 : v;

            int hi = h / 60;   // sector 0..5
            int f = h % 60;    // position within the sector, 0..59

            int p = v * (255 - s) / 255;
            int q = v * (255 - s * f / 60) / 255;
            int t = v * (255 - s * (60 - f) / 60) / 255;

            switch (hi)
            {
                case 0: return new RgbColor(v, t, p);
                case 1: return new RgbColor(q, v, p);
                case 2: return new RgbColor(p, v, t);
                case 3: return new RgbColor(p, q, v);
                case 4: return new RgbColor(t, p, v);
                default: return new RgbColor(v, p, q); // case 5
            }
        }

        /// <summary>"#RRGGBB" — handy for the client's USS / hex needs.</summary>
        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

        public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object? obj) => obj is RgbColor o && Equals(o);
        public override int GetHashCode() => Packed;

        public static bool operator ==(RgbColor a, RgbColor b) => a.Equals(b);
        public static bool operator !=(RgbColor a, RgbColor b) => !a.Equals(b);

        public override string ToString() => ToHex();
    }
}
