using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Match
{
    /// <summary>
    /// Picks a club's best eleven for a formation (greedy, deterministic).
    /// Until the tactics system arrives (task 3.2) everyone plays 4-3-3.
    /// </summary>
    public static class LineupSelector
    {
        /// <summary>Default 4-3-3 formation expressed as role slots.</summary>
        public static readonly PositionRole[] DefaultFormation =
        {
            PositionRole.Goalkeeper,
            PositionRole.CentreBack, PositionRole.CentreBack,
            PositionRole.FullBack, PositionRole.FullBack,
            PositionRole.DefensiveMidfielder,
            PositionRole.CentralMidfielder, PositionRole.CentralMidfielder,
            PositionRole.Winger, PositionRole.Winger,
            PositionRole.Striker
        };

        public static Lineup BestEleven(Club club) => BestEleven(club, DefaultFormation);

        public static Lineup BestEleven(Club club, PositionRole[] formation)
        {
            var lineup = new Lineup { ClubId = club.Id };
            var used = new HashSet<int>();

            foreach (PositionRole slotRole in formation)
            {
                Player? best = null;
                int bestRating = int.MinValue;

                foreach (Player candidate in club.Squad.Players)
                {
                    if (used.Contains(candidate.Id)) continue;

                    int rating = PlayerRating.OverallFor(candidate, slotRole);
                    // Deterministic tie-break: lower id wins.
                    if (rating > bestRating || (rating == bestRating && best != null && candidate.Id < best.Id))
                    {
                        best = candidate;
                        bestRating = rating;
                    }
                }

                if (best != null)
                {
                    used.Add(best.Id);
                    lineup.Slots.Add(new LineupSlot { Role = slotRole, Player = best });
                }
            }

            lineup.Validate();
            return lineup;
        }
    }
}
