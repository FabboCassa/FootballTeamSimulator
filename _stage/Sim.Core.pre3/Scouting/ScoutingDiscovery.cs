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
    ///     11.3 replaced the walk itself with <see cref="WorldPlayerIndex"/>, which the host passes
    ///     in; without one the original walk still runs, unchanged.
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
            => Scan(world, assignment, knowledge, worldSeed, observerClubId, quality, maxResults, cfg, null);

        /// <summary>
        /// The same scan, run over the flattened <see cref="WorldPlayerIndex"/> when the host has one
        /// (task 11.3). Same predicate, same order, same result — the index only changes HOW the
        /// area's players are reached: a start and a count into one array instead of a walk down
        /// nations, leagues, clubs and squads. Pass null and the walk is used, which is what keeps
        /// every task 11.2 call site and test byte-identical.
        /// </summary>
        public static List<Candidate> Scan(World world, ScoutingAssignment assignment, KnowledgeStore knowledge,
                                           ulong worldSeed, int observerClubId, ScoutQuality quality,
                                           int maxResults, ScoutingBalance cfg, WorldPlayerIndex? index)
        {
            var found = new List<Candidate>();
            if (world == null || assignment == null || maxResults <= 0)
                return found;

            ScoutingFilters filters = assignment.Filters ?? new ScoutingFilters();

            // A NAMED target is not a place to go looking: the walk yields no clubs for it, and the
            // index must not quietly start returning the man himself as a "discovery".
            // (No null test on Area: it is a non-nullable property, and testing it here would make
            // the compiler treat every later Area access as maybe-null — CS8602 under our
            // TreatWarningsAsErrors.)
            if (assignment.Area.Kind == ScoutingAreaKind.Player)
                return found;

            if (index != null && index.World == world)
            {
                List<int> bounds = index.Bounds(assignment.Area);
                for (int b = 0; b < bounds.Count; b += 2)
                {
                    for (int slot = bounds[b]; slot < bounds[b + 1]; slot++)
                    {
                        if (index.ClubAt(slot) == observerClubId)
                            continue;

                        // The cheap rejections come off the primitive arrays; anything that survives
                        // pays for the object and the full public-fact test, exactly as in the walk.
                        if (filters.Role >= 0 && index.RoleAt(slot) != filters.Role)
                            continue;

                        int age = index.AgeAt(slot);
                        if (filters.MinAge > 0 && age < filters.MinAge)
                            continue;
                        if (filters.MaxAge > 0 && age > filters.MaxAge)
                            continue;

                        Player? indexed = index.PlayerAt(slot);
                        if (indexed == null)
                            continue;

                        Candidate? candidate = Judge(indexed, index.ClubAt(slot), filters, knowledge,
                                                     worldSeed, observerClubId, quality, cfg);
                        if (candidate != null)
                            found.Add(candidate);
                    }
                }
            }
            else
            {
                foreach (Club club in assignment.Area.Clubs(world))
                {
                    if (club.Id == observerClubId)
                        continue;

                    foreach (Player player in club.Squad.Players)
                    {
                        Candidate? candidate = Judge(player, club.Id, filters, knowledge,
                                                     worldSeed, observerClubId, quality, cfg);
                        if (candidate != null)
                            found.Add(candidate);
                    }
                }
            }

            // Best perceived player first; potential breaks ties, then the id makes the order total.
            found.Sort(Compare);

            if (found.Count > maxResults)
                found.RemoveRange(maxResults, found.Count - maxResults);

            return found;
        }

        /// <summary>
        /// The one place a candidate is judged, shared by the walk and the indexed scan so the two
        /// can never drift apart. Returns null when he does not match the brief.
        /// </summary>
        private static Candidate? Judge(Player player, int clubId, ScoutingFilters filters,
                                        KnowledgeStore knowledge, ulong worldSeed, int observerClubId,
                                        ScoutQuality quality, ScoutingBalance cfg)
        {
            if (!filters.MatchesFacts(player, cfg))
                return null;

            int known = knowledge != null ? knowledge.Get(observerClubId, player.Id) : 0;
            ScoutedRange overall = ScoutingModel.OverallOf(player, known, worldSeed, observerClubId, cfg, quality);
            ScoutedRange potential = ScoutingModel.PotentialOf(player, known, worldSeed, observerClubId, cfg, quality);

            if (!filters.MatchesEstimates(overall, potential))
                return null;

            return new Candidate
            {
                PlayerId = player.Id,
                ClubId = clubId,
                EstimatedOverall = overall.Estimate,
                EstimatedPotential = potential.Estimate
            };
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

        /// <summary>The same count, off the index when the host has one (task 11.3).</summary>
        public static int AreaPlayerCount(World world, ScoutingArea area, WorldPlayerIndex? index)
            => index != null && index.World == world ? index.AreaPlayerCount(area) : AreaPlayerCount(world, area);

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
