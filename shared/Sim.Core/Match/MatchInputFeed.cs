using System.Collections.Generic;
using Sim.Core.Config;

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
    ///
    /// It also keeps the benches' touchline shouts (watchable-match spec R11): a shout arrives on
    /// an input or a rule, is heard (or refused inside the cooldown), and expires by itself. The
    /// bookkeeping is integers only, so a match nobody shouts in is untouched by it.
    /// </summary>
    public sealed class MatchInputFeed
    {
        private readonly IReadOnlyList<MatchInputChange> _changes;
        private readonly IReadOnlyList<MatchRule>? _homeRules;
        private readonly IReadOnlyList<MatchRule>? _awayRules;
        private readonly bool[]? _homeFired;
        private readonly bool[]? _awayFired;
        private readonly int _familiarityMax;
        private readonly MatchBalance _match;
        private readonly TouchlineShouts _shouts;
        private readonly List<ShoutCall> _calls = new List<ShoutCall>();
        private int _index;
        private int _minute;

        public MatchInput Current { get; private set; }

        /// <summary>Every shout heard so far, in the order it was heard.</summary>
        public IReadOnlyList<ShoutCall> Shouts => _calls;

        /// <summary>Whether the last <see cref="Advance"/> heard a shout or let one expire.</summary>
        public bool ShoutsChanged { get; private set; }

        public MatchInputFeed(
            MatchPlan plan,
            IReadOnlyList<MatchRule>? homeRules,
            IReadOnlyList<MatchRule>? awayRules,
            int familiarityMax,
            MatchBalance? match = null)
        {
            Current = plan.Initial;
            _changes = plan.Changes;
            _homeRules = homeRules;
            _awayRules = awayRules;
            _familiarityMax = familiarityMax;
            _homeFired = homeRules != null && homeRules.Count > 0 ? new bool[homeRules.Count] : null;
            _awayFired = awayRules != null && awayRules.Count > 0 ? new bool[awayRules.Count] : null;

            _match = match ?? new MatchBalance();
            ShoutBalance shouts = _match.Shouts ?? new ShoutBalance();
            _shouts = new TouchlineShouts(shouts.DurationMinutes, shouts.CooldownMinutes);
            HearInput(plan.Initial);
        }

        /// <summary>The shout the side is playing to as of the last <see cref="Advance"/>.</summary>
        public TouchlineShout ActiveShout(bool home) => _shouts.Active(home, _minute);

        /// <summary>What the side's active shout does to its movement tactics (the identity with none).</summary>
        public ShoutEffect Effect(bool home)
        {
            TouchlineShout active = ActiveShout(home);
            return active == TouchlineShout.None
                ? ShoutEffect.None
                : ShoutEffect.Of(active, _shouts.Repeats(home), _match);
        }

        /// <summary>
        /// Writes the shouts heard from index <paramref name="from"/> on into the report's
        /// timeline, and returns the new count. A shout at the kickoff is filed under minute 1.
        /// </summary>
        public int WriteShoutEvents(MatchReport report, int from)
        {
            for (int i = from; i < _calls.Count; i++)
            {
                ShoutCall call = _calls[i];
                report.Events.Add(new MatchEvent
                {
                    Minute = call.Minute < 1 ? 1 : (call.Minute > 90 ? 90 : call.Minute),
                    Type = MatchEventType.Shout,
                    ClubId = call.Home ? report.HomeClubId : report.AwayClubId,
                    Shout = call.Shout
                });
            }

            return _calls.Count;
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
            TouchlineShout homeBefore = ActiveShout(true);
            TouchlineShout awayBefore = ActiveShout(false);
            int heardBefore = _calls.Count;
            _minute = minute;

            while (_index < _changes.Count && _changes[_index].FromMinute <= minute)
            {
                Current = _changes[_index].Input;
                HearInput(Current);
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

            ShoutsChanged = _calls.Count != heardBefore
                            || ActiveShout(true) != homeBefore
                            || ActiveShout(false) != awayBefore;
            return changed;
        }

        private void HearInput(MatchInput input)
        {
            Hear(true, input.HomeShout);
            Hear(false, input.AwayShout);
        }

        private void Hear(bool home, TouchlineShout shout)
        {
            if (shout == TouchlineShout.None) return;
            if (_shouts.Call(home, shout, _minute)) _calls.Add(new ShoutCall(_minute, home, shout));
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

                Hear(isHome, rule.Action.Shout);
                if (!rule.Action.ChangesInput) continue;

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
