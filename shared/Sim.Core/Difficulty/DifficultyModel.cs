using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;

namespace Sim.Core.Difficulty
{
    /// <summary>
    /// Resolves and applies single-player difficulty (task 5.7, ARCHITECTURE.md §7). PURE and
    /// deterministic — integer math, NO RNG (the only randomness lives in the AI lineup selection,
    /// which is seeded per fixture by the host). Difficulty is HONEST: it changes AI competence,
    /// market aggressiveness, board patience and the user's budget — never AI raw strength or a
    /// hidden penalty.
    ///
    /// Four entry points, one per lever, all opt-in (nothing here is on the match-engine path, so
    /// golden masters/replays are unaffected):
    ///   • <see cref="Resolve"/> — the per-level row → a <see cref="DifficultySettings"/>;
    ///   • <see cref="MatchContext"/> — the AI-competence slice for <see cref="Career.SeasonProgressor"/>;
    ///   • <see cref="SeedBudgets"/> — per-club transfer budgets, user generous / AI aggressive by level;
    ///   • <see cref="ApplyBoardPatience"/> — scales the board's reactivity in the career config IN PLACE
    ///     (call once on the career's config), leaving the sacking thresholds + per-evaluation cap
    ///     intact so the task-5.6 "warning before sacking" invariant survives.
    /// </summary>
    public static class DifficultyModel
    {
        /// <summary>The 0..2 row index for a level (anything unknown falls back to Normal).</summary>
        public static int Index(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Easy: return 0;
                case DifficultyLevel.Hard: return 2;
                default: return 1;
            }
        }

        /// <summary>Reads the per-level magnitudes into a value object the host carries for the career.</summary>
        public static DifficultySettings Resolve(DifficultyLevel level, DifficultyBalance cfg)
        {
            int i = Index(level);
            return new DifficultySettings(
                level,
                Row(cfg.AiLineupCompetence, i, 100),
                cfg.AiLineupMaxSlips,
                Row(cfg.UserBudgetPermille, i, 1000),
                Row(cfg.AiBudgetPermille, i, 1000),
                Row(cfg.BoardReactivityPermille, i, 1000),
                Row(cfg.AiRotationPercent, i, 0));
        }

        /// <summary>The rotation policy this level's AI managers follow (task 10.1). Kept here rather than
        /// in <see cref="DifficultySettings"/> so the two tuning constants stay in one place.</summary>
        public static Match.RotationPolicy Rotation(DifficultySettings s, DifficultyBalance cfg) =>
            new Match.RotationPolicy(
                s.AiRotationPercent, cfg.RotationFitnessTarget, cfg.RotationPenaltyPerFitnessPoint);

        /// <summary>Convenience: resolve straight from a full config.</summary>
        public static DifficultySettings Resolve(DifficultyLevel level, BalanceConfig cfg) =>
            Resolve(level, cfg.Difficulty);

        /// <summary>
        /// The AI-lineup slice the season simulator needs — competence AND rotation (the user club is never
        /// degraded either way). <paramref name="cfg"/> supplies the two rotation constants; omitting it
        /// uses the shipped defaults, which is what every host does today.
        /// </summary>
        public static DifficultyContext MatchContext(
            int humanClubId, DifficultySettings s, DifficultyBalance? cfg = null) =>
            new DifficultyContext(
                humanClubId, s.AiLineupCompetence, s.AiLineupMaxSlips,
                Rotation(s, cfg ?? new DifficultyBalance()));

        /// <summary>
        /// Seeds every club's <see cref="Club.TransferBudget"/> from the base finance/strength model,
        /// then scales it by difficulty: the user's club by <see cref="DifficultySettings.UserBudgetPermille"/>
        /// (generous on Easy), every other club by <see cref="DifficultySettings.AiBudgetPermille"/>
        /// (aggressive on Hard). Floored at the configured minimum so even a skint club can do business.
        /// Overwrites the budget, so call it at season start (like <see cref="BudgetModel.SeedBudgets"/>).
        /// </summary>
        public static void SeedBudgets(
            IReadOnlyList<League> leagues, int humanClubId, DifficultySettings s, BalanceConfig cfg)
        {
            var baseModel = new BudgetModel(cfg);
            long minBudget = cfg.Transfer.MinBudget;

            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    long basis = baseModel.BudgetFor(club, league.Division);
                    int permille = club.Id == humanClubId ? s.UserBudgetPermille : s.AiBudgetPermille;
                    long scaled = basis * permille / 1000;
                    if (scaled < minBudget) scaled = minBudget;
                    club.TransferBudget = scaled;
                }
            }
        }

        /// <summary>
        /// Scales the board's confidence reactivity (per-position swing at season end and at the
        /// mid-season running check) by <see cref="DifficultySettings.BoardReactivityPermille"/>,
        /// MUTATING the career config in place — so the existing <see cref="Career.BoardModel"/> /
        /// <see cref="Career.CoachCareerProgressor"/> chain reacts faster (Hard) or slower (Easy) with
        /// no per-call-site changes. Thresholds and <see cref="CareerBalance.MaxConfidenceDeltaPerEvaluation"/>
        /// are deliberately NOT scaled, preserving the task-5.6 guarantee that a warning season always
        /// precedes a sacking. Idempotency: call ONCE per career config (a second call double-scales).
        /// </summary>
        public static void ApplyBoardPatience(BalanceConfig cfg, DifficultySettings s)
        {
            CareerBalance c = cfg.Career;
            c.ConfidencePerPositionVsObjective =
                ScalePositive(c.ConfidencePerPositionVsObjective, s.BoardReactivityPermille);
            c.RunningConfidencePerPositionVsObjective =
                ScalePositive(c.RunningConfidencePerPositionVsObjective, s.BoardReactivityPermille);
        }

        // ----------------------------------------------------------------- internals

        /// <summary>Row lookup with a safe fallback if the config array is short/empty.</summary>
        private static int Row(int[] row, int index, int fallback) =>
            row != null && index >= 0 && index < row.Length ? row[index] : fallback;

        /// <summary>Scale by permille, but never round a positive knob down to zero (keeps the board reactive).</summary>
        private static int ScalePositive(int value, int permille)
        {
            int scaled = value * permille / 1000;
            if (value > 0 && scaled < 1) scaled = 1;
            return scaled;
        }
    }
}
