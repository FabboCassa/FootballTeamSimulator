using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Development
{
    /// <summary>
    /// The age- and modifier-aware development model (task 4.4), layered on the 4.3
    /// training foundation. Pure and deterministic: integer math plus exactly
    /// <see cref="PlayerAttributes.SkillCount"/> RNG draws per player per week (one per
    /// skill, in every regime) so changing age/minutes/focus re-thresholds the SAME rolls
    /// — isolating each effect in an A/B harness, exactly like <see cref="TrainingModel"/>.
    ///
    /// One weekly call decides the player's regime from his age (via <see cref="AgeCurve"/>):
    ///   - past his role's decline-onset age → he declines, faster with age, regardless of
    ///     headroom (an ageing player loses ground even if he never reached his potential);
    ///   - otherwise, below potential → he grows, with the rate scaled by the age curve and
    ///     by minutes / facilities / performance (youth who play and train well grow fastest);
    ///   - otherwise (peaked but not yet old) → gentle maintenance drift, exactly as 4.3.
    ///
    /// Anti-collapse (ARCHITECTURE.md §4.4/§4.5): ageing decline is gentle per week and
    /// floored at <see cref="DevelopmentBalance.AgeDeclineFloorPercentOfPotential"/>% of the
    /// player's potential, and drilled skills decline slower — so even a 38-year-old fades
    /// smoothly toward a floor, never crashing. Distinct from the 4.3 <see cref="TrainingModel"/>
    /// path: hosts that only call training stay byte-identical (golden masters safe).
    /// </summary>
    public static class DevelopmentModel
    {
        /// <summary>Applies one development week using a neutral context (age-only growth).</summary>
        public static void ApplyDevelopmentWeek(
            Player player, TeamTrainingFocus team, IndividualTrainingFocus individual,
            IRandomSource rng, DevelopmentBalance cfg) =>
            ApplyDevelopmentWeek(player, team, individual, DevelopmentContext.Neutral(cfg), rng, cfg);

        /// <summary>
        /// Applies one development week to a single player, mutating his attributes. Draws
        /// exactly <see cref="PlayerAttributes.SkillCount"/> values from <paramref name="rng"/>.
        /// </summary>
        public static void ApplyDevelopmentWeek(
            Player player, TeamTrainingFocus team, IndividualTrainingFocus individual,
            DevelopmentContext ctx, IRandomSource rng, DevelopmentBalance cfg)
        {
            int[] weights = TrainingModel.CombinedWeights(team, individual);
            int potential = player.Development.Potential;
            int declineMult = AgeCurve.DeclineMultiplierPermille(player.Age, player.Role, cfg);

            if (declineMult > 0)
                DeclineByAge(player, weights, potential, declineMult, rng, cfg);
            else if (potential - PlayerRating.Overall(player) > 0)
                Grow(player, weights, potential, ctx, rng, cfg);
            else
                DeclineMaintenance(player, weights, potential, rng, cfg);
        }

        /// <summary>
        /// Improvement regime: focused skills rise, gated per increment against potential
        /// (a strict ceiling that slows growth as it nears), and scaled by the age curve and
        /// by minutes / facilities / performance.
        /// </summary>
        private static void Grow(
            Player player, int[] weights, int potential, DevelopmentContext ctx,
            IRandomSource rng, DevelopmentBalance cfg)
        {
            int cap = cfg.HeadroomScaleCap > 0 ? cfg.HeadroomScaleCap : 1;
            int ageGrowth = AgeCurve.GrowthFactorPermille(player.Age, player.Role, cfg);
            int minutes = MinutesFactorPermille(ctx.PlayingSharePercent, cfg);
            int facility = FacilityFactorPermille(ctx.FacilityLevel, cfg);
            int performance = PerformanceFactorPermille(ctx.PerformanceRating, cfg);
            PlayerAttributes a = player.Attributes;

            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
            {
                int gap = potential - PlayerRating.Overall(player);   // recomputed: stops exactly at potential
                int headroom = gap < cap ? gap : cap;
                if (headroom < 0) headroom = 0;

                long permille = (long)weights[s] * cfg.GrowthPerMillePerWeight / 100;
                permille = permille * headroom / cap;                 // slow near potential (4.3)
                permille = permille * ageGrowth / 1000;               // 4.4 age curve
                permille = permille * minutes / 1000;                 // 4.4 minutes
                permille = permille * facility / 1000;                // 4.4 facilities
                permille = permille * performance / 1000;             // 4.4 performances
                if (permille > cfg.GrowthMaxPerMille) permille = cfg.GrowthMaxPerMille;

                bool roll = rng.NextInt(0, 1000) < (int)permille;     // always draw, for stream stability
                if (roll && gap > 0)
                    a[s] = a[s] + 1;
            }
        }

        /// <summary>
        /// Ageing decline: each skill may lose a point, more likely the older the player,
        /// but only while his overall stays above the anti-collapse floor (a percent of his
        /// potential). Drilled skills are protected (drilling keeps them sharp).
        /// </summary>
        private static void DeclineByAge(
            Player player, int[] weights, int potential, int declineMult,
            IRandomSource rng, DevelopmentBalance cfg)
        {
            int floor = potential * cfg.AgeDeclineFloorPercentOfPotential / 100;
            PlayerAttributes a = player.Attributes;

            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
            {
                long permille = (long)cfg.AgeDeclineBasePerMille * declineMult / 1000;
                if (weights[s] >= cfg.DeclineProtectionWeightThreshold)
                    permille = permille * (100 - cfg.DeclineProtectionPercent) / 100;

                bool roll = rng.NextInt(0, 1000) < (int)permille;     // always draw, for stream stability
                if (roll && PlayerRating.Overall(player) > floor && a[s] > AttributeScale.MinSkill)
                    a[s] = a[s] - 1;
            }
        }

        /// <summary>
        /// Maintenance drift for a peaked-but-not-old player — identical to the 4.3
        /// at-potential decline (capped at potential − DeclineFloorPoints, drilled skills
        /// protected). Keeps the world from stagnating before ageing proper kicks in.
        /// </summary>
        private static void DeclineMaintenance(
            Player player, int[] weights, int potential, IRandomSource rng, DevelopmentBalance cfg)
        {
            int floor = potential - cfg.DeclineFloorPoints;
            PlayerAttributes a = player.Attributes;

            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
            {
                int permille = cfg.DeclinePerMille;
                if (weights[s] >= cfg.DeclineProtectionWeightThreshold)
                    permille = permille * (100 - cfg.DeclineProtectionPercent) / 100;

                bool roll = rng.NextInt(0, 1000) < permille;          // always draw, for stream stability
                if (roll && PlayerRating.Overall(player) > floor && a[s] > AttributeScale.MinSkill)
                    a[s] = a[s] - 1;
            }
        }

        // --- Growth modifier factors (1/1000; 1000 = no change) ---

        /// <summary>Minutes → growth factor: a benched player (0%) develops at the floor rate, full minutes (100%) at full speed.</summary>
        private static int MinutesFactorPermille(int playingSharePercent, DevelopmentBalance cfg)
        {
            int share = playingSharePercent < 0 ? 0 : playingSharePercent > 100 ? 100 : playingSharePercent;
            int floor = cfg.MinutesGrowthFloorPermille;
            return floor + (1000 - floor) * share / 100;
        }

        /// <summary>Facility level → growth factor, neutral at the configured baseline level.</summary>
        private static int FacilityFactorPermille(int facilityLevel, DevelopmentBalance cfg)
        {
            int neutral = cfg.FacilityNeutralLevel > 0 ? cfg.FacilityNeutralLevel : 1;
            int factor = 1000 + (facilityLevel - neutral) * cfg.FacilityGrowthSwingPermille / neutral;
            return factor < 0 ? 0 : factor;
        }

        /// <summary>Recent performance → growth factor, neutral at the configured baseline rating.</summary>
        private static int PerformanceFactorPermille(int performanceRating, DevelopmentBalance cfg)
        {
            int neutral = cfg.PerformanceNeutralRating > 0 ? cfg.PerformanceNeutralRating : 1;
            int factor = 1000 + (performanceRating - neutral) * cfg.PerformanceGrowthSwingPermille / neutral;
            return factor < 0 ? 0 : factor;
        }
    }
}
