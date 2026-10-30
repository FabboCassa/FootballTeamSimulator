using System.Linq;
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
    /// Task 11 of the watchable-match spec (R7-R11): the V11 tuning, guarded by what can be
    /// asserted over a handful of matches. The bands themselves are read by the Explicit harnesses
    /// (<see cref="RealismHarnessTests"/>, <see cref="TacticHarnessTests"/>) and judged with the
    /// user; these are the cheap regressions under them.
    ///
    /// Fragility: the open-goal bounds are counts over twelve matches, set far from the harness
    /// value (about 0.5 open goals a match over 1,000 matches since R10, issue #81; about 6 before)
    /// so that only a real regression trips them; the band sample is flagged where it stands.
    /// </summary>
    [TestFixture]
    public class V11RealismTuningTests
    {
        private const ulong FirstSeed = 3400;
        private const int Matches = 4;
        private const int OpenGoalMatches = 48;
        private const ulong BandSeed = 30_000;   // the realism harness's first seed

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld() => _league = new LeagueGenerator().Generate(new Pcg32(20260611));

        private static BalanceConfig Config() => new BalanceConfig();

        private static MatchEngine Engine(BalanceConfig cfg) =>
            new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true, applyPositioning: true,
                generatePositions: true);

        private static MatchReport Play(BalanceConfig cfg, ulong seed) =>
            Engine(cfg).Simulate(LineupSelector.BestEleven(_league.Clubs[9]),
                LineupSelector.BestEleven(_league.Clubs[10]), new Pcg32(seed));

        private static MatchReport Play(BalanceConfig cfg, MatchTactics tactics, ulong seed) =>
            Engine(cfg).Simulate(LineupSelector.BestEleven(_league.Clubs[9]),
                LineupSelector.BestEleven(_league.Clubs[10]), new Pcg32(seed), tactics);

        private static MatchTactics Tactics(Pressing homePressing, int homeFamiliarity, int familiarityMax)
        {
            var home = new Tactic(Formation.F433,
                new TacticInstructions(Mentality.Balanced, homePressing, Tempo.Normal, Width.Normal));
            return new MatchTactics(new TacticContext(home, homeFamiliarity),
                new TacticContext(Tactic.Neutral, familiarityMax));
        }

        // ------------------------------------------------------------------ R4 / R7 regressions

        [Test]
        public void V11_OverASmallSample_ReadsNearTheR7Bands()
        {
            // STATISTICALLY FRAGILE: twenty-four matches, not the harness's thousand. Each band is
            // widened by its own width on either side (the box-entry floor is exact), so that only a real
            // regression, not the noise of fixed seeds, trips it. Twelve read 3.67 goals a match with
            // the keeper's reaction (#149) while the 2,000-match harness read 2.87.
            const int SampleMatches = 24;
            const double BandSlack = 1.0;
            BalanceConfig cfg = Config();
            Lineup a = LineupSelector.BestEleven(_league.Clubs[9]), b = LineupSelector.BestEleven(_league.Clubs[10]);
            var tally = new RealismTally();
            for (int i = 0; i < SampleMatches; i++)
            {
                MatchReport r = i % 2 == 0
                    ? Engine(cfg).Simulate(a, b, new Pcg32(BandSeed + (ulong)i))
                    : Engine(cfg).Simulate(b, a, new Pcg32(BandSeed + (ulong)i));
                tally.Add(new MatchAnalyzer().Measure(r)!, new RealismAnalyzer(cfg.Match).Measure(r)!, 0);
            }

            TestContext.Out.WriteLine(tally.Format("V11 sample"));
            string[] r7 =
            {
                RealismBands.Goals.Name, RealismBands.Shots.Name, RealismBands.OnTargetPercent.Name,
                RealismBands.Corners.Name, RealismBands.Fouls.Name, RealismBands.BoxEntriesPerSide.Name
            };
            foreach (RealismRow row in tally.Rows())
            {
                if (System.Array.IndexOf(r7, row.Band.Name) < 0) continue;
                double slack = double.IsInfinity(row.Band.Max) ? 0 : (row.Band.Max - row.Band.Min) * BandSlack;
                Assert.That(row.Value, Is.InRange(row.Band.Min - slack, row.Band.Max + slack), row.Band.Name);
            }
        }

        [Test]
        public void V11_OverASmallSample_ReadsNearTheRealMatchR2Bands()
        {
            // STATISTICALLY FRAGILE: sixteen matches, eight per mirror, not the harness's thousand
            // each. Every R2 band the harness gates and #87 reached is widened by its own width on
            // either side, so that only a real regression trips it (before #87's tuning goals read
            // 1.4 and PPDA 3.9).
            const int PerMirror = 8;
            const double BandSlack = 1.0;
            BalanceConfig cfg = Config();
            var realism = new RealismTally();
            var shape = new ShapeMovementTally();
            var shotPass = new ShotPassTally();
            foreach (Formation formation in new[] { Formation.F442, Formation.F433 })
            {
                Lineup a = LineupSelector.BestEleven(_league.Clubs[9], formation);
                Lineup b = LineupSelector.BestEleven(_league.Clubs[10], formation);
                var neutral = new TacticContext(new Tactic(formation), cfg.Tactics.FamiliarityMax);
                var reports = new MatchReport[PerMirror];
                System.Threading.Tasks.Parallel.For(0, PerMirror, i => reports[i] = i % 2 == 0
                    ? Engine(cfg).Simulate(a, b, new Pcg32(BandSeed + (ulong)i), new MatchTactics(neutral, neutral))
                    : Engine(cfg).Simulate(b, a, new Pcg32(BandSeed + (ulong)i), new MatchTactics(neutral, neutral)));
                foreach (MatchReport r in reports)
                {
                    realism.Add(new MatchAnalyzer().Measure(r)!, new RealismAnalyzer(cfg.Match).Measure(r)!, 0);
                    shape.Add(new ShapeMovementAnalyzer().Measure(r)!);
                    shotPass.Add(new ShotPassAnalyzer(cfg.Match).Measure(r)!);
                }
            }

            foreach ((RealismBand band, double value) in RealismHarnessTests.R2Rows(realism, shape, shotPass))
            {
                TestContext.Out.WriteLine($"[V11 R2 sample] {band.Name} {value:F3} (band {band.Describe()})");
                // Unreached rows are exempt only until #120 decides them (see RealismHarnessTests.Unreached).
                if (RealismHarnessTests.ReportOnly.ContainsKey(band.Name) || RealismHarnessTests.Unreached.ContainsKey(band.Name))
                    continue;
                double slack = (band.Max - band.Min) * BandSlack;
                Assert.That(value, Is.InRange(band.Min - slack, band.Max + slack), band.Name);
            }
        }

        [Test]
        public void OpenGoals_AreRareChances_NotEveryTouchNearTheBox()
        {
            // Before the lane needed the mouth in view and nobody a stride from it, the trigger
            // fired 89-353 times a match and every one of them was a forced shot.
            // Since R10 (issue #81) the keeper has to be beaten or out of position too, and the
            // harness reads about 0.5 a match (it was about 6): so the harness's own seeds rather
            // than four from FirstSeed (whose first twelve matches have none), and a floor of 0.1
            // a match — still zero only if the trigger never fires. Forty-eight of them, not twelve:
            // at 0.3-0.4 a match, twelve held one chance or none in about one stream in ten (the
            // R5 ball of issue #147 drew one, then 40 in the next 84).
            var chances = new int[OpenGoalMatches];
            Parallel.For(0, OpenGoalMatches, i =>
                chances[i] = RealismAnalyzer.Analyze(Play(Config(), BandSeed + (ulong)i))!.OpenGoalChances);

            double perMatch = (double)chances.Sum() / OpenGoalMatches;
            TestContext.Out.WriteLine($"[V11 open goals] {perMatch:F2} a match");
            Assert.That(perMatch, Is.InRange(0.1, 12.0));
        }

        [Test]
        public void NobodyStandsOnTheBall()
        {
            // The carry in the last stretch goes to a point 11 m out; a man who reached it and was
            // offered the same carry again stood on the ball there, a third of every match.
            int framesPerSecond = Config().Match.FramesPerMinute / 60;
            int longest = 0;
            long held = 0, frames = 0;
            for (ulong seed = FirstSeed; seed < FirstSeed + Matches; seed++)
            {
                PositionStream s = Play(Config(), seed).Positions!.Unpack();
                int run = 0;
                for (int t = 1; t < s.TickCount; t++)
                {
                    frames++;
                    bool same = s.Owner[t] != PositionStream.NoOwner && s.Owner[t] == s.Owner[t - 1];
                    run = same ? run + 1 : 0;
                    if (run > 10 * framesPerSecond) held++;
                    if (run > longest) longest = run;
                }
            }

            TestContext.Out.WriteLine($"[V11 on the ball] longest {longest / framesPerSecond} s, {100.0 * held / frames:F2}% of frames in spells over 10 s");
            Assert.That(longest, Is.LessThan(60 * framesPerSecond), "nobody keeps it a whole minute");
            Assert.That((double)held / frames, Is.LessThan(0.02), "spells over 10 s are rare");
        }

        // ------------------------------------------------------------------ R8-R10 plumbing

        [Test]
        public void Familiarity_Counts()
        {
            int max = new BalanceConfig().Tactics.FamiliarityMax;
            BalanceConfig cfg = Config();
            ulong known = MatchReportHasher.Hash(Play(cfg, Tactics(Pressing.Medium, max, max), FirstSeed));
            ulong unknown = MatchReportHasher.Hash(Play(cfg, Tactics(Pressing.Medium, 0, max), FirstSeed));
            Assert.That(unknown, Is.Not.EqualTo(known), "a side that does not know its tactic plays below itself");
        }

        [Test]
        public void RoleFit_CountsOnV11_OnlyForMenOutOfTheirRole()
        {
            Lineup natural = Natural(_league.Clubs[9]), other = Natural(_league.Clubs[10]);
            Lineup offRole = EffectLineups.OutOfRole(natural);
            Assert.That(EffectLineups.OutOfRoleCount(offRole), Is.GreaterThan(0));
            ulong Hash(Lineup home, int perStep, int max)
            {
                BalanceConfig cfg = Config();
                cfg.Match.V11OffRolePermillePerStep = perStep;
                cfg.Match.V11OffRoleMaxPermille = max;
                return MatchReportHasher.Hash(Engine(cfg).Simulate(home, other, new Pcg32(FirstSeed)));
            }

            Assert.That(Hash(natural, 0, 0), Is.EqualTo(Hash(natural, 120, 450)), "a man in his own role loses nothing");
            Assert.That(Hash(offRole, 0, 0), Is.Not.EqualTo(Hash(offRole, 120, 450)), "a man out of it plays below himself");
        }

        /// <summary>The best eleven with every man in his own role (it may field one a step off it).</summary>
        private static Lineup Natural(Club club)
        {
            Lineup best = LineupSelector.BestEleven(club);
            var natural = new Lineup { ClubId = best.ClubId };
            foreach (LineupSlot slot in best.Slots)
                natural.Slots.Add(new LineupSlot { Role = slot.Player.Role, Player = slot.Player, Position = slot.Position });
            return natural;
        }

        [Test]
        public void PressingCostsTheLegs_AndMediumIsTheIdentity()
        {
            int max = new BalanceConfig().Tactics.FamiliarityMax;
            ulong Hash(Pressing pressing, int[] fatigue)
            {
                BalanceConfig cfg = Config();
                cfg.Match.V11PressingFatiguePercent = fatigue;
                return MatchReportHasher.Hash(Play(cfg, Tactics(pressing, max, max), FirstSeed));
            }

            int[] free = { 100, 100, 100 }, dear = { 100, 100, 300 };
            Assert.That(Hash(Pressing.High, dear), Is.Not.EqualTo(Hash(Pressing.High, free)),
                "a high press tires the side that plays it");
            Assert.That(Hash(Pressing.Medium, dear), Is.EqualTo(Hash(Pressing.Medium, free)),
                "the middle entry is the identity");
        }

        [Test]
        public void TheRealismAnalyzer_ReadsTheBrainsOpenGoalGeometry()
        {
            // R10 (issue #81) made an open goal rare (about 0.5 a match), so the first of the
            // harness's seeds that has one, rather than FirstSeed's match, which has none.
            var none = new MatchBalance { V11OpenGoalRangeDm = 0 };
            MatchReport? r = null;
            for (ulong seed = BandSeed; seed < BandSeed + OpenGoalMatches && r == null; seed++)
            {
                MatchReport played = Play(Config(), seed);
                if (new RealismAnalyzer().Measure(played)!.OpenGoalChances > 0) r = played;
            }

            Assert.That(r, Is.Not.Null, "an open goal in one of the test seeds");
            Assert.That(new RealismAnalyzer(none).Measure(r!)!.OpenGoalChances, Is.Zero);
        }
    }
}
