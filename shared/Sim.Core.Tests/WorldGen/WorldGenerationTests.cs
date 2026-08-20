using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Tests.WorldGen
{
    /// <summary>
    /// Task 11.1 — the expanded league database. What these tests defend:
    /// the world is deterministic, its ids are stable and unique, the scope knobs actually change
    /// how much is loaded, and the pre-11.1 two-division generator is untouched.
    /// </summary>
    [TestFixture]
    public class WorldGenerationTests
    {
        private const ulong Seed = 20260819UL;

        private static WorldGenerationOptions Options(DatabaseSize size, params (string Code, int Tiers)[] playable)
        {
            var scope = new WorldScope { Size = size };
            foreach ((string code, int tiers) in playable)
                scope.Playable.Add(new PlayableNation { Code = code, PlayableTiers = tiers });

            return new WorldGenerationOptions { Scope = scope };
        }

        private static World Generate(DatabaseSize size, params (string Code, int Tiers)[] playable) =>
            new WorldGenerator(Options(size, playable)).Generate(Seed);

        [Test]
        public void SameSeedAndScope_ProducesIdenticalWorld()
        {
            string a = JsonSerializer.Serialize(Generate(DatabaseSize.Medium, ("ITA", 3)));
            string b = JsonSerializer.Serialize(Generate(DatabaseSize.Medium, ("ITA", 3)));

            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentWorlds()
        {
            WorldGenerationOptions options = Options(DatabaseSize.Small, ("ITA", 1));
            string a = JsonSerializer.Serialize(new WorldGenerator(options).Generate(1));
            string b = JsonSerializer.Serialize(new WorldGenerator(options).Generate(2));

            Assert.That(b, Is.Not.EqualTo(a));
        }

        [Test]
        public void PlayableNation_RunsItsChosenTiersAtFullDetail()
        {
            World world = Generate(DatabaseSize.Medium, ("ITA", 3));
            Nation? italy = world.FindNation("ITA");

            Assert.That(italy, Is.Not.Null);

            List<League> playable = italy!.Leagues.Where(l => l.DetailLevel == LeagueDetailLevel.Playable).ToList();
            Assert.That(playable.Count, Is.EqualTo(3));

            foreach (League league in playable)
            {
                Assert.That(league.NationCode, Is.EqualTo("ITA"));
                Assert.That(league.Clubs.Count % 2, Is.EqualTo(0));

                foreach (Club club in league.Clubs)
                    Assert.That(club.Squad.Players.Count, Is.EqualTo(SquadTemplate.TotalPlayers));
            }
        }

        [Test]
        public void EveryClubAndPlayerId_IsUniqueAcrossTheWorld()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 3));

            List<int> clubIds = world.Nations.SelectMany(n => n.Leagues).SelectMany(l => l.Clubs).Select(c => c.Id).ToList();
            List<int> playerIds = world.Nations.SelectMany(n => n.Leagues).SelectMany(l => l.Clubs)
                .SelectMany(c => c.Squad.Players).Select(p => p.Id).ToList();

            Assert.That(clubIds.Distinct().Count(), Is.EqualTo(clubIds.Count));
            Assert.That(playerIds.Distinct().Count(), Is.EqualTo(playerIds.Count));
        }

        [Test]
        public void GeneratedIds_NeverCollideWithALegacyTwoDivisionWorld()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 3));

            int lowestClub = world.Nations.SelectMany(n => n.Leagues).SelectMany(l => l.Clubs).Min(c => c.Id);
            int lowestPlayer = world.Nations.SelectMany(n => n.Leagues).SelectMany(l => l.Clubs)
                .SelectMany(c => c.Squad.Players).Min(p => p.Id);

            Assert.That(lowestClub, Is.GreaterThan(1000));
            Assert.That(lowestPlayer, Is.GreaterThan(100000));
        }

        [Test]
        public void ClubIdentity_IsTheSameWhicheverDatabaseSizeIsChosen()
        {
            Club small = Generate(DatabaseSize.Small, ("ITA", 3)).FindNation("ITA")!.FindTier(1)!.Clubs[0];
            Club large = Generate(DatabaseSize.Large, ("ITA", 3)).FindNation("ITA")!.FindTier(1)!.Clubs[0];

            Assert.That(large.Id, Is.EqualTo(small.Id));
            Assert.That(large.Name, Is.EqualTo(small.Name));
            Assert.That(large.Strength, Is.EqualTo(small.Strength));
        }

        [Test]
        public void BiggerPreset_LoadsMoreOfTheWorld()
        {
            World small = Generate(DatabaseSize.Small, ("ITA", 3));
            World medium = Generate(DatabaseSize.Medium, ("ITA", 3));
            World large = Generate(DatabaseSize.Large, ("ITA", 3));

            Assert.That(medium.PlayerCount(), Is.GreaterThan(small.PlayerCount()));
            Assert.That(large.PlayerCount(), Is.GreaterThan(medium.PlayerCount()));
            Assert.That(large.ClubCount(), Is.GreaterThan(medium.ClubCount()));
            Assert.That(medium.ClubCount(), Is.GreaterThan(small.ClubCount()));
        }

        [Test]
        public void BackgroundLeaguesHaveFixtures_DataOnlyLeaguesDoNot()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 3));

            List<League> background = world.LeaguesAt(LeagueDetailLevel.Background);
            List<League> dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly);

            Assert.That(background.Count, Is.GreaterThan(0));
            Assert.That(world.BackgroundSeason.Fixtures.Count, Is.GreaterThan(0));

            var backgroundClubs = new HashSet<int>(background.SelectMany(l => l.Clubs).Select(c => c.Id));
            var dataOnlyClubs = new HashSet<int>(dataOnly.SelectMany(l => l.Clubs).Select(c => c.Id));

            foreach (Fixture fixture in world.BackgroundSeason.Fixtures)
            {
                Assert.That(backgroundClubs.Contains(fixture.HomeClubId), Is.True);
                Assert.That(dataOnlyClubs.Contains(fixture.HomeClubId), Is.False);
            }
        }

        [Test]
        public void PlayableLeagueFixtures_AreNotInTheBackgroundSeason()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 3));
            var playableClubs = new HashSet<int>(world.PlayableLeagues().SelectMany(l => l.Clubs).Select(c => c.Id));

            foreach (Fixture fixture in world.BackgroundSeason.Fixtures)
                Assert.That(playableClubs.Contains(fixture.HomeClubId), Is.False);
        }

        [Test]
        public void Scope_NamingAnUnknownNation_Throws()
        {
            WorldGenerationOptions options = Options(DatabaseSize.Small, ("XXX", 1));

            Assert.Throws<InvalidOperationException>(() => new WorldGenerator(options).Generate(Seed));
        }

        [Test]
        public void StrongerNations_GetStrongerClubs()
        {
            World world = Generate(DatabaseSize.Large, ("ITA", 1));

            int england = world.FindNation("ENG")!.FindTier(1)!.Clubs[0].Strength;
            int iceland = world.FindNation("ISL")!.FindTier(1)!.Clubs[0].Strength;

            Assert.That(england, Is.GreaterThan(iceland));
        }

        [Test]
        public void PlayersAreGivenANationality_MostlyTheLeaguesOwn()
        {
            World world = Generate(DatabaseSize.Medium, ("ITA", 1));
            List<Player> players = world.FindNation("ITA")!.FindTier(1)!.Clubs.SelectMany(c => c.Squad.Players).ToList();

            Assert.That(players.All(p => p.Nationality.Length > 0), Is.True);

            // A big league imports heavily (Italy sits around half and half), but its own nation is
            // still by far the single largest group, and there is a real spread of foreigners.
            int domestic = players.Count(p => p.Nationality == "ITA");
            int biggestForeign = players.Where(p => p.Nationality != "ITA")
                .GroupBy(p => p.Nationality).Max(g => g.Count());

            Assert.That(domestic, Is.GreaterThan(biggestForeign * 3));
            Assert.That(domestic, Is.GreaterThan(players.Count / 3));
            Assert.That(players.Count(p => p.Nationality != "ITA"), Is.GreaterThan(0));
        }

        [Test]
        public void TheWorldRoundTripsThroughJson()
        {
            World world = Generate(DatabaseSize.Medium, ("ITA", 2));
            string json = JsonSerializer.Serialize(world);

            World? reloaded = JsonSerializer.Deserialize<World>(json);

            Assert.That(reloaded, Is.Not.Null);
            Assert.That(JsonSerializer.Serialize(reloaded), Is.EqualTo(json));
            Assert.That(reloaded!.FindClub(world.PlayableLeagues()[0].Clubs[0].Id), Is.Not.Null);
        }

        [Test]
        public void TheAtlasIsSaneEverywhere()
        {
            foreach (NationProfile profile in NationDatabase.BuiltIn())
            {
                Assert.That(profile.Divisions.Count, Is.GreaterThan(0));
                Assert.That(profile.Code.Length, Is.EqualTo(3));

                foreach (DivisionProfile division in profile.Divisions)
                {
                    Assert.That(division.ClubCount % 2, Is.EqualTo(0));
                    Assert.That(division.ClubCount, Is.GreaterThan(8));
                }
            }
        }

        [Test]
        public void TheBigFiveRunThreeTiers()
        {
            List<NationProfile> atlas = NationDatabase.BuiltIn();

            foreach (string code in new[] { "ENG", "ESP", "ITA", "GER", "FRA" })
            {
                NationProfile? profile = NationDatabase.Find(atlas, code);
                Assert.That(profile, Is.Not.Null);
                Assert.That(profile!.Divisions.Count, Is.GreaterThan(2));
            }
        }

        [Test]
        public void EveryNationPointsAtACultureThatCanNameIt()
        {
            Dictionary<string, NameCulture> cultures = CultureDatabase.BuiltIn();

            foreach (NationProfile profile in NationDatabase.BuiltIn())
            {
                Assert.That(cultures.ContainsKey(profile.CultureId), Is.True);

                NameCulture culture = cultures[profile.CultureId];
                Assert.That(culture.IsUsable, Is.True);

                int needed = profile.Divisions.Sum(d => d.ClubCount);
                Assert.That(culture.Towns.Length * culture.ClubPrefixes.Length, Is.GreaterThan(needed));
            }
        }

        [Test]
        public void SquadTemplate_AtFullSize_IsTheOriginalTemplate()
        {
            Assert.That(SquadTemplate.For(SquadTemplate.TotalPlayers), Is.EqualTo(SquadTemplate.Default));
        }

        [Test]
        public void SquadTemplate_AtASmallerSize_StillAddsUp()
        {
            foreach (int size in new[] { 2, 5, 7, 11, 16, 18, 20 })
            {
                int total = SquadTemplate.For(size).Sum(entry => entry.Count);
                Assert.That(total, Is.EqualTo(size));
                Assert.That(SquadTemplate.For(size).First(e => e.Role == PositionRole.Goalkeeper).Count, Is.GreaterThan(0));
            }
        }

        [Test]
        public void TheLegacyTwoDivisionGenerator_IsUntouched()
        {
            League legacy = new LeagueGenerator().Generate(new Pcg32(Seed));

            Assert.That(legacy.NationCode, Is.EqualTo(string.Empty));
            Assert.That(legacy.DetailLevel, Is.EqualTo(LeagueDetailLevel.Playable));
            Assert.That(legacy.Clubs.Count, Is.EqualTo(20));
            Assert.That(legacy.Clubs[0].Squad.Players.Count, Is.EqualTo(SquadTemplate.TotalPlayers));
            Assert.That(legacy.Clubs[0].Squad.Players[0].Nationality, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ACustomPresetOverridesTheSizeKnobs()
        {
            WorldGenerationOptions options = Options(DatabaseSize.Custom, ("ITA", 1));
            options.CustomPreset = new DatabaseSizePreset
            {
                MinNationReputation = 101, // nothing but the playable nation
                BackgroundMinReputation = 101,
                BackgroundTiers = 0,
                DataOnlyTiers = 0,
                BackgroundSquadSize = 18,
                DataOnlyPlayersPerClub = 5
            };

            World world = new WorldGenerator(options, new BalanceConfig()).Generate(Seed);

            Assert.That(world.Nations.Count, Is.EqualTo(1));
            Assert.That(world.Nations[0].Code, Is.EqualTo("ITA"));
        }
    }
}
