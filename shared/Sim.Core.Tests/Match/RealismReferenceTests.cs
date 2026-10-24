using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R2 band set of docs/specs/real-match-and-playing-styles.md, as held by
    /// <see cref="RealismReference"/>: one well-formed band per R2 row, sourced in
    /// docs/research/football-reference.md. The R1 movement bands of docs/specs/believable-match.md
    /// must equal the doc's §9 table, which tools/movement_reference.py prints.
    /// </summary>
    [TestFixture]
    public class RealismReferenceTests
    {
        // One name per row of the spec's R2 table, in table order.
        private static readonly string[] R2Rows =
        {
            "goals/match",
            "shots/match",
            "on target (excl. blocked) % of shots",
            "blocked % of shots",
            "inside the box % of shots",
            "headed shots/match",
            "keeper save rate %",
            "save rate inside the box %",
            "save rate outside the box %",
            "passes attempted/team",
            "pass accuracy %",
            "passes per open-play sequence",
            "10+ pass open-play sequences/team",
            "direct speed (open play) m/s",
            "PPDA",
            "crosses/team",
            "corners/match",
            "fouls/match",
            "ball in play min",
            "distance per outfield player km",
        };

        private static RealismBand[] DeclaredBands() =>
            typeof(RealismReference)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(RealismBand))
                .Select(f => (RealismBand)f.GetValue(null)!)
                .ToArray();

        [Test]
        public void EveryBand_HasFiniteMinBelowMax()
        {
            foreach (RealismBand band in DeclaredBands().Concat(RealismReference.All))
            {
                Assert.That(double.IsInfinity(band.Min) || double.IsInfinity(band.Max), Is.False, band.Name);
                Assert.That(band.Min, Is.LessThan(band.Max), band.Name);
            }
        }

        [Test]
        public void Bands_CoverEveryR2Row_OncePerRow()
        {
            string[] names = RealismReference.All.Select(b => b.Name).ToArray();

            Assert.That(names, Is.Unique);
            Assert.That(names, Is.EquivalentTo(R2Rows));
        }

        [Test]
        public void All_ListsEveryPublicBandField_AndNothingElse()
        {
            RealismBand[] declared = DeclaredBands();

            Assert.That(declared.Select(b => b.Name), Is.Unique);
            Assert.That(RealismReference.All, Is.EquivalentTo(declared));
        }
    
        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        // Constant name -> (min, max) from the "Band" column of the doc's §9 movement table.
        private static Dictionary<string, (double Min, double Max)> DocMovementBands()
        {
            string doc = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "research", "football-reference.md"));
            int start = doc.IndexOf("## 9. Movement reference", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "doc §9 Movement reference missing");
            int end = doc.IndexOf("\n## ", start + 1, System.StringComparison.Ordinal);
            string section = end < 0 ? doc.Substring(start) : doc.Substring(start, end - start);

            var bands = new Dictionary<string, (double, double)>();
            var row = new Regex(@"^\|[^|]*\(`(\w+)`\).*\|\s*([0-9.]+)-([0-9.]+)\s*\|\s*$", RegexOptions.Multiline);
            foreach (System.Text.RegularExpressions.Match m in row.Matches(section))
            {
                bands.Add(m.Groups[1].Value, (
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)));
            }
            return bands;
        }

        [Test]
        public void MovementBands_MatchDocTable()
        {
            Dictionary<string, (double Min, double Max)> doc = DocMovementBands();
            FieldInfo[] fields = typeof(RealismReference.Movement)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(RealismBand))
                .ToArray();

            Assert.That(fields.Select(f => f.Name), Is.EquivalentTo(doc.Keys));
            foreach (FieldInfo f in fields)
            {
                var band = (RealismBand)f.GetValue(null)!;
                Assert.That(band.Min, Is.EqualTo(doc[f.Name].Min), f.Name + " min");
                Assert.That(band.Max, Is.EqualTo(doc[f.Name].Max), f.Name + " max");
                Assert.That(band.Min, Is.LessThan(band.Max), f.Name);
            }
        }

        [Test]
        public void MovementAll_ListsEveryMovementBandField_AndNothingElse()
        {
            RealismBand[] declared = typeof(RealismReference.Movement)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(RealismBand))
                .Select(f => (RealismBand)f.GetValue(null)!)
                .ToArray();

            Assert.That(declared.Select(b => b.Name), Is.Unique);
            Assert.That(RealismReference.Movement.All, Is.EquivalentTo(declared));
        }
    }
}
