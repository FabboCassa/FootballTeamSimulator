using Sim.Core.Config;

namespace Sim.Core.Career
{
    /// <summary>A coarse, human-readable label for a board objective, derived from the expected
    /// finishing position within a division. Drives flavour text on the career screen.</summary>
    public enum ObjectiveTier
    {
        WinTitle = 0,
        ChallengeForHonours = 1,
        UpperMidTable = 2,
        MidTable = 3,
        AvoidRelegation = 4
    }

    /// <summary>How a finished season compares to the board's expectation.</summary>
    public enum SeasonOutcome
    {
        Underachieved = 0,
        Met = 1,
        Overachieved = 2
    }

    /// <summary>
    /// Pure helpers (task 5.6) that turn an expected finishing position into a board objective
    /// label, and a finished season into an <see cref="SeasonOutcome"/>. No state, no RNG —
    /// integer comparisons only. The numbers come from <see cref="BoardModel"/>; this file just
    /// classifies them.
    /// </summary>
    public static class BoardObjective
    {
        /// <summary>
        /// Labels an expected position within a division of <paramref name="clubCount"/> clubs.
        /// Top → WinTitle, then ChallengeForHonours / UpperMidTable / MidTable, bottom band →
        /// AvoidRelegation. Thresholds are fractions of the table, so they scale to any league size.
        /// </summary>
        public static ObjectiveTier TierFor(int expectedPosition, int clubCount)
        {
            if (clubCount < 1) clubCount = 1;
            if (expectedPosition < 1) expectedPosition = 1;
            if (expectedPosition > clubCount) expectedPosition = clubCount;

            if (expectedPosition == 1) return ObjectiveTier.WinTitle;

            // Fractional bands (1-based position / clubCount).
            int pct = expectedPosition * 100 / clubCount; // 1..100
            if (pct <= 25) return ObjectiveTier.ChallengeForHonours;
            if (pct <= 50) return ObjectiveTier.UpperMidTable;
            if (pct <= 75) return ObjectiveTier.MidTable;
            return ObjectiveTier.AvoidRelegation;
        }

        /// <summary>
        /// Classifies a finished season. A SMALLER position number is better, so finishing at least
        /// <see cref="CareerBalance.OverachieveBandPositions"/> ABOVE (lower than) the expected
        /// position is overachievement, and at least
        /// <see cref="CareerBalance.UnderachieveBandPositions"/> BELOW (higher than) it is
        /// underachievement; anything in between is meeting expectations.
        /// </summary>
        public static SeasonOutcome Classify(int actualPosition, int expectedPosition, CareerBalance cfg)
        {
            int gap = expectedPosition - actualPosition; // >0 = better than expected
            if (gap >= cfg.OverachieveBandPositions) return SeasonOutcome.Overachieved;
            if (-gap >= cfg.UnderachieveBandPositions) return SeasonOutcome.Underachieved;
            return SeasonOutcome.Met;
        }
    }
}
