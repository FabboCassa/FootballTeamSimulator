using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Career
{
    /// <summary>
    /// Runs the leagues the player is not playing in (task 11.1). The host calls this once a day,
    /// right after <see cref="SeasonProgressor.AdvanceDay(System.Collections.Generic.IReadOnlyList{League}, Season, ulong, System.Collections.Generic.IReadOnlyDictionary{int, Match.LineupPlan}?, System.Collections.Generic.IReadOnlyDictionary{int, Tactics.TacticContext}?, System.Collections.Generic.IReadOnlyDictionary{int, System.Collections.Generic.IReadOnlyList{Match.MatchRule}}?, Difficulty.DifficultyContext?)"/>,
    /// and every background fixture due by that day gets a score from
    /// <see cref="QuickResultResolver"/>.
    ///
    /// Background fixtures live in <see cref="World.BackgroundSeason"/>, deliberately apart from the
    /// career season, so the expensive daily loop over the player's own divisions never walks tens of
    /// thousands of matches it has nothing to do with.
    /// </summary>
    public sealed class BackgroundLeagueProgressor
    {
        private readonly BalanceConfig _config;

        public BackgroundLeagueProgressor(BalanceConfig? config = null)
        {
            _config = config ?? new BalanceConfig();
        }

        /// <summary>
        /// Resolves every unplayed background fixture due on or before <paramref name="day"/>.
        /// Idempotent: calling it twice for the same day is a no-op. Returns how many were played.
        /// </summary>
        public int AdvanceTo(World world, int day, ulong worldSeed)
        {
            Season season = world.BackgroundSeason;
            if (season.Fixtures.Count == 0)
            {
                season.CurrentDay = day;
                return 0;
            }

            int resolved = 0;

            foreach (Fixture fixture in season.Fixtures)
            {
                if (fixture.Played || fixture.Day > day)
                    continue;

                Club? home = world.FindClub(fixture.HomeClubId);
                Club? away = world.FindClub(fixture.AwayClubId);
                if (home == null || away == null)
                    continue;

                QuickResultResolver.Resolve(fixture, StrengthOf(home), StrengthOf(away), worldSeed, _config);
                resolved++;
            }

            season.CurrentDay = day;
            return resolved;
        }

        /// <summary>
        /// The last day any background league plays on. Bigger divisions play more rounds than the
        /// player's own, so the world's season can end AFTER the career season does — the host uses
        /// this to know when the whole world is finished. 0 when there are no background leagues.
        /// </summary>
        public static int LastDay(World world)
        {
            int last = 0;
            foreach (Fixture fixture in world.BackgroundSeason.Fixtures)
            {
                if (fixture.Day > last)
                    last = fixture.Day;
            }

            return last;
        }

        /// <summary>
        /// A club's strength for the cheap resolver: the generated baseline when it has one, otherwise
        /// the average overall of its squad (an old save, or a club built by something else).
        /// </summary>
        public static int StrengthOf(Club club)
        {
            if (club.Strength > 0)
                return club.Strength;

            int count = club.Squad.Players.Count;
            if (count == 0)
                return 1;

            int total = 0;
            foreach (Player player in club.Squad.Players)
                total += PlayerRating.Overall(player);

            return total / count;
        }
    }
}
