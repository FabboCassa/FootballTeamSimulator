using UnityEngine;

namespace Fts.Services
{
    /// <summary>
    /// A client-side, render-ready snapshot of a club's generated visual identity (task 6.1b):
    /// the crest geometry as plain ints and the palette as Unity <see cref="Color"/>s. This is the
    /// bridge that lets the dumb FTS.Views layer (which must not reference Sim.Core) draw a club's
    /// crest and tint — the presenter passes these fields straight into <c>CrestRenderer</c> and
    /// the theme. Produced by <see cref="ClubIdentityService"/> from the Sim.Core generator.
    ///
    /// <see cref="Shape"/>/<see cref="Pattern"/> match the Sim.Core CrestShape/CrestPattern enum
    /// values; <see cref="Primary"/> and <see cref="Secondary"/> are the two crest fills, in that
    /// order, <see cref="Accent"/> the trim and <see cref="Emblem"/> the legible initials colour.
    /// </summary>
    public readonly struct ClubVisual
    {
        public int Shape { get; }
        public int Pattern { get; }
        public Color Primary { get; }
        public Color Secondary { get; }
        public Color Accent { get; }
        public Color Emblem { get; }

        public ClubVisual(int shape, int pattern, Color primary, Color secondary, Color accent, Color emblem)
        {
            Shape = shape;
            Pattern = pattern;
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
            Emblem = emblem;
        }
    }
}
