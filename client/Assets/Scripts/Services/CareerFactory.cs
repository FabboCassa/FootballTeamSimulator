using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Fts.Services
{
    /// <summary>
    /// Builds new careers: a seeded two-division world plus first-season
    /// fixtures (Sim.Core). Seeding uses wall-clock time — allowed here
    /// (host side), never inside Sim.Core. The Pcg32 sequence ids below keep
    /// every generation stream independent; the save migration reuses them,
    /// so they must never change.
    /// </summary>
    public sealed class CareerFactory
    {
        public const ulong Div2GenSequence = 55;
        public const ulong FixtureSequence = 777;
        public const ulong Div2FixtureSequence = 778;
        public const int Div2FirstClubId = 101;
        public const int Div2FirstPlayerId = 5001;

        private readonly BalanceConfig _config = new BalanceConfig();

        public ulong NewSeed()
        {
            return unchecked((ulong)DateTime.UtcNow.Ticks ^ ((ulong)Environment.TickCount << 32));
        }

        /// <summary>Two divisions, top first. Same seed => identical world.</summary>
        public List<League> GenerateWorld(ulong seed)
        {
            var div1 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 1,
                Division = 1,
                LeagueName = "Lega Cartone",
                FirstClubId = 1,
                FirstPlayerId = 1
            }, _config).Generate(new Pcg32(seed));

            var div2 = new LeagueGenerator(new LeagueGenerationOptions
            {
                LeagueId = 2,
                Division = 2,
                LeagueName = "Lega Cartone 2",
                FirstClubId = Div2FirstClubId,
                FirstPlayerId = Div2FirstPlayerId
            }, _config).Generate(new Pcg32(seed, Div2GenSequence));

            return new List<League> { div1, div2 };
        }

        public CareerState Create(ulong seed, List<League> leagues, int userClubId)
        {
            // Seed initial transfer budgets (task 5.2b) so the world is coherent from creation;
            // the start-of-season window re-seeds them identically when the career opens.
            new BudgetModel(_config).SeedBudgets(leagues);

            // Give the user's club a scouting department (task 5.4b) so scouting is meaningful
            // from day one; AI clubs keep the base scout level. Facilities (task 5.5) will own
            // and let the user upgrade these later.
            SeedUserScouts(leagues, userClubId);

            return new CareerState
            {
                Seed = seed,
                Leagues = leagues,
                UserClubId = userClubId,
                CreatedUtc = DateTime.UtcNow.ToString("u"),
                Season = BuildFirstSeason(seed, leagues)
            };
        }

        /// <summary>
        /// Seeds the user's club a small scouting department (task 5.4b): three scouts whose best
        /// level (3) drives how fast watched players' ranges narrow (~7 weeks to full knowledge at
        /// ScoutingBalance defaults). AI clubs are left without scouts and scout at the base level.
        /// </summary>
        private static void SeedUserScouts(List<League> leagues, int userClubId)
        {
            Club userClub = null;
            foreach (League league in leagues)
            {
                Club c = league.FindClub(userClubId);
                if (c != null) { userClub = c; break; }
            }
            if (userClub == null)
                return;

            userClub.Scouts = new List<Scout>
            {
                new Scout { Id = 1, Name = "Chief Scout", Level = 3 },
                new Scout { Id = 2, Name = "Scout", Level = 3 },
                new Scout { Id = 3, Name = "Scout", Level = 2 }
            };
        }

        public Season BuildFirstSeason(ulong seed, List<League> leagues)
        {
            var season = new Season();
            var generator = new FixtureGenerator(_config.Season);

            List<Fixture> first = generator.Generate(leagues[0], new Pcg32(seed, FixtureSequence), 1);
            season.Fixtures.AddRange(first);

            if (leagues.Count > 1)
                season.Fixtures.AddRange(
                    generator.Generate(leagues[1], new Pcg32(seed, Div2FixtureSequence), first.Count + 1));

            return season;
        }
    }
}
