using System;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Condition
{
    /// <summary>
    /// The player support actions model (task 4.5): turns a coach conversation
    /// (<see cref="SupportAction"/>) into a capped, context-sensitive condition move
    /// and gates repeats behind a per-player cooldown (<see cref="SupportActionLog"/>).
    ///
    /// Pure and deterministic — integer arithmetic only, NO randomness and no
    /// transcendental functions, so an action's effect is a plain function of the
    /// player's current condition and the config. Nothing in the match engine or
    /// SeasonProgressor calls this, so it is opt-in and leaves golden masters/replays
    /// untouched (no engine Version bump).
    ///
    /// Two anti-spam mechanisms, as agreed:
    ///   - <b>context sensitivity</b> (soft): each action's magnitude scales with how
    ///     appropriate it is right now — praising a player who is already maxed out, or
    ///     encouraging one who is already happy, does almost nothing, so repeated use
    ///     saturates toward zero;
    ///   - <b>cooldown</b> (hard): <see cref="TryApply"/> refuses an action used on the
    ///     same player less than its cooldown ago, returning a blocked no-op.
    ///
    /// All magnitudes live in <see cref="SupportBalance"/>; the "when does it land"
    /// patterns below are structural (like the tactics counter-matrix).
    /// </summary>
    public static class SupportActionModel
    {
        private const int Max = AttributeScale.MaxCondition; // 100

        /// <summary>
        /// The neutral pivot for form/morale. Fixed at 50 to match <see cref="PlayerCondition"/>'s
        /// defaults and ConditionBalance.FormNeutral/MoraleNeutral; kept local so the model
        /// depends only on <see cref="SupportBalance"/>.
        /// </summary>
        private const int Neutral = 50;

        /// <summary>
        /// The condition deltas <paramref name="action"/> would apply to a player in
        /// <paramref name="condition"/> right now, before the 0..100 clamp. Pure: does
        /// NOT mutate and does NOT consult the cooldown. Use it to preview an action or
        /// to assert the configured/context behaviour; use <see cref="TryApply"/> to
        /// actually perform one (cooldown-gated).
        /// </summary>
        public static SupportOutcome Resolve(SupportAction action, PlayerCondition condition, SupportBalance cfg)
        {
            int morale = condition.Morale; // condition values are already clamped 0..100
            int form = condition.Form;
            int fitness = condition.Fitness;

            switch (action)
            {
                case SupportAction.Praise:
                {
                    // Reward: scaled by morale headroom (full ≤ neutral, 0 at the ceiling)
                    // and by recent form (good play earns a fuller reward; poor play rings hollow).
                    int headroom = ClampPermille((Max - morale) * 1000 / Max1(Max - Neutral));
                    int formFit = ClampPermille(form * 1000 / Max1(Neutral));
                    int ctx = headroom * formFit / 1000;
                    return Intended(Scale(cfg.PraiseMoraleBoost, ctx), 0, 0, false);
                }
                case SupportAction.Encourage:
                {
                    // A pick-me-up: strongest at low morale, linear to zero at the ceiling.
                    int ctx = ClampPermille((Max - morale) * 1000 / Max);
                    return Intended(Scale(cfg.EncourageMoraleBoost, ctx), 0, 0, false);
                }
                case SupportAction.Motivate:
                {
                    // A challenge: peaks for a mid-morale player; the maxed have nothing to
                    // prove and the broken need encouragement first. Small morale + form push.
                    int ctx = ClampPermille(1000 - Math.Abs(morale - Neutral) * 1000 / Max1(Neutral));
                    return Intended(Scale(cfg.MotivateMoraleBoost, ctx), Scale(cfg.MotivateFormNudge, ctx), 0, false);
                }
                case SupportAction.Criticize:
                {
                    // The stick: the morale hit lands harder on an already-fragile (low-morale)
                    // player and is shrugged off by a confident one, while the form "wake-up"
                    // spark is converted best by that same confident player. So criticising a
                    // happy in-form star is a useful kick; criticising someone who is down is a
                    // bounded, telegraphed mistake.
                    int fragility = ClampPermille((Max - morale) * 1000 / Max);
                    int confidence = ClampPermille(morale * 1000 / Max);
                    return Intended(-Scale(cfg.CriticizeMoraleHit, fragility), Scale(cfg.CriticizeFormSpark, confidence), 0, false);
                }
                case SupportAction.Rest:
                {
                    // A breather: fitness recovery scaled by how tired he is (~0 when fresh),
                    // plus a small flat morale lift. Costs that week's training (host honours it).
                    int fitnessRoom = ClampPermille((Max - fitness) * 1000 / Max);
                    return Intended(cfg.RestMoraleBoost, 0, Scale(cfg.RestFitnessRecovery, fitnessRoom), true);
                }
                default:
                    return SupportOutcome.Blocked;
            }
        }

        /// <summary>
        /// Performs <paramref name="action"/> on <paramref name="condition"/> if it is
        /// not on cooldown for <paramref name="playerId"/>: mutates the condition by the
        /// resolved deltas (clamped to 0..100), records the use to start the cooldown,
        /// and returns the ACTUAL applied deltas. If the action is still on cooldown it
        /// is a no-op and returns <see cref="SupportOutcome.Blocked"/> — this is the hard
        /// anti-spam gate.
        /// </summary>
        public static SupportOutcome TryApply(
            PlayerCondition condition, SupportAction action,
            SupportActionLog log, int playerId, int currentDay, SupportBalance cfg)
        {
            if (log.IsOnCooldown(playerId, action, currentDay, cfg))
                return SupportOutcome.Blocked;

            SupportOutcome intended = Resolve(action, condition, cfg);

            int beforeMorale = condition.Morale;
            int beforeForm = condition.Form;
            int beforeFitness = condition.Fitness;

            condition.Morale += intended.MoraleDelta;
            condition.Form += intended.FormDelta;
            condition.Fitness += intended.FitnessDelta;

            log.Record(playerId, action, currentDay);

            return new SupportOutcome(
                true,
                condition.Morale - beforeMorale,
                condition.Form - beforeForm,
                condition.Fitness - beforeFitness,
                intended.SkipsTraining);
        }

        // ----------------------------------------------------------------- helpers

        private static SupportOutcome Intended(int morale, int form, int fitness, bool skipsTraining)
            => new SupportOutcome(true, morale, form, fitness, skipsTraining);

        /// <summary>magnitude × permille / 1000, integer (truncates toward zero; sign preserved).</summary>
        private static int Scale(int magnitude, int permille) => magnitude * permille / 1000;

        private static int ClampPermille(int permille)
            => permille < 0 ? 0 : permille > 1000 ? 1000 : permille;

        /// <summary>Guards a denominator against zero (config could in theory set neutral = max).</summary>
        private static int Max1(int denominator) => denominator < 1 ? 1 : denominator;
    }
}
