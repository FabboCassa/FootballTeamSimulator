using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// Canonical normalized anchor for a lineup slot given its role and its peers
    /// (task 6.10). This is the SINGLE SOURCE of the formation geometry the client's
    /// visual pitch mirrors, the tilt baseline reads, and the match block transform
    /// builds on: X by role (defence deep, attack high) from
    /// <see cref="MatchBalance.FormationAnchorXPermilleByRole"/>, and Y by LINE.
    ///
    /// The width used to be laid out one ROLE at a time, each role group spread across
    /// the whole pitch on its own. In a 4-3-3 that put the two centre-backs on 250 and
    /// 750 permille — thirty-four metres apart, with a hole between them a bus could be
    /// driven through — because the pair was spread over the same span the full-backs
    /// were (engine phase 2, §1.3 of docs/engine/MATCH_ENGINE_PLAN.md).
    ///
    /// A footballer does not stand relative to the others who share his job title; he
    /// stands in a LINE, next to whoever else is in it. So the unit here is the line:
    /// roles that belong on the same band of the pitch (<see
    /// cref="MatchBalance.FormationLineByRole"/>) are laid out together — the wide men
    /// on the touchline margin, the rest spaced at a realistic distance from each other
    /// around the middle. A back four comes out as
    /// <c>FB 8 m · CB 27 m · CB 41 m · FB 60 m</c>, which is a line.
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
        /// Which band of the pitch a role stands on. Roles sharing a number stand on the
        /// same line: centre-backs next to full-backs, wingers next to the striker.
        /// </summary>
        public static int LineOf(PositionRole role, MatchBalance cfg)
        {
            int[] a = cfg.FormationLineByRole;
            int i = (int)role;
            return i >= 0 && i < a.Length ? a[i] : 3;
        }

        /// <summary>
        /// The slot's line counted among the lines the shape ACTUALLY occupies: 0 is the
        /// back line and <paramref name="lineCount"/> is how many outfield lines there are.
        ///
        /// This is what the match block spaces at ten to twelve metres, so the depth of the
        /// block follows from the shape rather than from a table: a 4-4-2 has three lines and
        /// defends twenty-two metres deep, a 4-2-3-1 has four and defends thirty-three. The
        /// goalkeeper is not part of it — he belongs to his six-yard box, not to the block.
        /// </summary>
        public static int LineRank(IReadOnlyList<PositionRole> roles, int slotIndex, MatchBalance cfg, out int lineCount)
        {
            int mine = LineOf(roles[slotIndex], cfg);

            // Distinct outfield lines below and including mine, counted without allocating.
            int rank = 0;
            lineCount = 0;
            for (int line = 1; line <= MaxLine; line++)
            {
                bool occupied = false;
                for (int i = 0; i < roles.Count; i++)
                {
                    if (roles[i] == PositionRole.Goalkeeper) continue;
                    if (LineOf(roles[i], cfg) == line) { occupied = true; break; }
                }

                if (!occupied) continue;
                if (line < mine) rank++;
                lineCount++;
            }

            return rank;
        }

        /// <summary>
        /// Width anchor (permille) for the slot at <paramref name="slotIndex"/>. The
        /// goalkeeper sits central; everybody else is placed within HIS LINE — the wide
        /// roles (and, in a line of four or more, its outermost pair whatever their role,
        /// because the outside men of a flat four ARE its wide players) on the touchline
        /// margin, the remainder spaced <see cref="MatchBalance.LineCentralSpacingDm"/>
        /// apart around the centre and kept inside the central margin.
        ///
        /// Mirrored by the client's FormationLayout, which calls straight into here, so the
        /// shape on the Tactics screen is the shape that lines up on the match pitch.
        /// </summary>
        public static int AnchorY(IReadOnlyList<PositionRole> roles, int slotIndex, MatchBalance cfg)
        {
            PositionRole role = roles[slotIndex];
            if (role == PositionRole.Goalkeeper) return 500;

            int line = LineOf(role, cfg);

            // My line, in slot order: how many of us, and how many are genuine wide roles.
            int members = 0, wideRoles = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                if (!InLine(roles, i, line, cfg)) continue;
                if (IsWide(roles[i])) wideRoles++;
                members++;
            }

            if (members <= 1) return 500;

            // The two places on the outside of the line. Genuine wide roles claim them; a line
            // of four or more fills both whatever its roles are called, because the outside men
            // of a flat four ARE its wide players (a 4-4-2's wide midfielders are encoded CM,
            // and they belong on the touchline, not tucked into the middle).
            int wideRolesUsed = wideRoles > 2 ? 2 : wideRoles;
            int wideSlots = wideRolesUsed;
            if (members >= cfg.LineWideFromMembers && wideSlots < 2) wideSlots = 2;
            if (wideSlots > members) wideSlots = members;
            int fillers = wideSlots - wideRolesUsed;
            int centrals = members - wideSlots;

            // One pass down the line: the wide roles take the outside places, a place still free
            // goes to the next man along, and everybody else is the centre of the line.
            int wideRank = -1, centralRank = 0;
            int wideSeen = 0, fillerSeen = 0, centralSeen = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                if (!InLine(roles, i, line, cfg)) continue;

                int rank;
                bool wide;
                if (IsWide(roles[i]) && wideSeen < wideRolesUsed) { wide = true; rank = wideSeen++; }
                else if (fillerSeen < fillers) { wide = true; rank = wideRolesUsed + fillerSeen++; }
                else { wide = false; rank = centralSeen++; }

                if (i != slotIndex) continue;
                if (wide) wideRank = rank;
                else centralRank = rank;
                break;
            }

            int offsetDm;
            if (wideRank >= 0)
            {
                int half = Pitch.WidthDm / 2 - cfg.WideRoleYMarginDm;
                offsetDm = (wideRank & 1) == 0 ? -half : half;
            }
            else
            {
                // Spaced a realistic distance from each other and centred on the pitch, rather
                // than spread over whatever width is left over.
                offsetDm = (2 * centralRank - (centrals - 1)) * cfg.LineCentralSpacingDm / 2;

                int limit = Pitch.WidthDm / 2 - cfg.CentralRoleYMarginDm;
                if (offsetDm < -limit) offsetDm = -limit;
                else if (offsetDm > limit) offsetDm = limit;
            }

            return 500 + offsetDm * 1000 / Pitch.WidthDm;
        }

        private static bool InLine(IReadOnlyList<PositionRole> roles, int i, int line, MatchBalance cfg) =>
            roles[i] != PositionRole.Goalkeeper && LineOf(roles[i], cfg) == line;

        /// <summary>Highest line index the config can name.</summary>
        private const int MaxLine = 15;

        private static bool IsWide(PositionRole role) =>
            role == PositionRole.FullBack || role == PositionRole.Winger;

    }
}
