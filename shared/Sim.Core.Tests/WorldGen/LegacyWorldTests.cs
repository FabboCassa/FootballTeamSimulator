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
    /// Task 11.1b — a career started before the world model must survive the upgrade untouched.
    ///
    /// The save migration lifts a pre-11.1 flat list of divisions into a one-nation World
    /// (<see cref="LegacyWorld"/>). These tests pin the promise that makes that safe: same clubs,
    /// same ids, same promotion and relegation, and — the easy one to get wrong — the same fixture
    /// calendar, because the new rollover schedules from a different RNG stream than the old one and
    /// an upgraded career must not have its season silently reshuffled underneath it.
    /// </summary>
    [TestFixture]
    public class LegacyWorldTests
    {
        private const ulong Seed = 20260820UL;

        private static readonly BalanceConfig Config = new BalanceConfig();

        /// <summary>The pre-11.1 two-division world, exactly as CareerFactory built it before 11.1b.</summary>
        private static List<League> TwoDivisions()
        {
            return new List<League>
            {
                new LeagueGenerator(new LeagueGenerationOptions
                {
                    LeagueId = 1, Division = 1, LeagueName = "Lega Cartone", FirstClubId = 1, FirstPlayerId = 1
                }, Config).Generate(new Pcg32(Seed)),

                new LeagueGenerator(new LeagueGenerationOptions
                {
                    LeagueId = 2, Division = 2, LeagueName = "Lega Cartone 2", FirstClubId = 101, FirstPlayerId = 5001
                }, Config).Generate(new Pcg32(Seed, 55))
            };
        }

        private static Season FirstSeason(List<League> leagues)
        {
            var season = new Season();
            var generator = new FixtureGenerator(Config.Season);
            List<Fixture> first = generator.Generate(leagues[0], new Pcg32(Seed, 777), 1);
            season.Fixtures.AddRange(first);
            season.Fixtures.AddRange(generator.Generate(leagues[1], new Pcg32(Seed, 778), first.Count + 1));
            return season;
        }

        /// <summary>Plays a season without the engine — enough to make a table, and fast.</summary>
        private static void PlayEverything(Season season, IReadOnlyList<League> leagues)
        {
            var strength = new Dictionary<int, int>();
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                    strength[club.Id] = BackgroundLeagueProgressor.StrengthOf(club);
            }

            foreach (Fixture fixture in season.Fixtures)
            {
                if (fixture.Played)
                    continue;

                QuickResultResolver.Resolve(fixture, strength[fixture.HomeClubId], strength[fixture.AwayClubId], Seed, Config);
            }
        }

        [Test]
        public void Wrap_KeepsEveryClubAndPlayerExactlyWhereItWas()
        {
            List<League> leagues = TwoDivisions();
            int firstClubId = leagues[0].Clubs[0].Id;
            int firstPlayerId = leagues[0].Clubs[0].Squad.Players[0].Id;
            int totalPlayers = leagues.Sum(l => l.Clubs.Sum(c => c.Squad.Players.Count));

            World world = LegacyWorld.Wrap(leagues);

            Assert.That(world.Nations.Count, Is.EqualTo(1));
            Assert.That(world.Nations[0].Code, Is.EqualTo(LegacyWorld.Code));
            Assert.That(world.PlayableLeagues().Count, Is.EqualTo(2));
            Assert.That(world.ClubCount(), Is.EqualTo(40));
            Assert.That(world.PlayerCount(), Is.EqualTo(totalPlayers));
            Assert.That(world.PlayableLeagues()[0].Clubs[0].Id, Is.EqualTo(firstClubId));
            Assert.That(world.PlayableLeagues()[0].Clubs[0].Squad.Players[0].Id, Is.EqualTo(firstPlayerId));
            Assert.That(world.BackgroundSeason.Fixtures.Count, Is.EqualTo(0));
        }

        [Test]
        public void Wrap_MarksTheWorldAsLegacy()
        {
            World world = LegacyWorld.Wrap(TwoDivisions());

            Assert.That(world.Scope.IsLegacy, Is.True);

            // The empty nation code is what keeps the old fixture stream alive; see the rollover test.
            foreach (League league in world.PlayableLeagues())
            {
                Assert.That(league.NationCode, Is.EqualTo(string.Empty));
                Assert.That(league.DetailLevel, Is.EqualTo(LeagueDetailLevel.Playable));
            }
        }

        [Test]
        public void Wrap_OrdersTheDivisionsTopFirst()
        {
            List<League> leagues = TwoDivisions();
            leagues.Reverse(); // hand them over bottom-first

            World world = LegacyWorld.Wrap(leagues);

            Assert.That(world.PlayableLeagues()[0].Division, Is.EqualTo(1));
            Assert.That(world.PlayableLeagues()[1].Division, Is.EqualTo(2));
        }

        [Test]
        public void AddLeague_GrowsAMigratedWorld_AsTheV2ToV3MigrationDoes()
        {
            List<League> leagues = TwoDivisions();
            World world = LegacyWorld.Wrap(new List<League> { leagues[0] });

            LegacyWorld.AddLeague(world, leagues[1]);

            Assert.That(world.PlayableLeagues().Count, Is.EqualTo(2));
            Assert.That(world.PlayableLeagues()[1].Division, Is.EqualTo(2));
            Assert.That(world.FindClub(leagues[1].Clubs[0].Id), Is.Not.Null);
        }

        [Test]
        public void MigratedWorld_RollsOverExactlyAsTheOldRolloverDid()
        {
            // Two independent copies of the same world, played to the same results.
            List<League> oldWay = TwoDivisions();
            Season oldSeason = FirstSeason(oldWay);
            PlayEverything(oldSeason, oldWay);

            List<League> newWay = TwoDivisions();
            Season newSeason = FirstSeason(newWay);
            PlayEverything(newSeason, newWay);

            RolloverResult before = new SeasonRollover(Config).EndSeason(oldWay, oldSeason, Seed);

            World world = LegacyWorld.Wrap(newWay);
            WorldRolloverResult after = new WorldRollover(Config).EndSeason(world, newSeason, Seed);

            Assert.That(after.ChampionByNation[LegacyWorld.Code], Is.EqualTo(before.ChampionClubId));
            Assert.That(after.PromotedClubIds, Is.EqualTo(before.PromotedClubIds));
            Assert.That(after.RelegatedClubIds, Is.EqualTo(before.RelegatedClubIds));
            Assert.That(after.SquadsToppedUp.Count, Is.EqualTo(0));
        }

        [Test]
        public void MigratedWorld_KeepsTheOldFixtureCalendar()
        {
            List<League> oldWay = TwoDivisions();
            Season oldSeason = FirstSeason(oldWay);
            PlayEverything(oldSeason, oldWay);

            List<League> newWay = TwoDivisions();
            Season newSeason = FirstSeason(newWay);
            PlayEverything(newSeason, newWay);

            Season expected = new SeasonRollover(Config).EndSeason(oldWay, oldSeason, Seed).NewSeason;
            Season actual = new WorldRollover(Config).EndSeason(LegacyWorld.Wrap(newWay), newSeason, Seed).NewCareerSeason;

            Assert.That(actual.Year, Is.EqualTo(expected.Year));
            Assert.That(actual.Fixtures.Count, Is.EqualTo(expected.Fixtures.Count));

            for (int i = 0; i < expected.Fixtures.Count; i++)
            {
                Assert.That(actual.Fixtures[i].HomeClubId, Is.EqualTo(expected.Fixtures[i].HomeClubId));
                Assert.That(actual.Fixtures[i].AwayClubId, Is.EqualTo(expected.Fixtures[i].AwayClubId));
                Assert.That(actual.Fixtures[i].Day, Is.EqualTo(expected.Fixtures[i].Day));
                Assert.That(actual.Fixtures[i].Round, Is.EqualTo(expected.Fixtures[i].Round));
            }
        }

        [Test]
        public void AGeneratedWorld_DoesNotUseTheLegacyFixtureStream()
        {
            // The legacy stream is keyed by division, so two nations' tier 1 would share it. A
            // generated league carries a nation code, which is what routes it to the per-league stream.
            var scope = new WorldScope { Size = DatabaseSize.Small };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });

            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Config).Generate(Seed);

            foreach (League league in world.PlayableLeagues())
                Assert.That(league.NationCode, Is.EqualTo("ITA"));
        }
    }
}
