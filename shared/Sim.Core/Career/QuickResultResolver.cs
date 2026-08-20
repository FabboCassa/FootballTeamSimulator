using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

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
        /// </summary>
        public static void Resolve(Fixture fixture, int homeStrength, int awayStrength, ulong worldSeed, BalanceConfig? config = null)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            Pcg32 rng = FixtureRng(worldSeed, fixture.Id);

            double home = homeStrength * (100.0 + cfg.Match.HomeAdvantagePercent) / 100.0
                          + cfg.World.QuickHomeAdvantageStrength;
            double difference = home - awayStrength;

            double homeExpected = Clamp(cfg.World.QuickBaseGoals * (1.0 + difference * cfg.World.QuickStrengthFactor), cfg.World);
            double awayExpected = Clamp(cfg.World.QuickBaseGoals * (1.0 - difference * cfg.World.QuickStrengthFactor), cfg.World);

            fixture.HomeGoals = DrawGoals(homeExpected, cfg.World.QuickGoalTrials, rng);
            fixture.AwayGoals = DrawGoals(awayExpected, cfg.World.QuickGoalTrials, rng);
            fixture.Played = true;
        }

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
