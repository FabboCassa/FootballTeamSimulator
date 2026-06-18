using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// Match engine: minute-by-minute action resolution producing score and
    /// event timeline (task 1.4), plus the replayable top-down position stream
    /// generated from that timeline (task 1.5, see PositionStreamGenerator).
    ///
    /// Model per minute:
    ///   1. an action happens with P = ActionChancePerMinute;
    ///   2. the attacking side is drawn from possession (midfield-driven);
    ///   3. the action becomes a goal with P = GoalCoefficient * r^ChanceSharpness,
    ///      where r = attack / (attack + opposing defense);
    ///   4. failed chances split into saved/missed (cosmetic, for the timeline).
    ///
    /// Deterministic: only +,-,*,/ on doubles and integer ops - no transcendental
    /// functions, so results are bit-identical across .NET / Mono / IL2CPP.
    /// </summary>
    public sealed class MatchEngine
    {
        /// <summary>Bump when changes invalidate stored replays/golden masters.</summary>
        public const int Version = 2; // v2: position stream added (1.5); score/event model unchanged from v1.

        private const int MatchMinutes = 90;

        private readonly MatchBalance _cfg;
        private readonly TacticsBalance _tactics;

        public MatchEngine(BalanceConfig? config = null)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            _cfg = cfg.Match;
            _tactics = cfg.Tactics;
        }

        /// <summary>
        /// Simulates a match. When <paramref name="tactics"/> is null the engine
        /// runs exactly as before the tactics system (task 3.2): same score/events
        /// per seed, so existing golden masters and replays are unaffected.
        /// </summary>
        public MatchReport Simulate(Lineup home, Lineup away, IRandomSource rng, MatchTactics? tactics = null)
        {
            return Simulate(new MatchPlan(new MatchInput(home, away, tactics)), rng);
        }

        /// <summary>
        /// Simulates a match from a <see cref="MatchPlan"/>: the kickoff input plus
        /// any scheduled input changes (substitutions, tactic changes) injected at
        /// given minutes (task 3.4). A plan with no changes consumes the RNG in the
        /// exact same order as the single-input <see cref="Simulate(Lineup,Lineup,IRandomSource,MatchTactics?)"/>
        /// overload, so existing golden masters/replays are unaffected.
        ///
        /// Determinism: minutes before a change use the unchanged input, so they
        /// reproduce byte-for-byte; only the remainder diverges. Re-running the
        /// same plan with the same seed reproduces the whole report.
        /// </summary>
        public MatchReport Simulate(MatchPlan plan, IRandomSource rng) => Simulate(plan, null, null, rng);

        /// <summary>
        /// Simulates a match with conditional pre-match plans (task 3.5):
        /// <paramref name="homeRules"/>/<paramref name="awayRules"/> are evaluated at
        /// each minute boundary against the live score and fire at most once,
        /// injecting an input change (substitution / tactic change) exactly as a
        /// scheduled <see cref="MatchInputChange"/> would. This is the AI-fallback /
        /// "skipped match" execution path and the foundation for online delegation.
        ///
        /// Determinism: rule evaluation consumes no randomness, so when both rule
        /// lists are null/empty (or simply never fire) the RNG is consumed in the
        /// exact same order as <see cref="Simulate(MatchPlan,IRandomSource)"/> —
        /// the result is byte-identical and existing golden masters/replays hold.
        /// A rule that fires leaves the prefix identical and only re-rolls the
        /// remainder, just like a static change.
        /// </summary>
        public MatchReport Simulate(
            MatchPlan plan,
            IReadOnlyList<MatchRule>? homeRules,
            IReadOnlyList<MatchRule>? awayRules,
            IRandomSource rng)
        {
            MatchInput active = plan.Initial;
            active.Home.Validate();
            active.Away.Validate();

            ComputeRatings(active, out TeamRatings homeRatings, out TeamRatings awayRatings, out double homePossession);

            var report = new MatchReport
            {
                HomeClubId = active.Home.ClubId,
                AwayClubId = active.Away.ClubId
            };

            bool[]? homeFired = homeRules != null && homeRules.Count > 0 ? new bool[homeRules.Count] : null;
            bool[]? awayFired = awayRules != null && awayRules.Count > 0 ? new bool[awayRules.Count] : null;

            int changeIndex = 0;
            for (int minute = 1; minute <= MatchMinutes; minute++)
            {
                // Apply every change effective by this minute, before any draw, so
                // the prefix is identical and the change first bites at FromMinute.
                bool changed = false;
                while (changeIndex < plan.Changes.Count && plan.Changes[changeIndex].FromMinute <= minute)
                {
                    active = plan.Changes[changeIndex].Input;
                    changeIndex++;
                    changed = true;
                }

                // Conditional rules: evaluate against the score so far (goals from
                // minutes < this one). Home rules read (home, away) goals; away rules
                // the mirror. No RNG is touched here, so a quiet ruleset is identity.
                if (homeFired != null)
                    changed |= FireRules(homeRules!, homeFired, minute, report.HomeGoals, report.AwayGoals, true, ref active);
                if (awayFired != null)
                    changed |= FireRules(awayRules!, awayFired, minute, report.AwayGoals, report.HomeGoals, false, ref active);

                if (changed)
                {
                    active.Home.Validate();
                    active.Away.Validate();
                    ComputeRatings(active, out homeRatings, out awayRatings, out homePossession);
                }

                if (rng.NextDouble() >= _cfg.ActionChancePerMinute) continue;

                bool homeAttacks = rng.NextDouble() < homePossession;
                Lineup attackingLineup = homeAttacks ? active.Home : active.Away;
                TeamRatings att = homeAttacks ? homeRatings : awayRatings;
                TeamRatings def = homeAttacks ? awayRatings : homeRatings;

                double r = att.Attack / (att.Attack + def.Defense);
                double goalProbability = _cfg.GoalCoefficient * IntPow(r, _cfg.ChanceSharpness);

                int shooterId = PickShooter(attackingLineup, rng);
                double roll = rng.NextDouble();

                MatchEventType type;
                if (roll < goalProbability)
                {
                    type = MatchEventType.Goal;
                    if (homeAttacks) report.HomeGoals++; else report.AwayGoals++;
                }
                else
                {
                    type = rng.NextInt(0, 100) < _cfg.SavedShareOfFailedChancesPercent
                        ? MatchEventType.ChanceSaved
                        : MatchEventType.ChanceMissed;
                }

                report.Events.Add(new MatchEvent
                {
                    Minute = minute,
                    Type = type,
                    ClubId = attackingLineup.ClubId,
                    PlayerId = shooterId
                });
            }

            // Position stream (1.5): generated after the result so it draws from
            // the RNG *after* every outcome roll - scores/events per seed are
            // identical to engine v1. Uses the final active lineups; for a match
            // with substitutions the rendered geometry reflects the latest XI
            // (presentation-only, never affects the result).
            report.Positions = new PositionStreamGenerator(_cfg).Generate(active.Home, active.Away, report, rng);

            return report;
        }

        /// <summary>
        /// Fires every not-yet-fired rule whose minute and scoreline gates are met
        /// this minute, mutating <paramref name="active"/>. Rules are evaluated in
        /// list order, so a later rule sees the input as a same-minute earlier rule
        /// left it (author rules in priority order). A rule that resolves to an
        /// invalid input (e.g. a substitution that would duplicate a player) is
        /// skipped but still marked spent — the silent fallback used elsewhere.
        /// Consumes no randomness. Returns whether the active input changed.
        /// </summary>
        private bool FireRules(
            IReadOnlyList<MatchRule> rules, bool[] fired, int minute,
            int ownGoals, int opponentGoals, bool isHome, ref MatchInput active)
        {
            bool changed = false;
            for (int i = 0; i < rules.Count; i++)
            {
                if (fired[i]) continue;

                MatchRule rule = rules[i];
                if (minute < rule.FromMinute) continue;
                if (rule.Action.IsEmpty || !rule.ConditionMet(ownGoals, opponentGoals)) continue;

                fired[i] = true; // spent once its gates open, applies cleanly or not

                MatchInput candidate = MatchRuleApplier.Apply(active, rule, isHome, _tactics.FamiliarityMax);
                try
                {
                    candidate.Home.Validate();
                    candidate.Away.Validate();
                }
                catch (System.InvalidOperationException)
                {
                    continue; // illegal change -> ignore silently, keep the prior input
                }

                active = candidate;
                changed = true;
            }

            return changed;
        }

        /// <summary>Home/away ratings (home advantage + optional tactics) and possession share for an input.</summary>
        private void ComputeRatings(
            MatchInput input, out TeamRatings homeRatings, out TeamRatings awayRatings, out double homePossession)
        {
            homeRatings = TeamRatings.From(input.Home).Scaled((100 + _cfg.HomeAdvantagePercent) / 100.0);
            awayRatings = TeamRatings.From(input.Away);

            if (input.Tactics != null)
            {
                TacticInstructions hi = input.Tactics.Home.Tactic.Instructions;
                TacticInstructions ai = input.Tactics.Away.Tactic.Instructions;
                TacticModifiers.Multipliers hm = TacticModifiers.Compute(hi, ai, input.Tactics.Home.Familiarity, _tactics);
                TacticModifiers.Multipliers am = TacticModifiers.Compute(ai, hi, input.Tactics.Away.Familiarity, _tactics);
                homeRatings = homeRatings.WithMultipliers(hm.Attack, hm.Midfield, hm.Defense);
                awayRatings = awayRatings.WithMultipliers(am.Attack, am.Midfield, am.Defense);
            }

            homePossession = Share(homeRatings.Midfield, awayRatings.Midfield, _cfg.PossessionSharpness);
        }

        /// <summary>Possession share of side A: a^e / (a^e + b^e).</summary>
        private static double Share(double a, double b, int exponent)
        {
            double pa = IntPow(a, exponent);
            double pb = IntPow(b, exponent);
            return pa / (pa + pb);
        }

        /// <summary>Deterministic integer-exponent power (no Math.Pow).</summary>
        private static double IntPow(double value, int exponent)
        {
            double result = 1.0;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }

        private int PickShooter(Lineup lineup, IRandomSource rng)
        {
            int[] weights = _cfg.ScorerWeightsByRole;

            int total = 0;
            foreach (LineupSlot slot in lineup.Slots)
                total += WeightOf(weights, slot.Role);

            int roll = rng.NextInt(0, total > 0 ? total : 1);
            foreach (LineupSlot slot in lineup.Slots)
            {
                roll -= WeightOf(weights, slot.Role);
                if (roll < 0) return slot.Player.Id;
            }

            return lineup.Slots[lineup.Slots.Count - 1].Player.Id;
        }

        private static int WeightOf(int[] weights, PositionRole role)
        {
            int index = (int)role;
            return index >= 0 && index < weights.Length ? weights[index] : 0;
        }
    }
}
