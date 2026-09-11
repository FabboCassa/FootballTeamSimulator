using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Match
{
    /// <summary>
    /// Match engine — TWO PATHS since the engine rework's phase 6, and which one a fixture takes
    /// is decided by one question: is anybody going to look at it?
    ///
    ///   - <b>watched</b> (the stream is on): the match is PLAYED, by
    ///     <see cref="Movement.MatchSimulator"/> — twenty-two agents, a ball, a referee — and the
    ///     score, the events and the picture all come out of that one simulation. It is the
    ///     truth, and it costs about half a second.
    ///   - <b>the world</b> (the stream is off): the minute model below, which is what this class
    ///     has always been. Hundreds of AI fixtures a matchday at a millisecond each, and the
    ///     league tables stay plausible because it is calibrated against the full engine's output
    ///     rather than against nothing.
    ///
    /// Before phase 6 there was only the minute model, and the movement was a re-enactment of a
    /// result it had already decided (see the deleted MatchDirector). That is the defect the
    /// whole rework exists to remove: what you watch is now what happened.
    ///
    /// Model per minute (the fast path):
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
        // v4: the engine rework's phase 1 — the simulation runs at 10 Hz on real units (players
        // 5.5-8.5 m/s, a pass up to 26 m/s, a ball with friction written per second) and the
        // position stream is written at 2 Hz.
        // v5: phase 2 — the team is a BLOCK. The formation is laid out by line instead of by
        // role, and where a man stands comes from his line's height, the block's width and its
        // capped slide toward the ball, not from a permille table plus an uncapped lerp at the
        // ball. The SCORE and EVENT model is still unchanged from v1; what moved, again, is the
        // picture, and it moved enough that a v4 replay cannot be rendered by a v5 client —
        // which is exactly what this number is for.
        // v6: phase 3 — the team DEFENDS. One duty per man off the ball (go to the ball, cover the
        // man who does, pick up a genuinely dangerous opponent, hold your place in the block).
        // v7: phase 4 — the man on the ball DECIDES, and decides with his attributes: options
        // weighed in one currency, execution error on every struck ball, the challenge as a duel.
        // v8: phase 5 — there is a REFEREE. A ball at a carrier's feet is out when it crosses a
        // line (sub-tick crossing point), there is an offside line and a flag, a challenge can be
        // mistimed into a foul with a card and a penalty, a shot can be blocked, a defender can
        // put it behind, and the second half is kicked off by the other side.
        // v9: phase 6 — THE CAUSALITY IS INVERTED. A match somebody watches is decided ON THE
        // PITCH: a man weighs the shot against his other options, strikes it as well as his
        // Shooting and Technique let him, and the ball, the bodies in front of it and the
        // keeper's dive settle it. The director and its Chance* super-powers are gone. The minute
        // model below survives as the FAST PATH for the world nobody watches, unchanged to the
        // bit — which is why every league table, every calibration and every balance check reads
        // exactly what it read on v8.
        public const int Version = 9;

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
            plan.Initial.Home.Validate();
            plan.Initial.Away.Validate();

            var report = new MatchReport
            {
                HomeClubId = plan.Initial.Home.ClubId,
                AwayClubId = plan.Initial.Away.ClubId
            };

            var feed = new MatchInputFeed(plan, homeRules, awayRules, _tactics.FamiliarityMax);

            // ENGINE PHASE 6. A match with a stream is a match somebody will watch, so it is
            // PLAYED and the pitch writes the report. Everything below this line is the fast
            // path, and it is exactly the engine that existed before the inversion.
            if (_generatePositions)
            {
                report.Positions = new MatchSimulator(_cfg, _condition, _applyCondition, _applyMatchFatigue)
                    .Generate(feed, report, rng);
                return report;
            }

            return SimulateFast(feed, report, rng);
        }

        /// <summary>
        /// The minute model: an action with P = ActionChancePerMinute, the attacking side drawn
        /// from possession, the goal drawn against attack/(attack+defence). Unchanged, to the
        /// bit, from the engine of every phase before this one — it is the world's path now, not
        /// the player's, and its calibrations are the ones the balance harness holds.
        /// </summary>
        private MatchReport SimulateFast(MatchInputFeed feed, MatchReport report, IRandomSource rng)
        {
            MatchInput active = feed.Current;

            // Ratings are recomputed each minute below so within-match fatigue can track
            // the clock. With fatigue off they are minute-independent, so recomputing
            // yields identical values and consumes no RNG -> byte-identical to before.
            TeamRatings homeRatings = default, awayRatings = default;
            double homePossession = 0;

            for (int minute = 1; minute <= MatchMinutes; minute++)
            {
                // Apply every change effective by this minute, before any draw, so the prefix is
                // identical and the change first bites at FromMinute; then the conditional rules,
                // against the score from minutes < this one. Neither touches the RNG, so a plan
                // with no changes and no rules consumes draws in exactly the order engine v1 did.
                if (feed.Advance(minute, report.HomeGoals, report.AwayGoals)) active = feed.Current;

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

            return report;
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
