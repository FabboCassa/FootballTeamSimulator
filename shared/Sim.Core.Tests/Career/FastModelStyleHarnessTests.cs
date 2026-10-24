using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using static Sim.Core.Tests.Career.FastModelHarnessTests;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// R24 of the real-match spec: the fast model (<see cref="QuickResultResolver"/>) against the full
    /// engine for every catalogue style and formation, on the bench of <see cref="FastModelHarnessTests"/>
    /// (a side against a Balanced 4-3-3 side of the same club, venue alternating).
    ///   - styles: each style in its first suggested formation. Gated: goals for and against within
    ///     0.15 of the full engine's, win/draw/loss shares within 3 pp, and wherever the engine's shift
    ///     from the neutral run is beyond two standard errors, the fast model's expected goals move the
    ///     same way. The fast side is drawn many times per fixture so its own noise drops out.
    ///   - formations: each catalogue formation on Balanced; the same direction gate.
    ///
    /// STATISTICALLY FRAGILE: 3 pp is about three standard errors of a 2,000-match share, over 15
    /// styles x 3 shares. The user pastes the output and we judge it together. Explicit, because it
    /// is tens of thousands of watched matches. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release
    ///     --filter "TestCategory=FastModelHarness" --logger "console;verbosity=detailed"
    /// A quick look: append  -- TestRunParameters.Parameter(name=\"styleMatches\", value=\"400\")
    /// (or "formationMatches"); below the full sample the tables are only read. The style tables are
    /// fitted on the default seeds; a hold-out check passes another "styleSeed".
    /// </summary>
    [TestFixture]
    public class FastModelStyleHarnessTests
    {
        private const string Reason = "Report-only harness: tens of thousands of watched matches.";
        private const int StyleMatches = 2000;
        private const int FormationMatches = 1000;
        private const int FastDrawsPerFixture = 50;
        private const ulong StyleSeed = 41_000;
        private const ulong FormationSeed = 44_000;
        private const ulong FastWorldSeed = 39_500;

        /// <summary>
        /// The sign of each style's shift in the full engine (goals for, goals against), read off the
        /// style harness at the full sample: 0 where the shift is inside two standard errors.
        /// <see cref="QuickTacticPercentTests"/> pins the shipped tables to these.
        /// </summary>
        internal static readonly (PlayingStyle Style, int GoalsFor, int GoalsAgainst)[] EngineSigns =
        {
            (PlayingStyle.Balanced, 0, 0),
            (PlayingStyle.TikiTaka, -1, 0),
            (PlayingStyle.PositionalPlay, +1, +1),
            (PlayingStyle.Gegenpress, +1, +1),
            (PlayingStyle.VerticalTikiTaka, +1, +1),
            (PlayingStyle.ControlPossession, 0, -1),
            (PlayingStyle.WingPlay, +1, +1),
            (PlayingStyle.RouteOne, +1, +1),
            (PlayingStyle.Direct, +1, +1),
            (PlayingStyle.FluidCounter, +1, +1),
            (PlayingStyle.DirectCounter, 0, -1),
            (PlayingStyle.MidBlock, 0, +1),
            (PlayingStyle.Catenaccio, +1, 0),
            (PlayingStyle.ParkTheBus, 0, -1),
            (PlayingStyle.ManOrientedPress, +1, +1)
        };

        [Test, Explicit(Reason), Category("FastModelHarness")]
        public void Harness_FastModelVsFullEngine_Styles()
        {
            List<Club> clubs = Clubs();
            BalanceConfig cfg = new BalanceConfig();
            int n = TestContext.Parameters.Get("styleMatches", StyleMatches);
            ulong seed = (ulong)TestContext.Parameters.Get("styleSeed", (long)StyleSeed);
            var clock = Stopwatch.StartNew();

            SideRun neutral = RunFull(cfg, clubs, Tactic.Neutral, n, seed);
            (double neutralFor, double neutralAgainst) = FastExpected(cfg, clubs, Tactic.Neutral, n);

            var rows = new List<StyleRow>();
            foreach (StyleDefinition style in PlayingStyles.All)
            {
                var tactic = new Tactic(style.SuggestedFormations[0], style.Instructions);
                SideRun full = RunFull(cfg, clubs, tactic, n, seed);
                (double fastFor, double fastAgainst) = FastExpected(cfg, clubs, tactic, n);
                rows.Add(new StyleRow($"{style.Name} ({Formations.Name(tactic.Formation)})",
                    Tally.Of(full), FastTally(cfg, clubs, tactic, n),
                    full.MeanFor - neutral.MeanFor, StandardError(full.For, neutral.For),
                    full.MeanAgainst - neutral.MeanAgainst, StandardError(full.Against, neutral.Against),
                    fastFor - neutralFor, fastAgainst - neutralAgainst));
            }

            var misses = new List<string>();
            TestContext.Out.WriteLine(FormatStyles(rows, n, seed, clock.Elapsed.TotalSeconds, neutral, misses));
            if (n < StyleMatches) return;
            Assert.That(misses, Is.Empty, "R24 styles");
        }

        [Test, Explicit(Reason), Category("FastModelHarness")]
        public void Harness_FastModelVsFullEngine_Formations()
        {
            List<Club> clubs = Clubs();
            BalanceConfig cfg = new BalanceConfig();
            int n = TestContext.Parameters.Get("formationMatches", FormationMatches);
            var clock = Stopwatch.StartNew();

            SideRun neutral = RunFull(cfg, clubs, Tactic.Neutral, n, FormationSeed);
            (double neutralFor, double neutralAgainst) = FastExpected(cfg, clubs, Tactic.Neutral, n);

            var sb = new StringBuilder();
            var misses = new List<string>();
            sb.AppendLine(Inv($"[fast-model-formations] v{MatchEngine.Version} full engine {n} matches per formation (Balanced, vs Balanced 4-3-3) vs fast-model expected goals"));
            sb.AppendLine(Inv($"  neutral side goals for {neutral.MeanFor:F3}, against {neutral.MeanAgainst:F3}"));
            sb.AppendLine("  formation       full dGF (se)  dGF%   fast dGF   GF     full dGA (se)  dGA%   fast dGA   GA");
            foreach (Formation formation in Formations.Catalogue.Where(f => f != Tactic.Neutral.Formation))
            {
                var tactic = new Tactic(formation);
                SideRun full = RunFull(cfg, clubs, tactic, n, FormationSeed);
                (double fastFor, double fastAgainst) = FastExpected(cfg, clubs, tactic, n);
                double dFor = full.MeanFor - neutral.MeanFor, dAgainst = full.MeanAgainst - neutral.MeanAgainst;
                double seFor = StandardError(full.For, neutral.For), seAgainst = StandardError(full.Against, neutral.Against);
                bool? gf = DirectionRow.Agrees(dFor, seFor, fastFor - neutralFor);
                bool? ga = DirectionRow.Agrees(dAgainst, seAgainst, fastAgainst - neutralAgainst);
                string name = Formations.Name(formation);
                if (gf == false) misses.Add(name + " GF");
                if (ga == false) misses.Add(name + " GA");
                sb.AppendLine(Inv($"  {name,-14} {dFor,7:+0.000;-0.000;0.000} ({seFor:F3}) {100 * dFor / neutral.MeanFor,5:+0;-0;0}   {fastFor - neutralFor,7:+0.000;-0.000;0.000}   {Mark(gf),-5}  {dAgainst,7:+0.000;-0.000;0.000} ({seAgainst:F3}) {100 * dAgainst / neutral.MeanAgainst,5:+0;-0;0}   {fastAgainst - neutralAgainst,7:+0.000;-0.000;0.000}   {Mark(ga)}"));
            }

            sb.AppendLine(Inv($"  {clock.Elapsed.TotalSeconds:F0} s"));
            TestContext.Out.WriteLine(sb.ToString());
            if (n < FormationMatches) return;
            Assert.That(misses, Is.Empty, "R24 formation directions");
        }

        [Test]
        public void StyleRow_GatesGoalsSharesAndDirections()
        {
            var full = new Tally(1.50, 1.40, 40, 25, 35);
            var close = new StyleRow("close", full, new Tally(1.62, 1.30, 42.5, 23, 34.5), 0.20, 0.05, 0.0, 0.05, 0.10, 0.01);
            Assert.That(close.Misses(), Is.Empty);

            var far = new StyleRow("far", full, new Tally(1.70, 1.20, 43.5, 25, 31.5), 0.20, 0.05, -0.20, 0.05, -0.10, -0.10);
            Assert.That(far.Misses(), Is.EquivalentTo(new[] { "far GF", "far GA", "far win", "far loss", "far dGF" }));
        }

        [Test]
        public void Tally_ReadsTheTreatedSidesGoalsAndShares()
        {
            var run = new SideRun(4);
            int[] goalsFor = { 2, 1, 0, 3 }, goalsAgainst = { 1, 1, 2, 3 };
            goalsFor.CopyTo(run.For, 0);
            goalsAgainst.CopyTo(run.Against, 0);

            Tally t = Tally.Of(run);
            Assert.That((t.For, t.Against), Is.EqualTo((1.5, 1.75)));
            Assert.That((t.Win, t.Draw, t.Loss), Is.EqualTo((25.0, 50.0, 25.0)));
        }

        [Test]
        public void EngineSigns_CoverEveryStyleOnce()
        {
            Assert.That(EngineSigns.Select(s => s.Style), Is.EquivalentTo(PlayingStyles.All.Select(s => s.Style)));
        }

        // ------------------------------------------------------------------ the fast side

        /// <summary>The fast model's drawn scores over the same fixtures as <see cref="RunFull"/>, many draws a fixture.</summary>
        private static Tally FastTally(BalanceConfig cfg, List<Club> clubs, Tactic set, int n)
        {
            double goalsFor = 0, goalsAgainst = 0, win = 0, draw = 0;
            for (int i = 0; i < n; i++)
            {
                int s = BackgroundLeagueProgressor.StrengthOf(clubs[i % clubs.Count]);
                bool home = i % 2 == 0;
                for (int k = 0; k < FastDrawsPerFixture; k++)
                {
                    var fixture = new Fixture { Id = i };
                    QuickResultResolver.Resolve(fixture, s, s, FastWorldSeed + (ulong)k, cfg,
                        home ? set : Tactic.Neutral, home ? Tactic.Neutral : set);
                    int own = home ? fixture.HomeGoals : fixture.AwayGoals, other = home ? fixture.AwayGoals : fixture.HomeGoals;
                    goalsFor += own;
                    goalsAgainst += other;
                    if (own > other) win++;
                    else if (own == other) draw++;
                }
            }

            double total = (double)n * FastDrawsPerFixture;
            return new Tally(goalsFor / total, goalsAgainst / total, 100 * win / total, 100 * draw / total,
                100 * (total - win - draw) / total);
        }

        // ------------------------------------------------------------------ the report

        /// <summary>The treated side's mean goals for/against and its win/draw/loss shares in percent.</summary>
        internal readonly struct Tally
        {
            public Tally(double goalsFor, double goalsAgainst, double win, double draw, double loss)
            {
                For = goalsFor; Against = goalsAgainst; Win = win; Draw = draw; Loss = loss;
            }

            public readonly double For, Against, Win, Draw, Loss;

            public static Tally Of(SideRun run)
            {
                int n = run.For.Length, win = 0, draw = 0;
                for (int i = 0; i < n; i++)
                {
                    if (run.For[i] > run.Against[i]) win++;
                    else if (run.For[i] == run.Against[i]) draw++;
                }

                return new Tally(run.MeanFor, run.MeanAgainst, 100.0 * win / n, 100.0 * draw / n, 100.0 * (n - win - draw) / n);
            }
        }

        internal sealed class StyleRow
        {
            public StyleRow(string name, Tally full, Tally fast, double fullDFor, double seFor, double fullDAgainst,
                double seAgainst, double fastDFor, double fastDAgainst)
            {
                Name = name; Full = full; Fast = fast;
                FullDFor = fullDFor; SeFor = seFor; FullDAgainst = fullDAgainst; SeAgainst = seAgainst;
                FastDFor = fastDFor; FastDAgainst = fastDAgainst;
            }

            public readonly string Name;
            public readonly Tally Full, Fast;
            public readonly double FullDFor, SeFor, FullDAgainst, SeAgainst, FastDFor, FastDAgainst;

            public bool? ForAgrees => DirectionRow.Agrees(FullDFor, SeFor, FastDFor);
            public bool? AgainstAgrees => DirectionRow.Agrees(FullDAgainst, SeAgainst, FastDAgainst);

            public List<string> Misses()
            {
                var misses = new List<string>();
                if (Math.Abs(Fast.For - Full.For) > MaxGoalsGap) misses.Add(Name + " GF");
                if (Math.Abs(Fast.Against - Full.Against) > MaxGoalsGap) misses.Add(Name + " GA");
                if (Math.Abs(Fast.Win - Full.Win) > MaxSharePpGap) misses.Add(Name + " win");
                if (Math.Abs(Fast.Draw - Full.Draw) > MaxSharePpGap) misses.Add(Name + " draw");
                if (Math.Abs(Fast.Loss - Full.Loss) > MaxSharePpGap) misses.Add(Name + " loss");
                if (ForAgrees == false) misses.Add(Name + " dGF");
                if (AgainstAgrees == false) misses.Add(Name + " dGA");
                return misses;
            }
        }

        private static string FormatStyles(List<StyleRow> rows, int n, ulong seed, double seconds, SideRun neutral, List<string> misses)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Inv($"[fast-model-styles] v{MatchEngine.Version} full engine {n} matches per style (first suggested formation, vs Balanced 4-3-3, same club, seed {seed}) vs fast model x{FastDrawsPerFixture} draws, {seconds:F0} s"));
            sb.AppendLine(Inv($"  neutral side goals for {neutral.MeanFor:F3}, against {neutral.MeanAgainst:F3}; gates: goals ±{MaxGoalsGap}, shares ±{MaxSharePpGap} pp"));
            sb.AppendLine("  style                        GF full/fast  gap    GA full/fast  gap    W full/fast  gap   D full/fast  gap   L full/fast  gap  verdict");
            foreach (StyleRow r in rows)
            {
                List<string> rowMisses = r.Misses();
                misses.AddRange(rowMisses);
                sb.AppendLine(Inv($"  {r.Name,-27} {r.Full.For,5:F2} {r.Fast.For,5:F2} {r.Fast.For - r.Full.For,6:+0.00;-0.00;0.00}   {r.Full.Against,5:F2} {r.Fast.Against,5:F2} {r.Fast.Against - r.Full.Against,6:+0.00;-0.00;0.00}   {r.Full.Win,4:F1} {r.Fast.Win,4:F1} {r.Fast.Win - r.Full.Win,5:+0.0;-0.0;0.0}  {r.Full.Draw,4:F1} {r.Fast.Draw,4:F1} {r.Fast.Draw - r.Full.Draw,5:+0.0;-0.0;0.0}  {r.Full.Loss,4:F1} {r.Fast.Loss,4:F1} {r.Fast.Loss - r.Full.Loss,5:+0.0;-0.0;0.0}  {(rowMisses.Count == 0 ? "IN" : "OUT")}"));
            }

            sb.AppendLine();
            sb.AppendLine("  shift from the neutral run  full dGF (se)  dGF%   fast dGF   GF     full dGA (se)  dGA%   fast dGA   GA");
            foreach (StyleRow r in rows)
            {
                sb.AppendLine(Inv($"  {r.Name,-27} {r.FullDFor,7:+0.000;-0.000;0.000} ({r.SeFor:F3}) {100 * r.FullDFor / neutral.MeanFor,5:+0;-0;0}   {r.FastDFor,7:+0.000;-0.000;0.000}   {Mark(r.ForAgrees),-5}  {r.FullDAgainst,7:+0.000;-0.000;0.000} ({r.SeAgainst:F3}) {100 * r.FullDAgainst / neutral.MeanAgainst,5:+0;-0;0}   {r.FastDAgainst,7:+0.000;-0.000;0.000}   {Mark(r.AgainstAgrees)}"));
            }

            sb.AppendLine(misses.Count == 0 ? "  misses: none" : "  misses: " + string.Join(", ", misses));
            return sb.ToString();
        }

        private static string Mark(bool? agrees) => agrees == null ? "flat" : agrees.Value ? "same" : "OPP";
    }
}
