using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>What a carrier in the chance zone did next (see <see cref="ChanceDecisionAnalyzer"/>).</summary>
    public enum ChanceAction
    {
        Shot = 0,
        PassForward = 1,
        PassBack = 2,
        Carry = 3,
        Clear = 4,
        Lost = 5
    }

    /// <summary>
    /// The chance-decision counts of one match (believable-match spec R2), both sides together:
    /// carriers in the chance zone by free or pressed, by distance band, and by what they did.
    /// Raw counts only, so matches pool by adding them (<see cref="Plus"/>).
    /// </summary>
    public sealed class ChanceDecisionMetrics
    {
        public const int ActionCount = 6;
        private const int BucketCount = 4;

        private readonly int[] _counts = new int[BucketCount * ActionCount];

        public int Matches { get; set; } = 1;

        /// <summary>Every counted possession, all buckets and actions.</summary>
        public int Total
        {
            get
            {
                int sum = 0;
                foreach (int c in _counts) sum += c;
                return sum;
            }
        }

        /// <param name="free">No outfield defender within 3 m.</param>
        /// <param name="near">Within 12 m of the goal centre (else 12-16 m).</param>
        public int Count(bool free, bool near, ChanceAction action) => _counts[Index(free, near, action)];

        /// <summary>Possessions counted in one bucket, whatever they ended in.</summary>
        public int Chances(bool free, bool near)
        {
            int sum = 0;
            for (int a = 0; a < ActionCount; a++) sum += Count(free, near, (ChanceAction)a);
            return sum;
        }

        public void Add(bool free, bool near, ChanceAction action) => _counts[Index(free, near, action)]++;

        /// <summary>A new reading holding both: every count added, matches included.</summary>
        public ChanceDecisionMetrics Plus(ChanceDecisionMetrics o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            var sum = new ChanceDecisionMetrics { Matches = Matches + o.Matches };
            for (int i = 0; i < _counts.Length; i++) sum._counts[i] = _counts[i] + o._counts[i];
            return sum;
        }

        private static int Index(bool free, bool near, ChanceAction action) =>
            ((free ? 0 : 2) + (near ? 0 : 1)) * ActionCount + (int)action;
    }
}
