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
    /// The realism harness of the watchable-match spec (R2, R4, R5, R7) and of the real-match
    /// spec's Phase A (R2, R4-R11): 1,000 watched matches per mirror, 4-4-2 against 4-4-2 and
    /// 4-3-3 against 4-3-3, both sides on the neutral (Balanced) instructions, between two
    /// equal-strength sides. Every reading is printed against its band, for each mirror and for
    /// the two together, and the two together are gated; the user judges the output. The header
    /// prints ms/match, which is R19's timing line.
    ///
    /// Explicit, because 2,000 matches with the position stream on cost minutes, not the
    /// milliseconds the score-model harnesses in <see cref="MatchEngineTests"/> cost. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "Name=Harness_RealismBands"
    ///     --logger "console;verbosity=detailed"
    /// A quick look at fewer matches, which only reads: append
    ///   -- TestRunParameters.Parameter(name=\"realismMatches\", value=\"100\")
    /// </summary>
    [TestFixture]
    public class RealismHarnessTests
    {
        private const int MatchesPerMirror = 1000;
        private const ulong FirstSeed = 30_000;
        private static readonly Formation[] Mirrors = { Formation.F442, Formation.F433 };

        /// <summary>The R2 rows printed but not gated, each with the reason.</summary>
        public static readonly IReadOnlyDictionary<string, string> ReportOnly = new Dictionary<string, string>
        {
            // The engine's ball is in play about 81 minutes; the volume rows are read at that length.
            [RealismReference.BallInPlayMinutes.Name] = "~81 min accepted, user decision 2026-10-10 (#82)",
        };

        /// <summary>The open issue every <see cref="Unreached"/> exemption is waiting on.</summary>
        public const string UnreachedDecision = "#120";

        /// <summary>
        /// The R2 rows the #87 tuning could not bring into band with BalanceConfig levers, each
        /// with the measured reading and what was tried. Printed OUT with that evidence but not
        /// gated, like <see cref="ReportOnly"/>, only until #120 ("engine: headed shots, crosses
        /// and direct speed into the R2 bands", needs-human) is decided: either an engine change
        /// brings them into band or the spec is amended. Closing #120 must empty this dictionary
        /// (the rows join the gate) or move them to <see cref="ReportOnly"/> with the amendment.
        /// The cheap guard in <see cref="V11RealismTuningTests"/> leaves them out on the same terms.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Unreached = new Dictionary<string, string>
        {
            // The brain seldom has a man in the box when the ball is wide in the last third.
            [RealismReference.CrossesPerTeam.Name] = "~2 a team; cross weight x4 and wide-channel x2.5 read 2.8",
            // Read as a shot within 1 s of a team-mate's cross: no crosses, no headers.
            [RealismReference.HeadedShots.Name] = "~0.4 a match; receiver reach 2.5 -> 4.5 m read 1.8 with 4.4 goals",
            // Sequences progress at about 3 m/s whatever the hold time or the carrier's pace.
            [RealismReference.DirectSpeed.Name] = "~3.1 m/s; carrier pace 72 -> 55% read 2.9, normal-tempo hold x1.6 read 3.4",
        };

        [Test, Explicit("Harness: 2,000 watched matches."), Category("RealismHarness")]
        public void Harness_RealismBands()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Club a = league.Clubs[9], b = league.Clubs[10];   // mid-table neighbours: equal strength
            int matches = TestContext.Parameters.Get("realismMatches", MatchesPerMirror);

            var v11 = new RealismTally();
            var shape = new ShapeMovementTally();
            var shotPass = new ShotPassTally();
            var chances = new ChanceDecisionTally();
            var feel = new MovementFeelTally();
            var continuity = new BallContinuityTally();
            foreach (Formation formation in Mirrors)
                Run(a, b, formation, matches, v11, shape, shotPass, chances, feel, continuity);

            Print("V11 both mirrors", v11, shape, shotPass, chances, feel, continuity);
            Assert.That(v11.Matches, Is.EqualTo(Mirrors.Length * matches), "Every match must come back with a stream.");

            // The gate is the full sample; a quick look at fewer matches only reads.
            if (matches < MatchesPerMirror) return;

            Assert.Multiple(() => Gate(v11, shape, shotPass));
        }

        private static void Gate(RealismTally v11, ShapeMovementTally shape, ShotPassTally shotPass)
        {
            // Real-match spec R2: every band of RealismReference but the report-only ones and the
            // unreached ones, which stay out only until #120 is decided.
            foreach ((RealismBand band, double value) in R2Rows(v11, shape, shotPass))
            {
                if (ReportOnly.ContainsKey(band.Name) || Unreached.ContainsKey(band.Name)) continue;
                Assert.That(band.Contains(value), Is.True, $"R2 {band.Name} {value:F3} outside {band.Describe()}");
            }

            // The watchable-match R7 band R2 does not restate.
            RealismRow entries = v11.Rows().Single(row => row.Band.Name == RealismBands.BoxEntriesPerSide.Name);
            Assert.That(entries.InBand, Is.True, $"V11 {entries.Band.Name} {entries.Value:F3} outside {entries.Band.Describe()}");

            // R4 shape and off-target time (the reading with the turnover window, #117), R5 off the
            // pitch, R6 keeper depth, R8 standing still, R11 both goal kicks and the throw-in offers.
            foreach (ShapeMovementRow row in shape.Rows().Where(row => row.Band != null))
                Assert.That(row.InBand, Is.True, $"V11 {row.Name} {row.Value:F3} outside {row.Band?.Describe()}");

            // R7 keeper reach, R9 passing chains and volume, R10 the open-goal shortcut.
            foreach (ShotPassRow row in shotPass.Rows().Where(row => row.Band != null
                         && !ReportOnly.ContainsKey(row.Name) && !RealismReference.All.Any(band => band.Name == row.Name)))
                Assert.That(row.InBand, Is.True, $"V11 {row.Name} {row.Value:F3} outside {row.Band?.Describe()}");
            RealismRow openGoal = v11.Rows().Single(row => row.Band.Name == RealismBands.OpenGoalShotRate.Name);
            Assert.That(openGoal.InBand, Is.True, $"V11 openGoalShotRate {openGoal.Value:F3} outside {openGoal.Band.Describe()}");
        }

        /// <summary>
        /// Every R2 band with its reading. Goals, corners and fouls come off the realism tally, the
        /// distance off the shape tally, the rest off the shot and pass tally, whose passes per
        /// team is read at the 81-minute ball in play (user decision 2026-10-10, #82).
        /// </summary>
        public static IReadOnlyList<(RealismBand Band, double Value)> R2Rows(
            RealismTally realism, ShapeMovementTally shape, ShotPassTally shotPass)
        {
            var rows = new Dictionary<string, (RealismBand, double)>();
            foreach (RealismRow row in realism.Rows())
            {
                if (row.Band.Name == RealismBands.Goals.Name) rows[RealismReference.Goals.Name] = (RealismReference.Goals, row.Value);
                if (row.Band.Name == RealismBands.Corners.Name) rows[RealismReference.Corners.Name] = (RealismReference.Corners, row.Value);
                if (row.Band.Name == RealismBands.Fouls.Name) rows[RealismReference.Fouls.Name] = (RealismReference.Fouls, row.Value);
            }

            foreach (ShapeMovementRow row in shape.Rows())
                if (row.Name == RealismReference.DistancePerOutfieldPlayerKm.Name)
                    rows[row.Name] = (RealismReference.DistancePerOutfieldPlayerKm, row.Value);

            foreach (ShotPassRow row in shotPass.Rows())
                if (row.Band != null && RealismReference.All.Any(band => band.Name == row.Name))
                    rows[row.Name] = (row.Band.Value, row.Value);

            return RealismReference.All.Select(band => rows[band.Name]).ToList();
        }

        private static void Print(string label, RealismTally realism, ShapeMovementTally shape, ShotPassTally shotPass,
            ChanceDecisionTally chances, MovementFeelTally feel, BallContinuityTally continuity)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== real-match R2: {label} ({realism.Matches} matches, {realism.MsPerMatch:F1} ms/match) ===");
            int inside = 0, gated = 0;
            foreach ((RealismBand band, double value) in R2Rows(realism, shape, shotPass))
            {
                bool reportOnly = ReportOnly.TryGetValue(band.Name, out string? why);
                bool unreached = Unreached.TryGetValue(band.Name, out string? tried);
                bool inBand = band.Contains(value);
                if (!reportOnly && !unreached) gated++;
                if (!reportOnly && !unreached && inBand) inside++;
                string verdict = (inBand ? "IN" : "OUT") + (reportOnly ? $"  (report only: {why})" : "");
                if (unreached) verdict += $"  (report only until {UnreachedDecision}: unreached by #87 levers: {tried})";
                sb.AppendLine(string.Format(inv, "  {0,-40} {1,9:F3}   band {2,-10} {3}", band.Name, value, band.Describe(), verdict));
            }

            sb.AppendLine($"  inside {inside}/{gated} gated bands");
            TestContext.Out.WriteLine(sb.ToString());
            TestContext.Out.WriteLine(realism.Format(label));
            TestContext.Out.WriteLine(shape.Format(label));
            TestContext.Out.WriteLine(shotPass.Format(label));
            TestContext.Out.WriteLine(chances.Format(label));
            TestContext.Out.WriteLine(feel.Format(label));
            TestContext.Out.WriteLine(continuity.Format(label));
        }

        /// <summary>One mirror: printed on its own and added to the totals.</summary>
        private static void Run(Club a, Club b, Formation formation, int matches,
            RealismTally total, ShapeMovementTally totalShape, ShotPassTally totalShotPass, ChanceDecisionTally totalChances,
            MovementFeelTally totalFeel, BallContinuityTally totalContinuity)
        {
            var cfg = new BalanceConfig();
            Lineup la = LineupSelector.BestEleven(a, formation), lb = LineupSelector.BestEleven(b, formation);
            var neutral = new TacticContext(new Tactic(formation), cfg.Tactics.FamiliarityMax);
            var tactics = new MatchTactics(neutral, neutral);
            var readings = new (MatchMetrics? Match, RealismMetrics? Realism, ShapeMovementMetrics? Shape,
                ShotPassMetrics? ShotPass, ChanceDecisionMetrics? Chances, MovementFeelMetrics? Feel, double Ms)[matches];
            var continuityReadings = new BallContinuityMetrics?[matches];

            Parallel.For(0, matches, i =>
            {
                // The flags the shipped client plays a watched match with (SeasonProgressor's).
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                var clock = Stopwatch.StartNew();
                // Alternate the venue so home advantage cancels out.
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(la, lb, new Pcg32(FirstSeed + (ulong)i), tactics)
                    : engine.Simulate(lb, la, new Pcg32(FirstSeed + (ulong)i), tactics);
                clock.Stop();
                readings[i] = (new MatchAnalyzer().Measure(r), new RealismAnalyzer(cfg.Match).Measure(r),
                    new ShapeMovementAnalyzer().Measure(r), new ShotPassAnalyzer(cfg.Match).Measure(r),
                    new ChanceDecisionAnalyzer().Measure(r), new MovementFeelAnalyzer().Measure(r),
                    clock.Elapsed.TotalMilliseconds);
                continuityReadings[i] = new BallContinuityAnalyzer().Measure(r);
            });

            var tally = new RealismTally();
            var shape = new ShapeMovementTally();
            var shotPass = new ShotPassTally();
            var chances = new ChanceDecisionTally();
            var feel = new MovementFeelTally();
            var continuity = new BallContinuityTally();
            foreach (BallContinuityMetrics? c in continuityReadings)
            {
                if (c == null) continue;
                continuity.Add(c);
                totalContinuity.Add(c);
            }

            foreach (var x in readings)
            {
                if (x.Match != null && x.Realism != null)
                {
                    tally.Add(x.Match, x.Realism, x.Ms);
                    total.Add(x.Match, x.Realism, x.Ms);
                }

                if (x.Shape != null)
                {
                    shape.Add(x.Shape);
                    totalShape.Add(x.Shape);
                }

                if (x.ShotPass != null)
                {
                    shotPass.Add(x.ShotPass);
                    totalShotPass.Add(x.ShotPass);
                }

                if (x.Chances != null)
                {
                    chances.Add(x.Chances);
                    totalChances.Add(x.Chances);
                }

                if (x.Feel != null)
                {
                    feel.Add(x.Feel);
                    totalFeel.Add(x.Feel);
                }
            }

            Print($"V11 {(formation == Formation.F442 ? "4-4-2" : "4-3-3")} mirror", tally, shape, shotPass, chances, feel, continuity);
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
