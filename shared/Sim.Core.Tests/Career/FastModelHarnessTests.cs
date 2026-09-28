using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// R17 of the watchable-match spec: the fast model (<see cref="QuickResultResolver"/>) against
    /// the full engine on the V11 brain. Two readings, both printed as a table:
    ///   - outcomes: 1,000 fixtures of a generated league (every pairing, both venues) played by
    ///     the full engine, and the same fixtures through the fast model — mean goals within 0.15,
    ///     home/draw/away shares within 3 pp. The fast side is drawn many times per fixture so its
    ///     own sampling noise drops out and the 3 pp is spent on the engine's.
    ///   - instructions: each non-neutral instruction against a neutral side of the same club,
    ///     and the treated side's goals for/against compared with the neutral run on the same
    ///     seeds. Where the engine's shift is beyond two standard errors, the fast model's
    ///     expected goals must move the same way; a shift inside the noise is only reported.
    ///
    /// STATISTICALLY FRAGILE: 3 pp is about two standard errors of a 1,000-match share, so the
    /// user pastes the output and we judge it together. Explicit, because it is several thousand
    /// watched matches. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release
    ///     --filter "TestCategory=FastModelHarness" --logger "console;verbosity=detailed"
    /// A quick look: append  -- TestRunParameters.Parameter(name=\"instructionMatches\", value=\"200\")
    /// </summary>
    [TestFixture]
    public class FastModelHarnessTests
    {
        private const string Reason = "Report-only harness: thousands of watched V11 matches.";
        private const int Matches = 1000;
        private const int InstructionMatches = 1000;
        private const int FastDrawsPerFixture = 50;
        private const ulong FullSeed = 39_000;
        private const ulong FastWorldSeed = 39_500;
        private const ulong InstructionSeed = 40_000;

        private const double MaxGoalsGap = 0.15;
        private const double MaxSharePpGap = 3.0;
        private const double SignalStandardErrors = 2.0;

        [Test, Explicit(Reason), Category("FastModelHarness")]
        public void Harness_FastModelVsV11_Outcomes()
        {
            List<Club> clubs = Clubs();
            BalanceConfig cfg = V11();
            var full = new OutcomeTally();
            var fast = new OutcomeTally();
            var homeGoals = new int[Matches];
            var awayGoals = new int[Matches];
            var clock = Stopwatch.StartNew();

            Parallel.For(0, Matches, i =>
            {
                (Club home, Club away) = Pairing(clubs, i);
                MatchReport r = Engine(cfg).Simulate(
                    LineupSelector.BestEleven(home), LineupSelector.BestEleven(away), new Pcg32(FullSeed + (ulong)i));
                homeGoals[i] = r.HomeGoals;
                awayGoals[i] = r.AwayGoals;
            });

            for (int i = 0; i < Matches; i++)
            {
                (Club home, Club away) = Pairing(clubs, i);
                int hs = BackgroundLeagueProgressor.StrengthOf(home), aws = BackgroundLeagueProgressor.StrengthOf(away);
                full.Add(homeGoals[i], awayGoals[i], 1.0, hs - aws);
                for (int k = 0; k < FastDrawsPerFixture; k++)
                {
                    var fixture = new Fixture { Id = i };
                    QuickResultResolver.Resolve(fixture, hs, aws, FastWorldSeed + (ulong)k, cfg);
                    fast.Add(fixture.HomeGoals, fixture.AwayGoals, 1.0 / FastDrawsPerFixture, hs - aws);
                }
            }

            var misses = new List<string>();
            TestContext.Out.WriteLine(FormatOutcomes(full, fast, clock.Elapsed.TotalSeconds, misses));
            Assert.That(misses, Is.Empty, "R17 outcomes");
        }

        [Test, Explicit(Reason), Category("FastModelHarness")]
        public void Harness_FastModelVsV11_InstructionDirections()
        {
            List<Club> clubs = Clubs();
            BalanceConfig cfg = V11();
            int n = TestContext.Parameters.Get("instructionMatches", InstructionMatches);
            IReadOnlyList<(string Name, TacticInstructions Set)> settings = Settings();
            var clock = Stopwatch.StartNew();

            SideRun neutral = RunFull(cfg, clubs, TacticInstructions.Neutral, n);
            (double neutralFor, double neutralAgainst) = FastExpected(cfg, clubs, TacticInstructions.Neutral, n);

            var rows = new List<DirectionRow>();
            foreach ((string name, TacticInstructions set) in settings)
            {
                SideRun treated = RunFull(cfg, clubs, set, n);
                (double fastFor, double fastAgainst) = FastExpected(cfg, clubs, set, n);
                rows.Add(new DirectionRow(name,
                    treated.MeanFor - neutral.MeanFor, StandardError(treated.For, neutral.For),
                    treated.MeanAgainst - neutral.MeanAgainst, StandardError(treated.Against, neutral.Against),
                    fastFor - neutralFor, fastAgainst - neutralAgainst));
            }

            var misses = new List<string>();
            TestContext.Out.WriteLine(FormatDirections(rows, n, clock.Elapsed.TotalSeconds, misses));
            Assert.That(misses, Is.Empty, "R17 instruction directions");
        }

        [Test]
        public void Direction_IsGatedOnlyWhereTheEngineShowsASignal()
        {
            Assert.That(DirectionRow.Agrees(0.20, 0.05, 0.10), Is.EqualTo(true), "same sign");
            Assert.That(DirectionRow.Agrees(0.20, 0.05, -0.10), Is.EqualTo(false), "opposite sign");
            Assert.That(DirectionRow.Agrees(0.20, 0.05, 0.0), Is.EqualTo(false), "the fast model must move at all");
            Assert.That(DirectionRow.Agrees(0.08, 0.05, -0.10), Is.Null, "inside two standard errors: no direction to match");
        }

        [Test]
        public void OutcomeTally_ReadsGoalsAndShares()
        {
            var t = new OutcomeTally();
            t.Add(2, 1, 1.0, 2);
            t.Add(1, 1, 0.5, 0);
            t.Add(0, 2, 0.5, -4);
            Assert.That(t.MeanGoals, Is.EqualTo((3 + 1 + 1) / 2.0).Within(1e-9));
            Assert.That(t.HomePercent, Is.EqualTo(50.0).Within(1e-9));
            Assert.That(t.DrawPercent, Is.EqualTo(25.0).Within(1e-9));
            Assert.That(t.AwayPercent, Is.EqualTo(25.0).Within(1e-9));
            Assert.That(t.GdPerStrengthPoint, Is.EqualTo(0.5).Within(1e-9), "gd = gap / 2 on every fixture");
        }

        // ------------------------------------------------------------------ the full engine

        private sealed class SideRun
        {
            public SideRun(int n) { For = new int[n]; Against = new int[n]; }
            public readonly int[] For;
            public readonly int[] Against;
            public double MeanFor => Mean(For);
            public double MeanAgainst => Mean(Against);
        }

        /// <summary>The treated side plays <paramref name="set"/> against a neutral side of the same club, venue alternating.</summary>
        private static SideRun RunFull(BalanceConfig cfg, List<Club> clubs, TacticInstructions set, int n)
        {
            var run = new SideRun(n);
            int fam = cfg.Tactics.FamiliarityMax;

            Parallel.For(0, n, i =>
            {
                Club club = clubs[i % clubs.Count];
                bool home = i % 2 == 0;
                var treated = new TacticContext(new Tactic(Formation.F433, set), fam);
                TacticContext other = TacticContext.Neutral(fam);
                MatchReport r = Engine(cfg).Simulate(
                    LineupSelector.BestEleven(club), LineupSelector.BestEleven(club), new Pcg32(InstructionSeed + (ulong)i),
                    home ? new MatchTactics(treated, other) : new MatchTactics(other, treated));
                run.For[i] = home ? r.HomeGoals : r.AwayGoals;
                run.Against[i] = home ? r.AwayGoals : r.HomeGoals;
            });

            return run;
        }

        /// <summary>The fast model's mean expected goals for/against the treated side over the same fixtures.</summary>
        private static (double For, double Against) FastExpected(BalanceConfig cfg, List<Club> clubs, TacticInstructions set, int n)
        {
            double goalsFor = 0, goalsAgainst = 0;
            for (int i = 0; i < n; i++)
            {
                int s = BackgroundLeagueProgressor.StrengthOf(clubs[i % clubs.Count]);
                bool home = i % 2 == 0;
                QuickResultResolver.ExpectedGoals(s, s, cfg,
                    home ? set : TacticInstructions.Neutral, home ? TacticInstructions.Neutral : set,
                    out double h, out double a);
                goalsFor += home ? h : a;
                goalsAgainst += home ? a : h;
            }

            return (goalsFor / n, goalsAgainst / n);
        }

        // ------------------------------------------------------------------ the report

        private sealed class OutcomeTally
        {
            private double _weight, _goals, _home, _draw, _away, _x, _y, _xx, _xy;

            /// <summary><paramref name="strengthGap"/> is home minus away strength, for the goal-difference slope.</summary>
            public void Add(int homeGoals, int awayGoals, double weight, int strengthGap)
            {
                _weight += weight;
                _goals += (homeGoals + awayGoals) * weight;
                int gd = homeGoals - awayGoals;
                _x += strengthGap * weight;
                _y += gd * weight;
                _xx += (double)strengthGap * strengthGap * weight;
                _xy += (double)strengthGap * gd * weight;
                if (homeGoals > awayGoals) _home += weight;
                else if (homeGoals == awayGoals) _draw += weight;
                else _away += weight;
            }

            public double MeanGoals => _goals / _weight;
            public double HomePercent => 100.0 * _home / _weight;
            public double DrawPercent => 100.0 * _draw / _weight;
            public double AwayPercent => 100.0 * _away / _weight;

            /// <summary>Least-squares goal difference gained per point of strength gap: how hard the stronger side wins.</summary>
            public double GdPerStrengthPoint
            {
                get
                {
                    double mx = _x / _weight, my = _y / _weight;
                    double varX = _xx / _weight - mx * mx;
                    return varX > 0 ? (_xy / _weight - mx * my) / varX : 0;
                }
            }
        }

        private static string FormatOutcomes(OutcomeTally full, OutcomeTally fast, double seconds, List<string> misses)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Inv($"[fast-model] V11 full engine {Matches} matches vs fast model x{FastDrawsPerFixture} draws per fixture, {seconds:F0} s"));
            sb.AppendLine("  reading        full      fast      gap   tolerance");
            Row(sb, misses, "mean goals", full.MeanGoals, fast.MeanGoals, MaxGoalsGap, "F2");
            Row(sb, misses, "home win %", full.HomePercent, fast.HomePercent, MaxSharePpGap, "F1");
            Row(sb, misses, "draw %", full.DrawPercent, fast.DrawPercent, MaxSharePpGap, "F1");
            Row(sb, misses, "away win %", full.AwayPercent, fast.AwayPercent, MaxSharePpGap, "F1");
            sb.AppendLine(Inv($"  GD/str pt    {full.GdPerStrengthPoint,8:F3} {fast.GdPerStrengthPoint,9:F3}            (reported, not gated)"));
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, List<string> misses, string name, double full, double fast, double tolerance, string format)
        {
            double gap = fast - full;
            bool inside = Math.Abs(gap) <= tolerance;
            if (!inside) misses.Add(name);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-12} {1,8} {2,9} {3,8}   ±{4}  {5}",
                name, full.ToString(format, CultureInfo.InvariantCulture), fast.ToString(format, CultureInfo.InvariantCulture),
                gap.ToString("+0.00;-0.00", CultureInfo.InvariantCulture), tolerance.ToString(CultureInfo.InvariantCulture), inside ? "IN" : "OUT"));
        }

        private sealed class DirectionRow
        {
            public DirectionRow(string name, double fullFor, double seFor, double fullAgainst, double seAgainst, double fastFor, double fastAgainst)
            {
                Name = name;
                FullFor = fullFor; SeFor = seFor; FullAgainst = fullAgainst; SeAgainst = seAgainst;
                FastFor = fastFor; FastAgainst = fastAgainst;
            }

            public readonly string Name;
            public readonly double FullFor, SeFor, FullAgainst, SeAgainst, FastFor, FastAgainst;

            /// <summary>null when the engine's shift is inside the noise, else whether the fast model moves the same way.</summary>
            public static bool? Agrees(double full, double standardError, double fast)
            {
                if (Math.Abs(full) <= SignalStandardErrors * standardError) return null;
                return Math.Sign(fast) == Math.Sign(full);
            }
        }

        private static string FormatDirections(List<DirectionRow> rows, int n, double seconds, List<string> misses)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Inv($"[fast-model-instructions] V11 full engine {n} matches per setting (+ neutral) vs fast-model expected goals, {seconds:F0} s"));
            sb.AppendLine("  treated side    full dGF (se)    fast dGF   GF     full dGA (se)    fast dGA   GA");
            foreach (DirectionRow r in rows)
            {
                bool? gf = DirectionRow.Agrees(r.FullFor, r.SeFor, r.FastFor);
                bool? ga = DirectionRow.Agrees(r.FullAgainst, r.SeAgainst, r.FastAgainst);
                if (gf == false) misses.Add(r.Name + " GF");
                if (ga == false) misses.Add(r.Name + " GA");
                sb.AppendLine(Inv($"  {r.Name,-14} {r.FullFor,7:+0.000;-0.000;0.000} ({r.SeFor:F3})   {r.FastFor,7:+0.000;-0.000;0.000}   {Mark(gf),-5}  {r.FullAgainst,7:+0.000;-0.000;0.000} ({r.SeAgainst:F3})   {r.FastAgainst,7:+0.000;-0.000;0.000}   {Mark(ga)}"));
            }

            return sb.ToString();
        }

        private static string Mark(bool? agrees) => agrees == null ? "flat" : agrees.Value ? "same" : "OPP";

        // ------------------------------------------------------------------ the bench

        private static IReadOnlyList<(string, TacticInstructions)> Settings()
        {
            TacticInstructions n = TacticInstructions.Neutral;
            return new[]
            {
                ("defensive", new TacticInstructions(Mentality.Defensive, n.Pressing, n.Tempo, n.Width)),
                ("attacking", new TacticInstructions(Mentality.Attacking, n.Pressing, n.Tempo, n.Width)),
                ("press low", new TacticInstructions(n.Mentality, Pressing.Low, n.Tempo, n.Width)),
                ("press high", new TacticInstructions(n.Mentality, Pressing.High, n.Tempo, n.Width)),
                ("tempo slow", new TacticInstructions(n.Mentality, n.Pressing, Tempo.Slow, n.Width)),
                ("tempo fast", new TacticInstructions(n.Mentality, n.Pressing, Tempo.Fast, n.Width)),
                ("narrow", new TacticInstructions(n.Mentality, n.Pressing, n.Tempo, Width.Narrow)),
                ("wide", new TacticInstructions(n.Mentality, n.Pressing, n.Tempo, Width.Wide))
            };
        }

        private static List<Club> Clubs() => new LeagueGenerator().Generate(new Pcg32(20260611)).Clubs;

        /// <summary>Fixture <paramref name="i"/> of a double round robin, cycled: every pairing, both venues.</summary>
        private static (Club Home, Club Away) Pairing(List<Club> clubs, int i)
        {
            int c = clubs.Count;
            int p = i % (c * (c - 1));
            int home = p / (c - 1), away = p % (c - 1);
            if (away >= home) away++;
            return (clubs[home], clubs[away]);
        }

        private static BalanceConfig V11() => new BalanceConfig();

        /// <summary>The flags the shipped client plays a watched match with (SeasonProgressor's); nothing reads the stats.</summary>
        private static MatchEngine Engine(BalanceConfig cfg) =>
            new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true, applyPositioning: true,
                generatePositions: true, buildStats: false);

        private static double Mean(int[] values)
        {
            double sum = 0;
            foreach (int v in values) sum += v;
            return sum / values.Length;
        }

        /// <summary>Standard error of the difference of two sample means.</summary>
        private static double StandardError(int[] a, int[] b) => Math.Sqrt(Variance(a) / a.Length + Variance(b) / b.Length);

        private static double Variance(int[] values)
        {
            double mean = Mean(values), sum = 0;
            foreach (int v in values) sum += (v - mean) * (v - mean);
            return values.Length > 1 ? sum / (values.Length - 1) : 0;
        }

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }
}
