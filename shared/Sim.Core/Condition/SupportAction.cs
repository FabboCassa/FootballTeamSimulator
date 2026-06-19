namespace Sim.Core.Condition
{
    /// <summary>
    /// A lightweight coach-to-player support conversation (task 4.5). These are the
    /// morale levers described in ARCHITECTURE.md §4.4: the user always has a way to
    /// lift a player who is down or reward one who is doing well.
    ///
    /// Numeric values are stable and used as save keys (do not renumber). The set is
    /// deliberately small and the effects are capped and context-sensitive (see
    /// <see cref="SupportActionModel"/>), so the tools shape morale without ever
    /// spiralling — "challenge, not chaos".
    /// </summary>
    public enum SupportAction
    {
        /// <summary>Reward good work: morale lift, best with room to lift and good recent form.</summary>
        Praise = 0,
        /// <summary>A pick-me-up: morale lift, strongest when the player is down.</summary>
        Encourage = 1,
        /// <summary>A challenge/push: small morale lift plus a form nudge, peaking for a mid-morale player.</summary>
        Motivate = 2,
        /// <summary>The stick: a morale hit (harder on the fragile) with a form spark a confident player converts.</summary>
        Criticize = 3,
        /// <summary>A breather: fitness recovery (scaled by tiredness) plus a small morale lift; costs that week's training.</summary>
        Rest = 4
    }

    /// <summary>
    /// The condition deltas a support action actually applied to a player. All values
    /// are post-clamp (what the player's 0..100 condition really moved by), so a host
    /// can show honest feedback like "Morale +6". <see cref="SkipsTraining"/> flags the
    /// opportunity cost of <see cref="SupportAction.Rest"/> for the host to honour in
    /// its development tick; Sim.Core itself never schedules training.
    /// </summary>
    public readonly struct SupportOutcome
    {
        /// <summary>Whether the action was actually applied (false = blocked by cooldown, all deltas 0).</summary>
        public readonly bool Applied;
        public readonly int MoraleDelta;
        public readonly int FormDelta;
        public readonly int FitnessDelta;
        /// <summary>True for a rest action: the host should skip this player's training for the period.</summary>
        public readonly bool SkipsTraining;

        public SupportOutcome(bool applied, int moraleDelta, int formDelta, int fitnessDelta, bool skipsTraining)
        {
            Applied = applied;
            MoraleDelta = moraleDelta;
            FormDelta = formDelta;
            FitnessDelta = fitnessDelta;
            SkipsTraining = skipsTraining;
        }

        /// <summary>A no-op outcome (action blocked by cooldown).</summary>
        public static SupportOutcome Blocked => new SupportOutcome(false, 0, 0, 0, false);
    }
}
