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
    /// Task 11.2 adds <see cref="EvolveAreaWeek"/> alongside it: the AREA briefs of ONE club (in
    /// practice the user's), which is where discovery, the per-area knowledge meter and the
    /// precision-vs-breadth ceiling live. It is deliberately a separate entry point rather than a
    /// widening of <see cref="EvolveWeek"/> — a continental scan walks tens of thousands of
    /// players, and no version of that belongs in a loop over every club in the world.
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

        /// <summary>
        /// One week of ONE club's scouting department, area briefs and named targets alike (task 11.2).
        ///
        /// Per assignment, in order:
        ///   1. the scout's week is counted (<see cref="ScoutingAssignment.WeeksElapsed"/>);
        ///   2. a named target simply gets deeper — full rate, ceiling at full knowledge, which is
        ///      the precise/expensive option the user keeps from task 5.4b;
        ///   3. an area brief first raises the club's KNOWLEDGE OF THE AREA (slowly), which sets
        ///      this week's per-player ceiling;
        ///   4. every player already on that brief's shortlist gets deeper, up to that ceiling;
        ///   5. finally the scout looks for new names, and the best few that match the brief and are
        ///      not already on the list are filed as fresh reports with one week of knowledge each.
        ///
        /// Returns how many NEW names were filed this week, so the host can badge the Reports tab.
        /// Deterministic: no RNG, and both the area walk and the ranking are totally ordered.
        /// </summary>
        public int EvolveAreaWeek(World world, Club club, ScoutingAssignmentBook assignments,
                                  KnowledgeStore knowledge, AreaKnowledgeStore? areaKnowledge,
                                  ScoutingReportBook? reports, ulong worldSeed, int week)
        {
            if (world == null || club == null || assignments == null || knowledge == null)
                return 0;

            int clubId = club.Id;
            int filed = 0;

            foreach (ScoutingAssignment assignment in assignments.AssignmentsFor(clubId))
            {
                Scout? scout = FindScout(club, assignment.ScoutId);
                ScoutQuality quality = ScoutQuality.Of(scout, _cfg);
                int level = ScoutLevel(club, scout);

                assignment.WeeksElapsed++;

                if (assignment.Area.Kind == ScoutingAreaKind.Player)
                {
                    // A named target is advanced by EvolveWeek (it reads exactly these through
                    // ScoutingAssignmentBook.For), at the full task 5.4 rate and with no area
                    // ceiling — the precise/expensive option the user kept. Accruing it here too
                    // would silently double its speed, so this loop only counts the week.
                    continue;
                }

                string areaKey = assignment.Area.Key;
                int area = areaKnowledge != null
                    ? areaKnowledge.Accrue(clubId, areaKey, level, _cfg)
                    : 0;
                int cap = ScoutingModel.KnowledgeCap(assignment.Area.Kind, area, _cfg);

                // (4) deepen what this brief has already found.
                if (reports != null)
                {
                    List<int> known = reports.PlayersOfArea(clubId, areaKey);
                    foreach (int playerId in known)
                    {
                        int current = knowledge.Get(clubId, playerId);
                        int deeper = ScoutingModel.AccrueInArea(
                            current, level, assignment.Area.Kind, cap, quality, _cfg);
                        if (deeper != current)
                            knowledge.Set(clubId, playerId, deeper, _cfg);
                    }
                }

                // (5) look for new names — unless this brief's shortlist is already full, in which
                // case the scout keeps deepening what he has and waits for the manager to make room.
                int wanted = ScoutingModel.CandidatesPerWeek(assignment.Area.Kind, _cfg);
                if (wanted <= 0 || reports == null)
                    continue;

                int room = _cfg.MaxReportsPerArea > 0
                    ? _cfg.MaxReportsPerArea - reports.CountOfArea(clubId, areaKey)
                    : wanted;
                if (room <= 0)
                    continue;
                if (wanted > room)
                    wanted = room;

                // Scan deep enough that the names already on the shortlist — from ANY brief, since a
                // player is only ever reported once — cannot hide the next new one.
                int scan = reports.Count(clubId) + wanted;
                List<ScoutingDiscovery.Candidate> candidates = ScoutingDiscovery.Scan(
                    world, assignment, knowledge, worldSeed, clubId, quality, scan, _cfg);

                int added = 0;
                foreach (ScoutingDiscovery.Candidate candidate in candidates)
                {
                    if (added >= wanted)
                        break;

                    if (reports.Has(clubId, candidate.PlayerId))
                        continue;

                    bool filedNow = reports.Add(clubId, new ScoutReportEntry
                    {
                        PlayerId = candidate.PlayerId,
                        AreaKey = areaKey,
                        ScoutId = assignment.ScoutId,
                        FoundWeek = week
                    }, _cfg.MaxReportsPerArea, _cfg.MaxReportsPerClub);

                    if (!filedNow)
                        continue;

                    // A newly found player is not a blank page: the scout saw him to find him.
                    int seeded = ScoutingModel.AccrueInArea(
                        knowledge.Get(clubId, candidate.PlayerId), level, assignment.Area.Kind, cap, quality, _cfg);
                    knowledge.Set(clubId, candidate.PlayerId, seeded, _cfg);

                    added++;
                    filed++;
                }
            }

            return filed;
        }

        /// <summary>The level one scout works at; falls back to the club department when he is unknown.</summary>
        private int ScoutLevel(Club club, Scout? scout)
        {
            int level = scout != null ? scout.Level : ClubScoutLevel(club);
            if (level <= 0) level = _cfg.BaseClubScoutLevel;
            if (level > _cfg.MaxScoutLevel) level = _cfg.MaxScoutLevel;
            return level;
        }

        private static Scout? FindScout(Club club, int scoutId)
        {
            if (scoutId <= 0 || club.Scouts == null)
                return null;

            foreach (Scout s in club.Scouts)
            {
                if (s.Id == scoutId)
                    return s;
            }

            return null;
        }
    }
}
