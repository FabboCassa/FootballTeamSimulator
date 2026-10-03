using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
    /// The R3 quality-tier harness of the real-match spec: 1,000 watched matches between two equal
    /// sides at the world generator's top-tier average, 1,000 between two at its second-tier
    /// average, on the same seeds, printed with the R3 gaps (<see cref="QualityTierTally"/>). A
    /// REPORT: the user judges the output.
    ///
    /// "Average" is read off a generated world: the clubs of the tier whose strength lies nearest
    /// the league's mean, each playing itself (equal sides).
    ///
    /// Explicit, because 2,000 watched matches cost minutes. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release
    ///     --filter "Name=Harness_QualityTierComparison" --logger "console;verbosity=detailed"
    /// A quick look at fewer matches: append
    ///   -- TestRunParameters.Parameter(name=\"tierMatches\", value=\"100\")
    /// </summary>
    [TestFixture]
    public class QualityTierHarnessTests
    {
        private const int MatchesPerTier = 1000;
        private const ulong FirstSeed = 73_000;
        private const ulong WorldSeed = 20260611;
        private const string NationCode = "ITA";
        private const int SidesPerTier = 4;

        [Test, Explicit("Report-only harness: 2,000 watched matches."), Category("RealismHarness")]
        public void Harness_QualityTierComparison()
        {
            World world = Generate();
            Nation nation = world.Nations.Single(n => n.Code == NationCode);
            int matches = TestContext.Parameters.Get("tierMatches", MatchesPerTier);
            var clock = Stopwatch.StartNew();

            QualityTierTally top = Run(nation.FindTier(1)!, matches, out string topSides);
            QualityTierTally second = Run(nation.FindTier(2)!, matches, out string secondSides);

            TestContext.Out.WriteLine($"top tier:    {topSides}");
            TestContext.Out.WriteLine($"second tier: {secondSides}");
            TestContext.Out.WriteLine(QualityTierTally.Format(
                $"V11, {matches}/tier, {clock.Elapsed.TotalSeconds:F0} s", top, second));

            Assert.That(top.Matches, Is.EqualTo(matches), "Every top-tier match must come back with a stream.");
            Assert.That(second.Matches, Is.EqualTo(matches), "Every second-tier match must come back with a stream.");
        }

        private static QualityTierTally Run(League league, int matches, out string sides)
        {
            List<Club> clubs = AverageClubs(league);
            Lineup[] home = clubs.Select(c => LineupSelector.BestEleven(c)).ToArray();
            Lineup[] away = clubs.Select(c => LineupSelector.BestEleven(c)).ToArray();
            var cfg = new BalanceConfig();
            var readings = new ShotPassMetrics?[matches];
            var goals = new int[matches];

            Parallel.For(0, matches, i =>
            {
                int c = i % clubs.Count;
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true, buildStats: false);
                MatchReport report = engine.Simulate(home[c], away[c], new Pcg32(FirstSeed + (ulong)i));
                readings[i] = ShotPassAnalyzer.Analyze(report);
                goals[i] = report.HomeGoals + report.AwayGoals;
            });

            var tally = new QualityTierTally();
            for (int i = 0; i < matches; i++)
                if (readings[i] != null) tally.Add(readings[i]!, goals[i]);

            CultureInfo inv = CultureInfo.InvariantCulture;
            double mean = league.Clubs.Average(c => c.Strength);
            double xi = home.SelectMany(l => l.Slots).Average(s => PlayerRating.Overall(s.Player));
            sides = string.Format(inv, "{0}, league mean strength {1:F1}, sides {2} (mean XI overall {3:F1})",
                league.Name, mean, string.Join(", ", clubs.Select(c => $"{c.Name} {c.Strength}")), xi);
            return tally;
        }

        /// <summary>The clubs whose strength lies nearest the league mean, ties to the earlier club.</summary>
        private static List<Club> AverageClubs(League league)
        {
            double mean = league.Clubs.Average(c => c.Strength);
            return league.Clubs
                .Select((c, i) => (Club: c, Index: i))
                .OrderBy(x => Math.Abs(x.Club.Strength - mean))
                .ThenBy(x => x.Index)
                .Take(SidesPerTier)
                .Select(x => x.Club)
                .ToList();
        }

        private static World Generate()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = NationCode, PlayableTiers = 2 });
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, new BalanceConfig()).Generate(WorldSeed);
        }
    }
}
