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
