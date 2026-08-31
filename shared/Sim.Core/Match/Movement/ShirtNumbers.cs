using Sim.Core.Domain;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Shirt numbers for a starting eleven, derived from the roles alone (task 13.1).
    /// The domain has no squad number, and an online replay reaches the client as a bare
    /// MatchReport with no squad attached — so the number a token wears, and the only name
    /// the commentary can use over the network, has to be derivable from the lineup itself.
    ///
    /// Each role has a list of traditional numbers; slots take the first one still free, in
    /// slot order, and anything left over takes the lowest free number. Pure function of the
    /// role list: deterministic, no RNG, identical on every runtime.
    /// </summary>
    internal static class ShirtNumbers
    {
        private const int MaxNumber = 30;

        private static readonly int[][] PreferredByRole =
        {
            new[] { 1, 12, 13 },              // Goalkeeper
            new[] { 4, 5, 6, 3, 2 },          // CentreBack
            new[] { 2, 3, 12, 13, 15 },       // FullBack
            new[] { 6, 4, 8, 16 },            // DefensiveMidfielder
            new[] { 8, 14, 16, 4, 6 },        // CentralMidfielder
            new[] { 10, 20, 8, 14 },          // AttackingMidfielder
            new[] { 7, 11, 17, 19 },          // Winger
            new[] { 9, 19, 18, 21, 10 }       // Striker
        };

        public static int[] For(Lineup lineup)
        {
            int n = lineup.Slots.Count;
            var shirts = new int[n];
            var taken = new bool[MaxNumber + 1];

            for (int i = 0; i < n; i++)
            {
                int role = (int)lineup.Slots[i].Role;
                int[] preferred = role >= 0 && role < PreferredByRole.Length
                    ? PreferredByRole[role]
                    : PreferredByRole[4];

                int number = 0;
                for (int p = 0; p < preferred.Length && number == 0; p++)
                    if (!taken[preferred[p]]) number = preferred[p];

                if (number == 0)
                    for (int k = 2; k <= MaxNumber && number == 0; k++)
                        if (!taken[k]) number = k;

                if (number == 0) number = i + 1; // unreachable for an 11-man lineup
                taken[number] = true;
                shirts[i] = number;
            }

            return shirts;
        }
    }
}
