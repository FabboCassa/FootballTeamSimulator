using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// One man's place in his formation, as the V11 positioning reads it: his role, which line of
    /// the shape he stands in, how far across it, and any free-positioning offset off his line.
    /// </summary>
    public readonly struct V11Slot
    {
        public PositionRole Role { get; }

        /// <summary>His outfield line, 0 being the back line.</summary>
        public int LineRank { get; }

        /// <summary>How many outfield lines the shape has.</summary>
        public int LineCount { get; }

        /// <summary>Across the pitch, in permille, for the home side (the away side is mirrored).</summary>
        public int BaseYPermille { get; }

        /// <summary>Free positioning: how far in front of his line he has been put, in dm.</summary>
        public int OffsetXDm { get; }

        public V11Slot(PositionRole role, int lineRank, int lineCount, int baseYPermille, int offsetXDm)
        {
            Role = role;
            LineRank = lineRank;
            LineCount = lineCount;
            BaseYPermille = baseYPermille;
            OffsetXDm = offsetXDm;
        }

        /// <summary>A man on his formation anchor, with no free positioning.</summary>
        public static V11Slot InFormation(IReadOnlyList<PositionRole> roles, int index, MatchBalance cfg)
        {
            int rank = FormationGeometry.LineRank(roles, index, cfg, out int lines);
            return new V11Slot(roles[index], rank, lines, FormationGeometry.AnchorY(roles, index, cfg), 0);
        }

        /// <summary>A man who times runs in behind: a striker, a winger, or anybody in the front line.</summary>
        public bool IsForward =>
            Role == PositionRole.Striker || Role == PositionRole.Winger
            || (Role != PositionRole.Goalkeeper && LineRank == LineCount - 1);
    }
}
