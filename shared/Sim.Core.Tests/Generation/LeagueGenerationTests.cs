using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.Generation
{
    [TestFixture]
    public class LeagueGenerationTests
    {
        private static League Generate(ulong seed, LeagueGenerationOptions? options = null) =>
            new LeagueGenerator(options).Generate(new Pcg32(seed));

        private static IEnumerable<Player> AllPlayers(League league) =>
            league.Clubs.SelectMany(c => c.Squad.Players);

        [Test]
        public void SameSeed_ProducesIdenticalLeague()
        {
            string a = JsonSerializer.Serialize(Generate(123456));
            string b = JsonSerializer.Serialize(Generate(123456));

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentLeagues()
        {
            string a = JsonSerializer.Serialize(Generate(1));
            string b = JsonSerializer.Serialize(Generate(2));

            Assert.That(b, Is.Not.EqualTo(a));
        }

        [Test]
        public void Generates20Clubs_With22PlayersEach_AndUniqueIds()
        {
            League league = Generate(42);

            Assert.That(league.Clubs, Has.Count.EqualTo(20));
            Assert.That(league.Clubs.All(c => c.Squad.Players.Count == 22), Is.True);

            var playerIds = AllPlayers(league).Select(p => p.Id).ToList();
            Assert.That(playerIds.Distinct().Count(), Is.EqualTo(playerIds.Count), "Player ids must be unique");

            var clubNames = league.Clubs.Select(c => c.Name).ToList();
            Assert.That(clubNames.Distinct().Count(), Is.EqualTo(clubNames.Count), "Club names must be unique");
        }

        [Test]
        public void SquadsFollowTheTemplate()
        {
            League league = Generate(7);

            foreach (Club club in league.Clubs)
            {
                foreach (var (role, expected) in SquadTemplate.Default)
                {
                    int actual = club.Squad.Players.Count(p => p.Role == role);
                    Assert.That(actual, Is.EqualTo(expected),
                        $"{club.Name}: expected {expected} x {role}");
                }
            }
        }

        [Test]
        public void Ages_AreWithinBounds_WithPlausibleMean()
        {
            League league = Generate(99);
            var ages = AllPlayers(league).Select(p => p.Age).ToList();

            Assert.That(ages.Min(), Is.GreaterThanOrEqualTo(17));
            Assert.That(ages.Max(), Is.LessThanOrEqualTo(36));
            Assert.That(ages.Average(), Is.InRange(22.5, 27.5), "Squad age should peak in the mid-20s");
        }

        [Test]
        public void Goalkeepers_AreTheOnlyOnesWhoCanKeep()
        {
            League league = Generate(2024);

            var keepers = AllPlayers(league).Where(p => p.Role == PositionRole.Goalkeeper).ToList();
            var outfielders = AllPlayers(league).Where(p => p.Role != PositionRole.Goalkeeper).ToList();

            Assert.That(keepers.Average(p => p.Attributes.Goalkeeping), Is.GreaterThan(45.0));
            Assert.That(outfielders.Max(p => p.Attributes.Goalkeeping), Is.LessThanOrEqualTo(15));
        }

        [Test]
        public void League_HasAStrengthHierarchy()
        {
            League league = Generate(555);

            var averages = league.Clubs
                .Select(c => c.Squad.Players.Average(p => (double)PlayerRating.Overall(p)))
                .ToList();

            Assert.That(averages.Max() - averages.Min(), Is.GreaterThanOrEqualTo(8.0),
                "Top and bottom clubs should be clearly apart");
            Assert.That(averages.Average(), Is.InRange(45.0, 72.0));
        }

        [Test]
        public void Potential_IsNeverBelowCurrentOverall()
        {
            League league = Generate(31337);

            foreach (Player p in AllPlayers(league))
                Assert.That(p.Development.Potential, Is.GreaterThanOrEqualTo(PlayerRating.Overall(p)),
                    $"{p.FullName} (age {p.Age})");
        }

        [Test]
        public void YoungPlayers_HaveMoreHeadroomThanVeterans()
        {
            League league = Generate(808);

            double youthHeadroom = AllPlayers(league).Where(p => p.Age <= 21)
                .Average(p => p.Development.Potential - PlayerRating.Overall(p));
            double veteranHeadroom = AllPlayers(league).Where(p => p.Age >= 30)
                .Average(p => p.Development.Potential - PlayerRating.Overall(p));

            Assert.That(youthHeadroom, Is.GreaterThan(veteranHeadroom + 3.0));
        }

        [Test]
        public void NoDuplicateFullNames_InsideAnySquad()
        {
            League league = Generate(4242);

            foreach (Club club in league.Clubs)
            {
                var names = club.Squad.Players.Select(p => p.FullName).ToList();
                Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count), club.Name);
            }
        }
    }
}
