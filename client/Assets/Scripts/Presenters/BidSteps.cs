using System;

namespace Fts.Presenters
{
    /// <summary>
    /// The raise ladder for an auction bid. Bidding to the euro is noise: what a bidder actually wants is
    /// "a bit more" / "a lot more", and how much that IS depends on the lot — 25k matters on a 300k squad
    /// filler and is invisible on a 25M phenomenon. This turns the current price (and the server's minimum
    /// raise) into four round increments that scale with the lot, so the same four buttons read sensibly
    /// at every price level.
    /// </summary>
    public static class BidSteps
    {
        /// <summary>Round denominations a football price is normally quoted in.</summary>
        private static readonly long[] Ladder =
        {
            5_000, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000,
            1_000_000, 2_500_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000,
        };

        /// <summary>How many raise buttons the bid panel shows.</summary>
        public const int Count = 4;

        /// <summary>
        /// Four increasing raises for a lot currently at <paramref name="currentPrice"/>. The smallest is
        /// never below <paramref name="minIncrement"/> (the server's own rule) and sits around 2% of the
        /// price; the largest lands around a quarter of it.
        /// </summary>
        public static long[] For(long currentPrice, long minIncrement)
        {
            long floor = Math.Max(minIncrement > 0 ? minIncrement : 0, currentPrice / 50);
            if (floor < Ladder[0]) floor = Ladder[0];

            int index = 0;
            while (index < Ladder.Length - 1 && Ladder[index] < floor) index++;

            var steps = new long[Count];
            for (int i = 0; i < Count; i++)
            {
                int at = index + i;
                steps[i] = at < Ladder.Length ? Ladder[at] : Ladder[Ladder.Length - 1] * (at - Ladder.Length + 2);
            }
            return steps;
        }
    }
}
