namespace Sim.Core.Identity
{
    /// <summary>The outer silhouette of a club crest (task 6.1). The client's painter2D draws it.</summary>
    public enum CrestShape
    {
        Shield = 0,
        Circle = 1,
        Diamond = 2,
        RoundedSquare = 3,
    }

    /// <summary>
    /// How the two crest fill colours are arranged inside the shape (task 6.1). The client's
    /// painter2D clips each pattern to the crest silhouette.
    /// </summary>
    public enum CrestPattern
    {
        Solid = 0,            // FillA only
        VerticalHalves = 1,   // left FillA | right FillB
        HorizontalHalves = 2, // top FillA / bottom FillB
        DiagonalSash = 3,     // FillA with a FillB diagonal band
        VerticalStripes = 4,  // alternating vertical stripes
        Hoops = 5,            // alternating horizontal bands
        Quarters = 6,         // 2×2 quartered
    }

    /// <summary>
    /// A fully-resolved, render-ready crest spec (task 6.1) — everything the client's painter2D
    /// needs to draw a club badge with no further lookups: the outer <see cref="Shape"/>, the
    /// internal <see cref="Pattern"/>, the two fill colours, a trim/border colour and the
    /// legible <see cref="Emblem"/> colour for the club's initials drawn on <see cref="FillA"/>.
    ///
    /// Colours are copied in (resolved from the palette) so the renderer is a pure painter:
    /// <see cref="FillA"/> = palette primary, <see cref="FillB"/> = secondary (guaranteed
    /// distinct from A), <see cref="Trim"/> = accent, <see cref="Emblem"/> = the neutral that
    /// is legible on A. Pure value type — no behaviour.
    /// </summary>
    public readonly struct CrestDesign
    {
        public CrestShape Shape { get; }
        public CrestPattern Pattern { get; }
        public RgbColor FillA { get; }
        public RgbColor FillB { get; }
        public RgbColor Trim { get; }
        public RgbColor Emblem { get; }

        public CrestDesign(CrestShape shape, CrestPattern pattern,
                           RgbColor fillA, RgbColor fillB, RgbColor trim, RgbColor emblem)
        {
            Shape = shape;
            Pattern = pattern;
            FillA = fillA;
            FillB = fillB;
            Trim = trim;
            Emblem = emblem;
        }
    }
}
