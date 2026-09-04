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

        /// <summary>
        /// The order a SMALL squad is filled in (task 11.2). A data-only club's handful of notable
        /// players is a SPINE — someone who scores, someone who defends, someone who runs the
        /// middle — not a scaled-down copy of a 22-man roster.
        ///
        /// This exists because proportional allocation alone gets it wrong at exactly the sizes that
        /// ship. Largest-remainder over the <see cref="Default"/> weights hands a 5-man club's four
        /// outfield slots to the four heaviest roles (centre-back, full-back, central midfielder,
        /// winger) and a 7-man club's six to those plus the two midfield flavours — so EVERY
        /// data-only club in the Small and Medium databases had the same roles and not one striker
        /// anywhere. Harmless until task 11.2 lets the player send a scout to a continent with
        /// "find me a striker", at which point he can never come back with a name.
        /// </summary>
        private static readonly PositionRole[] SpineOrder =
        {
            PositionRole.Striker,
            PositionRole.CentreBack,
            PositionRole.CentralMidfielder,
            PositionRole.Winger,
            PositionRole.FullBack,
            PositionRole.AttackingMidfielder,
            PositionRole.DefensiveMidfielder
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
        /// Below that, in two phases:
        ///   1. COVERAGE — keepers first (two while the squad can afford them, otherwise one), then
        ///      one player per outfield role in <see cref="SpineOrder"/> while the slots last. This
        ///      is what guarantees that even a 2-man club has a shooter.
        ///   2. PROPORTION — whatever is left over is shared out in the <see cref="Default"/>
        ///      proportions by largest remainder, ties broken by the role's order in Default.
        ///
        /// Integer-only and free of any floating-point comparison, so the composition is identical
        /// on every platform. Adding phase 1 (task 11.2) left the sizes the presets actually use for
        /// BACKGROUND clubs — 18 and 20 — and the Large database's 11-man data-only clubs producing
        /// byte-identical compositions; only the 5- and 7-man data-only squads changed, which is
        /// precisely the case that was broken.
        /// </summary>
        public static (PositionRole Role, int Count)[] For(int totalPlayers)
        {
            if (totalPlayers >= TotalPlayers)
                return Default;

            if (totalPlayers < 2)
                totalPlayers = 2;

            int keepers = totalPlayers >= 14 ? 2 : 1;
            int outfield = totalPlayers - keepers;

            // Indexed like Default, so index i means the same role in both.
            var counts = new int[Default.Length];
            counts[0] = keepers;

            // --- phase 1: coverage ---
            int covered = outfield < SpineOrder.Length ? outfield : SpineOrder.Length;
            for (int i = 0; i < covered; i++)
                counts[IndexOf(SpineOrder[i])] += 1;

            int remaining = outfield - covered;

            // --- phase 2: proportion ---
            if (remaining > 0)
            {
                // Outfield weights = the Default counts, keepers excluded.
                int outfieldWeight = TotalPlayers - Default[0].Count;

                var remainders = new List<(int Index, int Remainder)>(Default.Length - 1);
                int assigned = 0;

                for (int i = 1; i < Default.Length; i++)
                {
                    int numerator = Default[i].Count * remaining;
                    int count = numerator / outfieldWeight;
                    counts[i] += count;
                    remainders.Add((i, numerator % outfieldWeight));
                    assigned += count;
                }

                // Largest remainder, ties by role order (the list is already in role order and the
                // sort below is a stable insertion sort, so the tie-break is deterministic).
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

                int leftover = remaining - assigned;
                for (int i = 0; i < remainders.Count && leftover > 0; i++)
                {
                    counts[remainders[i].Index] += 1;
                    leftover--;
                }

                // Residue beyond one pass round the roles (unreachable with the shipped weights)
                // goes to the striker, keeping the total exact whatever the weights become.
                if (leftover > 0)
                    counts[IndexOf(PositionRole.Striker)] += leftover;
            }

            var result = new (PositionRole Role, int Count)[Default.Length];
            for (int i = 0; i < Default.Length; i++)
                result[i] = (Default[i].Role, counts[i]);

            return result;
        }

        private static int IndexOf(PositionRole role)
        {
            for (int i = 0; i < Default.Length; i++)
            {
                if (Default[i].Role == role)
                    return i;
            }

            return Default.Length - 1;
        }
    }
}
