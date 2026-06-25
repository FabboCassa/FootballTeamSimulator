using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// Turns a facility tier into its in-game effect, and prices the next upgrade (task 5.5).
    /// PURE and deterministic (integer math, NO RNG): every mapping is a stable function of the
    /// tier, so the same facilities always produce the same effects on .NET / Mono / IL2CPP. The
    /// match engine never calls this — it is opt-in, so golden masters/replays are unaffected.
    ///
    /// Tier 1 reproduces the pre-5.5 baseline: training maps to the development model's neutral
    /// facility level (a fresh world develops exactly as 4.4) and scouting to the base scout level
    /// (task 5.4); each upgrade only lifts the effect. Magnitudes live in <see cref="FinanceBalance"/>;
    /// the shapes are structural here, like the role offsets in AgeCurve.
    /// </summary>
    public static class FacilityEffects
    {
        /// <summary>
        /// Training tier → the development <see cref="Development.DevelopmentContext.FacilityLevel"/>
        /// (0–100). Tier 1 = the development model's neutral level; each tier adds
        /// <see cref="FinanceBalance.TrainingFacilityLevelPerTier"/>, capped at the skill ceiling.
        /// </summary>
        public static int TrainingFacilityLevel(int trainingTier, BalanceConfig cfg)
        {
            int tier = Clamp(trainingTier, cfg.Finance.MaxFacilityTier);
            int level = cfg.Development.FacilityNeutralLevel
                        + (tier - 1) * cfg.Finance.TrainingFacilityLevelPerTier;
            if (level > AttributeScale.MaxSkill) level = AttributeScale.MaxSkill;
            if (level < 0) level = 0;
            return level;
        }

        /// <summary>Stadium tier → seating capacity (drives gate receipts).</summary>
        public static int StadiumCapacity(int stadiumTier, FinanceBalance cfg)
        {
            int tier = Clamp(stadiumTier, cfg.MaxFacilityTier);
            return cfg.StadiumBaseCapacity + (tier - 1) * cfg.StadiumCapacityPerTier;
        }

        /// <summary>
        /// Scouting tier → effective scout level (task 5.4): tier maps directly to a level,
        /// clamped to <see cref="ScoutingBalance.MaxScoutLevel"/>. Tier 1 = the base scout level.
        /// </summary>
        public static int ScoutLevel(int scoutingTier, BalanceConfig cfg)
        {
            int tier = Clamp(scoutingTier, cfg.Finance.MaxFacilityTier);
            int level = tier;
            if (level > cfg.Scouting.MaxScoutLevel) level = cfg.Scouting.MaxScoutLevel;
            if (level < 1) level = 1;
            return level;
        }

        /// <summary>Academy tier → youth quality rating (0–100). Consumed by youth intake (a later system).</summary>
        public static int AcademyRating(int academyTier, FinanceBalance cfg)
        {
            int tier = Clamp(academyTier, cfg.MaxFacilityTier);
            int rating = cfg.AcademyRatingBase + (tier - 1) * cfg.AcademyRatingPerTier;
            if (rating > 100) rating = 100;
            if (rating < 0) rating = 0;
            return rating;
        }

        /// <summary>
        /// Cost to upgrade a facility from its current tier to the next one — rises with the
        /// current tier (base × tier²). Returns 0 when already at the maximum tier.
        /// </summary>
        public static long UpgradeCost(int currentTier, FinanceBalance cfg)
        {
            int tier = Clamp(currentTier, cfg.MaxFacilityTier);
            if (tier >= cfg.MaxFacilityTier) return 0;
            return cfg.FacilityUpgradeBaseCost * (tier * (long)tier);
        }

        /// <summary>Cost to upgrade one of a club's facilities by a tier (0 if maxed).</summary>
        public static long UpgradeCost(Club club, FacilityType type, BalanceConfig cfg) =>
            UpgradeCost(club.Facilities.TierOf(type), cfg.Finance);

        /// <summary>
        /// A sensible starting stadium tier for a club of the given best-XI strength, so big
        /// clubs begin with big grounds (income then scales with squad size). Clamped to the
        /// facility range.
        /// </summary>
        public static int SuggestedStadiumTier(int clubStrength, FinanceBalance cfg)
        {
            int per = cfg.StrengthPerStadiumTier > 0 ? cfg.StrengthPerStadiumTier : 1;
            int tier = 1 + (clubStrength - cfg.StadiumTierStrengthFloor) / per;
            return Clamp(tier, cfg.MaxFacilityTier);
        }

        private static int Clamp(int tier, int max)
        {
            if (tier < 1) return 1;
            if (tier > max) return max;
            return tier;
        }
    }
}
