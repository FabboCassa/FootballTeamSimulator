using System;
using System.Globalization;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R15 of the real-match spec: generating a match on engine v12 costs at most 25% more than on
    /// the v11 engine, measured in the same run. The v11 build is a Sim.Core.dll compiled from the
    /// last v11 commit and loaded beside the current one (<see cref="SimCoreHandle"/>); each match
    /// is played on both builds back to back, in alternating order, so load on the machine and the
    /// process priority weigh on both alike. Two kinds of match are timed: the watched one (position
    /// stream on, the client's match screen) and the unwatched career fixture (stream off).
    ///
    /// Explicit: it needs the reference DLL and a thousand matches per build. Build the reference with
    ///   .\tools\build-simcore-reference.ps1            (prints the DLL path; default commit e3992bc, v11 before Phase A)
    /// and run
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "TestCategory=EngineTimingHarness"
    ///     --logger "console;verbosity=detailed" -- TestRunParameters.Parameter(name=\"v11SimCore\", value=\"PATH\")
    /// Timing is noisy by nature: the user pastes the output and we judge it together.
    /// </summary>
    [TestFixture]
    public class EngineTimingHarnessTests
    {
        private const int Matches = 1000;
        private const int WatchedWarmUp = 8;

        // A minute-model match lasts ~0.02 ms, so a short warm-up would time the JIT instead of the
        // engine: the build warmed second read ~0.2 ms against ~0.02 ms until it had run a few thousand.
        private const int UnwatchedWarmUp = 5000;
        private const ulong FirstSeed = 30_000;
        private const int HomeClub = 9;
        private const int AwayClub = 10;
        private const int ReferenceVersion = 11;
        private const double MaxCostRatio = 1.25;
        private const int Seasons = 100;
        // With 5 warm-up seasons the build timed first read up to 1.4x the other: JIT tiering, not the model.
        private const int SeasonWarmUp = 30;

        [Test, Explicit("Harness: a thousand matches on two engine builds."), Category("EngineTimingHarness")]
        public void Harness_V12GenerationTime_IsWithin25PercentOfV11()
        {
            string? path = TestContext.Parameters.Get("v11SimCore");
            if (string.IsNullOrEmpty(path))
                Assert.Fail("Pass the v11 Sim.Core.dll: -- TestRunParameters.Parameter(name=\"v11SimCore\", value=\"PATH\")");
            int n = TestContext.Parameters.Get("timingMatches", Matches);

            SimCoreHandle v11 = SimCoreHandle.Load(path!, HomeClub, AwayClub);
            SimCoreHandle v12 = SimCoreHandle.Of(typeof(MatchEngine).Assembly, HomeClub, AwayClub);
            Assert.That(v11.Version, Is.EqualTo(ReferenceVersion), "the reference must be a v11 build");
            Assert.That(v12.Version, Is.EqualTo(MatchEngine.Version));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Inv($"[engine-timing] v{v11.Version} reference {path} vs v{v12.Version}, {n} matches each, same run"));
            sb.AppendLine("  match kind          v11 ms   v12 ms   ratio   limit");
            bool watchedInside = Measure(sb, "watched (stream)", v11, v12, n, WatchedWarmUp, generatePositions: true);
            bool unwatchedInside = Measure(sb, "unwatched", v11, v12, n, UnwatchedWarmUp, generatePositions: false);
            TestContext.Out.WriteLine(sb.ToString());

            Assert.That(watchedInside && unwatchedInside, Is.True, "R15 generation time");
        }

        /// <summary>
        /// R25 for the fast model: whole Large-world background seasons resolved on the v11 build and on this
        /// one, alternating, per fixture. Two worlds: a neutral one (no club has instructions: today's host
        /// path) and a styled one (every club a catalogue style; the v11 build gets the style's four v11
        /// axes, this one the whole style and its formation). Same reference DLL and parameter as above.
        /// </summary>
        [Test, Explicit("Harness: whole background seasons on two builds."), Category("EngineTimingHarness")]
        public void Harness_FastModelGenerationTime_IsWithin25PercentOfV11()
        {
            string? path = TestContext.Parameters.Get("v11SimCore");
            if (string.IsNullOrEmpty(path))
                Assert.Fail("Pass the v11 Sim.Core.dll: -- TestRunParameters.Parameter(name=\"v11SimCore\", value=\"PATH\")");
            int seasons = TestContext.Parameters.Get("timingSeasons", Seasons);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Inv($"[fast-model-timing] v11 reference {path} vs v{MatchEngine.Version}, Large world, {seasons} background seasons each, same run"));
            sb.AppendLine("  world        fixtures   v11 us   now us   ratio   limit");
            bool neutralInside = MeasureSeasons(sb, "neutral", path!, styled: false, seasons);
            bool styledInside = MeasureSeasons(sb, "styled", path!, styled: true, seasons);
            TestContext.Out.WriteLine(sb.ToString());

            Assert.That(neutralInside && styledInside, Is.True, "R25 fast-model generation time");
        }

        private static bool MeasureSeasons(System.Text.StringBuilder sb, string kind, string path, bool styled, int seasons)
        {
            BackgroundWorldHandle v11 = BackgroundWorldHandle.Load(path, styled);
            BackgroundWorldHandle now = BackgroundWorldHandle.Of(typeof(MatchEngine).Assembly, styled);
            for (int i = 0; i < SeasonWarmUp; i++) { v11.ResolveSeason(); now.ResolveSeason(); }

            double oldMs = 0, newMs = 0;
            for (int i = 0; i < seasons; i++)
            {
                if (i % 2 == 0) { oldMs += v11.ResolveSeason(); newMs += now.ResolveSeason(); }
                else { newMs += now.ResolveSeason(); oldMs += v11.ResolveSeason(); }
            }

            double oldUs = 1000 * oldMs / seasons / v11.Fixtures, newUs = 1000 * newMs / seasons / now.Fixtures;
            bool inside = WithinLimit(oldUs, newUs);
            sb.AppendLine(Inv($"  {kind,-10} {now.Fixtures,9}  {oldUs,7:F3}  {newUs,7:F3}   {newUs / oldUs,5:F2}   ≤{MaxCostRatio:F2}  {(inside ? "IN" : "OUT")}"));
            return inside;
        }

        [Test]
        public void BackgroundHandle_ResolvesTheWholeStyledSeason()
        {
            BackgroundWorldHandle now = BackgroundWorldHandle.Of(typeof(MatchEngine).Assembly, styled: true);
            Assert.That(now.Fixtures, Is.GreaterThan(1000));
            Assert.That(now.ResolveSeason(), Is.GreaterThan(0.0), "and again after the reset");
            Assert.That(now.ResolveSeason(), Is.GreaterThan(0.0));
        }

        [Test]
        public void Handle_PlaysTheSameMatchAsTheEngine()
        {
            var cfg = new BalanceConfig();
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            MatchReport direct = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true, applyPositioning: true,
                    generatePositions: true, buildStats: true)
                .Simulate(LineupSelector.BestEleven(league.Clubs[AwayClub]), LineupSelector.BestEleven(league.Clubs[HomeClub]),
                    new Pcg32(FirstSeed));

            (ulong hash, double ms) = SimCoreHandle.Of(typeof(MatchEngine).Assembly, HomeClub, AwayClub)
                .Play(FirstSeed, swapVenue: true, generatePositions: true);

            Assert.That(hash, Is.EqualTo(MatchReportHasher.Hash(direct)));
            Assert.That(ms, Is.GreaterThan(0.0));
        }

        [Test]
        public void Handle_LoadsABuildFromDisk_BesideTheCurrentOne()
        {
            string current = typeof(MatchEngine).Assembly.Location;

            SimCoreHandle loaded = SimCoreHandle.Load(current, HomeClub, AwayClub);
            SimCoreHandle here = SimCoreHandle.Of(typeof(MatchEngine).Assembly, HomeClub, AwayClub);

            Assert.That(loaded.Version, Is.EqualTo(MatchEngine.Version));
            Assert.That(loaded.Play(FirstSeed, false, false).Hash, Is.EqualTo(here.Play(FirstSeed, false, false).Hash),
                "the same build in another load context plays the same match");
        }

        [TestCase(100.0, 125.0, true)]
        [TestCase(100.0, 125.1, false)]
        [TestCase(100.0, 80.0, true)]
        public void CostRatio_IsGatedAt25Percent(double v11Ms, double v12Ms, bool inside)
        {
            Assert.That(WithinLimit(v11Ms, v12Ms), Is.EqualTo(inside));
        }

        private static bool WithinLimit(double v11Ms, double v12Ms) => v12Ms <= v11Ms * MaxCostRatio;

        private static bool Measure(System.Text.StringBuilder sb, string kind, SimCoreHandle v11, SimCoreHandle v12, int n, int warmUp, bool generatePositions)
        {
            for (int i = 0; i < warmUp; i++)
            {
                v11.Play(FirstSeed + (ulong)i, i % 2 == 1, generatePositions);
                v12.Play(FirstSeed + (ulong)i, i % 2 == 1, generatePositions);
            }

            var oldMs = new double[n];
            var newMs = new double[n];
            Parallel.For(0, n, i =>
            {
                ulong seed = FirstSeed + (ulong)i;
                bool swap = i % 2 == 1;
                if (i % 4 < 2)
                {
                    oldMs[i] = v11.Play(seed, swap, generatePositions).Ms;
                    newMs[i] = v12.Play(seed, swap, generatePositions).Ms;
                }
                else
                {
                    newMs[i] = v12.Play(seed, swap, generatePositions).Ms;
                    oldMs[i] = v11.Play(seed, swap, generatePositions).Ms;
                }
            });

            double oldMean = Mean(oldMs), newMean = Mean(newMs);
            bool inside = WithinLimit(oldMean, newMean);
            sb.AppendLine(Inv($"  {kind,-18} {oldMean,7:F3}  {newMean,7:F3}   {newMean / oldMean,5:F2}   ≤{MaxCostRatio:F2}  {(inside ? "IN" : "OUT")}"));
            return inside;
        }

        private static double Mean(double[] values)
        {
            double sum = 0;
            foreach (double v in values) sum += v;
            return sum / values.Length;
        }

        private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    }
}
