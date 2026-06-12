using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.Config
{
    [TestFixture]
    public class BalanceConfigTests
    {
        [Test]
        public void Defaults_RoundTripThroughJson()
        {
            var config = new BalanceConfig();

            string json = JsonSerializer.Serialize(config);
            BalanceConfig restored = JsonSerializer.Deserialize<BalanceConfig>(json)!;

            Assert.That(JsonSerializer.Serialize(restored), Is.EqualTo(json));
            Assert.That(restored.Generation.TopClubStrength, Is.EqualTo(72));
            Assert.That(restored.Match.HomeAdvantagePercent, Is.EqualTo(6));
        }

        [Test]
        public void PartialJsonOverride_KeepsOtherDefaults()
        {
            // A server-pushed config will typically override only a few values.
            const string json = "{\"Generation\":{\"TopClubStrength\":90}}";

            BalanceConfig config = JsonSerializer.Deserialize<BalanceConfig>(json)!;

            Assert.That(config.Generation.TopClubStrength, Is.EqualTo(90));
            // NOTE: section objects are replaced wholesale by deserializers, so a partial
            // section gets type defaults for its other values - this is the documented behaviour.
            Assert.That(config.Generation.BottomClubStrength, Is.EqualTo(52));
            Assert.That(config.Match.HomeAdvantagePercent, Is.EqualTo(6));
        }

        [Test]
        public void ConfigChange_ChangesGenerationOutput()
        {
            var strong = new BalanceConfig();
            strong.Generation.TopClubStrength = 90;
            strong.Generation.BottomClubStrength = 85;

            League defaultLeague = new LeagueGenerator().Generate(new Pcg32(777));
            League strongLeague = new LeagueGenerator(null, strong).Generate(new Pcg32(777));

            double defaultMean = defaultLeague.Clubs
                .SelectMany(c => c.Squad.Players)
                .Average(p => (double)PlayerRating.Overall(p));
            double strongMean = strongLeague.Clubs
                .SelectMany(c => c.Squad.Players)
                .Average(p => (double)PlayerRating.Overall(p));

            Assert.That(strongMean, Is.GreaterThan(defaultMean + 10.0),
                "Raising strength tunables must visibly raise generated quality");
        }

        [Test]
        public void SameSeedSameConfig_StillIdentical()
        {
            var cfg = new BalanceConfig();
            cfg.Generation.SkillNoise = 4;

            string a = JsonSerializer.Serialize(new LeagueGenerator(null, cfg).Generate(new Pcg32(5)));
            string b = JsonSerializer.Serialize(new LeagueGenerator(null, cfg).Generate(new Pcg32(5)));

            Assert.That(b, Is.EqualTo(a));
        }
    }
}
