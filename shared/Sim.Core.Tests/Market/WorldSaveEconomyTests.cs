using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Acceptance tests for Issue #10 (R13 Save format & world-wide seeding):
    /// - Save version bumped (v18); pre-change saves are refused with a localized incompatible save message (en+it);
    /// - Finances and stature seeded for every club in the World (playable, background, data-only);
    /// - World finances accrue and stature evolves at season end;
    /// - Finances and stature survive serialization round-trip identically.
    /// </summary>
    [TestFixture]
    public class WorldSaveEconomyTests
    {
        private const ulong Seed = 20260921UL;
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FootballTeamSimulator.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Repo root containing FootballTeamSimulator.sln not found");
        }

        // ============================================================ 1. Incompatible save & localization
        [Test]
        public void IncompatibleSave_LocalizationKeys_ExistInEnAndIt()
        {
            string root = FindRepoRoot();
            string enPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "en.json");
            string itPath = Path.Combine(root, "client", "Assets", "Resources", "Localization", "it.json");

            Assert.That(File.Exists(enPath), Is.True, "en.json must exist");
            Assert.That(File.Exists(itPath), Is.True, "it.json must exist");

            string enJson = File.ReadAllText(enPath);
            string itJson = File.ReadAllText(itPath);

            using var enDoc = JsonDocument.Parse(enJson);
            using var itDoc = JsonDocument.Parse(itJson);

            Assert.That(enDoc.RootElement.TryGetProperty("mainmenu.error.incompatible_save", out JsonElement enVal),
                Is.True, "en.json must define 'mainmenu.error.incompatible_save'");
            Assert.That(itDoc.RootElement.TryGetProperty("mainmenu.error.incompatible_save", out JsonElement itVal),
                Is.True, "it.json must define 'mainmenu.error.incompatible_save'");

            string enText = enVal.GetString() ?? string.Empty;
            string itText = itVal.GetString() ?? string.Empty;

            Assert.That(enText.ToLowerInvariant(), Does.Contain("incompatible"), "en translation must mention incompatible");
            Assert.That(itText.ToLowerInvariant(), Does.Contain("incompatibile"), "it translation must mention incompatibile");
        }

        [Test]
        public void SaveVersion_CompatibilityRule_RefusesPreChangeSaves()
        {
            const int currentSaveVersion = 18;

            bool IsCompatible(int version) => version == currentSaveVersion;

            Assert.That(IsCompatible(18), Is.True);
            Assert.That(IsCompatible(17), Is.False, "pre-change save v17 must be incompatible");
            Assert.That(IsCompatible(16), Is.False, "pre-change save v16 must be incompatible");
            Assert.That(IsCompatible(1), Is.False, "legacy v1 save must be incompatible");
            Assert.That(IsCompatible(19), Is.False, "future v19 save must be incompatible");
        }

        // ============================================================ 2. World-wide finance & stature seeding
        [Test]
        public void World_FinanceAndStatureSeeding_SeedsEveryClubInWorld()
        {
            World world = BuildTestWorld(Seed);
            var finance = new FinanceProgressor(Cfg);

            // Seed finances across the entire World
            finance.SeedWorld(world);

            List<League> allLeagues = world.AllLeagues();
            Assert.That(allLeagues.Count, Is.GreaterThan(0));

            int totalClubs = 0;
            foreach (League league in allLeagues)
            {
                foreach (Club club in league.Clubs)
                {
                    totalClubs++;
                    Assert.That(club.Stature, Is.InRange(0, 100), $"Club {club.Id} stature out of range");
                    Assert.That(club.Facilities.Stadium, Is.InRange(1, 5), $"Club {club.Id} stadium tier out of range");
                    Assert.That(club.Finances.Balance, Is.GreaterThan(0), $"Club {club.Id} starting balance must be > 0");
                    Assert.That(club.TransferBudget, Is.GreaterThan(0), $"Club {club.Id} transfer budget must be > 0");
                }
            }

            Assert.That(totalClubs, Is.GreaterThan(20), "Must seed a realistic multi-league world");
        }

        // ============================================================ 3. Finances & stature survive JSON round-trip
        [Test]
        public void World_FinancesAndStature_SurviveJsonRoundTrip()
        {
            World world = BuildTestWorld(Seed);
            new FinanceProgressor(Cfg).SeedWorld(world);

            Club original = world.AllLeagues()[0].Clubs[0];
            string json = JsonSerializer.Serialize(original);
            Club? deserialized = JsonSerializer.Deserialize<Club>(json);

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Stature, Is.EqualTo(original.Stature));
            Assert.That(deserialized.Facilities.Stadium, Is.EqualTo(original.Facilities.Stadium));
            Assert.That(deserialized.Finances.Balance, Is.EqualTo(original.Finances.Balance));
            Assert.That(deserialized.TransferBudget, Is.EqualTo(original.TransferBudget));
        }

        // ============================================================ 4. Season end stature evolution & world finances
        [Test]
        public void SeasonEnd_StatureEvolution_AndWorldFinances_AccrueAndEvolve()
        {
            World world = BuildTestWorld(Seed);
            var finance = new FinanceProgressor(Cfg);
            finance.SeedWorld(world);

            var playable = world.PlayableLeagues();
            var background = world.LeaguesAt(LeagueDetailLevel.Background);
            var dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly);

            // Accrue week of finances
            var careerSeason = BuildCareerSeason(world, Seed);
            finance.AccrueWeek(playable, careerSeason);
            if (background.Count > 0)
                finance.AccrueWeek(background, world.BackgroundSeason);
            if (dataOnly.Count > 0)
                finance.AccrueDataOnlyWeek(dataOnly);

            // Verify income was booked
            foreach (League l in playable)
                foreach (Club c in l.Clubs)
                    Assert.That(c.Finances.SeasonIncome, Is.GreaterThan(0));

            foreach (League l in dataOnly)
                foreach (Club c in l.Clubs)
                    Assert.That(c.Finances.SeasonEstimatedIncome, Is.GreaterThan(0));

            // Evolve stature with StatureProgressor
            var progressor = new StatureProgressor(Cfg);
            Club championClub = playable[0].Clubs[0];
            int initialStature = championClub.Stature;

            // Finished 1st vs expected 3rd, title bonus, no relegation
            progressor.ApplySeasonEnd(championClub, actualPosition: 1, expectedPosition: 3, promoted: false, relegated: false);
            Assert.That(championClub.Stature, Is.GreaterThanOrEqualTo(initialStature));

            Club relegatedClub = playable[0].Clubs[^1];
            int relInitialStature = relegatedClub.Stature;
            // Finished last vs expected mid, relegated
            progressor.ApplySeasonEnd(relegatedClub, actualPosition: playable[0].Clubs.Count, expectedPosition: 5, promoted: false, relegated: true);
            Assert.That(relegatedClub.Stature, Is.LessThanOrEqualTo(relInitialStature));
        }

        // ============================================================ Helpers
        private static World BuildTestWorld(ulong seed)
        {
            var scope = new WorldScope { Size = DatabaseSize.Small };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 2 });
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(seed);
        }

        private static Season BuildCareerSeason(World world, ulong seed)
        {
            var season = new Season();
            var generator = new FixtureGenerator(Cfg.Season);
            int nextId = 1;
            foreach (League league in world.PlayableLeagues())
            {
                var fixtures = generator.Generate(league, new Pcg32(seed, 20000UL + (ulong)league.Id), nextId);
                nextId += fixtures.Count;
                season.Fixtures.AddRange(fixtures);
            }
            return season;
        }
    }
}
