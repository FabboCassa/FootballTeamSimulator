using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Wraps a pre-11.1 flat list of divisions into a <see cref="World"/> (task 11.1b).
    ///
    /// Before 11.1 a career WAS its two divisions: a `List&lt;League&gt;` and nothing around it. The
    /// save now holds a world, so an existing career is lifted into one — as a single invented
    /// nation containing exactly those divisions, with the same club and player ids, the same
    /// squads, the same standings. Nothing is regenerated and nothing is thrown away: the career
    /// carries on exactly as it was, it simply now has a (very small) world around it.
    ///
    /// Two markers make a migrated world recognisable for the rest of its life:
    ///   • its leagues keep an EMPTY <see cref="League.NationCode"/>, which is what
    ///     <see cref="Career.WorldRollover"/> reads to keep generating their fixtures off the old
    ///     stream, so an upgraded career's calendar does not shift under it;
    ///   • its <see cref="WorldScope"/> is left empty, so <see cref="WorldScope.IsLegacy"/> is true
    ///     and the UI can say so rather than pretending the player picked this scope.
    /// </summary>
    public static class LegacyWorld
    {
        /// <summary>Code of the invented nation a migrated career lives in.</summary>
        public const string Code = "LEG";

        /// <summary>Reputation given to it: mid-table, so club strengths read sensibly beside a generated world.</summary>
        public const int Reputation = 72;

        public static World Wrap(List<League> leagues, string nationName = "Cartonia")
        {
            var nation = new Nation
            {
                Id = 0,
                Code = Code,
                Name = nationName,
                Continent = Continent.Europe,
                Reputation = Reputation,
                CultureId = "italian"
            };

            var ordered = new List<League>(leagues);
            ordered.Sort((a, b) => a.Division.CompareTo(b.Division));

            foreach (League league in ordered)
            {
                league.DetailLevel = LeagueDetailLevel.Playable;
                league.NationCode = string.Empty; // the legacy marker - see the class comment
                nation.Leagues.Add(league);
            }

            var world = new World { NextGeneratedPlayerId = WorldIds.FirstDynamicPlayerId };
            world.Nations.Add(nation);
            return world;
        }

        /// <summary>
        /// Adds a division to a migrated world, keeping the tier order. Used by the pre-11.1 save
        /// migrations that themselves add a division (v2 → v3 grew the single league into two).
        /// </summary>
        public static void AddLeague(World world, League league)
        {
            if (world.Nations.Count == 0)
            {
                World wrapped = Wrap(new List<League> { league });
                world.Nations.AddRange(wrapped.Nations);
                world.InvalidateIndex();
                return;
            }

            league.DetailLevel = LeagueDetailLevel.Playable;
            league.NationCode = string.Empty;

            Nation nation = world.Nations[0];
            nation.Leagues.Add(league);
            nation.Leagues.Sort((a, b) => a.Division.CompareTo(b.Division));
            world.InvalidateIndex();
        }
    }
}
