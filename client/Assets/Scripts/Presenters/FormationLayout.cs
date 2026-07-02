using System.Collections.Generic;
using Sim.Core.Domain;

namespace Fts.Presenters
{
    /// <summary>
    /// Maps a lineup's role slots to normalized pitch coordinates for the 6.7 visual pitch.
    /// Mirrors Sim.Core's MatchBalance anchor geometry (PositionStreamGenerator.BuildAnchors)
    /// so the shape shown on the Squad/Tactics screens matches the one the match renderer draws:
    /// X by role (defence deep, attack high), Y spread across the width among same-role peers.
    /// Coordinates are in [0,1]; X runs from the team's own goal (0) to the attacked goal (1).
    /// </summary>
    internal static class FormationLayout
    {
        // X permille by role index (GK,CB,FB,DM,CM,AM,W,ST) and touchline Y margins as a
        // fraction of the 680 dm pitch width — the same constants MatchBalance ships.
        private static readonly int[] AnchorXPermille = { 40, 180, 200, 340, 440, 540, 620, 720 };
        private const float WideMarginFrac = 80f / 680f;
        private const float CentralMarginFrac = 170f / 680f;

        public static (float X, float Y) Normalized(IReadOnlyList<PositionRole> roles, int slotIndex)
        {
            PositionRole role = roles[slotIndex];
            int roleIndex = (int)role;
            float nx = (roleIndex >= 0 && roleIndex < AnchorXPermille.Length ? AnchorXPermille[roleIndex] : 500) / 1000f;

            bool wide = role == PositionRole.FullBack || role == PositionRole.Winger;
            float lo = wide ? WideMarginFrac : CentralMarginFrac;
            float hi = 1f - lo;

            int group = 0, indexInGroup = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] != role) continue;
                if (i < slotIndex) indexInGroup++;
                group++;
            }

            float ny = (role == PositionRole.Goalkeeper || group <= 1)
                ? 0.5f
                : lo + (hi - lo) * indexInGroup / (group - 1);

            return (nx, ny);
        }
    }
}
