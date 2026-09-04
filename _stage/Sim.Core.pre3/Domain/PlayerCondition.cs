namespace Sim.Core.Domain
{
    /// <summary>
    /// Short-term player state, each value in [0, 100]. See ARCHITECTURE.md 4.4:
    /// these create challenge but every malus they cause is capped (anti-frustration).
    /// Dynamics (mean reversion, recovery) arrive in Phase 4; this is the data holder.
    /// </summary>
    public sealed class PlayerCondition
    {
        private int _form = 50, _morale = 50, _fitness = 100;

        /// <summary>Short-term performance trend. 50 = neutral.</summary>
        public int Form    { get => _form;    set => _form    = AttributeScale.ClampCondition(value); }

        /// <summary>Mental wellbeing. Driven by playing time, results, support actions.</summary>
        public int Morale  { get => _morale;  set => _morale  = AttributeScale.ClampCondition(value); }

        /// <summary>Physical freshness. Drains with minutes, recovers with rest. Never causes unavailability.</summary>
        public int Fitness { get => _fitness; set => _fitness = AttributeScale.ClampCondition(value); }
    }
}
