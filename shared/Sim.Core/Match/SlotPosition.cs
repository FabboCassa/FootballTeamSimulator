using System;

namespace Sim.Core.Match
{
    /// <summary>
    /// A lineup slot's normalized on-pitch spot (task 6.10 — free positioning).
    /// X = line height in 1/1000 of the pitch length, from the team's own goal (0)
    /// toward the attacked goal (1000); Y = width in 1/1000 across the pitch
    /// (0/1000 = touchlines, 500 = centre). Integer permille → deterministic,
    /// serializable, and independent of the dm <see cref="Pitch"/> geometry.
    ///
    /// This is the raw geometry the user drags; the ROLE a player is then playing is
    /// resolved from it by <see cref="Tactics.ZoneRole"/>, and the small continuous
    /// tilt of the team's shape by <see cref="Tactics.PositionalTilt"/>. Distinct from
    /// <see cref="PitchPoint"/> (which is absolute decimetres for the position stream).
    /// </summary>
    public readonly struct SlotPosition : IEquatable<SlotPosition>
    {
        public readonly int XPermille;
        public readonly int YPermille;

        public SlotPosition(int xPermille, int yPermille)
        {
            XPermille = Clamp(xPermille);
            YPermille = Clamp(yPermille);
        }

        private static int Clamp(int v) => v < 0 ? 0 : (v > 1000 ? 1000 : v);

        public bool Equals(SlotPosition other) =>
            XPermille == other.XPermille && YPermille == other.YPermille;

        public override bool Equals(object? obj) => obj is SlotPosition o && Equals(o);

        public override int GetHashCode() => (XPermille * 1009) + YPermille;
    }
}
