using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    [TestFixture]
    public class SeasonRolloverTests
    {
        private const ulong WorldSeed = 1122334455;
        private const int SafetyCap = 400;

        private static (List<League> leagues, Season season) NewWorld()
        {
            var div1 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 1,
                Division = 1,
                LeagueName = "D1"
            }).Generate(new Pcg32(WorldSeed));

            var div2 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 2,
                Division = 2,
                LeagueName = "D2",
                FirstClubId = 101,
                FirstPlayerId = 5001
            }).Generate(new Pcg32(WorldSeed, 55));

            var leagues = new List<League> { div1, div2 };

            var season = new Season();
            List<Fixture> first = new FixtureGenerator().Generate(div1, new Pcg32(WorldSeed, 777), 1);
            season.Fixtures.AddRange(first);
            season.Fixtures.AddRange(new FixtureGenerator().Generate(div2, new Pcg32(WorldSeed, 778), first.Count + 1));

            return (leagues, season);
        }

        private static void PlayFullSeason(List<League> leagues, Season season)
        {
            var progressor = new SeasonProgressor();
            int guard = 0;
            while (season.Fixtures.Any(f => !f.Played) && guard++ < SafetyCap)
                progressor.AdvanceDay(leagues, season, WorldSeed);

            Assert.That(season.Fixtures.All(f => f.Played), Is.True, "Season must complete within the day cap.");
        }

        [Test]
        public void World_HasUniqueClubAndPlayerIds_AcrossDivisions()
        {
            (List<League> leagues, _) = NewWorld();

            var clubIds = leagues.SelectMany(l => l.Clubs).Select(c => c.Id).ToList();
            var playerIds = leagues.SelectMany(l => l.Clubs).SelectMany(c => c.Squad.Players).Select(p => p.Id).ToList();

            Assert.That(clubIds.Distinct().Count(), Is.EqualTo(clubIds.Count));
            Assert.That(playerIds.Distinct().Count(), Is.EqualTo(playerIds.Count));
        }

        [Test]
        public void DivisionTwo_IsWeakerOnAverage()
        {
            (List<League> leagues, _) = NewWorld();

            double Avg(League league) =>
                league.Clubs.SelectMany(c => c.Squad.Players).Average(p => PlayerRating.Overall(p));

            Assert.That(Avg(leagues[1]), Is.LessThan(Avg(leagues[0]) - 5),
                "Division 2 squads should be clearly weaker than division 1.");
        }

        [Test]
        public void Rollover_SwapsExactlyTheRightClubs()
        {
            (List<League> leagues, Season season) = NewWorld();
            PlayFullSeason(leagues, season);

            List<LeagueTableRow> d1Table = LeagueTable.Compute(leagues[0], season);
            List<LeagueTableRow> d2Table = LeagueTable.Compute(leagues[1], season);
            var expectedDown = d1Table.Skip(17).Select(r => r.ClubId).ToList();
            var expectedUp = d2Table.Take(3).Select(r => r.ClubId).ToList();
            int expectedChampion = d1Table[0].ClubId;

            RolloverResult result = new SeasonRollover().EndSeason(leagues, season, WorldSeed);

            Assert.That(result.ChampionClubId, Is.EqualTo(expectedChampion));
            Assert.That(result.RelegatedClubIds, Is.EqualTo(expectedDown));
            Assert.That(result.PromotedClubIds, Is.EqualTo(expectedUp));

            var d1Ids = leagues[0].Clubs.Select(c => c.Id).ToList();
            var d2Ids = leagues[1].Clubs.Select(c => c.Id).ToList();
            Assert.That(d1Ids, Has.Count.EqualTo(20));
            Assert.That(d2Ids, Has.Count.EqualTo(20));
            Assert.That(expectedUp.All(id => d1Ids.Contains(id)), Is.True, "Promoted clubs must be in division 1.");
            Assert.That(expectedDown.All(id => d2Ids.Contains(id)), Is.True, "Relegated clubs must be in division 2.");
        }

        [Test]
        public void Rollover_AgesPlayers_AndStartsACleanSeason()
        {
            (List<League> leagues, Season season) = NewWorld();
            PlayFullSeason(leagues, season);

            var agesBefore = leagues.SelectMany(l => l.Clubs).SelectMany(c => c.Squad.Players)
                .ToDictionary(p => p.Id, p => p.Age);

            RolloverResult result = new SeasonRollover().EndSeason(leagues, season, WorldSeed);
            Season newSeason = result.NewSeason;

            foreach (Player p in leagues.SelectMany(l => l.Clubs).SelectMany(c => c.Squad.Players))
                Assert.That(p.Age, Is.EqualTo(agesBefore[p.Id] + 1));

            Assert.That(newSeason.Year, Is.EqualTo(season.Year + 1));
            Assert.That(newSeason.CurrentDay, Is.EqualTo(1));
            Assert.That(newSeason.Fixtures, Has.Count.EqualTo(760));
            Assert.That(newSeason.Fixtures.All(f => !f.Played), Is.True);
            Assert.That(newSeason.Scorers, Is.Empty);
            Assert.That(newSeason.Fixtures.Select(f => f.Id).Distinct().Count(), Is.EqualTo(760),
                "Fixture ids must stay unique across divisions.");
        }

        [Test]
        public void Rollover_Throws_WhenSeasonUnfinished()
        {
            (List<League> leagues, Season season) = NewWorld();

            Assert.That(() => new SeasonRollover().EndSeason(leagues, season, WorldSeed),
                Throws.InvalidOperationException);
        }
    }
}
