using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Identity;

namespace Sim.Core.Tests.Identity
{
    /// <summary>
    /// Task 6.1 (art pass) foundation: the deterministic club-identity generator. There is no
    /// stored art — a club's colours + crest are regenerated from (clubId, worldSeed) on demand
    /// — so the tests assert the properties the client renderer relies on: the generation is
    /// deterministic and seed-sensitive, the integer HSV→RGB is correct, every channel/enum is
    /// valid, identities are well-spread across clubs, every crest shape/pattern is reachable,
    /// and the two readability GUARANTEES hold for the whole league ("challenge, not chaos"):
    ///   (a) the two crest fills are always clearly distinguishable, and
    ///   (b) the emblem/text colour is always legible on the primary.
    ///
    /// Nothing here is called by the match engine, so the golden master / existing tests are
    /// unaffected (the generator is opt-in by being called).
    /// </summary>
    [TestFixture]
    public class IdentityTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static IdentityBalance I => Cfg.Identity;
        private const ulong WorldSeed = 0xABCDEF12UL;
        private const int Sample = 1000;

        private static ClubIdentity Gen(int clubId, ulong seed = WorldSeed)
            => ClubIdentityGenerator.Generate(clubId, seed, I);

        // ------------------------------------------------------- determinism

        [Test]
        public void Generate_IsDeterministic_SameInputsSameIdentity()
        {
            for (int id = 1; id <= 50; id++)
            {
                ClubIdentity a = Gen(id);
                ClubIdentity b = Gen(id);
                Assert.That(SameIdentity(a, b), Is.True, $"club {id} identity must be stable across calls");
            }
        }

        [Test]
        public void Generate_IsSeedSensitive_DifferentWorldDiffers()
        {
            int differ = 0;
            const int n = 300;
            for (int id = 1; id <= n; id++)
            {
                if (Gen(id, 111UL).Colors.Primary != Gen(id, 222UL).Colors.Primary)
                    differ++;
            }
            Assert.That(differ, Is.GreaterThanOrEqualTo((int)(n * 0.9)),
                "the same club in two different worlds should almost always look different");
        }

        // ------------------------------------------------------- HSV→RGB sanity

        [Test]
        public void Hsv_ToRgb_HitsKnownPrimariesAndGrey()
        {
            Assert.That(RgbColor.FromHsv(0, 255, 255), Is.EqualTo(new RgbColor(255, 0, 0)), "red");
            Assert.That(RgbColor.FromHsv(120, 255, 255), Is.EqualTo(new RgbColor(0, 255, 0)), "green");
            Assert.That(RgbColor.FromHsv(240, 255, 255), Is.EqualTo(new RgbColor(0, 0, 255)), "blue");
            Assert.That(RgbColor.FromHsv(0, 0, 200), Is.EqualTo(new RgbColor(200, 200, 200)), "grey (no saturation)");

            // Hue wraps and channels round-trip through Packed.
            Assert.That(RgbColor.FromHsv(360, 255, 255), Is.EqualTo(RgbColor.FromHsv(0, 255, 255)));
            Assert.That(RgbColor.FromPacked(0xD4AF37), Is.EqualTo(new RgbColor(0xD4, 0xAF, 0x37)));
        }

        // ------------------------------------------------------- validity

        [Test]
        public void AllChannelsAndEnums_AreValid_AcrossTheLeague()
        {
            for (int id = 1; id <= Sample; id++)
            {
                ClubIdentity ci = Gen(id);
                AssertColorValid(ci.Colors.Primary);
                AssertColorValid(ci.Colors.Secondary);
                AssertColorValid(ci.Colors.Accent);
                AssertColorValid(ci.Colors.Neutral);
                AssertColorValid(ci.Crest.FillA);
                AssertColorValid(ci.Crest.FillB);
                AssertColorValid(ci.Crest.Trim);
                AssertColorValid(ci.Crest.Emblem);

                Assert.That((int)ci.Crest.Shape, Is.InRange(0, 3));
                Assert.That((int)ci.Crest.Pattern, Is.InRange(0, 6));
            }
        }

        // ------------------------------------------------------- THE ✅ (a): distinguishable fills

