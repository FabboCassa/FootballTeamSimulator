using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>One playable nation and how deep into its pyramid the player wants to play (task 11.1).</summary>
    public sealed class PlayableNation
    {
        /// <summary>Nation code, e.g. "ITA". Must exist in the nation registry the world was generated from.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>How many tiers of this nation run at <see cref="LeagueDetailLevel.Playable"/>, from tier 1 down.</summary>
        public int PlayableTiers { get; set; } = 1;
    }

    /// <summary>
    /// The scope the world was generated with (task 11.1): which nations are playable and how
    /// big the surrounding database is. Stored in the save and, per the FM rule the user chose,
    /// IMMUTABLE for that career — the world is regenerated from (seed + scope), so changing the
    /// scope mid-save would silently rewrite club and player ids.
    /// </summary>
    public sealed class WorldScope
    {
        public List<PlayableNation> Playable { get; set; } = new List<PlayableNation>();

        public DatabaseSize Size { get; set; } = DatabaseSize.Medium;

        /// <summary>True when the world is one of the pre-11.1 two-division careers (no nations).</summary>
        public bool IsLegacy => Playable.Count == 0;

        public bool IsPlayableNation(string code)
        {
            foreach (PlayableNation nation in Playable)
            {
                if (nation.Code == code)
                    return true;
            }

            return false;
        }

        /// <summary>Tiers played in full detail for a nation (0 when the nation is not playable).</summary>
        public int PlayableTiersOf(string code)
        {
            foreach (PlayableNation nation in Playable)
            {
                if (nation.Code == code)
                    return nation.PlayableTiers;
            }

            return 0;
        }

        public WorldScope Clone()
        {
            var clone = new WorldScope { Size = Size };
            foreach (PlayableNation nation in Playable)
                clone.Playable.Add(new PlayableNation { Code = nation.Code, PlayableTiers = nation.PlayableTiers });

            return clone;
        }
    }
}
