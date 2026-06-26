using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// Picks a club's best eleven for a formation (greedy, deterministic).
    /// Defaults to 4-3-3; the tactics system (task 3.2) can request any shape.
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

        /// <summary>Best eleven for a named formation shape (task 3.2).</summary>
        public static Lineup BestEleven(Club club, Formation formation) =>
            BestEleven(club, Formations.Roles(formation));

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

        /// <summary>
        /// A difficulty-aware eleven (task 5.7): an AI manager of the given competence picks each
        /// slot, but with a chance of slipping to a weaker (but real) candidate when his competence
        /// roll misses — modelling a less-than-perfect manager fielding a genuinely weaker XI, NOT a
        /// hidden stat penalty. <paramref name="competencePercent"/> is the per-slot chance (0..100)
        /// of taking the best available player; a miss demotes one rank, repeated up to
        /// <paramref name="maxSlips"/> times. At competence ≥ 100 no roll can miss, so this returns
        /// exactly <see cref="BestEleven(Club, PositionRole[])"/> (and the host skips it on Hard,
        /// keeping the pre-5.7 path). Deterministic given <paramref name="rng"/>.
        /// </summary>
        public static Lineup CompetentEleven(
            Club club, PositionRole[] formation, int competencePercent, int maxSlips, IRandomSource rng)
        {
            var lineup = new Lineup { ClubId = club.Id };
            var used = new HashSet<int>();

            for (int i = 0; i < formation.Length; i++)
            {
                PositionRole slotRole = formation[i];
                int slotsRemaining = formation.Length - i; // this slot + the ones still to fill

                // Candidates for this slot, best-rated first (lower id breaks ties) — a total order.
                var ranked = new List<Player>();
                foreach (Player candidate in club.Squad.Players)
                    if (!used.Contains(candidate.Id))
                        ranked.Add(candidate);

                if (ranked.Count == 0) continue;

                ranked.Sort((a, b) =>
                {
                    int ra = PlayerRating.OverallFor(a, slotRole);
                    int rb = PlayerRating.OverallFor(b, slotRole);
                    if (ra != rb) return rb - ra;      // higher rating first
                    return a.Id - b.Id;                // stable tie-break
                });

                // A miss OVERLOOKS the best candidate — he is benched for the whole match (not merely
                // shuffled to another slot), so a less competent manager genuinely fields a weaker XI.
                // The guard keeps the side legal: never bench so many that the remaining slots can't be
                // filled. At competence ≥ 100 the roll never misses, so this returns the best XI exactly.
                int slips = 0;
                while (ranked.Count > slotsRemaining && slips < maxSlips
                       && rng.NextInt(0, 100) >= competencePercent)
                {
                    used.Add(ranked[0].Id); // overlooked → sits out this match
                    ranked.RemoveAt(0);
                    slips++;
                }

                Player chosen = ranked[0];
                used.Add(chosen.Id);
                lineup.Slots.Add(new LineupSlot { Role = slotRole, Player = chosen });
            }

            lineup.Validate();
            return lineup;
        }
    }
}
