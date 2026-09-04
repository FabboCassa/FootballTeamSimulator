using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// The default AI scouting behaviour (task 5.4): which players a club watches when the
    /// host has set no explicit assignment for it. A club keeps an eye on the best talent in
    /// its own league that it does not already own — a simple, realistic "watch the league's
    /// standouts" policy that makes the whole world's knowledge grow without the host having
    /// to micro-manage every AI club.
    ///
    /// Pure and deterministic: the watch list is the top-N players by overall (ties broken by
    /// id) outside the club's own squad, N = <see cref="ScoutingBalance.DefaultWatchCount"/>.
    /// No RNG, order-independent of the league's club ordering.
    /// </summary>
    public static class ScoutingPolicy
    {
        /// <summary>The players <paramref name="club"/> watches by default within <paramref name="league"/>.</summary>
        public static List<int> DefaultWatchList(Club club, League league, ScoutingBalance cfg)
        {
            var own = new HashSet<int>();
            foreach (Player p in club.Squad.Players) own.Add(p.Id);

            // Collect candidate (overall, id) outside the club's squad.
            var candidates = new List<(int Overall, int Id)>();
            foreach (Club c in league.Clubs)
            {
                if (c.Id == club.Id) continue;
                foreach (Player p in c.Squad.Players)
                {
                    if (own.Contains(p.Id)) continue;
                    candidates.Add((PlayerRating.Overall(p), p.Id));
                }
            }

            // Best first; deterministic tiebreak by id ascending.
            candidates.Sort((a, b) =>
            {
                int byOverall = b.Overall.CompareTo(a.Overall);
                return byOverall != 0 ? byOverall : a.Id.CompareTo(b.Id);
            });

            int take = cfg.DefaultWatchCount;
            if (take < 0) take = 0;

            var result = new List<int>(take < candidates.Count ? take : candidates.Count);
            for (int i = 0; i < candidates.Count && result.Count < take; i++)
                result.Add(candidates[i].Id);
            return result;
        }
    }
}
