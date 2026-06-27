namespace Sim.Core.Identity
{
    /// <summary>
    /// A club's colour palette (task 6.1). Four roles drive the whole art layer — kits,
    /// player tokens, crest, and the per-club tint of the UI theme:
    ///   • <see cref="Primary"/>   — the dominant club colour (vivid hue).
    ///   • <see cref="Secondary"/> — the contrast colour (a harmonic hue, or a light/dark
    ///     neutral), used for the away kit, the second crest fill and trims.
    ///   • <see cref="Accent"/>    — a metallic / highlight colour (gold, silver, white,
    ///     near-black) for crest borders and small flourishes.
    ///   • <see cref="Neutral"/>   — the light-or-dark base that contrasts the primary, the
    ///     legible colour for numbers / initials / text drawn on the primary.
    ///
    /// Guarantees from <see cref="ClubIdentityGenerator"/>: Primary and Secondary are always
    /// visually distinct (a minimum colour distance), and Neutral is always legible on Primary
    /// (a minimum luminance contrast) — "challenge, not chaos": an auto-generated kit is never
    /// an unreadable mush. Pure value type — no behaviour.
    /// </summary>
    public readonly struct ClubColors
    {
        public RgbColor Primary { get; }
        public RgbColor Secondary { get; }
        public RgbColor Accent { get; }
        public RgbColor Neutral { get; }

        public ClubColors(RgbColor primary, RgbColor secondary, RgbColor accent, RgbColor neutral)
        {
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
            Neutral = neutral;
        }
    }
}
