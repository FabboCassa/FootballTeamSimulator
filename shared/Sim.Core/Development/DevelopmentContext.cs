using Sim.Core.Config;

namespace Sim.Core.Development
{
    /// <summary>
    /// The per-player inputs a development week needs beyond the player's own age, role,
    /// attributes and potential (task 4.4). Plain value data the host assembles each week:
    ///   - <see cref="PlayingSharePercent"/>: how much the player has featured recently
    ///     (0 = benched, 100 = ever-present). Youth need games to grow.
    ///   - <see cref="FacilityLevel"/>: training-ground quality (0–100). Real levels arrive
    ///     with facilities in task 5.5; until then hosts pass the neutral level.
    ///   - <see cref="PerformanceRating"/>: recent match-performance signal (0–100). The
    ///     host can feed real ratings later, or use form/condition as a proxy now.
    ///
    /// <see cref="Neutral"/> (full minutes, neutral facility &amp; performance) makes growth
    /// depend on age alone — what AI clubs and any un-tracked player get.
    /// </summary>
    public readonly struct DevelopmentContext
    {
        public readonly int PlayingSharePercent;
        public readonly int FacilityLevel;
        public readonly int PerformanceRating;

        public DevelopmentContext(int playingSharePercent, int facilityLevel, int performanceRating)
        {
            PlayingSharePercent = playingSharePercent;
            FacilityLevel = facilityLevel;
            PerformanceRating = performanceRating;
        }

        /// <summary>Full minutes, neutral facility and performance → growth scaled by age only.</summary>
        public static DevelopmentContext Neutral(DevelopmentBalance cfg) =>
            new DevelopmentContext(100, cfg.FacilityNeutralLevel, cfg.PerformanceNeutralRating);
    }
}
