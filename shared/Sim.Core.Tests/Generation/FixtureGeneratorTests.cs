using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.Generation
{
    [TestFixture]
    public class FixtureGeneratorTests
    {
        private static League DummyLeague(int clubCount)
        {
            var league = new League { Name = "Test League" };
            for (int i = 1; i <= clubCount; i++)
                league.Clubs.Add(new Club { Id = i, Name = $"Club {i}" });
            return league;
        }

        private static List<Fixture> Generate(int clubCount, ulong seed = 42, SeasonBalance? cfg = null) =>
            new FixtureGenerator(cfg).Generate(DummyLeague(clubCount), new Pcg32(seed));

        [Test]
        public void TwentyClubs_Produces38Rounds_380Fixtures()
        {
            List<Fixture> fixtures = Generate(20);

            Assert.That(fixtures, Has.Count.EqualTo(380));
            Assert.That(fixtures.Max(f => f.Round), Is.EqualTo(38));
            Assert.That(fixtures.Count(f => f.Round == 1), Is.EqualTo(10));
        }

        [Test]
        public void EachOrderedPair_MeetsExactlyOnce()
        {
            List<Fixture> fixtures = Generate(8);

            var orderedPairs = fixtures.Select(f => (f.HomeClubId, f.AwayClubId)).ToList();

            Assert.That(orderedPairs.Distinct().Count(), Is.EqualTo(orderedPairs.Count),
                "No ordered pair may repeat.");
            Assert.That(orderedPairs, Has.Count.EqualTo(8 * 7),
                "Every ordered pair must occur: home and away meeting for each club pair.");
            Assert.That(fixtures.All(f => f.HomeClubId != f.AwayClubId), Is.True);
        }

        [Test]
        public void EachClub_PlaysExactlyOncePerRound()
        {
            List<Fixture> fixtures = Generate(20);

            foreach (var round in fixtures.GroupBy(f => f.Round))
            {
                var clubs = round.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).ToList();
                Assert.That(clubs.Distinct().Count(), Is.EqualTo(20),
                    $"Round {round.Key} must feature all 20 clubs exactly once.");
            }
        }

        [Test]
        public void Days_FollowConfiguredCalendar()
        {
            var cfg = new SeasonBalance { FirstMatchDay = 5, DaysBetweenRounds = 4 };
            List<Fixture> fixtures = Generate(6, 42, cfg);

            foreach (Fixture f in fixtures)
                Assert.That(f.Day, Is.EqualTo(5 + (f.Round - 1) * 4));
        }

        [Test]
        public void SameSeed_ProducesIdenticalSchedule()
        {
            string a = JsonSerializer.Serialize(Generate(20, 7));
            string b = JsonSerializer.Serialize(Generate(20, 7));

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void OddClubCount_Throws()
        {
            Assert.That(() => Generate(7), Throws.InvalidOperationException);
        }
    }
}
