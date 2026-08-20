using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>Realistic 22-man squad composition used by the generator.</summary>
    public static class SquadTemplate
    {
        public static readonly (PositionRole Role, int Count)[] Default =
        {
            (PositionRole.Goalkeeper, 3),
            (PositionRole.CentreBack, 4),
            (PositionRole.FullBack, 3),
            (PositionRole.DefensiveMidfielder, 2),
            (PositionRole.CentralMidfielder, 3),
            (PositionRole.AttackingMidfielder, 2),
            (PositionRole.Winger, 3),
            (PositionRole.Striker, 2)
        };

        public static int TotalPlayers
        {
            get
            {
                int total = 0;
                foreach (var (_, count) in Default) total += count;
                return total;
            }
        }

        /// <summary>
        /// A squad composition of any size (task 11.1). Background clubs carry smaller squads and
        /// data-only clubs carry a handful of "key players", so the generator needs the same role
        /// shape scaled down.
        ///
        /// <paramref name="totalPlayers"/> == <see cref="TotalPlayers"/> returns exactly
        /// <see cref="Default"/> — the full-detail path is untouched, so every golden master stands.
        ///
        /// Below that: keepers first (two while the squad can afford them, otherwise one), then the
        /// outfield roles shared out in the Default proportions by largest remainder — a stable,
        /// integer-only rule, so the composition is identical on every platform. Ties break by the
        /// role's order in <see cref="Default"/>, never by a floating-point comparison.
        /// </summary>
        public static (PositionRole Role, int Count)[] For(int totalPlayers)
        {
            if (totalPlayers >= TotalPlayers)
                return Default;

            if (totalPlayers < 2)
                totalPlayers = 2;

            int keepers = totalPlayers >= 14 ? 2 : 1;
            int outfield = totalPlayers - keepers;

            // Outfield weights = the Default counts, keepers excluded.
            int outfieldWeight = TotalPlayers - Default[0].Count;

            var result = new List<(PositionRole Role, int Count)>(Default.Length)
            {
                (PositionRole.Goalkeeper, keepers)
            };

            var remainders = new List<(int Index, int Remainder)>();
            int assigned = 0;

            for (int i = 1; i < Default.Length; i++)
            {
                int numerator = Default[i].Count * outfield;
                int count = numerator / outfieldWeight;
                result.Add((Default[i].Role, count));
                remainders.Add((i, numerator % outfieldWeight));
                assigned += count;
            }

            // Largest remainder, ties by role order (the list is already in role order and the sort
            // below is a stable insertion sort, so the tie-break is deterministic).
            for (int i = 1; i < remainders.Count; i++)
            {
                (int Index, int Remainder) key = remainders[i];
                int j = i - 1;
                while (j >= 0 && remainders[j].Remainder < key.Remainder)
                {
                    remainders[j + 1] = remainders[j];
                    j--;
                }

                remainders[j + 1] = key;
            }

            int leftover = outfield - assigned;
            for (int i = 0; i < remainders.Count && leftover > 0; i++)
            {
                int slot = remainders[i].Index; // 1-based into Default == same index in result
                result[slot] = (result[slot].Role, result[slot].Count + 1);
                leftover--;
            }

            // Any residue (tiny squads) goes to strikers, so even a 2-man data-only club has a shooter.
            if (leftover > 0)
            {
                int last = result.Count - 1;
                result[last] = (result[last].Role, result[last].Count + leftover);
            }

            return result.ToArray();
        }
    }
}
