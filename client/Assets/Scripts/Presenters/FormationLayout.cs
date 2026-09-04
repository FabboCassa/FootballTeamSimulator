using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Presenters
{
    /// <summary>
    /// Maps a lineup's role slots to normalized pitch coordinates for the 6.7 visual pitch.
    ///
    /// It used to carry its own copy of the anchor constants, which meant the Tactics screen and
    /// the match pitch could drift apart silently — and at engine phase 2, when the width stopped
    /// being laid out one role at a time and started being laid out one LINE at a time, they
    /// would have. So it now asks <see cref="FormationGeometry"/>, the single source both the
    /// simulation and the positional tilt read, and does nothing but normalize the answer.
    ///
    /// Coordinates are in [0,1]; X runs from the team's own goal (0) to the attacked goal (1).
    /// </summary>
    internal static class FormationLayout
    {
        private static readonly MatchBalance Match = new BalanceConfig().Match;

        public static (float X, float Y) Normalized(IReadOnlyList<PositionRole> roles, int slotIndex)
        {
            int x = FormationGeometry.AnchorX(roles[slotIndex], Match);
            int y = FormationGeometry.AnchorY(roles, slotIndex, Match);
            return (x / 1000f, y / 1000f);
        }
    }
}
