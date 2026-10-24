using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sim.Core.Match.Analysis;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The R2 band set of docs/specs/real-match-and-playing-styles.md, as held by
    /// <see cref="RealismReference"/>: one well-formed band per R2 row, sourced in
    /// docs/research/football-reference.md.
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
    }
}
