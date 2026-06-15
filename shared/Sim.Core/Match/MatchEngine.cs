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
            home.Validate();
            away.Validate();

            TeamRatings homeRatings = TeamRatings.From(home).Scaled((100 + _cfg.HomeAdvantagePercent) / 100.0);
            TeamRatings awayRatings = TeamRatings.From(away);

            if (tactics != null)
            {
                TacticInstructions hi = tactics.Home.Tactic.Instructions;
                TacticInstructions ai = tactics.Away.Tactic.Instructions;
                TacticModifiers.Multipliers hm = TacticModifiers.Compute(hi, ai, tactics.Home.Familiarity, _tactics);
                TacticModifiers.Multipliers am = TacticModifiers.Compute(ai, hi, tactics.Away.Familiarity, _tactics);
                homeRatings = homeRatings.WithMultipliers(hm.Attack, hm.Midfield, hm.Defense);
                awayRatings = awayRatings.WithMultipliers(am.Attack, am.Midfield, am.Defense);
            }

            double homePossession = Share(homeRatings.Midfield, awayRatings.Midfield, _cfg.PossessionSharpness);

            var report = new MatchReport
            {
                HomeClubId = home.ClubId,
                AwayClubId = away.ClubId
            };

            for (int minute = 1; minute <= MatchMinutes; minute++)
            {
                if (rng.NextDouble() >= _cfg.ActionChancePerMinute) continue;

                bool homeAttacks = rng.NextDouble() < homePossession;
                Lineup attackingLineup = homeAttacks ? home : away;
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
            // identical to engine v1.
            report.Positions = new PositionStreamGenerator(_cfg).Generate(home, away, report, rng);

            return report;
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
