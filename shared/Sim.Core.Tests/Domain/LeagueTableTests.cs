using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Domain;

namespace Sim.Core.Tests.Domain
{
    [TestFixture]
    public class LeagueTableTests
    {
        private static League ThreeClubs()
        {
            var league = new League();
            league.Clubs.Add(new Club { Id = 1, Name = "A" });
            league.Clubs.Add(new Club { Id = 2, Name = "B" });
            league.Clubs.Add(new Club { Id = 3, Name = "C" });
            return league;
        }

        private static Season SeasonWith(params Fixture[] fixtures)
        {
            var season = new Season();
            season.Fixtures.AddRange(fixtures);
            return season;
        }

        private static Fixture Played(int home, int away, int homeGoals, int awayGoals) =>
            new Fixture { HomeClubId = home, AwayClubId = away, HomeGoals = homeGoals, AwayGoals = awayGoals, Played = true };

        [Test]
        public void PointsAndRecord_AreComputedCorrectly()
        {
            // A 3-0 C, B 1-0 C, A 1-1 B.
            var season = SeasonWith(
                Played(1, 3, 3, 0),
                Played(2, 3, 1, 0),
                Played(1, 2, 1, 1));

            List<LeagueTableRow> table = LeagueTable.Compute(ThreeClubs(), season);

            LeagueTableRow a = table.Single(r => r.ClubId == 1);
            Assert.That(a.Played, Is.EqualTo(2));
            Assert.That(a.Wins, Is.EqualTo(1));
            Assert.That(a.Draws, Is.EqualTo(1));
            Assert.That(a.Losses, Is.EqualTo(0));
            Assert.That(a.GoalsFor, Is.EqualTo(4));
            Assert.That(a.GoalsAgainst, Is.EqualTo(1));
            Assert.That(a.Points, Is.EqualTo(4));

            LeagueTableRow c = table.Single(r => r.ClubId == 3);
            Assert.That(c.Points, Is.EqualTo(0));
            Assert.That(c.Losses, Is.EqualTo(2));
        }

        [Test]
        public void EqualPoints_BrokenByGoalDifference()
        {
            // A and B both 4 points; A has GD +3, B has GD +1.
            var season = SeasonWith(
                Played(1, 3, 3, 0),
                Played(2, 3, 1, 0),
                Played(1, 2, 1, 1));

            List<LeagueTableRow> table = LeagueTable.Compute(ThreeClubs(), season);

            Assert.That(table.Select(r => r.ClubId), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void EqualPointsAndGoalDifference_BrokenByGoalsFor()
        {
            // A 2-1 C (GD +1, GF 2), B 1-0 C (GD +1, GF 1): both 3 points.
            var season = SeasonWith(
                Played(1, 3, 2, 1),
                Played(2, 3, 1, 0));

            List<LeagueTableRow> table = LeagueTable.Compute(ThreeClubs(), season);

            Assert.That(table.Select(r => r.ClubId), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ForeignFixtures_AreIgnored()
        {
            // A fixture between clubs of another division must not affect this table.
            var season = SeasonWith(
                Played(1, 2, 2, 0),
                Played(101, 102, 5, 5));

            List<LeagueTableRow> table = LeagueTable.Compute(ThreeClubs(), season);

            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table.Sum(r => r.Played), Is.EqualTo(2), "Only the in-league fixture counts.");
        }

        [Test]
        public void UnplayedFixtures_AreIgnored_AndAllClubsAppear()
        {
            var season = SeasonWith(new Fixture { HomeClubId = 1, AwayClubId = 2, Played = false });

            List<LeagueTableRow> table = LeagueTable.Compute(ThreeClubs(), season);

            Assert.That(table, Has.Count.EqualTo(3));
            Assert.That(table.All(r => r.Played == 0 && r.Points == 0), Is.True);
        }
    }
}
