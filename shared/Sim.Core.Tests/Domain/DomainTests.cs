using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Domain;

namespace Sim.Core.Tests.Domain
{
    [TestFixture]
    public class DomainTests
    {
        private static League BuildSampleLeague()
        {
            var league = new League { Id = 1, Name = "Serie Cartoon", Division = 1 };

            for (int c = 1; c <= 2; c++)
            {
                var club = new Club
                {
                    Id = c,
                    Name = $"Club {c}",
                    ShortName = $"CL{c}",
                    Coach = new Coach { Id = c, Name = $"Coach {c}", IsHuman = c == 1, Reputation = 40 + c }
                };

                for (int p = 0; p < 3; p++)
                {
                    club.Squad.Players.Add(new Player
                    {
                        Id = c * 100 + p,
                        FirstName = "Mario",
                        LastName = $"Rossi{p}",
                        Age = 20 + p,
                        Role = (PositionRole)p,
                        Attributes = { Pace = 60 + p, Shooting = 55, Goalkeeping = p == 0 ? 70 : 10 },
                        Condition = { Form = 50, Morale = 60, Fitness = 95 },
                        Development = { Potential = 80 },
                        Contract = { WeeklyWage = 1_000 + p, SeasonsRemaining = 3 }
                    });
                }

                league.Clubs.Add(club);
            }

            return league;
        }

        [Test]
        public void League_RoundTripsThroughJson()
        {
            var original = BuildSampleLeague();

            string json = JsonSerializer.Serialize(original);
            League restored = JsonSerializer.Deserialize<League>(json)!;

            // Deep equality via re-serialization.
            Assert.That(JsonSerializer.Serialize(restored), Is.EqualTo(json));

            // Spot checks.
            Assert.That(restored.Clubs, Has.Count.EqualTo(2));
            Assert.That(restored.Clubs[0].Squad.Players[0].FullName, Is.EqualTo("Mario Rossi0"));
            Assert.That(restored.Clubs[0].Squad.Players[0].Attributes.Pace, Is.EqualTo(60));
            Assert.That(restored.Clubs[1].Coach.Reputation, Is.EqualTo(42));
        }

        [Test]
        public void Skills_AreClampedTo1_100()
        {
            var attrs = new PlayerAttributes { Pace = 150, Shooting = -20, Passing = 100 };

            Assert.That(attrs.Pace, Is.EqualTo(100));
            Assert.That(attrs.Shooting, Is.EqualTo(1));
            Assert.That(attrs.Passing, Is.EqualTo(100));
        }

        [Test]
        public void Condition_IsClampedTo0_100()
        {
            var condition = new PlayerCondition { Form = -5, Morale = 250, Fitness = 0 };

            Assert.That(condition.Form, Is.EqualTo(0));
            Assert.That(condition.Morale, Is.EqualTo(100));
            Assert.That(condition.Fitness, Is.EqualTo(0));
        }

        [Test]
        public void Potential_IsClampedToSkillRange()
        {
            var dev = new PlayerDevelopment { Potential = 0 };
            Assert.That(dev.Potential, Is.EqualTo(1));

            dev.Potential = 999;
            Assert.That(dev.Potential, Is.EqualTo(100));
        }

        [Test]
        public void FullName_FallsBackToLastName()
        {
            var player = new Player { LastName = "Zoff" };
            Assert.That(player.FullName, Is.EqualTo("Zoff"));
        }
    }
}
