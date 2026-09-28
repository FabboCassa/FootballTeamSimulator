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
    /// The realism harness of the watchable-match spec (R2, R4, R5, R7): 1,000 watched matches
    /// between two equal-strength sides, measured and printed against the bands in
    /// <see cref="RealismBands"/>, and gated on the R7 bands; the user judges the output. The
    /// header prints ms/match, which is R19's timing line.
    ///
    /// Explicit, because 1,000 matches with the position stream on cost minutes, not the
    /// milliseconds the score-model harnesses in <see cref="MatchEngineTests"/> cost. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "Name=Harness_RealismBands"
    ///     --logger "console;verbosity=detailed"
    /// </summary>
    [TestFixture]
    public class RealismHarnessTests
    {
        private const int Matches = 1000;
        private const ulong FirstSeed = 30_000;

        [Test, Explicit("Report-only harness: 1,000 watched matches."), Category("RealismHarness")]
        public void Harness_RealismBands()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Club a = league.Clubs[9], b = league.Clubs[10];   // mid-table neighbours: equal strength

            RealismTally v11 = Run(a, b);

            TestContext.Out.WriteLine(v11.Format("V11"));

            Assert.That(v11.Matches, Is.EqualTo(Matches), "Every match must come back with a stream.");

            string[] gated =
            {
                RealismBands.Goals.Name, RealismBands.Shots.Name, RealismBands.OnTargetPercent.Name,
                RealismBands.Corners.Name, RealismBands.Fouls.Name, RealismBands.BoxEntriesPerSide.Name
            };
            foreach (RealismRow row in v11.Rows().Where(row => gated.Contains(row.Band.Name)))
                Assert.That(row.InBand, Is.True, $"V11 {row.Band.Name} {row.Value:F3} outside {row.Band.Describe()}");
        }

        private static RealismTally Run(Club a, Club b)
        {
            var cfg = new BalanceConfig();

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

            var rows = tally.Rows().ToDictionary(row => row.Band.Name);

            AssertRow(rows[RealismBands.Goals.Name], 3.0, true);
            AssertRow(rows[RealismBands.Shots.Name], 24.0, true);
            AssertRow(rows[RealismBands.OnTargetPercent.Name], 50.0, false);
            AssertRow(rows[RealismBands.Corners.Name], 10.0, true);
            AssertRow(rows[RealismBands.Fouls.Name], 30.0, false);
            AssertRow(rows[RealismBands.BoxEntriesPerSide.Name], 4.0, true);
            AssertRow(rows[RealismBands.OpenGoalShotRate.Name], 0.9, true);
            AssertRow(rows[RealismBands.SterilePossessionShare.Name], 0.2, false);
            AssertRow(rows[RealismBands.MedianOffTargetSeconds.Name], 4.0, true);
            Assert.That(tally.MsPerMatch, Is.EqualTo(120.0));

            string text = tally.Format("V11");
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
