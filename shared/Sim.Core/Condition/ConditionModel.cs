using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Condition
{
    /// <summary>A team's result from one player's point of view (drives form/morale).</summary>
    public enum TeamResult
    {
        Loss = 0,
        Draw = 1,
        Win = 2
    }

    /// <summary>
    /// The condition model (task 4.1): turns a player's short-term state
    /// (<see cref="PlayerCondition"/>: form, morale, fitness) into a capped match
    /// performance multiplier, and evolves that state across matches and rest days.
    ///
    /// Design pillars (ARCHITECTURE.md §4.4 — "challenge, not chaos"):
    ///   - every malus is capped: a player never performs below
    ///     <see cref="ConditionBalance.PerformanceFloorPercent"/> of his ability;
    ///   - form is a bounded, mean-reverting walk, so cold streaks always end;
    ///   - fitness drains with minutes and recovers with rest (no injuries — the
    ///     cost of a tired player is reduced performance, never unavailability);
    ///   - morale moves with playing time and results and decays toward neutral.
    ///
    /// Pure and deterministic: integer arithmetic plus a single seeded RNG draw for
    /// the form walk; only +,-,*,/ on doubles in the multiplier (no transcendental
    /// functions), so results are identical across .NET / Mono / IL2CPP. The host
    /// owns the stored <see cref="PlayerCondition"/> values and the call schedule.
    /// </summary>
    public static class ConditionModel
    {
        /// <summary>
        /// Match performance multiplier for a player's current condition.
        /// Exactly 1.0 at neutral (Form = FormNeutral, Morale = MoraleNeutral,
        /// Fitness = 100); clamped to [floor, cap]. Form and morale swing both ways;
        /// fitness can only ever reduce performance (it tops out at 100 = no effect).
        /// </summary>
        public static double PerformanceMultiplier(PlayerCondition condition, ConditionBalance cfg)
        {
            double formTerm =
                (condition.Form - cfg.FormNeutral) / 50.0 * (cfg.FormSwingPermille / 1000.0);
            double moraleTerm =
                (condition.Morale - cfg.MoraleNeutral) / 50.0 * (cfg.MoraleSwingPermille / 1000.0);
            double fitnessTerm =
                (condition.Fitness - 100) / 100.0 * (cfg.FitnessSwingPermille / 1000.0);

            double multiplier = 1.0 + formTerm + moraleTerm + fitnessTerm;

            double floor = cfg.PerformanceFloorPercent / 100.0;
            double cap = cfg.PerformanceCapPercent / 100.0;
            return multiplier < floor ? floor : (multiplier > cap ? cap : multiplier);
        }

        /// <summary>
        /// Evolves a player's condition after a match he was involved with the squad for.
        /// <paramref name="minutesPlayed"/> is 0 for an unused player (bench/stands).
        /// Fitness drains in proportion to minutes; form takes its mean-reverting walk
        /// (plus a result nudge if he featured); morale moves with playing time and result.
        /// </summary>
        public static void ApplyMatchResult(
            PlayerCondition condition, int minutesPlayed, TeamResult result,
            IRandomSource rng, ConditionBalance cfg)
        {
            bool played = minutesPlayed > 0;

            if (played)
                condition.Fitness -= cfg.FitnessDrainPer90Minutes * minutesPlayed / 90;

            StepForm(condition, played, result, rng, cfg);

            condition.Morale += played ? cfg.MoralePlayBonus : -cfg.MoraleBenchPenalty;
            if (result == TeamResult.Win) condition.Morale += cfg.MoraleWinBonus;
            else if (result == TeamResult.Loss) condition.Morale -= cfg.MoraleLossPenalty;
        }

        /// <summary>
        /// Recovers a player over <paramref name="days"/> of rest: fitness climbs back
        /// (capped at 100) and morale drifts toward neutral. No-op for days &lt;= 0.
        /// </summary>
        public static void ApplyRest(PlayerCondition condition, int days, ConditionBalance cfg)
        {
            if (days <= 0) return;

            condition.Fitness += cfg.FitnessRecoveryPerDay * days;

            int moraleGap = cfg.MoraleNeutral - condition.Morale;
            int decay = cfg.MoraleDecayPerDay * days;
            if (moraleGap > 0) condition.Morale += decay < moraleGap ? decay : moraleGap;
            else if (moraleGap < 0) condition.Morale -= decay < -moraleGap ? decay : -moraleGap;
        }

        /// <summary>
        /// One match step of the bounded, mean-reverting form walk: pull a fraction of
        /// the gap back toward neutral, add a small uniform random step, and (for a
        /// player who featured) nudge by the result. Mean reversion guarantees a cold
        /// streak self-corrects; the clamp keeps form in [0, 100].
        /// </summary>
        private static void StepForm(
            PlayerCondition condition, bool played, TeamResult result,
            IRandomSource rng, ConditionBalance cfg)
        {
            int gap = cfg.FormNeutral - condition.Form;
            int reversion = gap * cfg.FormReversionPermille / 1000;
            int random = rng.NextInt(-cfg.FormRandomStep, cfg.FormRandomStep + 1);

            int nudge = 0;
            if (played)
                nudge = result == TeamResult.Win ? cfg.FormResultNudge
                      : result == TeamResult.Loss ? -cfg.FormResultNudge : 0;

            condition.Form += reversion + random + nudge;
        }
    }
}
