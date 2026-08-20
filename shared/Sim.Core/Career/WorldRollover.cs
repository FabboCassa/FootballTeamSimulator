using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Career
{
    /// <summary>What a multi-nation season rollover did (task 11.1).</summary>
    public sealed class WorldRolloverResult
    {
        public int EndedYear { get; set; }

        /// <summary>Champion of each nation's tier 1, keyed by nation code. Only simulated tiers appear.</summary>
        public Dictionary<string, int> ChampionByNation { get; set; } = new Dictionary<string, int>();

        /// <summary>Clubs that moved up a tier, in the order they were processed (nation, then tier).</summary>
        public List<int> PromotedClubIds { get; set; } = new List<int>();

        /// <summary>Clubs that moved down a tier.</summary>
        public List<int> RelegatedClubIds { get; set; } = new List<int>();

        /// <summary>Clubs whose squad was topped up after being promoted into a fuller-detail tier.</summary>
        public List<int> SquadsToppedUp { get; set; } = new List<int>();

        /// <summary>The next season's fixtures for the PLAYABLE leagues — the career season.</summary>
        public Season NewCareerSeason { get; set; } = new Season();
    }

    /// <summary>
    /// End-of-season processing for a multi-nation world (task 11.1). The two-division career is
    /// still handled by <see cref="SeasonRollover"/>, untouched; this is its bigger sibling and it
    /// differs in three ways that matter:
    ///
    ///   • promotion/relegation runs PER NATION, down each nation's own pyramid, so England's
    ///     bottom club does not drop into Spain;
    ///   • it spans detail levels — a club promoted out of a BACKGROUND tier into a PLAYABLE one
    ///     has an 18-man squad in a 22-man division, so its squad is topped up on the way in
    ///     (deterministically, from the nation's own naming culture);
    ///   • it rolls both calendars: the career season for the playable leagues and the world's own
    ///     background season for the cheap ones.
    ///
    /// DATA-ONLY tiers never take part: they have no fixtures, so they have no table to be promoted
    /// from. Their players still age, because the world should not stand still where nobody looks.
    /// </summary>
    public sealed class WorldRollover
    {
        /// <summary>Odd constants decorrelating the per-year streams.</summary>
        private const ulong YearSeedMix = 0xA24BAED4963EE407UL;
        private const ulong BackgroundSeedMix = 0xC2B2AE3D27D4EB4FUL;
        private const ulong TopUpSeedMix = 0xD6E8FEB86659FD93UL;

        /// <summary>
        /// Fixture stream base for the playable divisions. Public because the host builds season 1
        /// off the very same base (Fts.Services.CareerFactory) and the two must not drift: season 2
        /// scheduled off a different stream than season 1 would be a silent, seed-dependent bug.
        /// </summary>
        public const ulong CareerFixtureSequenceBase = 20000UL;

        /// <summary>The stream SeasonRollover uses; kept for worlds migrated from a pre-11.1 save.</summary>
        private const ulong LegacyFixtureSequenceBase = 777UL;
        private const ulong BackgroundFixtureSequenceBase = 30000UL;

        private readonly BalanceConfig _config;
        private readonly Dictionary<string, NameCulture> _cultures;

        public WorldRollover(BalanceConfig? config = null, Dictionary<string, NameCulture>? cultures = null)
        {
            _config = config ?? new BalanceConfig();
            _cultures = cultures ?? CultureDatabase.BuiltIn();
        }

        /// <summary>
        /// Mutates the world (club movement, ageing, squad top-ups) and returns the new career
        /// season. The world's background season is replaced in place. Throws if a playable fixture
        /// is still unplayed — the same guard the two-division rollover has.
        /// </summary>
        public WorldRolloverResult EndSeason(World world, Season careerSeason, ulong worldSeed)
        {
            foreach (Fixture fixture in careerSeason.Fixtures)
            {
                if (!fixture.Played)
                    throw new InvalidOperationException($"Season {careerSeason.Year} still has unplayed fixtures (id {fixture.Id}).");
            }

            // The background pyramid does not share the career calendar: a 28-club division plays 54
            // rounds where a 20-club one plays 38, so its season ends later. Finish whatever is still
            // outstanding before any table is read, or a nation's promotion would be decided on a
            // half-played table.
            new BackgroundLeagueProgressor(_config).AdvanceTo(world, int.MaxValue, worldSeed);

            var result = new WorldRolloverResult { EndedYear = careerSeason.Year };
            int nextYear = careerSeason.Year + 1;

            foreach (Nation nation in world.Nations)
            {
                List<League> simulated = SimulatedTiers(nation);
                if (simulated.Count == 0)
                    continue;

                // EVERY table is computed BEFORE anything moves. Computing them tier by tier while
                // clubs are already being swapped is a real bug in a pyramid deeper than two: the
                // clubs just relegated into tier 2 sit there on zero points, so the tier 2/3 pass
                // would relegate them straight through to tier 3 in the same rollover.
                var tables = new List<List<LeagueTableRow>>(simulated.Count);
                foreach (League league in simulated)
                    tables.Add(LeagueTable.Compute(league, SeasonOf(league, world, careerSeason), _config.Season));

                if (simulated[0].Division == 1 && tables[0].Count > 0)
                    result.ChampionByNation[nation.Code] = tables[0][0].ClubId;

                for (int i = 0; i + 1 < simulated.Count; i++)
                    SwapClubs(simulated[i], tables[i], simulated[i + 1], tables[i + 1], result);
            }

            world.InvalidateIndex();

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                    {
                        foreach (Player player in club.Squad.Players)
                            player.Age++;
                    }
                }
            }

            TopUpPromotedSquads(world, nextYear, worldSeed, result);

            result.NewCareerSeason = BuildCareerSeason(world, nextYear, worldSeed);
            world.BackgroundSeason = BuildBackgroundSeason(world, nextYear, worldSeed);

            return result;
        }

        /// <summary>Tiers that actually play a season, top-first: everything except DATA-ONLY.</summary>
        private static List<League> SimulatedTiers(Nation nation)
        {
            var leagues = new List<League>();
            foreach (League league in nation.Leagues)
            {
                if (league.DetailLevel != LeagueDetailLevel.DataOnly)
                    leagues.Add(league);
            }

            leagues.Sort((a, b) => a.Division.CompareTo(b.Division));
            return leagues;
        }

        private static Season SeasonOf(League league, World world, Season careerSeason) =>
            league.DetailLevel == LeagueDetailLevel.Playable ? careerSeason : world.BackgroundSeason;

        private void SwapClubs(
            League upper, List<LeagueTableRow> upperTable,
            League lower, List<LeagueTableRow> lowerTable,
            WorldRolloverResult result)
        {
            int count = _config.Season.PromotedRelegatedCount;
            if (count <= 0)
                return;

            int moving = count;
            if (moving > upperTable.Count) moving = upperTable.Count;
            if (moving > lowerTable.Count) moving = lowerTable.Count;
            if (moving <= 0)
                return;

            var goingDown = new List<Club>(moving);
            for (int i = upperTable.Count - moving; i < upperTable.Count; i++)
            {
                Club? club = upper.FindClub(upperTable[i].ClubId);
                if (club != null)
                    goingDown.Add(club);
            }

            var goingUp = new List<Club>(moving);
            for (int i = 0; i < moving; i++)
            {
                Club? club = lower.FindClub(lowerTable[i].ClubId);
                if (club != null)
                    goingUp.Add(club);
            }

            foreach (Club club in goingDown)
            {
                upper.Clubs.Remove(club);
                lower.Clubs.Add(club);
                result.RelegatedClubIds.Add(club.Id);
            }

            foreach (Club club in goingUp)
            {
                lower.Clubs.Remove(club);
                upper.Clubs.Add(club);
                result.PromotedClubIds.Add(club.Id);
            }
        }

        /// <summary>
        /// A club coming up from a background tier arrives with a background-sized squad. Rather than
        /// let it play a full division a man short, it is filled to the full template at its own
        /// strength, with names from its nation's culture. New ids come from the world's dynamic
        /// block, so they can never collide with a generated one.
        /// </summary>
        private void TopUpPromotedSquads(World world, int year, ulong worldSeed, WorldRolloverResult result)
        {
            int target = SquadTemplate.TotalPlayers;

            foreach (Nation nation in world.Nations)
            {
                NameCulture? culture = FindCulture(nation.CultureId);

                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel != LeagueDetailLevel.Playable)
                        continue;

                    foreach (Club club in league.Clubs)
                    {
                        if (club.Squad.Players.Count >= target)
                            continue;

                        FillSquad(world, club, culture, nation.Code, year, worldSeed);
                        result.SquadsToppedUp.Add(club.Id);
                    }
                }
            }
        }

        private void FillSquad(World world, Club club, NameCulture? culture, string nationCode, int year, ulong worldSeed)
        {
            var rng = new Pcg32(
                worldSeed ^ ((ulong)(uint)club.Id * TopUpSeedMix) ^ ((ulong)(uint)year * YearSeedMix),
                7700UL);

            var generator = new PlayerGenerator(_config.Generation, culture);
            int baseline = BackgroundLeagueProgressor.StrengthOf(club);

            foreach (var (role, wanted) in SquadTemplate.Default)
            {
                int have = 0;
                foreach (Player player in club.Squad.Players)
                {
                    if (player.Role == role)
                        have++;
                }

                for (int i = have; i < wanted; i++)
                {
                    int target = AttributeScale.ClampSkill(
                        baseline + rng.NextInt(-_config.Generation.PlayerTargetNoise, _config.Generation.PlayerTargetNoise + 1));

                    Player recruit = generator.Generate(NextPlayerId(world), role, target, rng);
                    recruit.Nationality = nationCode;
                    club.Squad.Players.Add(recruit);
                }
            }
        }

        private static int NextPlayerId(World world)
        {
            if (world.NextGeneratedPlayerId < WorldIds.FirstDynamicPlayerId)
                world.NextGeneratedPlayerId = WorldIds.FirstDynamicPlayerId;

            return world.NextGeneratedPlayerId++;
        }

        private NameCulture? FindCulture(string cultureId)
        {
            if (!string.IsNullOrEmpty(cultureId)
                && _cultures.TryGetValue(cultureId, out NameCulture? culture)
                && culture != null
                && culture.IsUsable)
            {
                return culture;
            }

            return null;
        }

        private Season BuildCareerSeason(World world, int year, ulong worldSeed)
        {
            var season = new Season { Year = year, CurrentDay = 1 };
            var generator = new FixtureGenerator(_config.Season);
            int nextFixtureId = 1;

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel != LeagueDetailLevel.Playable)
                        continue;

                    // A league migrated from a pre-11.1 save has no nation code, and its fixtures
                    // must keep coming off the stream SeasonRollover used, or an in-progress career
                    // would get a different calendar the moment it is upgraded.
                    var rng = new Pcg32(
                        worldSeed ^ ((ulong)(uint)year * YearSeedMix),
                        string.IsNullOrEmpty(league.NationCode)
                            ? LegacyFixtureSequenceBase + (ulong)(uint)league.Division
                            : CareerFixtureSequenceBase + (ulong)(uint)league.Id);

                    List<Fixture> fixtures = generator.Generate(league, rng, nextFixtureId);
                    nextFixtureId += fixtures.Count;
                    season.Fixtures.AddRange(fixtures);
                }
            }

            return season;
        }

        private Season BuildBackgroundSeason(World world, int year, ulong worldSeed)
        {
            var season = new Season { Year = year, CurrentDay = 1 };
            var generator = new FixtureGenerator(_config.Season);
            int nextFixtureId = 1;

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    if (league.DetailLevel != LeagueDetailLevel.Background)
                        continue;

                    var rng = new Pcg32(
                        (worldSeed ^ BackgroundSeedMix) ^ ((ulong)(uint)year * YearSeedMix),
                        BackgroundFixtureSequenceBase + (ulong)(uint)league.Id);

                    List<Fixture> fixtures = generator.Generate(league, rng, nextFixtureId);
                    nextFixtureId += fixtures.Count;
                    season.Fixtures.AddRange(fixtures);
                }
            }

            return season;
        }
    }
}
