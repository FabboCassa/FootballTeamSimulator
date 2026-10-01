using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Career
{
    /// <summary>
    /// The cheap match resolver for BACKGROUND leagues (task 11.1): a score, and nothing else.
    ///
    /// A Large world holds well over a thousand clubs. Running the real <see cref="Match.MatchEngine"/>
    /// on all of them would mean thousands of minute-by-minute simulations with position streams every
    /// single matchday — the thing that makes a big database unaffordable on a phone. So the leagues
    /// nobody watches are resolved here instead: two clamped expected-goal figures from the two clubs'
    /// strengths, then a small binomial draw each. Twenty random numbers a match, no lineups, no
    /// events, no scorers.
    ///
    /// Determinism: the score depends only on (world seed, fixture id) — never on the day it is
    /// resolved on or the order leagues are walked in — exactly like the full engine's per-fixture RNG.
    /// The stream is offset from the career one so a background fixture and a career fixture that
    /// happen to share an id never share a draw.
    ///
    /// No Math.Exp anywhere: a binomial with a handful of trials stands in for the Poisson the real
    /// engine's goal distribution approximates, which keeps the whole thing inside the determinism
    /// rules (ARCHITECTURE.md 4.1).
    /// </summary>
    public static class QuickResultResolver
    {
        /// <summary>Odd constants: one lifts the whole background stream off the career one, one decorrelates fixtures.</summary>
        private const ulong BackgroundSeedMix = 0xC2B2AE3D27D4EB4FUL;
        private const ulong FixtureSeedMix = 0x9E3779B97F4A7C15UL;

        /// <summary>The per-fixture RNG of the background world. Public so a test can reproduce a score.</summary>
        public static Pcg32 FixtureRng(ulong worldSeed, int fixtureId)
        {
            ulong seed = (worldSeed ^ BackgroundSeedMix) ^ ((ulong)(uint)fixtureId * FixtureSeedMix);
            return new Pcg32(seed, (ulong)(uint)fixtureId);
        }

        /// <summary>
        /// Plays the fixture and writes the score onto it. <paramref name="homeStrength"/> and
        /// <paramref name="awayStrength"/> are the clubs' generated baselines
        /// (<see cref="Club.Strength"/>), i.e. roughly the overall of an average first-teamer.
        /// Missing instructions are the neutral set, which is the identity.
        /// </summary>
        public static void Resolve(
            Fixture fixture, int homeStrength, int awayStrength, ulong worldSeed, BalanceConfig? config = null,
            TacticInstructions? homeInstructions = null, TacticInstructions? awayInstructions = null)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            Pcg32 rng = FixtureRng(worldSeed, fixture.Id);

            ExpectedGoals(homeStrength, awayStrength, cfg,
                homeInstructions ?? TacticInstructions.Neutral, awayInstructions ?? TacticInstructions.Neutral,
                out double homeExpected, out double awayExpected);

            int trials = cfg.World.QuickV11GoalTrials;
            fixture.HomeGoals = DrawGoals(homeExpected, trials, rng);
            fixture.AwayGoals = DrawGoals(awayExpected, trials, rng);
            fixture.Played = true;
        }

        /// <summary>
        /// The two clamped expected-goal figures the score is drawn from: strengths through the
        /// V11 calibration, then each side's instruction percentages (its own
        /// GoalsFor plus the opponent's GoalsAgainst).
        /// </summary>
        public static void ExpectedGoals(
            int homeStrength, int awayStrength, BalanceConfig cfg, TacticInstructions home, TacticInstructions away,
            out double homeExpected, out double awayExpected)
        {
            WorldBalance w = cfg.World;
            double baseGoals = w.QuickV11BaseGoals;
            double factor = w.QuickV11StrengthFactor;
            int homeBonus = w.QuickV11HomeAdvantageStrength;

            double homeStrengthNow = homeStrength * (100.0 + cfg.Match.HomeAdvantagePercent) / 100.0 + homeBonus;
            double difference = homeStrengthNow - awayStrength;

            // percent / 100.0 is exactly 1.0 for neutral sides, so they read the calibration alone.
            homeExpected = Clamp(baseGoals * (1.0 + difference * factor) * (InstructionPercent(home, away, w) / 100.0), w);
            awayExpected = Clamp(baseGoals * (1.0 - difference * factor) * (InstructionPercent(away, home, w) / 100.0), w);
        }

        private static int InstructionPercent(TacticInstructions own, TacticInstructions opponent, WorldBalance w) =>
            100
            + MovementTactics.Pick(w.QuickMentalityGoalsForPercent, (int)own.Mentality)
            + MovementTactics.Pick(w.QuickPressingGoalsForPercent, (int)own.Pressing)
            + MovementTactics.Pick(w.QuickTempoGoalsForPercent, (int)own.Tempo)
            + MovementTactics.Pick(w.QuickWidthGoalsForPercent, (int)own.Width)
            + MovementTactics.Pick(w.QuickMentalityGoalsAgainstPercent, (int)opponent.Mentality)
            + MovementTactics.Pick(w.QuickPressingGoalsAgainstPercent, (int)opponent.Pressing)
            + MovementTactics.Pick(w.QuickTempoGoalsAgainstPercent, (int)opponent.Tempo)
            + MovementTactics.Pick(w.QuickWidthGoalsAgainstPercent, (int)opponent.Width);

        private static double Clamp(double expected, WorldBalance cfg)
        {
            if (expected < cfg.QuickMinExpectedGoals) return cfg.QuickMinExpectedGoals;
            if (expected > cfg.QuickMaxExpectedGoals) return cfg.QuickMaxExpectedGoals;
            return expected;
        }

        private static int DrawGoals(double expected, int trials, IRandomSource rng)
        {
            if (trials < 1)
                trials = 1;

            double probability = expected / trials;
            int goals = 0;

            for (int i = 0; i < trials; i++)
            {
                if (rng.NextDouble() < probability)
                    goals++;
            }

            return goals;
        }
    }
}
