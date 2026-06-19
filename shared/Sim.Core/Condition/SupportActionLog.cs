using System.Collections.Generic;
using Sim.Core.Config;

namespace Sim.Core.Condition
{
    /// <summary>
    /// Per-player, per-action cooldown bookkeeping for the support actions (task 4.5):
    /// remembers the calendar day each (player, action) pair was last used, so the
    /// model can refuse a repeat until the action's cooldown elapses. This is the hard
    /// anti-spam gate (the context sensitivity in <see cref="SupportActionModel"/> is
    /// the soft one).
    ///
    /// In-memory and host-owned: like the tactic familiarity log, Sim.Core keeps it
    /// pure and the host (client save / server store) persists it. Days are whatever
    /// integer calendar the host counts in (the project uses CareerState.CurrentDay);
    /// only differences matter, so the unit is the host's.
    /// </summary>
    public sealed class SupportActionLog
    {
        /// <summary>Last-used day keyed by (playerId, action). Absent ⇒ never used.</summary>
        private readonly Dictionary<(int PlayerId, SupportAction Action), int> _lastUsedDay
            = new Dictionary<(int, SupportAction), int>();

        /// <summary>How many days an action is locked for after use.</summary>
        public static int CooldownDays(SupportAction action, SupportBalance cfg)
        {
            switch (action)
            {
                case SupportAction.Praise: return cfg.PraiseCooldownDays;
                case SupportAction.Encourage: return cfg.EncourageCooldownDays;
                case SupportAction.Motivate: return cfg.MotivateCooldownDays;
                case SupportAction.Criticize: return cfg.CriticizeCooldownDays;
                case SupportAction.Rest: return cfg.RestCooldownDays;
                default: return 0;
            }
        }

        /// <summary>
        /// True if <paramref name="action"/> was used on this player less than its
        /// cooldown ago — i.e. it cannot be used again yet on <paramref name="currentDay"/>.
        /// </summary>
        public bool IsOnCooldown(int playerId, SupportAction action, int currentDay, SupportBalance cfg)
        {
            if (!_lastUsedDay.TryGetValue((playerId, action), out int last))
                return false;
            return currentDay - last < CooldownDays(action, cfg);
        }

        /// <summary>Days remaining before the action can be used again (0 if available now).</summary>
        public int DaysUntilAvailable(int playerId, SupportAction action, int currentDay, SupportBalance cfg)
        {
            if (!_lastUsedDay.TryGetValue((playerId, action), out int last))
                return 0;
            int remaining = CooldownDays(action, cfg) - (currentDay - last);
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>Stamps an action as used on <paramref name="currentDay"/>, starting its cooldown.</summary>
        public void Record(int playerId, SupportAction action, int currentDay)
            => _lastUsedDay[(playerId, action)] = currentDay;

        /// <summary>Snapshot for the host to persist (playerId, action int, last-used day).</summary>
        public IEnumerable<(int PlayerId, int Action, int Day)> Export()
        {
            foreach (var kv in _lastUsedDay)
                yield return (kv.Key.PlayerId, (int)kv.Key.Action, kv.Value);
        }

        /// <summary>Rehydrates one stored entry (host load). Unknown action ints are ignored by the cast.</summary>
        public void Import(int playerId, int action, int day)
            => _lastUsedDay[(playerId, (SupportAction)action)] = day;
    }
}
