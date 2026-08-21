using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Scouting
{
    /// <summary>
    /// A scout's three attributes, resolved into the three percentages the model actually uses
    /// (task 11.2). Kept as a small value type so <see cref="ScoutingModel"/> stays a pure
    /// function of numbers and never has to know what a <see cref="Scout"/> is.
    ///
    ///   • <see cref="AbilityPercent"/> scales the knowledge a report of CURRENT ability is built
    ///     at — a good judge of ability reads a player as if he had watched him longer.
    ///   • <see cref="PotentialPercent"/> does the same for the potential band, separately: a scout
    ///     can be excellent at reading what a player IS and hopeless at reading what he will BECOME.
    ///   • <see cref="SpeedPercent"/> (adaptability) scales how fast he accrues knowledge away from
    ///     a single club, so it is what makes a wide brief survivable.
    ///
    /// 100 is neutral everywhere, and <see cref="Neutral"/> reproduces the pre-11.2 numbers
    /// EXACTLY — which is why every task 5.4 test and every existing save still reads the same.
    /// A scout with the neutral attribute value (<see cref="ScoutingBalance.NeutralScoutAttribute"/>)
    /// resolves to exactly 100/100/100.
    /// </summary>
    public readonly struct ScoutQuality
    {
        /// <summary>Percent of the raw knowledge used when reading current ability (100 = neutral).</summary>
        public int AbilityPercent { get; }

        /// <summary>Percent of the raw knowledge used when reading potential (100 = neutral).</summary>
        public int PotentialPercent { get; }

        /// <summary>Percent of the weekly knowledge gain on an area brief (100 = neutral).</summary>
        public int SpeedPercent { get; }

        public ScoutQuality(int abilityPercent, int potentialPercent, int speedPercent)
        {
            AbilityPercent = abilityPercent < 1 ? 1 : abilityPercent;
            PotentialPercent = potentialPercent < 1 ? 1 : potentialPercent;
            SpeedPercent = speedPercent < 1 ? 1 : speedPercent;
        }

        /// <summary>The pre-11.2 department: no scout attributes, everything at 100%.</summary>
        public static ScoutQuality Neutral => new ScoutQuality(100, 100, 100);

        /// <summary>Resolves a scout's attributes; a null scout is the neutral department.</summary>
        public static ScoutQuality Of(Scout? scout, ScoutingBalance cfg)
        {
            if (scout == null)
                return Neutral;

            return new ScoutQuality(
                Scale(scout.JudgingAbility, cfg.ScoutJudgingBasePercent, cfg.ScoutJudgingMaxPercent, cfg),
                Scale(scout.JudgingPotential, cfg.ScoutJudgingBasePercent, cfg.ScoutJudgingMaxPercent, cfg),
                Scale(scout.Adaptability, cfg.ScoutAdaptabilityBasePercent, cfg.ScoutAdaptabilityMaxPercent, cfg));
        }

        /// <summary>
        /// Maps an attribute on the [1, 100] scale onto [basePercent, maxPercent], linearly, so that
        /// the neutral attribute lands exactly on 100 (the base/max pairs in
        /// <see cref="ScoutingBalance"/> are chosen to make that true).
        /// </summary>
        private static int Scale(int attribute, int basePercent, int maxPercent, ScoutingBalance cfg)
        {
            int a = AttributeScale.ClampSkill(attribute);
            int span = maxPercent - basePercent;
            int neutral = cfg.NeutralScoutAttribute > 0 ? cfg.NeutralScoutAttribute : 50;

            // basePercent + span * a / 100, arranged so that a == neutral gives exactly 100 when the
            // config pair is consistent; the explicit re-centre below makes it true for ANY pair.
            int raw = basePercent + span * a / 100;
            int atNeutral = basePercent + span * neutral / 100;
            int corrected = raw + (100 - atNeutral);
            return corrected < 1 ? 1 : corrected;
        }
    }
}
