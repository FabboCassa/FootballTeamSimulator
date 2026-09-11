using Sim.Core.Config;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The mark out of ten (engine phase 7, docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// A rating is an OPINION, and the only honest way to publish one is to say out loud what it
    /// is made of — so it is a sum of the things the match actually counted, each with a weight
    /// that lives in <see cref="PerformanceBalance"/> where every other tunable lives. Nothing
    /// here is hidden and nothing here is random.
    ///
    /// Everybody starts at <see cref="PerformanceBalance.RatingBase"/> — 6.0, "he was there and
    /// nothing happened" — and moves from it. A keeper is marked on the two things only he does,
    /// because saves and goals conceded are his match and a pass completion rate is not. A man who
    /// came on for a quarter of an hour has his swing scaled back toward the base: fifteen minutes
    /// is not a nine, however good they were.
    ///
    /// THE DEFENSIVE WORK IS PAID ON THE DIFFERENCE, NOT ON THE COUNT — and that is a correction,
    /// written after the first measured run. A flat bonus per tackle reads as football until you
    /// measure what this engine actually produces: **306 tackles, 233 clearances and 192
    /// interceptions a match**, which is about thirty-three defensive actions per man where real
    /// football has two or three. At one tenth apiece that handed every player on the pitch three
    /// extra points and the average mark came out at 8.5. So a man is marked on how far he is above
    /// or below what THIS match asked of everybody else — pro-rated for the minutes he was on the
    /// pitch — which is self-calibrating: it reads the same whether the engine counts three
    /// recoveries a man or thirty, and it survives phase 8 changing the rate again.
    ///
    /// Integer arithmetic in tenths (60 = 6.0). It reproduces bit for bit on .NET, Mono and
    /// IL2CPP, which matters because a host may feed it back into the development model.
    /// </summary>
    public static class MatchRatingModel
    {
        private const int FullMatchMinutes = 90;

        /// <summary>
        /// The mark, in tenths of a point out of ten.
        /// <paramref name="averageDefensiveActionsPer90"/> is what the average outfielder in THIS
        /// match did over ninety minutes (tackles + interceptions + clearances) and
        /// <paramref name="averageDuelsLostPer90"/> how often he was dispossessed; pass 0 for both
        /// and the two terms simply pay the figures at face value, which is what a caller with a
        /// single player and no match around him wants.
        /// </summary>
        public static int Rate(
            PlayerMatchStats stats, PerformanceBalance cfg,
            int averageDefensiveActionsPer90 = 0, int averageDuelsLostPer90 = 0)
        {
            if (stats.MinutesPlayed <= 0) return 0;

            int swing = stats.Keeper
                ? KeeperSwing(stats, cfg)
                : OutfieldSwing(stats, cfg, averageDefensiveActionsPer90, averageDuelsLostPer90);

            swing += Shared(stats, cfg);

            if (cfg.ScaleRatingByMinutes)
            {
                int minutes = stats.MinutesPlayed;
                if (minutes > FullMatchMinutes) minutes = FullMatchMinutes;
                swing = swing * minutes / FullMatchMinutes;
            }

            int rating = cfg.RatingBase + swing;
            if (rating < cfg.RatingFloor) rating = cfg.RatingFloor;
            if (rating > cfg.RatingCeiling) rating = cfg.RatingCeiling;
            return rating;
        }

        /// <summary>What he did with the ball, and how much more than his share he did without it.</summary>
        private static int OutfieldSwing(
            PlayerMatchStats stats, PerformanceBalance cfg, int averagePer90, int averageLostPer90)
        {
            int swing = 0;

            swing += stats.Goals * cfg.RatingPerGoal;
            swing += stats.Assists * cfg.RatingPerAssist;
            swing += stats.KeyPasses * cfg.RatingPerKeyPass;
            swing += stats.ShotsOnTarget * cfg.RatingPerShotOnTarget;
            swing += stats.Blocks * cfg.RatingPerBlock;

            // The work off the ball, against what this match asked of everybody — pro-rated, so a
            // man who played half an hour is measured against half an hour's share.
            int minutes = stats.MinutesPlayed > FullMatchMinutes ? FullMatchMinutes : stats.MinutesPlayed;
            int expected = averagePer90 * minutes / FullMatchMinutes;
            swing += Clamp(
                (stats.DefensiveActions - expected) * cfg.RatingPerDefensiveActionAboveAverage,
                cfg.RatingDefensiveCap);

            // And the balls he GAVE AWAY, on the same relative footing. Duels won are deliberately
            // not paid here: a duel won IS a tackle, and a tackle is already in the line above —
            // paying it twice was how the first version made every busy defender a nine. What is
            // new information is how often the ball was taken off HIM, and like everything else it
            // only means something against what the rest of the match managed.
            int expectedLost = averageLostPer90 * minutes / FullMatchMinutes;
            swing += Clamp((expectedLost - stats.DuelsLost) * cfg.RatingPerDuelLost, cfg.RatingDuelCap);

            // Passing is judged on the rate rather than the count, and only once he has attempted
            // enough of them for the rate to mean anything.
            if (stats.PassesAttempted >= cfg.RatingMinPassesForAccuracy)
            {
                int accuracy = 100 * stats.PassesCompleted / stats.PassesAttempted;
                swing += (accuracy - cfg.RatingPassAccuracyPivotPercent)
                         * cfg.RatingPerTenPercentOfPassing / 10;
            }

            return swing;
        }

        /// <summary>The keeper's match: what he stopped, and what went past him.</summary>
        private static int KeeperSwing(PlayerMatchStats stats, PerformanceBalance cfg)
        {
            int swing = 0;

            swing += stats.Saves * cfg.RatingPerSave;
            swing -= stats.GoalsConceded * cfg.RatingPerGoalConceded;
            swing += stats.Goals * cfg.RatingPerGoal;
            swing += stats.Assists * cfg.RatingPerAssist;

            return swing;
        }

        /// <summary>What the referee wrote down, which counts against anybody.</summary>
        private static int Shared(PlayerMatchStats stats, PerformanceBalance cfg)
        {
            int swing = 0;

            swing -= stats.Fouls * cfg.RatingPerFoul;
            swing -= stats.Offsides * cfg.RatingPerOffside;
            swing -= stats.YellowCards * cfg.RatingPerYellowCard;
            swing -= stats.RedCards * cfg.RatingPerRedCard;

            return swing;
        }

        private static int Clamp(int value, int cap) =>
            value > cap ? cap : (value < -cap ? -cap : value);
    }
}
