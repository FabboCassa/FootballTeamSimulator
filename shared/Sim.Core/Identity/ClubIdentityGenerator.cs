using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Identity
{
    /// <summary>
    /// Generates a club's full visual identity — palette + crest — deterministically from
    /// (clubId, worldSeed) (task 6.1, the art pass). Like <see cref="Market.ClubPersonalities"/>
    /// it derives a stable local <see cref="Pcg32"/> from the two ids and draws from it, so the
    /// identity is the same on every call / platform / reload and needs no storage (it is
    /// regenerated for display). It consumes no shared RNG stream, so it is replay-safe; and
    /// nothing in the match engine calls it, so golden masters are unaffected (opt-in).
    ///
    /// Two readability guarantees baked into the maths ("challenge, not chaos"):
    ///   1. Primary and Secondary are always at least <see cref="IdentityBalance.MinFillColorDistance"/>
    ///      apart — a generated two-tone kit/crest never collapses into one colour.
    ///   2. The crest emblem (and the palette <see cref="ClubColors.Neutral"/>) is always the
    ///      light-or-dark neutral that contrasts the primary, so initials drawn on the primary
    ///      are always legible.
    /// </summary>
    public static class ClubIdentityGenerator
    {
        // Mixing constants (splitmix-style) so neighbouring club ids land far apart in the
        // RNG space — the same decorrelation trick used across the world progressors.
        private const ulong ClubMix = 0x9E3779B97F4A7C15UL;
        private const ulong Sequence = 0xC1DEUL;

        public static ClubIdentity Generate(int clubId, ulong worldSeed, IdentityBalance cfg)
        {
            ulong seed = worldSeed ^ ((ulong)(uint)clubId * ClubMix);
            var rng = new Pcg32(seed, Sequence);

            RgbColor neutralLight = RgbColor.FromPacked(cfg.NeutralLight);
            RgbColor neutralDark = RgbColor.FromPacked(cfg.NeutralDark);

            // --- Primary: a vivid hue ---
            int hue = rng.NextInt(0, 360);
            int sp = DrawRange(rng, cfg.PrimarySaturationMin, cfg.PrimarySaturationMax);
            int vp = DrawRange(rng, cfg.PrimaryValueMin, cfg.PrimaryValueMax);
            RgbColor primary = RgbColor.FromHsv(hue, sp, vp);

            RgbColor contrastNeutral =
                primary.Luminance >= cfg.ContrastLuminanceThreshold ? neutralDark : neutralLight;

            // --- Secondary: a harmonic hue, or a neutral contrast ---
            int scheme = WeightedPick(rng, cfg.SchemeWeights);
            RgbColor secondary;
            switch (scheme)
            {
                case 0: secondary = HuedSecondary(rng, hue + cfg.ComplementaryHueOffset, cfg); break;
                case 1: secondary = HuedSecondary(rng, hue + cfg.AnalogousHueOffset, cfg); break;
                case 2: secondary = HuedSecondary(rng, hue - cfg.AnalogousHueOffset, cfg); break;
                case 3: secondary = HuedSecondary(rng, hue + cfg.TriadicHueOffset, cfg); break;
                default: secondary = contrastNeutral; break; // NeutralContrast scheme
            }

            // Guarantee #1: the two fills must be clearly distinguishable.
            if (primary.DistanceTo(secondary) < cfg.MinFillColorDistance)
                secondary = contrastNeutral;

            // --- Accent: a metallic / highlight from the palette ---
            RgbColor accent = RgbColor.FromPacked(Pick(rng, cfg.AccentColors));

            // Guarantee #2: emblem/text legible on the primary.
            RgbColor emblem = contrastNeutral;

            var colors = new ClubColors(primary, secondary, accent, contrastNeutral);

            // --- Crest geometry ---
            var shape = (CrestShape)WeightedPick(rng, cfg.ShapeWeights);
            var pattern = (CrestPattern)WeightedPick(rng, cfg.PatternWeights);
            var crest = new CrestDesign(shape, pattern, primary, secondary, accent, emblem);

            return new ClubIdentity(clubId, colors, crest);
        }

        /// <summary>A hued secondary colour at the given (unwrapped) hue, drawing its own saturation/value.</summary>
        private static RgbColor HuedSecondary(Pcg32 rng, int hue, IdentityBalance cfg)
        {
            int s = DrawRange(rng, cfg.SecondarySaturationMin, cfg.SecondarySaturationMax);
            int v = DrawRange(rng, cfg.SecondaryValueMin, cfg.SecondaryValueMax);
            return RgbColor.FromHsv(hue, s, v);
        }

        /// <summary>Inclusive integer draw in [min, max], tolerant of a swapped/degenerate range.</summary>
        private static int DrawRange(Pcg32 rng, int min, int max)
        {
            if (max < min) { int t = min; min = max; max = t; }
            return min == max ? min : rng.NextInt(min, max + 1);
        }

        /// <summary>Uniformly picks an element of <paramref name="values"/> (returns 0 if empty).</summary>
        private static int Pick(Pcg32 rng, int[] values)
        {
            if (values == null || values.Length == 0) return 0;
            return values[rng.NextInt(0, values.Length)];
        }

        /// <summary>Picks an index in [0, weights.Length) proportional to the weights (index 0 if all non-positive).</summary>
        private static int WeightedPick(Pcg32 rng, int[] weights)
        {
            if (weights == null || weights.Length == 0) return 0;

            int total = 0;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i] > 0) total += weights[i];

            if (total <= 0) return 0;

            int roll = rng.NextInt(0, total);
            int acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                acc += weights[i];
                if (roll < acc) return i;
            }
            return weights.Length - 1; // unreachable; satisfies the compiler
        }
    }
}
