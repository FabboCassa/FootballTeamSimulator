namespace Sim.Core.Match
{
    /// <summary>
    /// How willing a manager is to rest tired players (task 10.1). PURE, integer, no RNG.
    ///
    /// Why this exists: until 10.1 the only lineup lever was <see cref="LineupSelector.CompetentEleven"/>'s
    /// competence, and a competence miss BENCHES the best candidate — which, with live condition, is
    /// indistinguishable from rotating the squad. The balance harness caught the consequence: a less
    /// competent AI accidentally fielded fresher legs, so the difficulty lever inverted (control gap +6.9
    /// points a season, condition-only gap −0.5). Rotation is therefore made an explicit choice of its
    /// own, leaving competence to mean exactly one thing: quality of selection.
    ///
    /// The model is deliberately blunt: a player below <see cref="FitnessTarget"/> is ranked as if he were
    /// worse, by <see cref="PenaltyPerPoint"/> rating points per missing fitness point, scaled by
    /// <see cref="Percent"/>. A manager who rotates fully (100) prefers a fresh squad player to a drained
    /// starter; one who never rotates (0) is byte-identical to the pre-10.1 selector, which is what keeps
    /// every existing golden master and replay valid.
    /// </summary>
    public readonly struct RotationPolicy
    {
        /// <summary>0 = never rests anyone (the pre-10.1 behaviour), 100 = applies the full penalty.</summary>
        public readonly int Percent;

        /// <summary>Fitness below which a player counts as tired; at or above it he is ranked on merit.</summary>
        public readonly int FitnessTarget;

        /// <summary>Rating points a fully-rotating manager deducts per missing fitness point.</summary>
        public readonly int PenaltyPerPoint;

        public RotationPolicy(int percent, int fitnessTarget, int penaltyPerPoint)
        {
            Percent = percent < 0 ? 0 : percent > 100 ? 100 : percent;
            FitnessTarget = fitnessTarget < 0 ? 0 : fitnessTarget;
            PenaltyPerPoint = penaltyPerPoint < 0 ? 0 : penaltyPerPoint;
        }

        /// <summary>A manager who never rests anyone — the identity policy.</summary>
        public static RotationPolicy None => new RotationPolicy(0, 0, 0);

        /// <summary>True when the policy can actually move a ranking.</summary>
        public bool IsActive => Percent > 0 && FitnessTarget > 0 && PenaltyPerPoint > 0;

        /// <summary>
        /// The rating this manager ranks the player on. A fresh player (fitness at or above the target)
        /// always keeps his true rating, so a fully-fit squad ranks exactly as it did before 10.1.
        /// </summary>
        public int Adjust(int rating, int fitness)
        {
            if (!IsActive) return rating;

            int deficit = FitnessTarget - fitness;
            if (deficit <= 0) return rating;

            return rating - (deficit * PenaltyPerPoint * Percent) / 100;
        }
    }
}
