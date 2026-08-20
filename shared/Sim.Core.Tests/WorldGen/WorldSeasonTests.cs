using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.WorldGen
{
    /// <summary>
    /// Task 11.1 — running a multi-nation world: the cheap resolver for background leagues, the
    /// daily background progressor, and promotion/relegation down a nation's pyramid (including the
    /// case where a club is promoted across a detail boundary and arrives short of a full squad).
    /// </summary>
    [TestFixture]
    public class WorldSeasonTests
    {
        private const ulong Seed = 20260819UL;

        private static readonly BalanceConfig Config = new BalanceConfig();

        private static World Generate(DatabaseSize size, params (string Code, int Tiers)[] playable)
        {
            var scope = new WorldScope { Size = size };
            foreach ((string code, int tiers) in playable)
                scope.Playable.Add(new PlayableNation { Code = code, PlayableTiers = tiers });

            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Config).Generate(Seed);
        }

        /// <summary>
        /// A first career season for the playable leagues — what the host (task 11.1b) will build.
        /// </summary>
        private static Season CareerSeason(World world)
        {
            var season = new Season { Year = 1, CurrentDay = 1 };
            var generator = new FixtureGenerator(Config.Season);
            int nextId = 1;

            foreach (League league in world.PlayableLeagues())
            {
                List<Fixture> fixtures = generator.Generate(league, new Pcg32(Seed, 20000UL + (ulong)league.Id), nextId);
                nextId += fixtures.Count;
                season.Fixtures.AddRange(fixtures);
            }

            return season;
        }

        /// <summary>Plays a whole season without the match engine — fast, and enough to make a table.</summary>
        private static void PlayEverything(World world, Season careerSeason)
        {
            foreach (Fixture fixture in careerSeason.Fixtures)
            {
                if (fixture.Played)
                    continue;

                Club? home = world.FindClub(fixture.HomeClubId);
                Club? away = world.FindClub(fixture.AwayClubId);
                if (home == null || away == null)
                    continue;

                QuickResultResolver.Resolve(fixture, home.Strength, away.Strength, Seed, Config);
            }

            careerSeason.CurrentDay = careerSeason.Fixtures.Count == 0 ? 1 : careerSeason.Fixtures.Max(f => f.Day);
            new BackgroundLeagueProgressor(Config).AdvanceTo(world, int.MaxValue, Seed);
        }

        [Test]
        public void QuickResolver_IsDeterministic()
        {
            var a = new Fixture { Id = 42 };
            var b = new Fixture { Id = 42 };

            QuickResultResolver.Resolve(a, 70, 62, Seed, Config);
            QuickResultResolver.Resolve(b, 70, 62, Seed, Config);

            Assert.That(b.HomeGoals, Is.EqualTo(a.HomeGoals));
            Assert.That(b.AwayGoals, Is.EqualTo(a.AwayGoals));
            Assert.That(a.Played, Is.True);
        }

        [Test]
        public void QuickResolver_GivesTheStrongerSideTheBetterOfIt()
        {
            int strongWins = 0;
            int weakWins = 0;

            for (int id = 1; id <= 400; id++)
            {
                var fixture = new Fixture { Id = id };
                QuickResultResolver.Resolve(fixture, 75, 55, Seed, Config);

                if (fixture.HomeGoals > fixture.AwayGoals) strongWins++;
                else if (fixture.HomeGoals < fixture.AwayGoals) weakWins++;
            }

            Assert.That(strongWins, Is.GreaterThan(weakWins * 2));
        }

        [Test]
        public void QuickResolver_ScoresStayInFootballTerritory()
        {
            int goals = 0;
            const int matches = 2000;

            for (int id = 1; id <= matches; id++)
            {
                var fixture = new Fixture { Id = id };
                QuickResultResolver.Resolve(fixture, 68, 66, Seed, Config);

                Assert.That(fixture.HomeGoals, Is.LessThan(12));
                Assert.That(fixture.AwayGoals, Is.LessThan(12));
                goals += fixture.HomeGoals + fixture.AwayGoals;
            }

            double average = (double)goals / matches;
            Assert.That(average, Is.GreaterThan(2.0));
            Assert.That(average, Is.LessThan(3.4));
        }

        [Test]
        public void BackgroundProgressor_PlaysOnlyWhatIsDue_AndIsIdempotent()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 1));
            Assert.That(world.BackgroundSeason.Fixtures.Count, Is.GreaterThan(0));

            int firstDay = world.BackgroundSeason.Fixtures.Min(f => f.Day);
            var progressor = new BackgroundLeagueProgressor(Config);

            int played = progressor.AdvanceTo(world, firstDay, Seed);
            Assert.That(played, Is.GreaterThan(0));
            Assert.That(world.BackgroundSeason.Fixtures.Any(f => !f.Played), Is.True);

            int again = progressor.AdvanceTo(world, firstDay, Seed);
            Assert.That(again, Is.EqualTo(0));

            foreach (Fixture fixture in world.BackgroundSeason.Fixtures)
            {
                if (fixture.Day > firstDay)
                    Assert.That(fixture.Played, Is.False);
            }
        }

        [Test]
        public void BackgroundResults_DoNotDependOnHowManyDaysAreAdvancedAtOnce()
        {
            World stepped = Generate(DatabaseSize.Medium, ("ITA", 1));
            World jumped = Generate(DatabaseSize.Medium, ("ITA", 1));

            var progressor = new BackgroundLeagueProgressor(Config);
            int lastDay = BackgroundLeagueProgressor.LastDay(stepped);

            for (int day = 1; day <= lastDay; day++)
                progressor.AdvanceTo(stepped, day, Seed);

            progressor.AdvanceTo(jumped, lastDay, Seed);

            for (int i = 0; i < stepped.BackgroundSeason.Fixtures.Count; i++)
            {
                Assert.That(jumped.BackgroundSeason.Fixtures[i].HomeGoals, Is.EqualTo(stepped.BackgroundSeason.Fixtures[i].HomeGoals));
                Assert.That(jumped.BackgroundSeason.Fixtures[i].AwayGoals, Is.EqualTo(stepped.BackgroundSeason.Fixtures[i].AwayGoals));
            }
        }

        [Test]
        public void Rollover_PromotesAndRelegatesDownAllThreeTiersOfANation()
        {
            World world = Generate(DatabaseSize.Small, ("ITA", 3));
            Season season = CareerSeason(world);
            PlayEverything(world, season);

            Nation italy = world.FindNation("ITA")!;
            List<LeagueTableRow> tier1 = LeagueTable.Compute(italy.FindTier(1)!, season, Config.Season);
            List<LeagueTableRow> tier2 = LeagueTable.Compute(italy.FindTier(2)!, season, Config.Season);

            int relegatedFromTop = tier1[tier1.Count - 1].ClubId;
            int promotedFromSecond = tier2[0].ClubId;

            WorldRolloverResult result = new WorldRollover(Config).EndSeason(world, season, Seed);

            Assert.That(result.EndedYear, Is.EqualTo(1));
            Assert.That(result.ChampionByNation["ITA"], Is.EqualTo(tier1[0].ClubId));

            // Two boundaries in a three-tier pyramid => twice the promoted/relegated count.
            Assert.That(result.PromotedClubIds.Count, Is.EqualTo(Config.Season.PromotedRelegatedCount * 2));
            Assert.That(result.RelegatedClubIds.Count, Is.EqualTo(Config.Season.PromotedRelegatedCount * 2));

            Assert.That(italy.FindTier(2)!.Clubs.Any(c => c.Id == relegatedFromTop), Is.True);
            Assert.That(italy.FindTier(1)!.Clubs.Any(c => c.Id == promotedFromSecond), Is.True);

            foreach (League league in italy.Leagues)
                Assert.That(league.Clubs.Count % 2, Is.EqualTo(0));
        }

        [Test]
        public void Rollover_NeverMovesAClubBetweenNations()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 2));
            Season season = CareerSeason(world);
            PlayEverything(world, season);

            var nationOf = new Dictionary<int, string>();
            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                        nationOf[club.Id] = nation.Code;
                }
            }

            new WorldRollover(Config).EndSeason(world, season, Seed);

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                        Assert.That(nationOf[club.Id], Is.EqualTo(nation.Code));
                }
            }
        }

        [Test]
        public void Rollover_TopsUpASquadPromotedIntoAFullDetailTier()
        {
            // England is playable to tier 2 and its tier 3 is a background league, so the three
            // clubs coming up arrive with a background-sized squad.
            World world = Generate(DatabaseSize.Large, ("ENG", 2));
            Nation england = world.FindNation("ENG")!;

            Assert.That(england.FindTier(3)!.DetailLevel, Is.EqualTo(LeagueDetailLevel.Background));
            Assert.That(england.FindTier(3)!.Clubs[0].Squad.Players.Count, Is.LessThan(SquadTemplate.TotalPlayers));

            Season season = CareerSeason(world);
            PlayEverything(world, season);

            WorldRolloverResult result = new WorldRollover(Config).EndSeason(world, season, Seed);

            Assert.That(result.SquadsToppedUp.Count, Is.EqualTo(Config.Season.PromotedRelegatedCount));

            foreach (League league in world.PlayableLeagues())
            {
                foreach (Club club in league.Clubs)
                    Assert.That(club.Squad.Players.Count, Is.EqualTo(SquadTemplate.TotalPlayers));
            }

            List<int> ids = world.PlayableLeagues().SelectMany(l => l.Clubs).SelectMany(c => c.Squad.Players).Select(p => p.Id).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        }

        [Test]
        public void Rollover_AgesTheWholeWorld_EvenWhereNobodyLooks()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 1));
            Season season = CareerSeason(world);
            PlayEverything(world, season);

            League dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly)[0];
            Player watched = dataOnly.Clubs[0].Squad.Players[0];
            int ageBefore = watched.Age;

            new WorldRollover(Config).EndSeason(world, season, Seed);

            Assert.That(watched.Age, Is.EqualTo(ageBefore + 1));
        }

        [Test]
        public void Rollover_BuildsBothCalendarsForTheNewSeason()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 2));
            Season season = CareerSeason(world);
            PlayEverything(world, season);

            int playableFixtures = season.Fixtures.Count;
            WorldRolloverResult result = new WorldRollover(Config).EndSeason(world, season, Seed);

            Assert.That(result.NewCareerSeason.Year, Is.EqualTo(2));
            Assert.That(result.NewCareerSeason.Fixtures.Count, Is.EqualTo(playableFixtures));
            Assert.That(result.NewCareerSeason.Fixtures.All(f => !f.Played), Is.True);

            Assert.That(world.BackgroundSeason.Year, Is.EqualTo(2));
            Assert.That(world.BackgroundSeason.Fixtures.Count, Is.GreaterThan(0));
            Assert.That(world.BackgroundSeason.Fixtures.All(f => !f.Played), Is.True);
        }

        [Test]
        public void Rollover_RefusesToCloseASeasonWithUnplayedFixtures()
        {
            World world = Generate(DatabaseSize.Small, ("ITA", 2));
            Season season = CareerSeason(world);

            Assert.Throws<InvalidOperationException>(() => new WorldRollover(Config).EndSeason(world, season, Seed));
        }

        [Test]
        public void ANationWhoseTiersAreAllDataOnly_NeverRollsOver()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 1));
            League dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly)[0];
            List<int> before = dataOnly.Clubs.Select(c => c.Id).ToList();

            Season season = CareerSeason(world);
            PlayEverything(world, season);
            new WorldRollover(Config).EndSeason(world, season, Seed);

            Assert.That(dataOnly.Clubs.Select(c => c.Id).ToList(), Is.EqualTo(before));
        }
    }
}
