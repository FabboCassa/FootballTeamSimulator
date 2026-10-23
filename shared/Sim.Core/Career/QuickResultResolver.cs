using Sim.Core.Config;
using Sim.Core.Domain;
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
        /// Missing instructions are the neutral set, which is the identity; instructions alone play in 4-3-3.
        /// </summary>
        public static void Resolve(
            Fixture fixture, int homeStrength, int awayStrength, ulong worldSeed, BalanceConfig? config = null,
            TacticInstructions? homeInstructions = null, TacticInstructions? awayInstructions = null) =>
            Resolve(fixture, homeStrength, awayStrength, worldSeed, config,
                InFourThreeThree(homeInstructions), InFourThreeThree(awayInstructions));

        /// <summary>Plays the fixture with each side's whole tactic: its style's instructions and its formation.</summary>
        public static void Resolve(
            Fixture fixture, int homeStrength, int awayStrength, ulong worldSeed, BalanceConfig? config,
            Tactic homeTactic, Tactic awayTactic)
        {
            BalanceConfig cfg = config ?? new BalanceConfig();
            Resolve(fixture, QuickSide.Of(homeStrength, homeTactic, cfg.World), QuickSide.Of(awayStrength, awayTactic, cfg.World),
                worldSeed, cfg);
        }

        /// <summary>Plays the fixture between two sides already read (<see cref="QuickSide"/>).</summary>
        internal static void Resolve(Fixture fixture, QuickSide home, QuickSide away, ulong worldSeed, BalanceConfig cfg)
        {
            Pcg32 rng = FixtureRng(worldSeed, fixture.Id);

            ExpectedGoals(home, away, cfg, out double homeExpected, out double awayExpected);

            int trials = cfg.World.QuickV12GoalTrials;
            fixture.HomeGoals = DrawGoals(homeExpected, trials, rng);
            fixture.AwayGoals = DrawGoals(awayExpected, trials, rng);
            fixture.Played = true;
        }

        /// <summary>
        /// The two clamped expected-goal figures the score is drawn from: strengths through the
        /// engine v12 calibration, then each side's tactic percentage (<see cref="QuickTacticPercent"/>).
        /// Instructions alone play in 4-3-3, the identity shape.
        /// </summary>
        public static void ExpectedGoals(
            int homeStrength, int awayStrength, BalanceConfig cfg, TacticInstructions home, TacticInstructions away,
            out double homeExpected, out double awayExpected) =>
            ExpectedGoals(homeStrength, awayStrength, cfg, new Tactic(Formation.F433, home), new Tactic(Formation.F433, away),
                out homeExpected, out awayExpected);

        public static void ExpectedGoals(
            int homeStrength, int awayStrength, BalanceConfig cfg, Tactic home, Tactic away,
            out double homeExpected, out double awayExpected) =>
            ExpectedGoals(QuickSide.Of(homeStrength, home, cfg.World), QuickSide.Of(awayStrength, away, cfg.World), cfg,
                out homeExpected, out awayExpected);

        private static void ExpectedGoals(QuickSide home, QuickSide away, BalanceConfig cfg,
            out double homeExpected, out double awayExpected)
        {
            WorldBalance w = cfg.World;
            double baseGoals = w.QuickV12BaseGoals;
            double factor = w.QuickV12StrengthFactor;
            int homeBonus = w.QuickV12HomeAdvantageStrength;

            double homeStrengthNow = home.Strength * (100.0 + cfg.Match.HomeAdvantagePercent) / 100.0 + homeBonus;
            double difference = homeStrengthNow - away.Strength;

            // percent / 100.0 is exactly 1.0 for neutral sides, so they read the calibration alone.
            homeExpected = Clamp(baseGoals * (1.0 + difference * factor) * ((100 + home.GoalsFor + away.GoalsAgainst) / 100.0), w);
            awayExpected = Clamp(baseGoals * (1.0 - difference * factor) * ((100 + away.GoalsFor + home.GoalsAgainst) / 100.0), w);
        }

        private static Tactic InFourThreeThree(TacticInstructions? instructions) =>
            new Tactic(Formation.F433, instructions ?? TacticInstructions.Neutral);

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

    /// <summary>
    /// One side as the fast model reads it: its strength and the two percent readings of its tactic
    /// (<see cref="QuickTacticPercent.Readings"/>).
    /// </summary>
    internal readonly struct QuickSide
    {
        public readonly int Strength;
        public readonly int GoalsFor;
        public readonly int GoalsAgainst;

        private QuickSide(int strength, int goalsFor, int goalsAgainst)
        {
            Strength = strength;
            GoalsFor = goalsFor;
            GoalsAgainst = goalsAgainst;
        }

        public static QuickSide Of(int strength, in Tactic tactic, WorldBalance w)
        {
            QuickTacticPercent.Readings(tactic, w, out int goalsFor, out int goalsAgainst);
            return new QuickSide(strength, goalsFor, goalsAgainst);
        }
    }
}
