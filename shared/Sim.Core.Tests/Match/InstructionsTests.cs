using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// THE INSTRUCTIONS COUNT (engine phase 8 — docs/engine/MATCH_ENGINE_PLAN.md §4).
    ///
    /// The tactics screen has exposed four axes since task 3.2 and they have moved the RESULT
    /// through <see cref="TacticModifiers"/> ever since. Phase 8 is the phase in which they move
    /// the PICTURE — and since phase 6 inverted the causality, moving the picture is moving the
    /// match. The plan states the acceptance in one line: opposed tactics must produce measurable
    /// differences, and if they do not, the tactic does not count and the phase is not finished.
    ///
    /// Three claims are pinned here.
    ///
    /// 1. THE NEUTRAL INSTRUCTION IS THE IDENTITY. A match played with no tactics at all and the
    ///    same match played with the all-neutral tactic hash the same, bit for bit. That is not a
    ///    curiosity: it is the safety argument of the whole phase. Everything phase 8 adds is
    ///    expressed as a deviation from the middle entry of a config table, the middle entry reads
    ///    100 (or 0), and every site spends it as <c>x * 100 / 100</c> or <c>x + 0</c>. So the
    ///    twenty readings of the `pitch` scenario — which runs at neutral tactics — cannot move,
    ///    the balance harness cannot move, and phase 6's calibration argument stands untouched.
    ///
    /// 2. EACH AXIS MOVES ITS OWN READING. One axis at a time, the same fixtures, the home side
    ///    carrying the instruction and the away side neutral: how high the block stood, where the
    ///    ball was won back, how quickly it was moved, how much of it went down the touchline.
    ///
    /// 3. THE SHOT IS A DECISION THE COACH INFLUENCES. Phase 6 measured every shot coming from
    ///    inside the box and wrote down that "have a go" is an instruction rather than one more
    ///    constant. It is Mentality and Tempo together, and an attacking, fast side shoots more.
    ///
    /// A WORD ON FRAGILITY, because it is owed. These are measurements over simulated matches,
    /// not arithmetic identities. Each one asserts only the EXTREMES of its axis, with a margin,
    /// over several seeds — the same discipline `[press]` has followed since phase 3 — and prints
    /// the middle setting for the eye. The numbers to judge are in the printed lines, and the
    /// harness's `instructions` scenario measures the same thing over far more matches.
    /// </summary>
    [TestFixture]
    public class InstructionsTests
    {
        private const int Seeds = 8;
        private const ulong FirstSeed = 6100;

        private static League _league = null!;
        private static Club _home = null!;
        private static Club _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _home = _league.Clubs[9];
            _away = _league.Clubs[10];
        }

        // ------------------------------------------------------------------ the identity

        [Test]
        public void Neutral_Instructions_AreTheIdentity()
        {
            var cfg = new BalanceConfig();
            var engine = new MatchEngine();

            for (ulong seed = FirstSeed; seed < FirstSeed + 4; seed++)
            {
                MatchReport bare = engine.Simulate(
                    LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away),
                    new Pcg32(seed));

                MatchReport neutral = engine.Simulate(
                    LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away),
                    new Pcg32(seed),
                    new MatchTactics(
                        TacticContext.Neutral(cfg.Tactics.FamiliarityMax),
                        TacticContext.Neutral(cfg.Tactics.FamiliarityMax)));

                Assert.That(MatchReportHasher.Hash(neutral), Is.EqualTo(MatchReportHasher.Hash(bare)),
                    "a neutral instruction must be the identity: it is what keeps the pitch " +
                    "readings, the golden master and the whole of phase 6's calibration still");
            }
        }

        [Test]
        public void TheSameInstruction_PlaysTheSameMatch()
        {
            TacticInstructions loud = new TacticInstructions(
                Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide);

            MatchReport a = Play(FirstSeed, loud);
            MatchReport b = Play(FirstSeed, loud);

            Assert.That(MatchReportHasher.Hash(b), Is.EqualTo(MatchReportHasher.Hash(a)),
                "the instructions consume no randomness of their own");
        }

        // ------------------------------------------------------------------ one axis at a time

        [Test]
        public void Mentality_MovesTheLineTheSideHolds()
        {
            double low = Average(Mentality.Defensive, s => s.BlockHeightM);
            double mid = Average(Mentality.Balanced, s => s.BlockHeightM);
            double high = Average(Mentality.Attacking, s => s.BlockHeightM);

            TestContext.Out.WriteLine(
                $"[instructions-line] how high the block stood without the ball: " +
                $"defensive {low:F1} m   balanced {mid:F1} m   attacking {high:F1} m");

            Assert.That(high, Is.GreaterThan(low + 3.0),
                "an attacking side holds a line at least three metres higher than a defensive one");
            Assert.That(mid, Is.InRange(low, high),
                "and the middle setting sits between the two");
        }

        [Test]
        public void Pressing_MovesWhereTheBallIsWonBack()
        {
            double low = Average(Pressing.Low, s => s.RecoveryHeightM);
            double mid = Average(Pressing.Medium, s => s.RecoveryHeightM);
            double high = Average(Pressing.High, s => s.RecoveryHeightM);

            TestContext.Out.WriteLine(
                $"[instructions-press] where the ball was won back, from its own goal: " +
                $"low {low:F1} m   medium {mid:F1} m   high {high:F1} m");

            Assert.That(high, Is.GreaterThan(low + 2.0),
                "a high press wins the ball further up the pitch — the reading the plan asks for by name");
            Assert.That(mid, Is.InRange(low, high),
                "and the middle setting sits between the two");
        }

        [Test]
        public void Tempo_MovesTheBallOnQuicker()
        {
            double slow = Average(Tempo.Slow, s => s.Passes);
            double normal = Average(Tempo.Normal, s => s.Passes);
            double fast = Average(Tempo.Fast, s => s.Passes);

            TestContext.Out.WriteLine(
                $"[instructions-tempo] passes played by the side carrying the instruction: " +
                $"slow {slow:F0}   normal {normal:F0}   fast {fast:F0}");

            Assert.That(fast, Is.GreaterThan(slow + 30.0),
                "a side told to play quickly releases the ball sooner, so it plays more passes");
            Assert.That(normal, Is.InRange(slow, fast),
                "and the middle setting sits between the two");
        }

        [Test]
        public void Width_PutsMoreOfThePlayDownTheTouchline()
        {
            double narrow = Average(Width.Narrow, s => s.Crosses);
            double normal = Average(Width.Normal, s => s.Crosses);
            double wide = Average(Width.Wide, s => s.Crosses);

            TestContext.Out.WriteLine(
                $"[instructions-width] crosses played by the side carrying the instruction: " +
                $"narrow {narrow:F1}   normal {normal:F1}   wide {wide:F1}");

            Assert.That(wide, Is.GreaterThan(narrow + 1.0),
                "a wide side finds the man on the touchline more often — and a cross in this " +
                "engine IS a pass from out there near the goal");
            Assert.That(normal, Is.InRange(narrow, wide),
                "and the middle setting sits between the two");
        }

        // ------------------------------------------------------------------ the shot

        [Test]
        public void HaveAGo_IsAnInstruction_AndItIsMentalityAndTempoTogether()
        {
            Reading patient = Measure(new TacticInstructions(
                Mentality.Defensive, Pressing.Medium, Tempo.Slow, Width.Normal));
            Reading neutral = Measure(TacticInstructions.Neutral);
            Reading eager = Measure(new TacticInstructions(
                Mentality.Attacking, Pressing.Medium, Tempo.Fast, Width.Normal));

            TestContext.Out.WriteLine(
                $"[instructions-shots] shots a match (in the box / edge / long range): " +
                $"patient {patient.Shots:F1} ({patient.ShotsInBox:F1}/{patient.ShotsEdge:F1}/{patient.ShotsLong:F1})   " +
                $"neutral {neutral.Shots:F1} ({neutral.ShotsInBox:F1}/{neutral.ShotsEdge:F1}/{neutral.ShotsLong:F1})   " +
                $"eager {eager.Shots:F1} ({eager.ShotsInBox:F1}/{eager.ShotsEdge:F1}/{eager.ShotsLong:F1})");

            Assert.That(eager.Shots, Is.GreaterThan(patient.Shots + 1.0),
                "a side told to attack and to play quickly has a go more often");

            // NOT a strict inequality, and the reason is written down: phase 6 measured every
            // strike coming from inside the box, so both sides of this comparison can legitimately
            // read zero from outside it. What must never happen is the eager side attempting
            // FEWER of them — and how far the appetite actually pushes the threshold out is the
            // number to read off the line above, not to assert blind.
            Assert.That(eager.ShotsEdge + eager.ShotsLong,
                Is.GreaterThanOrEqualTo(patient.ShotsEdge + patient.ShotsLong),
                "and the ones he takes from further out are not fewer");
        }

        // ------------------------------------------------------------------ the bench

        private static MatchReport Play(ulong seed, TacticInstructions instructions)
        {
            var cfg = new BalanceConfig();
            return new MatchEngine().Simulate(
                LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away),
                new Pcg32(seed),
                new MatchTactics(
                    new TacticContext(new Tactic(Formation.F433, instructions), cfg.Tactics.FamiliarityMax),
                    TacticContext.Neutral(cfg.Tactics.FamiliarityMax)));
        }

        private static double Average(Mentality m, Func<Reading, double> read) =>
            read(Measure(new TacticInstructions(m, Pressing.Medium, Tempo.Normal, Width.Normal)));

        private static double Average(Pressing p, Func<Reading, double> read) =>
            read(Measure(new TacticInstructions(Mentality.Balanced, p, Tempo.Normal, Width.Normal)));

        private static double Average(Tempo t, Func<Reading, double> read) =>
            read(Measure(new TacticInstructions(Mentality.Balanced, Pressing.Medium, t, Width.Normal)));

        private static double Average(Width w, Func<Reading, double> read) =>
            read(Measure(new TacticInstructions(Mentality.Balanced, Pressing.Medium, Tempo.Normal, w)));

        /// <summary>
        /// Plays the same fixtures with one instruction set and reads the home side off the
        /// finished matches. Everything here is READ after the last roll of the dice — the
        /// contract every measuring instrument in this rework has kept since phase 0.
        /// </summary>
        private static Reading Measure(TacticInstructions instructions)
        {
            var analyzer = new MatchAnalyzer();
            var reading = new Reading();

            for (ulong seed = FirstSeed; seed < FirstSeed + Seeds; seed++)
            {
                MatchReport report = Play(seed, instructions);
                MatchMetrics? metrics = analyzer.Measure(report);
                if (metrics == null || report.Positions == null) continue;

                reading.Add(metrics.Home, report);
            }

            return reading;
        }

        /// <summary>What one instruction set produced, averaged over the seeds.</summary>
        private sealed class Reading
        {
            private double _blockHeight, _recoveryHeight, _passes, _crosses;
            private double _shots, _inBox, _edge, _long;
            private int _matches, _heights, _recoveries;

            public double BlockHeightM => _heights == 0 ? 0 : _blockHeight / _heights / 10.0;
            public double RecoveryHeightM => _recoveries == 0 ? 0 : _recoveryHeight / _recoveries / 10.0;
            public double Passes => Avg(_passes);
            public double Crosses => Avg(_crosses);
            public double Shots => Avg(_shots);
            public double ShotsInBox => Avg(_inBox);
            public double ShotsEdge => Avg(_edge);
            public double ShotsLong => Avg(_long);

            public void Add(SideMetrics home, MatchReport report)
            {
                _matches++;
                _passes += home.PassesAttempted;
                _crosses += home.Crosses;
                _shots += home.Shots;
                _inBox += home.ShotsInBox;
                _edge += home.ShotsEdge;
                _long += home.ShotsLong;

                if (report.Stats != null)
                {
                    _blockHeight += report.Stats.Home.DefendingHeightDm;
                    _heights++;
                }

                PositionStream stream = report.Positions!;
                foreach (BallAction a in stream.Actions)
                {
                    if (!a.Home) continue;
                    if (a.Kind != BallActionKind.Recovery && a.Kind != BallActionKind.Interception) continue;

                    _recoveryHeight += stream.BallAt(a.Tick).X;   // the home side attacks the far goal
                    _recoveries++;
                }
            }

            private double Avg(double total) => _matches == 0 ? 0 : total / _matches;
        }
    }
}
