using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// The scout FINDS players himself (task 11.2). Given an area and a brief, this walks the area's
    /// clubs and returns the candidates that match, ranked by what the scout believes they are worth
    /// — which is the whole point of the feature: the manager sets filters and reads a shortlist, he
    /// never browses tens of thousands of players by hand.
    ///
    /// Pure and deterministic: no RNG, no time, no dictionary iteration. The area enumerates clubs in
    /// world registry order and the final sort has a total tiebreak on player id, so the same world,
    /// brief and knowledge always produce the same list on every platform.
    ///
    /// Two rules that matter more than the code:
    ///   • Ability and potential are filtered and ranked on the ESTIMATE at the club's CURRENT
    ///     knowledge (through this scout's eyes), never on the truth. A vague brief therefore
    ///     surfaces the occasional dud and misses the occasional gem — intended, not a bug.
    ///   • Public facts (role, age, nationality, contract, value, wage) are tested FIRST, because
    ///     they cost nothing; only survivors pay for two estimate bands. A continental scan over a
    ///     Large world touches ~26k players a week, and this is what keeps that cheap enough. Task
    ///     11.3 replaces the walk itself with an index.
    /// </summary>
    public static class ScoutingDiscovery
    {
        /// <summary>One name the scan turned up, with the numbers the scout believes.</summary>
        public sealed class Candidate
        {
            public int PlayerId { get; set; }
            public int ClubId { get; set; }

            /// <summary>The scout's best guess at the player's current overall.</summary>
            public int EstimatedOverall { get; set; }

            /// <summary>The scout's best guess at the player's potential.</summary>
            public int EstimatedPotential { get; set; }
        }

        /// <summary>
        /// Scans <paramref name="assignment"/>'s area for players matching its brief, best first.
        /// Returns at most <paramref name="maxResults"/> candidates, never the observer's own players.
        /// </summary>
        public static List<Candidate> Scan(World world, ScoutingAssignment assignment, KnowledgeStore knowledge,
                                           ulong worldSeed, int observerClubId, ScoutQuality quality,
                                           int maxResults, ScoutingBalance cfg)
        {
            var found = new List<Candidate>();
            if (world == null || assignment == null || maxResults <= 0)
                return found;

            ScoutingFilters filters = assignment.Filters ?? new ScoutingFilters();

            foreach (Club club in assignment.Area.Clubs(world))
            {
                if (club.Id == observerClubId)
                    continue;

                foreach (Player player in club.Squad.Players)
                {
                    if (!filters.MatchesFacts(player, cfg))
                        continue;

                    int known = knowledge != null ? knowledge.Get(observerClubId, player.Id) : 0;
                    ScoutedRange overall = ScoutingModel.OverallOf(player, known, worldSeed, observerClubId, cfg, quality);
                    ScoutedRange potential = ScoutingModel.PotentialOf(player, known, worldSeed, observerClubId, cfg, quality);

                    if (!filters.MatchesEstimates(overall, potential))
                        continue;

                    found.Add(new Candidate
                    {
                        PlayerId = player.Id,
                        ClubId = club.Id,
                        EstimatedOverall = overall.Estimate,
                        EstimatedPotential = potential.Estimate
                    });
                }
            }

            // Best perceived player first; potential breaks ties, then the id makes the order total.
            found.Sort(Compare);

            if (found.Count > maxResults)
                found.RemoveRange(maxResults, found.Count - maxResults);

            return found;
        }

        /// <summary>
        /// How many players the area holds at all (before any filter). The UI shows it so a brief
        /// that returns nothing reads as "nobody here matches" rather than "the feature is broken".
        /// </summary>
        public static int AreaPlayerCount(World world, ScoutingArea area)
        {
            if (world == null || area == null)
                return 0;

            int total = 0;
            foreach (Club club in area.Clubs(world))
                total += club.Squad.Players.Count;

            return total;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int byOverall = b.EstimatedOverall.CompareTo(a.EstimatedOverall);
            if (byOverall != 0)
                return byOverall;

            int byPotential = b.EstimatedPotential.CompareTo(a.EstimatedPotential);
            return byPotential != 0 ? byPotential : a.PlayerId.CompareTo(b.PlayerId);
        }
    }
}