        [Test]
        public void CrestFills_AreAlwaysDistinguishable()
        {
            int worst = int.MaxValue;
            for (int id = 1; id <= Sample; id++)
            {
                CrestDesign c = Gen(id).Crest;
                int d = c.FillA.DistanceTo(c.FillB);
                if (d < worst) worst = d;
                Assert.That(d, Is.GreaterThanOrEqualTo(I.MinFillColorDistance),
                    $"club {id}: the two crest fills must stay distinguishable");
            }
            TestContext.WriteLine($"[identity-fills] worst fill distance over {Sample} clubs = {worst} (floor {I.MinFillColorDistance})");
        }

        // ------------------------------------------------------- THE ✅ (b): legible emblem

        [Test]
        public void Emblem_IsAlwaysLegibleOnThePrimary()
        {
            int worst = int.MaxValue;
            for (int id = 1; id <= Sample; id++)
            {
                ClubIdentity ci = Gen(id);
                int contrast = System.Math.Abs(ci.Crest.FillA.Luminance - ci.Crest.Emblem.Luminance);
                if (contrast < worst) worst = contrast;
                Assert.That(contrast, Is.GreaterThanOrEqualTo(100),
                    $"club {id}: initials drawn on the primary must be legible");
                // The palette's Neutral is the same legible colour.
                Assert.That(ci.Colors.Neutral, Is.EqualTo(ci.Crest.Emblem));
            }
            TestContext.WriteLine($"[identity-legible] worst emblem-vs-primary luminance contrast over {Sample} clubs = {worst}");
        }

        // ------------------------------------------------------- spread

        [Test]
        public void Identities_AreWellSpread_AcrossClubs()
        {
            var primaries = new HashSet<int>();
            for (int id = 1; id <= Sample; id++)
                primaries.Add(Gen(id).Colors.Primary.Packed);

            TestContext.WriteLine($"[identity-spread] {primaries.Count} distinct primary colours over {Sample} clubs");
            Assert.That(primaries.Count, Is.GreaterThanOrEqualTo((int)(Sample * 0.9)),
                "primary colours should be well-spread, not clustered");
        }

        // ------------------------------------------------------- crest geometry coverage

        [Test]
        public void EveryCrestShapeAndPattern_IsReachable()
        {
            var shapes = new int[4];
            var patterns = new int[7];
            for (int id = 1; id <= Sample; id++)
            {
                CrestDesign c = Gen(id).Crest;
                shapes[(int)c.Shape]++;
                patterns[(int)c.Pattern]++;
            }

            TestContext.WriteLine($"[identity-crest] shapes (Shield/Circle/Diamond/RoundedSquare) = {shapes[0]}/{shapes[1]}/{shapes[2]}/{shapes[3]}");
            TestContext.WriteLine($"[identity-crest] patterns (Solid/VHalves/HHalves/Sash/VStripes/Hoops/Quarters) = "
                + $"{patterns[0]}/{patterns[1]}/{patterns[2]}/{patterns[3]}/{patterns[4]}/{patterns[5]}/{patterns[6]}");

            for (int i = 0; i < shapes.Length; i++)
                Assert.That(shapes[i], Is.GreaterThan(0), $"crest shape {i} should appear");
            for (int i = 0; i < patterns.Length; i++)
                Assert.That(patterns[i], Is.GreaterThan(0), $"crest pattern {i} should appear");
        }

        // ------------------------------------------------------- helpers

        private static void AssertColorValid(RgbColor c)
        {
            Assert.That(c.R, Is.InRange(0, 255));
            Assert.That(c.G, Is.InRange(0, 255));
            Assert.That(c.B, Is.InRange(0, 255));
        }

        private static bool SameIdentity(ClubIdentity a, ClubIdentity b)
        {
            return a.ClubId == b.ClubId
                && a.Colors.Primary == b.Colors.Primary
                && a.Colors.Secondary == b.Colors.Secondary
                && a.Colors.Accent == b.Colors.Accent
                && a.Colors.Neutral == b.Colors.Neutral
                && a.Crest.Shape == b.Crest.Shape
                && a.Crest.Pattern == b.Crest.Pattern
                && a.Crest.FillA == b.Crest.FillA
                && a.Crest.FillB == b.Crest.FillB
                && a.Crest.Trim == b.Crest.Trim
                && a.Crest.Emblem == b.Crest.Emblem;
        }
    }
}
