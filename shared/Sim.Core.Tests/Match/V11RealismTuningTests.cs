using System.Diagnostics;
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
    /// Task 11 of the watchable-match spec (R7-R11, R19): the V11 tuning, guarded by what can be
    /// asserted over a handful of matches. The bands themselves are read by the Explicit harnesses
    /// (<see cref="RealismHarnessTests"/>, <see cref="TacticHarnessTests"/>) and judged with the
    /// user; these are the cheap regressions under them.
    ///
    /// Fragility: the open-goal and time bounds are counts over four matches, set far from the
    /// harness values (about 6 open goals a match, V11 about 1.2x V10 over 1,000 matches) so
    /// that only a real regression trips them; the band sample is flagged where it stands.
    /// </summary>
    [TestFixture]
    public class V11RealismTuningTests
    {
        private const ulong FirstSeed = 3400;
        private const int Matches = 4;
        private const ulong BandSeed = 30_000;   // the realism harness's first seed

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld() => _league = new LeagueGenerator().Generate(new Pcg32(20260611));

        private static BalanceConfig Config(MatchBrainVersion brain)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = brain;
            return cfg;
        }

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
            // STATISTICALLY FRAGILE: twelve matches, not the harness's thousand. Each band is
            // widened by its own width on either side (the box-entry floor is exact), so that only a real
            // regression, not the noise of twelve fixed seeds, trips it.
            const int SampleMatches = 12;
            const double BandSlack = 1.0;
            BalanceConfig cfg = Config(MatchBrainVersion.V11);
            Lineup a = LineupSelector.BestEleven(_league.Clubs[9]), b = LineupSelector.BestEleven(_league.Clubs[10]);
            var tally = new RealismTally();
            for (int i = 0; i < SampleMatches; i++)
            {
                MatchReport r = i % 2 == 0
                    ? Engine(cfg).Simulate(a, b, new Pcg32(BandSeed + (ulong)i))
                    : Engine(cfg).Simulate(b, a, new Pcg32(BandSeed + (ulong)i));
                tally.Add(new MatchAnalyzer().Measure(r)!, new RealismAnalyzer(cfg.Match).Measure(r)!, 0);
            }

            TestContext.Out.WriteLine(tally.Format("V11 sample", 0));
            string[] r7 =
            {
                RealismBands.Goals.Name, RealismBands.Shots.Name, RealismBands.OnTargetPercent.Name,
                RealismBands.Corners.Name, RealismBands.Fouls.Name, RealismBands.BoxEntriesPerSide.Name
            };
            foreach (RealismRow row in tally.Rows(0))
            {
                if (System.Array.IndexOf(r7, row.Band.Name) < 0) continue;
                double slack = double.IsInfinity(row.Band.Max) ? 0 : (row.Band.Max - row.Band.Min) * BandSlack;
                Assert.That(row.Value, Is.InRange(row.Band.Min - slack, row.Band.Max + slack), row.Band.Name);
            }
        }

        [Test]
        public void OpenGoals_AreRareChances_NotEveryTouchNearTheBox()
        {
            // Before the lane needed the mouth in view and nobody a stride from it, the trigger
            // fired 89-353 times a match and every one of them was a forced shot.
            int chances = 0;
            for (ulong seed = FirstSeed; seed < FirstSeed + Matches; seed++)
                chances += RealismAnalyzer.Analyze(Play(Config(MatchBrainVersion.V11), seed))!.OpenGoalChances;

            double perMatch = (double)chances / Matches;
            TestContext.Out.WriteLine($"[V11 open goals] {perMatch:F1} a match");
            Assert.That(perMatch, Is.InRange(0.5, 12.0));
        }

        [Test]
        public void NobodyStandsOnTheBall()
        {
            // The carry in the last stretch goes to a point 11 m out; a man who reached it and was
            // offered the same carry again stood on the ball there, a third of every match.
            const int FramesPerSecond = 2;
            int longest = 0;
            long held = 0, frames = 0;
            for (ulong seed = FirstSeed; seed < FirstSeed + Matches; seed++)
            {
                PositionStream s = Play(Config(MatchBrainVersion.V11), seed).Positions!.Unpack();
                int run = 0;
                for (int t = 1; t < s.TickCount; t++)
                {
                    frames++;
                    bool same = s.Owner[t] != PositionStream.NoOwner && s.Owner[t] == s.Owner[t - 1];
                    run = same ? run + 1 : 0;
                    if (run > 10 * FramesPerSecond) held++;
                    if (run > longest) longest = run;
                }
            }

            TestContext.Out.WriteLine($"[V11 on the ball] longest {longest / FramesPerSecond} s, {100.0 * held / frames:F2}% of frames in spells over 10 s");
            Assert.That(longest, Is.LessThan(60 * FramesPerSecond), "nobody keeps it a whole minute");
            Assert.That((double)held / frames, Is.LessThan(0.02), "spells over 10 s are rare");
        }

        // ------------------------------------------------------------------ R19

        [Test]
        public void V11_CostsLittleMoreThanV10()
        {
            // The harness gates R19 at +25% over 1,000 matches; this is the coarse guard.
            double v10Ms = Time(MatchBrainVersion.V10), v11Ms = Time(MatchBrainVersion.V11);
            TestContext.Out.WriteLine($"[time] V10 {v10Ms:F0} ms, V11 {v11Ms:F0} ms, ratio {v11Ms / v10Ms:F2}");
            Assert.That(v11Ms / v10Ms, Is.LessThan(2.0));
        }

        private static double Time(MatchBrainVersion brain)
        {
            BalanceConfig cfg = Config(brain);
            Play(cfg, FirstSeed);   // warm-up
            var clock = Stopwatch.StartNew();
            for (ulong seed = FirstSeed; seed < FirstSeed + 3; seed++) Play(cfg, seed);
            return clock.Elapsed.TotalMilliseconds / 3;
        }

        // ------------------------------------------------------------------ R8-R10 plumbing

        [Test]
        public void Familiarity_CountsOnV11_AndV10IsUntouched()
        {
            int max = new BalanceConfig().Tactics.FamiliarityMax;
            foreach (MatchBrainVersion brain in new[] { MatchBrainVersion.V10, MatchBrainVersion.V11 })
            {
                BalanceConfig cfg = Config(brain);
                ulong known = MatchReportHasher.Hash(Play(cfg, Tactics(Pressing.Medium, max, max), FirstSeed));
                ulong unknown = MatchReportHasher.Hash(Play(cfg, Tactics(Pressing.Medium, 0, max), FirstSeed));
                if (brain == MatchBrainVersion.V10) Assert.That(unknown, Is.EqualTo(known), "V10 reads familiarity in the fast model only");
                else Assert.That(unknown, Is.Not.EqualTo(known), "a side that does not know its tactic plays below itself");
            }
        }

        [Test]
        public void RoleFit_CountsOnV11_OnlyForMenOutOfTheirRole()
        {
            Lineup natural = Natural(_league.Clubs[9]), other = Natural(_league.Clubs[10]);
            Lineup offRole = EffectLineups.OutOfRole(natural);
            Assert.That(EffectLineups.OutOfRoleCount(offRole), Is.GreaterThan(0));
            ulong Hash(Lineup home, int perStep, int max)
            {
                BalanceConfig cfg = Config(MatchBrainVersion.V11);
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
        public void PressingCostsTheLegs_OnV11Only_AndMediumIsTheIdentity()
        {
            int max = new BalanceConfig().Tactics.FamiliarityMax;
            ulong Hash(MatchBrainVersion brain, Pressing pressing, int[] fatigue)
            {
                BalanceConfig cfg = Config(brain);
                cfg.Match.V11PressingFatiguePercent = fatigue;
                return MatchReportHasher.Hash(Play(cfg, Tactics(pressing, max, max), FirstSeed));
            }

            int[] free = { 100, 100, 100 }, dear = { 100, 100, 300 };
            Assert.That(Hash(MatchBrainVersion.V11, Pressing.High, dear), Is.Not.EqualTo(Hash(MatchBrainVersion.V11, Pressing.High, free)),
                "a high press tires the side that plays it");
            Assert.That(Hash(MatchBrainVersion.V11, Pressing.Medium, dear), Is.EqualTo(Hash(MatchBrainVersion.V11, Pressing.Medium, free)),
                "the middle entry is the identity");
            Assert.That(Hash(MatchBrainVersion.V10, Pressing.High, dear), Is.EqualTo(Hash(MatchBrainVersion.V10, Pressing.High, free)),
                "V10 has no such cost");
        }

        [Test]
        public void TheV11Knobs_LeaveV10Alone()
        {
            // The golden master is V10's; every V11 lever, pulled to an extreme, must not reach it.
            BalanceConfig plain = Config(MatchBrainVersion.V10), pulled = Config(MatchBrainVersion.V10);
            MatchBalance m = pulled.Match;
            m.V11KeeperStopPercent = 10;
            m.V11KeeperHoldPercent = 10;
            m.V11KeeperParryBehindPercent = 10;
            m.V11DeflectPercent = 10;
            m.V11FoulPercent = 10;
            m.V11ShotSpreadPercent = 10;
            m.V11MentalitySpreadPercent = 300;
            m.V11PressingSpreadPercent = 300;
            m.V11TempoSpreadPercent = 300;
            m.V11WidthSpreadPercent = 300;
            m.V11UnfamiliarPenaltyPermille = 900;
            m.V11OffRolePermillePerStep = 900;
            m.V11MatchFatigueAt90Permille = 900;

            Assert.That(MatchReportHasher.Hash(Play(pulled, FirstSeed)), Is.EqualTo(MatchReportHasher.Hash(Play(plain, FirstSeed))));
        }

        [Test]
        public void TheRealismAnalyzer_ReadsTheBrainsOpenGoalGeometry()
        {
            MatchReport r = Play(Config(MatchBrainVersion.V11), FirstSeed);
            var none = new MatchBalance { V11OpenGoalRangeDm = 0 };

            Assert.That(new RealismAnalyzer().Measure(r)!.OpenGoalChances, Is.GreaterThan(0));
            Assert.That(new RealismAnalyzer(none).Measure(r)!.OpenGoalChances, Is.Zero);
        }
    }
}
