using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Canonical normalized anchor for a lineup slot given its role and its peers
    /// (task 6.10). This is the SINGLE SOURCE of the formation geometry the client's
    /// visual pitch mirrors and the tilt baseline reads: X by role (defence deep,
    /// attack high) from <see cref="MatchBalance.FormationAnchorXPermilleByRole"/>,
    /// and Y spread across the width among same-role peers using the same touchline
    /// margins the position stream ships (<see cref="MatchBalance.WideRoleYMarginDm"/> /
    /// <see cref="MatchBalance.CentralRoleYMarginDm"/> over the <see cref="Pitch.WidthDm"/>).
    ///
    /// Purely a function of the (resolved) role list + slot index → deterministic,
    /// integer math. A lineup whose slots sit exactly on these anchors carries no
    /// positional tilt (the shape's attack/defence nature already comes from the role
    /// mix), so a clean formation preset is the identity for the 6.10 effect.
    /// </summary>
    public static class FormationGeometry
    {
        /// <summary>Line-height anchor (permille) for a role. Defence deep, attack high.</summary>
        public static int AnchorX(PositionRole role, MatchBalance cfg)
        {
            int[] a = cfg.FormationAnchorXPermilleByRole;
            int i = (int)role;
            return i >= 0 && i < a.Length ? a[i] : 500;
        }

        /// <summary>
        /// Width anchor (permille) for the slot at <paramref name="slotIndex"/>: goalkeepers
        /// and lone players sit central; same-role peers spread evenly between the role's
        /// touchline margins (wide roles hug the line). Mirrors the client's FormationLayout
        /// and the position stream's BuildAnchors so the on-screen shape matches the sim.
        /// </summary>
        public static int AnchorY(IReadOnlyList<PositionRole> roles, int slotIndex, MatchBalance cfg)
        {
            PositionRole role = roles[slotIndex];
            if (role == PositionRole.Goalkeeper) return 500;

            bool wide = role == PositionRole.FullBack || role == PositionRole.Winger;
            int marginDm = wide ? cfg.WideRoleYMarginDm : cfg.CentralRoleYMarginDm;
            int lo = marginDm * 1000 / Pitch.WidthDm;
            int hi = 1000 - lo;

            int group = 0, indexInGroup = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] != role) continue;
                if (i < slotIndex) indexInGroup++;
                group++;
            }

            if (group <= 1) return 500;
            return lo + (hi - lo) * indexInGroup / (group - 1);
        }
    }
}
