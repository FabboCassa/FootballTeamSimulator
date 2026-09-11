using System.Collections.Generic;

namespace Sim.Core.Match
{
    /// <summary>
    /// The bench, asked one minute at a time (engine phase 6).
    ///
    /// A match is a schedule of inputs — the kickoff eleven plus the substitutions and tactical
    /// changes due at given minutes (task 3.4), plus the conditional plans that fire off the
    /// scoreline (task 3.5). Until the causality was inverted, only <see cref="MatchEngine"/>'s
    /// minute loop needed to walk that schedule, because it was the only thing that knew the
    /// score. Now the PITCH knows the score, so the schedule had to become something both paths
    /// could walk: this.
    ///
    /// It consumes no randomness — a plan with no changes and no rules never moves a bit — which
    /// is what lets the fast path stay byte-identical to the engine that came before it.
    /// </summary>
    public sealed class MatchInputFeed
    {
        private readonly IReadOnlyList<MatchInputChange> _changes;
        private readonly IReadOnlyList<MatchRule>? _homeRules;
        private readonly IReadOnlyList<MatchRule>? _awayRules;
        private readonly bool[]? _homeFired;
        private readonly bool[]? _awayFired;
        private readonly int _familiarityMax;
        private int _index;

        public MatchInput Current { get; private set; }

        public MatchInputFeed(
            MatchPlan plan,
            IReadOnlyList<MatchRule>? homeRules,
            IReadOnlyList<MatchRule>? awayRules,
            int familiarityMax)
        {
            Current = plan.Initial;
            _changes = plan.Changes;
            _homeRules = homeRules;
            _awayRules = awayRules;
            _familiarityMax = familiarityMax;
            _homeFired = homeRules != null && homeRules.Count > 0 ? new bool[homeRules.Count] : null;
            _awayFired = awayRules != null && awayRules.Count > 0 ? new bool[awayRules.Count] : null;
        }

        /// <summary>
        /// Brings the input up to <paramref name="minute"/> against the score so far, and says
        /// whether anything changed. Scheduled changes are applied first, in order, then the
        /// conditional rules — home's read (own, opponent) goals and away's the mirror — exactly
        /// as the pre-phase-6 minute loop did them, so the fast path is unmoved.
        /// </summary>
        public bool Advance(int minute, int homeGoals, int awayGoals)
        {
            bool changed = false;

            while (_index < _changes.Count && _changes[_index].FromMinute <= minute)
            {
                Current = _changes[_index].Input;
                _index++;
                changed = true;
            }

            if (_homeFired != null)
                changed |= FireRules(_homeRules!, _homeFired, minute, homeGoals, awayGoals, true);
            if (_awayFired != null)
                changed |= FireRules(_awayRules!, _awayFired, minute, awayGoals, homeGoals, false);

            if (changed)
            {
                Current.Home.Validate();
                Current.Away.Validate();
            }

            return changed;
        }

        /// <summary>
        /// Fires every not-yet-fired rule whose minute and scoreline gates are met this minute.
        /// Rules are evaluated in list order, so a later rule sees the input as a same-minute
        /// earlier rule left it (author rules in priority order). A rule that resolves to an
        /// invalid input (e.g. a substitution that would duplicate a player) is skipped but still
        /// marked spent — the silent fallback used elsewhere. Consumes no randomness.
        /// </summary>
        private bool FireRules(
            IReadOnlyList<MatchRule> rules, bool[] fired, int minute,
            int ownGoals, int opponentGoals, bool isHome)
        {
            bool changed = false;
            for (int i = 0; i < rules.Count; i++)
            {
                if (fired[i]) continue;

                MatchRule rule = rules[i];
                if (minute < rule.FromMinute) continue;
                if (rule.Action.IsEmpty || !rule.ConditionMet(ownGoals, opponentGoals)) continue;

                fired[i] = true; // spent once its gates open, applies cleanly or not

                MatchInput candidate = MatchRuleApplier.Apply(Current, rule, isHome, _familiarityMax);
                try
                {
                    candidate.Home.Validate();
                    candidate.Away.Validate();
                }
                catch (System.InvalidOperationException)
                {
                    continue; // illegal change -> ignore silently, keep the prior input
                }

                Current = candidate;
                changed = true;
            }

            return changed;
        }
    }
}
