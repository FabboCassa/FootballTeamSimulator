using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
    /// The tactics harness of the watchable-match spec (R8–R11), in the style of
    /// <see cref="InstructionsTests"/> and <see cref="RealismHarnessTests"/>: watched matches played
    /// with the shipped flags, equal squads on both sides (the same club, rotating across the league),
    /// and readings PRINTED for the user to judge. A REPORT, not a gate — no band is asserted yet.
    ///
    /// The brain is the test-case parameter. Explicit, because each run is thousands of watched
    /// matches. Run one brain with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release
    ///     --filter "FullyQualifiedName~TacticHarnessTests&FullyQualifiedName~V10"
    ///     --logger "console;verbosity=detailed"
    /// (V11 instead of V10 for the new brain; drop the second clause for both.) A quick look at fewer
    /// matches: append  -- TestRunParameters.Parameter(name=\"perPairing\", value=\"20\")  (or
    /// "effectMatches").
    /// </summary>
    [TestFixture]
    public class TacticHarnessTests
    {
        private const string Reason = "Report-only harness: thousands of watched matches per brain.";
        private const int MatchesPerPairing = 200;
        private const int EffectMatches = 400;
        private const ulong TournamentSeed = 37_000;
        private const ulong EffectSeed = 38_000;
        private const int SubMinute = 60;
        private const int TiredSubs = 3;
        private const int FreshIdOffset = 10_000_000;
        private static readonly int[] ShoutMinutes = { 20, 40, 60, 80 };   // 20' apart: past the 15' cooldown

        private static readonly TouchlineShout[] Shouts =
        {
            TouchlineShout.PressHigh, TouchlineShout.KeepBall, TouchlineShout.AllForward,
            TouchlineShout.Encourage, TouchlineShout.Concentrate
        };

        [Explicit(Reason), Category("TacticHarness")]
        [TestCase(MatchBrainVersion.V10)]
        [TestCase(MatchBrainVersion.V11)]
        public void Harness_TacticTournament(MatchBrainVersion brain)
        {
            IReadOnlyList<TacticPreset> presets = TacticPresets.All;
            List<Club> clubs = Clubs();
            var pairs = TournamentTable.Pairings(presets.Count);
            int perPairing = TestContext.Parameters.Get("perPairing", MatchesPerPairing);
            int total = pairs.Count * perPairing;
            var aGoals = new int[total];
            var bGoals = new int[total];
            BalanceConfig cfg = Config(brain);
            int fam = cfg.Tactics.FamiliarityMax;
            var clock = Stopwatch.StartNew();

            Parallel.For(0, total, i =>
            {
                (int a, int b) = pairs[i / perPairing];
                Club club = clubs[i % clubs.Count];
                bool aHome = i % 2 == 0;   // alternate the venue so home advantage cancels out
                TacticContext ca = new TacticContext(presets[a].Tactic, fam), cb = new TacticContext(presets[b].Tactic, fam);
                Lineup la = LineupSelector.BestEleven(club, presets[a].Tactic.Formation);
                Lineup lb = LineupSelector.BestEleven(club, presets[b].Tactic.Formation);

                MatchReport r = aHome
                    ? Engine(cfg).Simulate(la, lb, new Pcg32(TournamentSeed + (ulong)i), new MatchTactics(ca, cb))
                    : Engine(cfg).Simulate(lb, la, new Pcg32(TournamentSeed + (ulong)i), new MatchTactics(cb, ca));
                aGoals[i] = aHome ? r.HomeGoals : r.AwayGoals;
                bGoals[i] = aHome ? r.AwayGoals : r.HomeGoals;
            });

            var table = new TournamentTable(presets.Count);
            for (int i = 0; i < total; i++)
            {
                (int a, int b) = pairs[i / perPairing];
                table.Add(a, b, aGoals[i], bGoals[i]);
            }

            TestContext.Out.WriteLine(table.Format(
                $"{brain}, {perPairing}/pairing, {total} matches, {clock.Elapsed.TotalSeconds:F0} s",
                presets.Select(p => p.Name).ToList()));

            for (int p = 0; p < presets.Count; p++)
                Assert.That(table.Games(p), Is.EqualTo((presets.Count - 1) * perPairing), presets[p].Name);
        }

        [Explicit(Reason), Category("TacticHarness")]
        [TestCase(MatchBrainVersion.V10)]
        [TestCase(MatchBrainVersion.V11)]
        public void Harness_EffectMeasurements(MatchBrainVersion brain)
        {
            List<Club> clubs = Clubs();
            BalanceConfig cfg = Config(brain);
            var samples = new EffectSample[TestContext.Parameters.Get("effectMatches", EffectMatches)];
            var clock = Stopwatch.StartNew();

            Parallel.For(0, samples.Length, i => samples[i] = Measure(cfg, clubs[i % clubs.Count], i));

            TestContext.Out.WriteLine(Report(brain, samples, cfg, clock.Elapsed.TotalSeconds));
            Assert.That(samples.All(s => s != null), Is.True, "every fixture must be measured");
        }

        // ------------------------------------------------------------------ one fixture, every treatment

        /// <summary>Everything read off one fixture, from the treated side's point of view.</summary>
        private sealed class EffectSample
        {
            public int BaseGd, FamFullGd, FamZeroGd, OutOfRoleGd, OutOfRoleSlots, BaseLateGd, SubsLateGd;
            public readonly int[] BaseWindowGd = new int[ShoutMinutes.Length];
            public readonly ScoreState[] BaseState = new ScoreState[ShoutMinutes.Length];
            public readonly double[,] BaseMetric = new double[Shouts.Length, ShoutMinutes.Length];
            public readonly int[,] ShoutWindowGd = new int[Shouts.Length, ShoutMinutes.Length];
            public readonly ScoreState[,] ShoutState = new ScoreState[Shouts.Length, ShoutMinutes.Length];
            public readonly double[,] ShoutMetric = new double[Shouts.Length, ShoutMinutes.Length];
        }

        private static EffectSample Measure(BalanceConfig cfg, Club club, int i)
        {
            bool home = i % 2 == 0;   // the treated side alternates venue
            ulong seed = EffectSeed + (ulong)i;
            Lineup treated = LineupSelector.BestEleven(club), other = LineupSelector.BestEleven(club);
            int fam = cfg.Tactics.FamiliarityMax;
            int heard = cfg.Match.Shouts.DurationMinutes;
            var s = new EffectSample { OutOfRoleSlots = EffectLineups.OutOfRoleCount(EffectLineups.OutOfRole(treated)) };

            MatchInput initial = Input(home, treated, other, null);
            PositionStream b = Play(cfg, new MatchPlan(initial), seed);
            int end = End(b);
            s.BaseGd = MatchWindow.GoalDifference(b, home, 0, end);
            s.BaseLateGd = MatchWindow.GoalDifference(b, home, SubMinute, end);
            for (int w = 0; w < ShoutMinutes.Length; w++)
            {
                int m = ShoutMinutes[w];
                s.BaseWindowGd[w] = MatchWindow.GoalDifference(b, home, m, m + heard);
                s.BaseState[w] = MatchWindow.StateAt(b, home, m);
                for (int k = 0; k < Shouts.Length; k++)
                    s.BaseMetric[k, w] = ShoutTargets.Read(ShoutTargets.MetricOf(Shouts[k]), b, home, m, m + heard);
            }

            s.FamFullGd = FullGd(cfg, Input(home, treated, other, Familiarity(home, fam, fam)), seed, home);
            s.FamZeroGd = FullGd(cfg, Input(home, treated, other, Familiarity(home, 0, fam)), seed, home);
            s.OutOfRoleGd = FullGd(cfg, Input(home, EffectLineups.OutOfRole(treated), other, null), seed, home);

            Lineup subbed = EffectLineups.WithFreshSubs(treated, EffectLineups.MostTired(treated, TiredSubs), FreshIdOffset);
            PositionStream subs = Play(cfg, new MatchPlan(initial).WithChange(SubMinute, Input(home, subbed, other, null)), seed);
            s.SubsLateGd = MatchWindow.GoalDifference(subs, home, SubMinute, End(subs));

            for (int k = 0; k < Shouts.Length; k++)
            {
                MatchPlan plan = new MatchPlan(initial);
                foreach (int m in ShoutMinutes) plan = plan.WithChange(m, initial.WithShout(home, Shouts[k]));
                PositionStream sh = Play(cfg, plan, seed);
                ShoutMetric metric = ShoutTargets.MetricOf(Shouts[k]);
                for (int w = 0; w < ShoutMinutes.Length; w++)
                {
                    int m = ShoutMinutes[w];
                    s.ShoutWindowGd[k, w] = MatchWindow.GoalDifference(sh, home, m, m + heard);
                    s.ShoutState[k, w] = MatchWindow.StateAt(sh, home, m);
                    s.ShoutMetric[k, w] = ShoutTargets.Read(metric, sh, home, m, m + heard);
                }
            }

            return s;
        }

        // ------------------------------------------------------------------ the report

        private static string Report(MatchBrainVersion brain, EffectSample[] samples, BalanceConfig cfg, double seconds)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var fam = new PairedEffect();
            var role = new PairedEffect();
            var subs = new PairedEffect();
            var baseStates = new StateTally();
            foreach (EffectSample s in samples)
            {
                fam.Add(s.FamFullGd, s.FamZeroGd);
                role.Add(s.BaseGd, s.OutOfRoleGd);
                subs.Add(s.SubsLateGd, s.BaseLateGd);
                for (int w = 0; w < ShoutMinutes.Length; w++) baseStates.Add(s.BaseState[w], s.BaseWindowGd[w]);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== effect measurements: {brain} ({samples.Length} fixtures, {seconds:F0} s; GD = treated side) ===");
            sb.AppendLine(Line(inv, "familiarity 100 vs 0 (GD/match)", fam, "R9 reads >= +0.25"));
            sb.AppendLine(Line(inv, $"natural vs out of role (GD/match, {samples[0].OutOfRoleSlots} men off role)", role, "R9 reads >= +0.25"));
            sb.AppendLine(Line(inv, $"{TiredSubs} fresh subs at {SubMinute}' vs none (GD {SubMinute}-90)", subs, "R10 reads >= +0.10"));
            sb.AppendLine(string.Format(inv,
                "  shouts: called at {0}', heard {1}' each | GD per window by score state (shout - none), n windows",
                string.Join("', ", ShoutMinutes), cfg.Match.Shouts.DurationMinutes));

            for (int k = 0; k < Shouts.Length; k++)
            {
                var metric = new PairedEffect();
                var states = new StateTally();
                foreach (EffectSample s in samples)
                    for (int w = 0; w < ShoutMinutes.Length; w++)
                    {
                        metric.Add(s.ShoutMetric[k, w], s.BaseMetric[k, w]);
                        states.Add(s.ShoutState[k, w], s.ShoutWindowGd[k, w]);
                    }

                sb.AppendLine(string.Format(inv,
                    "  {0,-12} {1,-24} none {2,7:F3}  shout {3,7:F3}  ({4:+0.0;-0.0}%)  |  {5}",
                    Shouts[k], ShoutTargets.Describe(ShoutTargets.MetricOf(Shouts[k])),
                    metric.BaselineMean, metric.TreatmentMean, metric.ChangePercent, States(inv, states, baseStates)));
            }

            return sb.ToString();
        }

        private static string Line(CultureInfo inv, string name, PairedEffect e, string target) =>
            string.Format(inv, "  {0,-58} {1:+0.000;-0.000}   ({2:+0.000;-0.000} vs {3:+0.000;-0.000})   {4}",
                name, e.Delta, e.TreatmentMean, e.BaselineMean, target);

        private static string States(CultureInfo inv, StateTally shout, StateTally none)
        {
            var parts = new List<string>();
            bool positiveEverywhere = true;
            foreach (ScoreState st in new[] { ScoreState.Leading, ScoreState.Level, ScoreState.Trailing })
            {
                double delta = shout.Mean(st) - none.Mean(st);
                positiveEverywhere &= delta > 0;
                parts.Add(string.Format(inv, "{0} {1:+0.000;-0.000} (n {2})", st.ToString().ToLowerInvariant(), delta, shout.Count(st)));
            }

            return string.Join("  ", parts) + (positiveEverywhere ? "  NET-POSITIVE IN EVERY STATE" : "");
        }

        // ------------------------------------------------------------------ the bench

        private static List<Club> Clubs() => new LeagueGenerator().Generate(new Pcg32(20260611)).Clubs;

        private static BalanceConfig Config(MatchBrainVersion brain)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = brain;
            return cfg;
        }

        /// <summary>The flags the shipped client plays a watched match with (SeasonProgressor's); nothing reads the stats.</summary>
        private static MatchEngine Engine(BalanceConfig cfg) =>
            new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true, applyPositioning: true,
                generatePositions: true, buildStats: false);

        private static PositionStream Play(BalanceConfig cfg, MatchPlan plan, ulong seed) =>
            Engine(cfg).Simulate(plan, new Pcg32(seed)).Positions!.Unpack();

        private static int FullGd(BalanceConfig cfg, MatchInput input, ulong seed, bool home)
        {
            PositionStream s = Play(cfg, new MatchPlan(input), seed);
            return MatchWindow.GoalDifference(s, home, 0, End(s));
        }

        /// <summary>One past the minute of the final frame, so the window covers every frame.</summary>
        private static int End(PositionStream s) => s.LastTick / s.TicksPerMinute + 1;

        private static MatchInput Input(bool home, Lineup treated, Lineup other, MatchTactics? tactics) =>
            home ? new MatchInput(treated, other, tactics) : new MatchInput(other, treated, tactics);

        private static MatchTactics Familiarity(bool home, int treated, int other)
        {
            var t = new TacticContext(Tactic.Neutral, treated);
            var o = new TacticContext(Tactic.Neutral, other);
            return home ? new MatchTactics(t, o) : new MatchTactics(o, t);
        }
    }
}
