using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Career
{
    /// <summary>
    /// The bridge between a played match and the two models that have never had any data from one
    /// (engine phase 7, docs/engine/MATCH_ENGINE_PLAN.md §4): condition, which until now credited
    /// every starter a flat ninety minutes and a substitute nothing at all, and development, whose
    /// <see cref="DevelopmentContext.PerformanceRating"/> has been a neutral constant since the day
    /// it was written because there was no performance to put in it.
    ///
    /// OPT-IN, AND OFF UNTIL A HOST TURNS IT ON. Nothing here is called by the engine or by the
    /// progressors on their own: a host has to build the dictionaries and pass them. That is a
    /// deliberate choice rather than shyness — real minutes and real ratings change how squads
    /// tire and how players grow, which is a BALANCE change, and a balance change is measured
    /// against the 1,000-match harness before it ships, not slipped in behind a statistic. Until
    /// then every figure of `balance.ps1` reads exactly what it read before this phase.
    ///
    /// It is also only ever available for a match that was PLAYED: the fast path has no picture,
    /// no picture means no statistics, and the world's thousands of background fixtures keep the
    /// flat-ninety credit they have always had.
    /// </summary>
    public static class MatchPerformanceFeed
    {
        /// <summary>
        /// A mark out of ten (in tenths) turned into the 0-100 performance signal the development
        /// model reads. The neutral mark maps exactly onto the model's own neutral rating, so a
        /// squad of 6.0s develops exactly as it did before anybody fed it anything.
        /// </summary>
        public static int DevelopmentRating(int ratingTenths, PerformanceBalance cfg, DevelopmentBalance development)
        {
            int rating = development.PerformanceNeutralRating
                + (ratingTenths - cfg.DevelopmentNeutralRating) * cfg.DevelopmentPointsPerTenth / 10;

            if (rating < 0) rating = 0;
            if (rating > 100) rating = 100;
            return rating;
        }

        /// <summary>Minutes played by player id, for one match.</summary>
        public static void CollectMinutes(MatchStats? stats, IDictionary<int, int> into)
        {
            if (stats == null) return;

            foreach (PlayerMatchStats player in stats.Players)
            {
                if (player.PlayerId <= 0 || player.MinutesPlayed <= 0) continue;

                into.TryGetValue(player.PlayerId, out int already);
                into[player.PlayerId] = already + player.MinutesPlayed;
            }
        }

        /// <summary>
        /// Development ratings by player id, for one match. A player who appears twice in a week
        /// (two fixtures) keeps his BEST mark rather than the last one — the alternative is that
        /// the order fixtures are visited in decides how a player grows, which is exactly the kind
        /// of order dependence everything else here is built to avoid.
        /// </summary>
        public static void CollectRatings(
            MatchStats? stats, PerformanceBalance cfg, DevelopmentBalance development, IDictionary<int, int> into)
        {
            if (stats == null) return;

            foreach (PlayerMatchStats player in stats.Players)
            {
                if (player.PlayerId <= 0 || player.MinutesPlayed <= 0) continue;

                int rating = DevelopmentRating(player.Rating, cfg, development);
                if (into.TryGetValue(player.PlayerId, out int already) && already >= rating) continue;

                into[player.PlayerId] = rating;
            }
        }

        /// <summary>Minutes played by player id over a whole matchday.</summary>
        public static Dictionary<int, int> Minutes(IReadOnlyList<MatchOutcome> outcomes)
        {
            var minutes = new Dictionary<int, int>();
            for (int i = 0; i < outcomes.Count; i++) CollectMinutes(outcomes[i].Report.Stats, minutes);
            return minutes;
        }

        /// <summary>Development ratings by player id over a whole matchday.</summary>
        public static Dictionary<int, int> Ratings(
            IReadOnlyList<MatchOutcome> outcomes, PerformanceBalance cfg, DevelopmentBalance development)
        {
            var ratings = new Dictionary<int, int>();
            for (int i = 0; i < outcomes.Count; i++)
                CollectRatings(outcomes[i].Report.Stats, cfg, development, ratings);

            return ratings;
        }
    }
}
