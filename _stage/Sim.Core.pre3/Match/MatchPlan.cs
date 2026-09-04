using System.Collections.Generic;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// One side-pair's inputs for a stretch of a match: the two lineups and the
    /// optional tactical setup in force. Swapping a player (substitution) or
    /// changing instructions just means a new <see cref="MatchInput"/>.
    /// </summary>
    public sealed class MatchInput
    {
        public Lineup Home { get; }
        public Lineup Away { get; }
        public MatchTactics? Tactics { get; }

        public MatchInput(Lineup home, Lineup away, MatchTactics? tactics = null)
        {
            Home = home;
            Away = away;
            Tactics = tactics;
        }
    }

    /// <summary>A change of inputs taking effect from the start of <see cref="FromMinute"/> (1..90).</summary>
    public readonly struct MatchInputChange
    {
        public readonly int FromMinute;
        public readonly MatchInput Input;

        public MatchInputChange(int fromMinute, MatchInput input)
        {
            FromMinute = fromMinute;
            Input = input;
        }
    }

    /// <summary>
    /// A full match as a schedule of inputs: the kickoff input plus zero or more
    /// changes injected at given minutes (substitutions, tactic changes). The
    /// engine is a pure function of (plan, seed), so:
    ///   - re-running the same plan reproduces the report exactly (replay), and
    ///   - appending a change at minute M leaves minutes &lt; M byte-identical and
    ///     only re-simulates the remainder (task 3.4 / ARCHITECTURE.md §4.3).
    /// The host re-simulates simply by re-running with the same seed and an
    /// extended plan — no engine state needs to be snapshotted.
    /// </summary>
    public sealed class MatchPlan
    {
        public MatchInput Initial { get; }

        /// <summary>Changes sorted ascending by <see cref="MatchInputChange.FromMinute"/>.</summary>
        public IReadOnlyList<MatchInputChange> Changes { get; }

        public MatchPlan(MatchInput initial, IReadOnlyList<MatchInputChange>? changes = null)
        {
            Initial = initial;
            Changes = Sort(changes);
        }

        /// <summary>Returns a new plan with one more change appended (kept sorted). The original is untouched.</summary>
        public MatchPlan WithChange(int fromMinute, MatchInput input)
        {
            var list = new List<MatchInputChange>(Changes) { new MatchInputChange(fromMinute, input) };
            return new MatchPlan(Initial, list);
        }

        private static IReadOnlyList<MatchInputChange> Sort(IReadOnlyList<MatchInputChange>? changes)
        {
            if (changes == null || changes.Count == 0)
                return System.Array.Empty<MatchInputChange>();

            // Stable insertion sort: small lists, and ties keep submission order
            // (a later same-minute change overrides an earlier one when applied).
            var sorted = new List<MatchInputChange>(changes);
            for (int i = 1; i < sorted.Count; i++)
            {
                MatchInputChange key = sorted[i];
                int j = i - 1;
                while (j >= 0 && sorted[j].FromMinute > key.FromMinute)
                {
                    sorted[j + 1] = sorted[j];
                    j--;
                }
                sorted[j + 1] = key;
            }

            return sorted;
        }
    }
}
