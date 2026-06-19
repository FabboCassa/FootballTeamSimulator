using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Development
{
    /// <summary>
    /// The position-dependent age curve (task 4.4). Pure integer math, no transcendental
    /// functions, so it runs identically on .NET / Mono / IL2CPP.
    ///
    /// Two factors, both derived from age + role:
    ///   - <see cref="GrowthFactorPermille"/>: how fast a player can still improve. Full
    ///     (1000) while young, tapering linearly to zero at the role's peak age — so a
    ///     17-year-old grows fast, a 26-year-old barely, and a peaked player not at all
    ///     (potential headroom still gates the absolute ceiling, see <see cref="DevelopmentModel"/>).
    ///   - <see cref="DeclineMultiplierPermille"/>: 0 before the role's decline-onset age,
    ///     then 1000 (×1 of the base rate) at onset and ramping up with each further year,
    ///     capped — so decline is gentle and never a cliff (anti-collapse).
    ///
    /// Per-role offsets are STRUCTURAL here (like the tactic +/- patterns in TacticModifiers):
    /// keepers and centre-backs peak late and decline slowly; pace-reliant wingers and
    /// strikers peak early and fade first. Only the magnitudes live in <see cref="DevelopmentBalance"/>.
    /// </summary>
    public static class AgeCurve
    {
        // PositionRole order: 0 GK, 1 CB, 2 FB, 3 DM, 4 CM, 5 AM, 6 W, 7 ST.

        /// <summary>Years added to the base peak age per role (keepers/defenders peak later, wide attackers earlier).</summary>
        private static readonly int[] PeakAgeOffsetByRole = { 5, 2, 0, 1, 0, -1, -2, -1 };

        /// <summary>Years added to the base decline-onset age per role (keepers/defenders decline later).</summary>
        private static readonly int[] DeclineOnsetOffsetByRole = { 5, 3, 1, 2, 1, 0, -2, -1 };

        /// <summary>Effective age at which this role stops being able to grow.</summary>
        public static int PeakAge(PositionRole role, DevelopmentBalance cfg) =>
            cfg.GrowthPeakAge + PeakAgeOffsetByRole[(int)role];

        /// <summary>Effective age at which this role begins to decline.</summary>
        public static int DeclineOnsetAge(PositionRole role, DevelopmentBalance cfg) =>
            cfg.DeclineOnsetAge + DeclineOnsetOffsetByRole[(int)role];

        /// <summary>
        /// Age scaling of growth speed, in 1/1000 (1000 = full speed, 0 = no age-driven growth).
        /// Flat at full speed up to <see cref="DevelopmentBalance.GrowthYouthFullAge"/>, then
        /// linearly down to 0 at this role's peak age.
        /// </summary>
        public static int GrowthFactorPermille(int age, PositionRole role, DevelopmentBalance cfg)
        {
            int peak = PeakAge(role, cfg);
            if (age <= cfg.GrowthYouthFullAge) return 1000;
            if (age >= peak) return 0;

            int span = peak - cfg.GrowthYouthFullAge;
            if (span <= 0) return 0;                     // degenerate config guard
            return 1000 * (peak - age) / span;
        }

        /// <summary>
        /// Age scaling of decline pressure, in 1/1000. Zero before the role's onset age (the
        /// player is not yet ageing); 1000 (×1 of the base decline rate) at onset and rising
        /// by <see cref="DevelopmentBalance.AgeDeclineRampPerMillePerYear"/> per year beyond it,
        /// clamped at <see cref="DevelopmentBalance.AgeDeclineMaxMultiplierPermille"/>.
        /// </summary>
        public static int DeclineMultiplierPermille(int age, PositionRole role, DevelopmentBalance cfg)
        {
            int onset = DeclineOnsetAge(role, cfg);
            if (age < onset) return 0;

            int mult = 1000 + (age - onset) * cfg.AgeDeclineRampPerMillePerYear;
            int cap = cfg.AgeDeclineMaxMultiplierPermille;
            return mult > cap ? cap : mult;
        }
    }
}
