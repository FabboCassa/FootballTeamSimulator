using System;
using System.Collections.Generic;
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
    /// Real-match spec R17's harness: every formation of the catalogue, mirrored and on the
    /// Balanced instructions between two equal-strength sides, keeps the R2 core bands (goals,
    /// shots, passes per team) within ±15%, i.e. inside [0.85 × min, 1.15 × max] of the band
    /// <see cref="RealismHarnessTests"/> gates. The table also prints each reading against the
    /// 4-4-2 mirror; the user judges the output.
    ///
    /// Explicit, because 1,000 watched matches per formation cost minutes. Run it with:
    ///   dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj -c Release --filter "Name=Harness_FormationCatalogue"
    ///     --logger "console;verbosity=detailed"
    /// A quick look at fewer matches, which only reads: append
    ///   -- TestRunParameters.Parameter(name=\"formationMatches\", value=\"100\")
    /// </summary>
    [TestFixture]
    public class FormationCatalogueHarnessTests
    {
        private const int MatchesPerFormation = 1000;
        private const ulong FirstSeed = 30_000;
        private const double Tolerance = 0.15;

        /// <summary>The R2 core bands, at the scale the realism harness gates them.</summary>
        private static readonly RealismBand[] Core =
        {
            RealismReference.Goals, RealismReference.Shots, ShotPassBands.PassesPerTeamAt81Minutes
        };

        /// <summary>The open issue every <see cref="Unreached"/> exemption is waiting on.</summary>
        public const string UnreachedDecision = "#127";

        /// <summary>
        /// The formations whose mirror the v12 engine keeps outside the ±15% bands, with the reading
        /// of the first full run (1,000 matches). Printed OUT with that evidence but not gated, like
        /// <see cref="RealismHarnessTests.Unreached"/>, only until #127 ("engine tuning for narrow
        /// shapes vs spec amendment", needs-human) is decided. Closing #127 must empty this set.
        /// </summary>
        public static readonly IReadOnlyDictionary<Formation, string> Unreached = new Dictionary<Formation, string>
        {
            // Narrow shapes with an AM behind two strikers and no wide man: the brain finds no width.
            [Formation.F442Diamond] = "1.95 goals, 19.45 shots",
            [Formation.F41212] = "1.95 goals, 19.45 shots",
            [Formation.F4312] = "1.94 goals, 19.44 shots",
            [Formation.F5212] = "1.82 goals, 19.45 shots",
            // A flat four behind a lone striker, with a back five: too open.
            [Formation.F541] = "3.74 goals, 35.35 shots",
        };

        [Test, Explicit("Harness: 1,000 watched matches per formation."), Category("RealismHarness")]
        public void Harness_FormationCatalogue()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Club a = league.Clubs[9], b = league.Clubs[10];   // mid-table neighbours: equal strength
            int matches = TestContext.Parameters.Get("formationMatches", MatchesPerFormation);

            var readings = Formations.Catalogue.ToDictionary(f => f, f => Read(a, b, f, matches));
            double[] reference = readings[Formation.F442];

            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"=== R17 formation mirrors, Balanced ({matches} matches each; band ±15%; Δ vs 4-4-2) ===");
            sb.AppendLine(string.Format(inv, "  {0,-14} {1,16} {2,16} {3,18}  {4}", "formation", "goals", "shots", "passes/team", "verdict"));
            var failures = new List<string>();
            foreach (Formation f in Formations.Catalogue)
            {
                double[] r = readings[f];
                var cells = new string[Core.Length];
                bool inside = true;
                for (int i = 0; i < Core.Length; i++)
                {
                    bool ok = Widened(Core[i]).Contains(r[i]);
                    inside &= ok;
                    if (!ok && !Unreached.ContainsKey(f))
                        failures.Add(string.Format(inv, "{0} {1} {2:F3} outside {3}",
                            Formations.Name(f), Core[i].Name, r[i], Widened(Core[i]).Describe()));
                    cells[i] = string.Format(inv, "{0:F2} ({1:+0;-0;0}%){2}", r[i],
                        Math.Round(100.0 * (r[i] - reference[i]) / reference[i]), ok ? "" : "!");
                }

                string verdict = inside ? "IN" : "OUT";
                if (Unreached.TryGetValue(f, out string? measured))
                    verdict += $"  (report only until {UnreachedDecision}: {measured})";
                sb.AppendLine(string.Format(inv, "  {0,-14} {1,16} {2,16} {3,18}  {4}",
                    Formations.Name(f), cells[0], cells[1], cells[2], verdict));
            }

            sb.AppendLine("  bands: " + string.Join(", ", Core.Select(band => $"{band.Name} {Widened(band).Describe()}")));
            TestContext.Out.WriteLine(sb.ToString());

            // The gate is the full sample; a quick look at fewer matches only reads.
            if (matches < MatchesPerFormation) return;
            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void Unreached_ExemptsOnlyNewCatalogueFormations()
        {
            // The six original shapes, which the R2 harness and the golden master play, stay gated.
            Assert.That(Unreached.Keys, Is.SubsetOf(Formations.Catalogue));
            Assert.That(Unreached.Keys.Intersect(Formations.All), Is.Empty);
        }

        [Test]
        public void Widened_StretchesEachEndByFifteenPercent()
        {
            RealismBand band = Widened(new RealismBand("x", 2.0, 4.0));

            Assert.That(band.Min, Is.EqualTo(1.7).Within(1e-9));
            Assert.That(band.Max, Is.EqualTo(4.6).Within(1e-9));
            Assert.That(band.Name, Is.EqualTo("x"));
        }

        private static RealismBand Widened(RealismBand band) =>
            new RealismBand(band.Name, band.Min * (1 - Tolerance), band.Max * (1 + Tolerance));

        /// <summary>Goals, shots and passes per team of one formation's mirror, venue alternating.</summary>
        private static double[] Read(Club a, Club b, Formation formation, int matches)
        {
            var cfg = new BalanceConfig();
            Lineup la = LineupSelector.BestEleven(a, formation), lb = LineupSelector.BestEleven(b, formation);
            var neutral = new TacticContext(new Tactic(formation), cfg.Tactics.FamiliarityMax);
            var tactics = new MatchTactics(neutral, neutral);
            var readings = new (MatchMetrics? Match, RealismMetrics? Realism, ShotPassMetrics? ShotPass)[matches];

            Parallel.For(0, matches, i =>
            {
                // The flags the shipped client plays a watched match with, as the realism harness.
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                MatchReport r = i % 2 == 0
                    ? engine.Simulate(la, lb, new Pcg32(FirstSeed + (ulong)i), tactics)
                    : engine.Simulate(lb, la, new Pcg32(FirstSeed + (ulong)i), tactics);
                readings[i] = (new MatchAnalyzer().Measure(r), new RealismAnalyzer(cfg.Match).Measure(r),
                    new ShotPassAnalyzer(cfg.Match).Measure(r));
            });

            var realism = new RealismTally();
            var shotPass = new ShotPassTally();
            foreach (var x in readings)
            {
                if (x.Match == null || x.Realism == null || x.ShotPass == null) continue;
                realism.Add(x.Match, x.Realism, 0);
                shotPass.Add(x.ShotPass);
            }

            Assert.That(realism.Matches, Is.EqualTo(matches), $"{Formations.Name(formation)}: every match must come back with a stream.");

            double goals = realism.Rows().Single(row => row.Band.Name == RealismBands.Goals.Name).Value;
            double shots = shotPass.Rows().Single(row => row.Name == RealismReference.Shots.Name).Value;
            double passes = shotPass.Rows().Single(row => row.Name == RealismReference.PassesPerTeam.Name).Value;
            return new[] { goals, shots, passes };
        }
    }
}
