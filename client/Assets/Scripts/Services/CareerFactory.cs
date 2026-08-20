using System;
using System.Collections.Generic;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Fts.Services
{
    /// <summary>
    /// Builds new careers: a seeded multi-nation world (task 11.1) plus the first season's
    /// fixtures for the divisions the player actually plays. Seeding uses wall-clock time —
    /// allowed here (host side), never inside Sim.Core.
    ///
    /// The Pcg32 sequence ids below keep every generation stream independent; the pre-11.1 save
    /// migrations reuse them, so they must never change even though a fresh career no longer
    /// generates its world that way.
    /// </summary>
    public sealed class CareerFactory
    {
        // --- pre-11.1 streams, kept ONLY for the save migrations in LocalJsonSaveRepository ---
        public const ulong Div2GenSequence = 55;
        public const ulong FixtureSequence = 777;
        public const ulong Div2FixtureSequence = 778;
        public const int Div2FirstClubId = 101;
        public const int Div2FirstPlayerId = 5001;

        /// <summary>
        /// Fixture stream base for a generated world's playable divisions (task 11.1b). Taken from
        /// WorldRollover so season 1 and every season after it are scheduled off the same stream.
        /// </summary>
        public const ulong WorldFixtureSequenceBase = WorldRollover.CareerFixtureSequenceBase;

        /// <summary>Nation a fresh career starts in unless the player picks another.</summary>
        public const string DefaultNation = "ITA";

        /// <summary>Tiers of it played at full detail by default — two, like the pre-11.1 world.</summary>
        public const int DefaultTiers = 2;

        private readonly BalanceConfig _config = new BalanceConfig();

        public ulong NewSeed()
        {
            return unchecked((ulong)DateTime.UtcNow.Ticks ^ ((ulong)Environment.TickCount << 32));
        }

        /// <summary>The scope a fresh career is offered before the player changes anything.</summary>
        public static WorldScope DefaultScope()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = DefaultNation, PlayableTiers = DefaultTiers });
            return scope;
        }

        /// <summary>The nations the career-setup screen can offer. Ordered as the atlas is.</summary>
        public static List<NationProfile> Atlas() => NationDatabase.BuiltIn();

        /// <summary>Same seed + same scope => identical world.</summary>
        public World GenerateWorld(ulong seed, WorldScope scope)
        {
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, _config).Generate(seed);
        }

        /// <summary>
        /// The playable nation ONLY, with nothing loaded around it (task 11.1b). Used by the
        /// career-setup screen, which needs the club list and nothing else: generating the full Large
        /// world on every tap of the nation row would cost a visible pause on a phone for information
        /// the screen does not show.
        ///
        /// Safe because club identity does not depend on the database size: ids come from
        /// <see cref="WorldIds"/>, which reserves a block per (nation, tier), and every generation
        /// stream is keyed by (seed, nation index, tier) alone. The club the player picks here is
        /// byte-identical to the one in the full world built at <see cref="Create"/>.
        /// </summary>
        public World GeneratePreview(ulong seed, WorldScope scope)
        {
            var preview = new WorldScope { Size = DatabaseSize.Custom };
            foreach (PlayableNation nation in scope.Playable)
                preview.Playable.Add(new PlayableNation { Code = nation.Code, PlayableTiers = nation.PlayableTiers });

            var options = new WorldGenerationOptions
            {
                Scope = preview,
                CustomPreset = new DatabaseSizePreset
                {
                    MinNationReputation = 101,     // no other nation clears the bar
                    BackgroundMinReputation = 101, // and nothing is simulated in the background
                    BackgroundTiers = 0,
                    DataOnlyTiers = 0,
                    BackgroundSquadSize = 18,
                    DataOnlyPlayersPerClub = 5
                }
            };

            return new WorldGenerator(options, _config).Generate(seed);
        }

        /// <summary>Starting scouting-facility tier for the user's club — drives a 3-scout, level-3 department.</summary>
        public const int UserStartScoutingTier = 3;

        public CareerState Create(ulong seed, World world, int userClubId, DifficultyLevel difficulty)
        {
            // Finances, facilities and coaches are seeded for every division that actually plays a
            // season — the player's own AND the background ones. Background clubs need them because
            // one of them can be promoted into a playable tier at the next rollover and would
            // otherwise arrive with no money and a blank coach. Data-only clubs are skipped: they
            // have no fixtures, no table and no way into the career.
            List<League> living = LivingLeagues(world);

            new FinanceProgressor(_config).SeedWorld(living);

            // The user's scouting department is the scouting FACILITY (task 5.5): start at tier 3
            // (a 3-scout, level-3 dept) so scouting is meaningful from day one. AI clubs keep tier 1.
            FacilitySync.ApplyScoutingTier(world.FindClub(userClubId), UserStartScoutingTier, _config);

            // Seed the coach career (task 5.6): reputation + opening objective scaled to squad
            // strength, neutral board confidence; the user's coach is then marked human.
            new CoachCareerProgressor(_config).SeedWorld(living);

            Club userClub = world.FindClub(userClubId);
            if (userClub != null)
                userClub.Coach.IsHuman = true;

            var career = new CareerState
            {
                Seed = seed,
                World = world,
                UserClubId = userClubId,
                Difficulty = difficulty,
                CreatedUtc = DateTime.UtcNow.ToString("u"),
                Season = BuildFirstSeason(seed, world)
            };

            // Seed a welcome notification (task 6.2) so the Inbox isn't empty on a fresh career.
            career.Inbox.Add(new InboxMessage
            {
                Id = 1,
                Category = InboxCategory.System,
                Key = "inbox.welcome",
                Args = new List<string> { userClub != null ? userClub.Name : string.Empty },
                Year = career.Season.Year,
                Day = career.Season.CurrentDay,
                Read = false
            });

            return career;
        }

        /// <summary>Playable + background divisions: everything that plays a season somewhere.</summary>
        public static List<League> LivingLeagues(World world)
        {
            var leagues = new List<League>();
            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel != LeagueDetailLevel.DataOnly)
                        leagues.Add(league);
                }
            }

            return leagues;
        }

        /// <summary>
        /// First-season fixtures for the PLAYABLE divisions. The background world already carries
        /// its own fixture list (WorldGenerator built it), so it is not touched here.
        /// </summary>
        public Season BuildFirstSeason(ulong seed, World world)
        {
            var season = new Season();
            var generator = new FixtureGenerator(_config.Season);
            int nextFixtureId = 1;

            foreach (League league in world.PlayableLeagues())
            {
                List<Fixture> fixtures = generator.Generate(
                    league, new Pcg32(seed, WorldFixtureSequenceBase + (ulong)league.Id), nextFixtureId);

                nextFixtureId += fixtures.Count;
                season.Fixtures.AddRange(fixtures);
            }

            return season;
        }
    }
}
