using System;
using System.Threading.Tasks;
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
    /// R16 (issue #90): the five in-possession axes the expanded model added — passing directness,
    /// build-up, final-third approach, shooting and dribbling — take effect. One axis at a time,
    /// the home side carries the instruction and the away side plays neutral; each extreme must
    /// move its own reading away from the neutral match, in its own direction, by a documented
    /// minimum. The neutral reading is the same fixtures with every axis at its middle level,
    /// which is the v12 match bit for bit (<see cref="InstructionsTests"/> pins the identity).
    ///
    /// Statistically sized: each reading is a mean over <see cref="Seeds"/> matches, the minimum
    /// for game-level instruction tests (spec constraints), and each minimum sits at about half
    /// the shift measured when the axis was wired, so seed noise alone cannot pass or fail it.
    /// </summary>
    [TestFixture]
    public class InPossessionInstructionsTests
    {
        private const int Seeds = 64;
        private const ulong FirstSeed = 9000;

        /// <summary>
        /// The shots and crosses axes read a mean over this many matches instead: a count a match
        /// spreads by 4 (shots) to 6 (crosses) between seeds, so over 64 the gap to neutral carried a
        /// standard error of 0.7-1.1 against minimums of 1.2 and 1.0, and every change to the match
        /// flipped them. Over 1,024 seeds engine v12 reads shots 15.8 / 17.4 / 20.2 and crosses
        /// 28.6 / 30.6 / 33.7; the R5 ball of issue #147 reads 15.5 / 17.0 / 19.8 and 29.2 / 30.8 /
        /// 33.4, where its first 64 seeds read gaps of 0.9 for both.
        /// </summary>
        private const int CountSeeds = 1024;

        // Documented minimums: how far each extreme must move its reading from neutral (issue #90).
        // Measured when wired (64 seeds, low / neutral / high): long-ball share 3.6 / 5.6 / 9.5 %;
        // own-third long share 6.8 / 12.7 / 23.9 %; crosses 29.5 / 31.5 / 34.6; shots 15.5 / 17.9 /
        // 20.5; dribbles 113.5 / 126.8 / 139.3. Each minimum is about half the smaller shift.
        private const double DirectnessShareMinPp = 1.0;
        private const double BuildUpShareMinPp = 3.0;
        private const double CrossesMin = 1.0;
        private const double ShotsMin = 1.2;
        private const double DribblesMin = 6.0;

        private static League _league = null!;
        private static Club _home = null!;
        private static Club _away = null!;
        private Reading? _neutral, _neutralForCounts;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _home = _league.Clubs[9];
            _away = _league.Clubs[10];
        }

        private Reading Neutral => _neutral ??= Measure(TacticInstructions.Neutral);
        private Reading NeutralForCounts => _neutralForCounts ??= Measure(TacticInstructions.Neutral, CountSeeds);

        [Test]
        public void PassingDirectness_MovesTheLongBallShare() =>
            AssertShift("directness", "long balls, % of passes", r => r.LongBallSharePercent, DirectnessShareMinPp,
                "short", With(directness: PassingDirectness.Short), "direct", With(directness: PassingDirectness.Direct));

        [Test]
        public void BuildUp_MovesTheLongBallShareFromTheOwnThird() =>
            AssertShift("buildup", "long balls from the own third, % of its passes", r => r.OwnThirdLongSharePercent, BuildUpShareMinPp,
                "play out", With(buildUp: BuildUp.PlayOutFromBack), "go long", With(buildUp: BuildUp.GoalKickLong));

        [Test]
        public void FinalThirdApproach_MovesTheCrosses() =>
            AssertShift("approach", "crosses a match", r => r.Crosses, CrossesMin,
                "work into box", With(approach: FinalThirdApproach.WorkIntoBox),
                "cross early", With(approach: FinalThirdApproach.CrossEarly), CountSeeds);

        [Test]
        public void ShotSelection_MovesTheShots() =>
            AssertShift("shooting", "shots a match", r => r.Shots, ShotsMin,
                "patient", With(shooting: ShotSelection.Patient), "shoot on sight", With(shooting: ShotSelection.ShootOnSight),
                CountSeeds);

        [Test]
        public void DribbleFrequency_MovesTheDribbles() =>
            AssertShift("dribbling", "dribbles a match", r => r.Dribbles, DribblesMin,
                "less", With(dribbling: DribbleFrequency.Less), "more", With(dribbling: DribbleFrequency.More));

        [Test]
        public void TheSameExpandedInstruction_PlaysTheSameMatch()
        {
            TacticInstructions loud = With(PassingDirectness.Direct, BuildUp.GoalKickLong,
                FinalThirdApproach.CrossEarly, ShotSelection.ShootOnSight, DribbleFrequency.More);

            Assert.That(MatchReportHasher.Hash(Play(FirstSeed, loud)), Is.EqualTo(MatchReportHasher.Hash(Play(FirstSeed, loud))),
                "the in-possession axes consume no randomness of their own");
        }

        // ------------------------------------------------------------------ the bench

        /// <summary>
        /// The low extreme must read at least <paramref name="min"/> under neutral and the high one
        /// at least <paramref name="min"/> over it. Goals for-against are printed beside each so the
        /// balance cost of an extreme can be judged.
        /// </summary>
        private void AssertShift(string tag, string metric, Func<Reading, double> read, double min,
            string lowName, TacticInstructions low, string highName, TacticInstructions high, int seeds = Seeds)
        {
            Reading neutral = seeds == Seeds ? Neutral : NeutralForCounts;
            Reading l = Measure(low, seeds);
            Reading h = Measure(high, seeds);

            TestContext.Out.WriteLine(
                $"[instructions-{tag}] {metric}: {lowName} {read(l):F1} ({l.Goals})   " +
                $"neutral {read(neutral):F1} ({neutral.Goals})   {highName} {read(h):F1} ({h.Goals})   " +
                $"(min shift {min})");

            Assert.That(read(h), Is.GreaterThan(read(neutral) + min), $"{highName} must raise {metric} over neutral");
            Assert.That(read(l), Is.LessThan(read(neutral) - min), $"{lowName} must lower {metric} under neutral");
        }

        private static TacticInstructions With(
            PassingDirectness directness = PassingDirectness.Mixed, BuildUp buildUp = BuildUp.Mixed,
            FinalThirdApproach approach = FinalThirdApproach.Mixed, ShotSelection shooting = ShotSelection.Normal,
            DribbleFrequency dribbling = DribbleFrequency.Normal) =>
            new TacticInstructions(
                Mentality.Balanced, directness, Tempo.Normal, Width.Normal,
                buildUp, approach, shooting, dribbling,
                OnBallLost.Normal, OnBallWon.Normal,
                LineOfEngagement.Mid, DefensiveLine.Normal, Pressing.Medium, MarkingScheme.Mixed, PressDirection.None);

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

        /// <summary>Plays the fixtures with one instruction set for the home side and reads the home side.</summary>
        private static Reading Measure(TacticInstructions instructions, int seeds = Seeds)
        {
            // Added as each match ends rather than kept: a thousand streams do not fit in a test run.
            // Every term is a whole count, so the sums do not depend on the order they arrive in.
            var reading = new Reading();
            Parallel.For(0, seeds, i =>
            {
                MatchReport report = Play(FirstSeed + (ulong)i, instructions);
                MatchMetrics? metrics = new MatchAnalyzer().Measure(report);
                if (metrics == null || report.Positions == null) return;
                lock (reading) reading.Add(metrics, report.Positions);
            });

            return reading;
        }

        /// <summary>What one instruction set produced for the home side, over the seeds.</summary>
        private sealed class Reading
        {
            private double _passes, _longBalls, _crosses, _shots, _dribbles, _ownThirdPasses, _ownThirdLong;
            private double _goalsFor, _goalsAgainst;
            private int _matches;

            public double LongBallSharePercent => _passes == 0 ? 0 : 100.0 * _longBalls / _passes;
            public double OwnThirdLongSharePercent => _ownThirdPasses == 0 ? 0 : 100.0 * _ownThirdLong / _ownThirdPasses;
            public double Crosses => Avg(_crosses);
            public double Shots => Avg(_shots);
            public double Dribbles => Avg(_dribbles);

            /// <summary>Goals for and against a match, printed so the balance cost of an extreme can be judged.</summary>
            public string Goals => $"{Avg(_goalsFor):F2}-{Avg(_goalsAgainst):F2}";

            public void Add(MatchMetrics metrics, PositionStream stream)
            {
                SideMetrics home = metrics.Home;
                _matches++;
                _goalsFor += home.Goals;
                _goalsAgainst += metrics.Away.Goals;
                _passes += home.PassesAttempted;
                _longBalls += home.LongBalls;
                _crosses += home.Crosses;
                _shots += home.Shots;
                _dribbles += home.Dribbles;

                // The home side attacks the far goal, so its own third is the first third of X.
                foreach (BallAction a in stream.Actions)
                {
                    if (!a.Home) continue;
                    if (a.Kind != BallActionKind.Pass && a.Kind != BallActionKind.LongBall && a.Kind != BallActionKind.Cross) continue;
                    if (stream.BallAt(a.Tick).X >= Pitch.LengthDm / 3) continue;

                    _ownThirdPasses++;
                    if (a.Kind == BallActionKind.LongBall) _ownThirdLong++;
                }
            }

            private double Avg(double total) => _matches == 0 ? 0 : total / _matches;
        }
    }
}
