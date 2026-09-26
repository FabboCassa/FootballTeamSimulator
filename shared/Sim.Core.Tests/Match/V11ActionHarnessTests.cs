using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R4 and R5 over 1,000 watched V11 matches (watchable-match spec, task 7): the open goal is
    /// shot at within 1.5 s in at least 90% of cases, and at most 15% of possessions are sterile.
    /// The same two equal-strength sides and seeds as <see cref="RealismHarnessTests"/>, so the
    /// numbers agree with its report; the full band report is printed too.
    ///
    /// Statistically fragile by nature (a pooled rate over a sample), and Explicit because 1,000
    /// matches with the position stream cost minutes. The matches run in parallel, each on its own
    /// engine and seed, so the numbers do not depend on the scheduling. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "Name=Harness_V11_OpenGoalAndCircling"
    ///     --logger "console;verbosity=detailed"
    /// </summary>
    [TestFixture]
    public class V11ActionHarnessTests
    {
        private const int Matches = 1000;
        private const ulong FirstSeed = 30_000;

        [Test, Explicit("Harness: 1,000 watched V11 matches."), Category("RealismHarness")]
        public void Harness_V11_OpenGoalAndCircling()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Lineup a = LineupSelector.BestEleven(league.Clubs[9]);
            Lineup b = LineupSelector.BestEleven(league.Clubs[10]);
            var cfg = new BalanceConfig();
            cfg.Match.Brain = MatchBrainVersion.V11;

            var metrics = new MatchMetrics?[Matches];
            var realism = new RealismMetrics?[Matches];
            var ms = new double[Matches];
            var wall = Stopwatch.StartNew();

            Parallel.For(0, Matches, i =>
            {
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                var clock = Stopwatch.StartNew();
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(a, b, new Pcg32(FirstSeed + (ulong)i))
                    : engine.Simulate(b, a, new Pcg32(FirstSeed + (ulong)i));
                ms[i] = clock.Elapsed.TotalMilliseconds;
                metrics[i] = new MatchAnalyzer().Measure(r);
                realism[i] = new RealismAnalyzer().Measure(r);
            });

            var tally = new RealismTally();
            for (int i = 0; i < Matches; i++)
            {
                Assert.That(metrics[i], Is.Not.Null, "every match must come back with a stream");
                Assert.That(realism[i], Is.Not.Null, "every match must come back with a stream");
                tally.Add(metrics[i]!, realism[i]!, ms[i]);
            }

            TestContext.Out.WriteLine(tally.Format("V11", tally.MsPerMatch));
            TestContext.Out.WriteLine($"wall clock {wall.Elapsed.TotalSeconds:F1} s (parallel)");

            double openGoal = 0, sterile = 0;
            foreach (RealismRow row in tally.Rows(tally.MsPerMatch))
            {
                if (row.Band.Name == RealismBands.OpenGoalShotRate.Name) openGoal = row.Value;
                if (row.Band.Name == RealismBands.SterilePossessionShare.Name) sterile = row.Value;
            }

            Assert.That(RealismBands.OpenGoalShotRate.Contains(openGoal), Is.True, $"openGoalShotRate {openGoal:F3}");
            Assert.That(RealismBands.SterilePossessionShare.Contains(sterile), Is.True, $"sterilePossessionShare {sterile:F3}");
        }
    }
}
