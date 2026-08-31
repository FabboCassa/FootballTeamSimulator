using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// Match engine: minute-by-minute action resolution producing score and
    /// event timeline (task 1.4), plus the replayable top-down position stream
    /// generated from that timeline (tasks 1.5 · 13.1, see Movement.PossessionSimulator).
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
        public const int Version = 3; // v3: possession movement model (13.1); score/event model unchanged from v1.

        private const int MatchMinutes = 90;

        private readonly MatchBalance _cfg;
        private readonly TacticsBalance _tactics;
        private readonly ConditionBalance _condition;
        private readonly PositioningBalance _positioning;
        private readonly bool _applyCondition;
        private readonly bool _applyMatchFatigue;
        private readonly bool _applyPositioning;
        private readonly bool _generatePositions;

        /// <summary>
        /// <paramref name="applyCondition"/> opts the engine into the condition model
        /// (task 4.1): each player's rating is scaled by his form/morale/fitness. It
        /// defaults to false so the standard match path is byte-identical to the
        /// pre-condition engine (existing golden masters and replays are unaffected);
        /// a squad of neutral-condition players is also identical even when it is true.
        ///
        /// <paramref name="applyMatchFatigue"/> opts into within-match fatigue (task 4.2
        /// refinement): each side's rating fades as the match wears on (scaled by its
        /// on-pitch XI's average Stamina), with a small recovery at the half-time break.
        /// Separate flag (defaults false) so it never touches the golden masters or the
        /// flag-off identity, and a neutral-condition squad with only this flag stays
        /// stamina-driven rather than condition-driven.
        /// </summary>
        ///
        /// <paramref name="applyPositioning"/> opts into free positioning (task 6.10):
        /// each side's ratings are nudged by <see cref="Tactics.PositionalTilt"/> from how
        /// far its players sit from their role anchors. Separate flag (defaults false) so
        /// it never touches the golden masters; and a lineup with no custom positions (or
        /// sitting on a clean formation preset) is the identity even when the flag is on.
        ///
        /// <paramref name="generatePositions"/> builds the replayable movement stream (13.1).
        /// It is the last thing a simulation does and every match is handed its own RNG, so
        /// turning it off cannot move a single bit of any result — it only skips work nobody
        /// is going to look at. The headless paths (AI matchdays, the balance harness) pass
        /// false; anything a human can watch or replay leaves it on.
        public MatchEngine(BalanceConfig? config = null, bool applyCondition = false, bool applyMatchFatigue = false, bool applyPositioning = false, bool generatePositions = true)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            _cfg = cfg.Match;
            _tactics = cfg.Tactics;
            _condition = cfg.Condition;
            _positioning = cfg.Positioning;
            _applyCondition = applyCondition;
            _applyMatchFatigue = applyMatchFatigue;
            _applyPositioning = applyPositioning;
            _generatePositions = generatePositions;
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

            // Ratings are recomputed each minute below so within-match fatigue can track
            // the clock. With fatigue off they are minute-independent, so recomputing
            // yields identical values and consumes no RNG -> byte-identical to before.
            TeamRatings homeRatings = default, awayRatings = default;
            double homePossession = 0;

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
                }

                ComputeRatings(active, minute, out homeRatings, out awayRatings, out homePossession);

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

            // Movement stream (1.5 · 13.1): generated after the result so it draws from
            // the RNG *after* every outcome roll - scores/events per seed are identical to
            // engine v1. It is handed the finished report and the possession share the
            // ratings produced, so the side that dominates the result model visibly keeps
            // the ball. Uses the final active lineups; for a match with substitutions the
            // rendered geometry reflects the latest XI (presentation-only, never a result).
            if (_generatePositions)
            {
                int possessionPermille = (int)(homePossession * 1000);
                report.Positions = new PossessionSimulator(_cfg)
                    .Generate(active.Home, active.Away, report, rng, possessionPermille);
            }

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

        /// <summary>Home/away ratings (home advantage + optional tactics + within-match fatigue) and possession share at a given minute.</summary>
        private void ComputeRatings(
            MatchInput input, int minute, out TeamRatings homeRatings, out TeamRatings awayRatings, out double homePossession)
        {
            homeRatings = BaseRatings(input.Home).Scaled((100 + _cfg.HomeAdvantagePercent) / 100.0);
            awayRatings = BaseRatings(input.Away);

            if (input.Tactics != null)
            {
                TacticInstructions hi = input.Tactics.Home.Tactic.Instructions;
                TacticInstructions ai = input.Tactics.Away.Tactic.Instructions;
                TacticModifiers.Multipliers hm = TacticModifiers.Compute(hi, ai, input.Tactics.Home.Familiarity, _tactics);
                TacticModifiers.Multipliers am = TacticModifiers.Compute(ai, hi, input.Tactics.Away.Familiarity, _tactics);
                homeRatings = homeRatings.WithMultipliers(hm.Attack, hm.Midfield, hm.Defense);
                awayRatings = awayRatings.WithMultipliers(am.Attack, am.Midfield, am.Defense);
            }

            if (_applyPositioning)
            {
                // Free-positioning shape tilt (task 6.10). Identity for a clean preset /
                // no custom positions, so this is byte-identical unless a lineup actually
                // carries off-anchor positions. Consumes no RNG.
                TacticModifiers.Multipliers ht = PositionalTilt.Compute(input.Home, _cfg, _positioning);
                TacticModifiers.Multipliers at = PositionalTilt.Compute(input.Away, _cfg, _positioning);
                homeRatings = homeRatings.WithMultipliers(ht.Attack, ht.Midfield, ht.Defense);
                awayRatings = awayRatings.WithMultipliers(at.Attack, at.Midfield, at.Defense);
            }

            if (_applyMatchFatigue)
            {
                homeRatings = homeRatings.Scaled(FatigueFactor(minute, AvgStamina(input.Home)));
                awayRatings = awayRatings.Scaled(FatigueFactor(minute, AvgStamina(input.Away)));
            }

            homePossession = Share(homeRatings.Midfield, awayRatings.Midfield, _cfg.PossessionSharpness);
        }

        /// <summary>
        /// Within-match rating multiplier for a side at <paramref name="minute"/> (task 4.2
        /// refinement). Tiredness builds linearly toward <see cref="ConditionBalance.MatchFatigueAt90Permille"/>
        /// by full time, steps back by <see cref="ConditionBalance.HalfTimeRecoveryPermille"/> at the
        /// break (so the second half restarts fresher than the close of the first), and is
        /// scaled by the side's average stamina: a low-stamina XI fades more, a high-stamina
        /// one barely fades. Both sides tiring equally is scale-invariant, so the effect on a
        /// result is relative — the fresher side gains the edge. Integer math; no RNG.
        /// </summary>
        private double FatigueFactor(int minute, int avgStamina)
        {
            int permille = _condition.MatchFatigueAt90Permille * minute / MatchMinutes;
            if (minute > MatchMinutes / 2) permille -= _condition.HalfTimeRecoveryPermille;
            if (permille < 0) permille = 0;

            int neutral = _condition.StaminaNeutral;
            int scaled = neutral > 0 ? permille * (2 * neutral - avgStamina) / neutral : permille;
            if (scaled < 0) scaled = 0;

            return (1000 - scaled) / 1000.0;
        }

        /// <summary>Average Stamina of the on-pitch XI (so substitutions of fresher legs lift it).</summary>
        private static int AvgStamina(Lineup lineup)
        {
            if (lineup.Slots.Count == 0) return 50;

            int sum = 0;
            foreach (LineupSlot slot in lineup.Slots) sum += slot.Player.Attributes.Stamina;
            return sum / lineup.Slots.Count;
        }

        /// <summary>Lineup ratings, condition-scaled when the engine opts in (else the pre-condition aggregation).</summary>
        private TeamRatings BaseRatings(Lineup lineup) =>
            _applyCondition ? TeamRatings.FromWithCondition(lineup, _condition) : TeamRatings.From(lineup);

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
