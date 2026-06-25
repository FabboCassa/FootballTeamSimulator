using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// Whole-world weekly scouting tick (task 5.4) — the knowledge twin of the development /
    /// valuation progressors. For every club in every league it advances knowledge of the
    /// players it is actively watching by one week of its best scout: a club with explicit
    /// <see cref="ScoutingAssignmentBook"/> entries uses them (the user club), and one without
    /// falls back to <see cref="ScoutingPolicy.DefaultWatchList"/> (the AI watches the league's
    /// standouts) — so the whole world's knowledge keeps growing, not just the user's.
    ///
    /// Pure and deterministic: integer math, NO RNG, order-independent (each (club, player)
    /// knowledge entry advances independently). Opt-in by being called: a host decides the
    /// cadence (the client wires it onto the weekly tick); the match engine and SeasonProgressor
    /// never touch it, so golden masters/replays are unaffected.
    /// </summary>
    public sealed class ScoutingProgressor
    {
        private readonly ScoutingBalance _cfg;

        public ScoutingProgressor(ScoutingBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>The effective scout level a club scouts at: its best scout, or the base level if it employs none.</summary>
        public int ClubScoutLevel(Club club)
        {
            int level = 0;
            foreach (Scout s in club.Scouts)
                if (s.Level > level) level = s.Level;

            if (level <= 0) level = _cfg.BaseClubScoutLevel;
            if (level > _cfg.MaxScoutLevel) level = _cfg.MaxScoutLevel;
            return level;
        }

        /// <summary>
        /// Advances every club's knowledge of its watched players by one week. A club with
        /// explicit <paramref name="assignments"/> uses them; otherwise (or when
        /// <paramref name="assignments"/> is null) every club falls back to the default policy.
        /// </summary>
        public void EvolveWeek(IReadOnlyList<League> leagues, KnowledgeStore knowledge,
                               ScoutingAssignmentBook? assignments)
        {
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    int level = ClubScoutLevel(club);

                    IEnumerable<int> watch = assignments != null && assignments.HasAny(club.Id)
                        ? assignments.For(club.Id)
                        : ScoutingPolicy.DefaultWatchList(club, league, _cfg);

                    foreach (int playerId in watch)
                        knowledge.Accrue(club.Id, playerId, level, _cfg);
                }
            }
        }
    }
}
