using System.Diagnostics;
using System.Linq;
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
    /// The realism harness of the watchable-match spec (R2, R4, R5, R7, R19): 1,000 watched
    /// matches between two equal-strength sides per brain, measured and printed against the bands
    /// in <see cref="RealismBands"/>. It is a REPORT, not a gate — V11 is expected to sit outside
    /// the bands until it is tuned (task 11), and the user judges the output.
    ///
    /// Explicit, because 2,000 matches with the position stream on cost minutes, not the
    /// milliseconds the score-model harnesses in <see cref="MatchEngineTests"/> cost (about 13 min in
    /// Release, 34 in Debug). Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "Name=Harness_RealismBands_V10AndV11"
    ///     --logger "console;verbosity=detailed"
    /// </summary>
    [TestFixture]
    public class RealismHarnessTests
    {
        private const int Matches = 1000;
        private const ulong FirstSeed = 30_000;

        [Test, Explicit("Report-only harness: 2,000 watched matches."), Category("RealismHarness")]
        public void Harness_RealismBands_V10AndV11()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Club a = league.Clubs[9], b = league.Clubs[10];   // mid-table neighbours: equal strength

            RealismTally v10 = Run(MatchBrainVersion.V10, a, b);
            RealismTally v11 = Run(MatchBrainVersion.V11, a, b);

            TestContext.Out.WriteLine(v10.Format("V10", v10.MsPerMatch));
            TestContext.Out.WriteLine(v11.Format("V11", v10.MsPerMatch));

            Assert.That(v10.Matches, Is.EqualTo(Matches), "Every match must come back with a stream.");
            Assert.That(v11.Matches, Is.EqualTo(Matches), "Every match must come back with a stream.");
        }

        private static RealismTally Run(MatchBrainVersion brain, Club a, Club b)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = brain;

            // The flags the shipped client plays a watched match with (SeasonProgressor's).
            var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);
            Lineup la = LineupSelector.BestEleven(a), lb = LineupSelector.BestEleven(b);

            var analyzer = new MatchAnalyzer();
            var realism = new RealismAnalyzer();
            var tally = new RealismTally();
            var clock = new Stopwatch();

            for (int i = 0; i < Matches; i++)
            {
                clock.Restart();
                // Alternate the venue so home advantage cancels out.
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(la, lb, new Pcg32(FirstSeed + (ulong)i))
                    : engine.Simulate(lb, la, new Pcg32(FirstSeed + (ulong)i));
                clock.Stop();

                MatchMetrics? m = analyzer.Measure(r);
                RealismMetrics? rm = realism.Measure(r);
                if (m != null && rm != null) tally.Add(m, rm, clock.Elapsed.TotalMilliseconds);
            }

            return tally;
        }

        // ------------------------------------------------------------------ the report itself

        [Test]
        public void Tally_MarksEachBandInOrOut()
        {
            var tally = new RealismTally();
            var m = new MatchMetrics
            {
                ReportGoals = 3,
                Home = new SideMetrics { Goals = 2, Shots = 14, ShotsOnTarget = 7, Corners = 6, Fouls = 16 },
                Away = new SideMetrics { Goals = 1, Shots = 10, ShotsOnTarget = 5, Corners = 4, Fouls = 14 }
            };
            var r = new RealismMetrics
            {
                OpenGoalChances = 10, OpenGoalShots = 9,
                Possessions = 100, SterilePossessions = 20,
                HomeBoxEntries = 5, AwayBoxEntries = 3,
                MedianOffTargetSeconds = 4
            };
            tally.Add(m, r, 120);

            var rows = tally.Rows(baselineMsPerMatch: 100).ToDictionary(row => row.Band.Name);

            AssertRow(rows[RealismBands.Goals.Name], 3.0, true);
            AssertRow(rows[RealismBands.Shots.Name], 24.0, true);
            AssertRow(rows[RealismBands.OnTargetPercent.Name], 50.0, false);
            AssertRow(rows[RealismBands.Corners.Name], 10.0, true);
            AssertRow(rows[RealismBands.Fouls.Name], 30.0, false);
            AssertRow(rows[RealismBands.BoxEntriesPerSide.Name], 4.0, true);
            AssertRow(rows[RealismBands.OpenGoalShotRate.Name], 0.9, true);
            AssertRow(rows[RealismBands.SterilePossessionShare.Name], 0.2, false);
            AssertRow(rows[RealismBands.MedianOffTargetSeconds.Name], 4.0, true);
            AssertRow(rows[RealismBands.TimeVsV10.Name], 1.2, true);
            Assert.That(tally.MsPerMatch, Is.EqualTo(120.0));

            string text = tally.Format("V11", 100);
            Assert.That(text, Does.Contain("V11"));
            Assert.That(text, Does.Contain("OUT"));
            Assert.That(text, Does.Contain("IN"));
        }

        private static void AssertRow(RealismRow row, double value, bool inBand)
        {
            Assert.That(row.Value, Is.EqualTo(value).Within(1e-9), row.Band.Name);
            Assert.That(row.InBand, Is.EqualTo(inBand), row.Band.Name);
        }
    }
}
