using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Sim.Core.Career
{
    /// <summary>Outcome of a season rollover, for the season-end screen.</summary>
    public sealed class RolloverResult
    {
        public int EndedYear { get; set; }

        /// <summary>Winner of the top division.</summary>
        public int ChampionClubId { get; set; }

        /// <summary>Clubs moved up a division (in final-table order).</summary>
        public List<int> PromotedClubIds { get; set; } = new List<int>();

        /// <summary>Clubs moved down a division (in final-table order).</summary>
        public List<int> RelegatedClubIds { get; set; } = new List<int>();

        public Season NewSeason { get; set; } = new Season();
    }

    /// <summary>
    /// End-of-season processing (task 2.7): promotion/relegation between
    /// adjacent divisions, +1 age for every player, and a fresh fixture list.
    /// New-season fixtures derive deterministically from (world seed, year).
    /// Development/decline curves arrive in 4.4; this only increments age.
    /// </summary>
    public sealed class SeasonRollover
    {
        /// <summary>Odd constant decorrelating per-year fixture seeds.</summary>
        private const ulong YearSeedMix = 0xA24BAED4963EE407UL;

        private readonly BalanceConfig _cfg;

        public SeasonRollover(BalanceConfig? config = null)
        {
            _cfg = config ?? new BalanceConfig();
        }

        /// <summary>
        /// Mutates the leagues (club movement, aging) and returns the result
        /// with the new season. Throws if the season is not finished.
        /// </summary>
        public RolloverResult EndSeason(List<League> leagues, Season season, ulong worldSeed)
        {
            foreach (Fixture fixture in season.Fixtures)
            {
                if (!fixture.Played)
                    throw new InvalidOperationException($"Season {season.Year} still has unplayed fixtures (id {fixture.Id}).");
            }

            // Stable top-down division order.
            var ordered = new List<League>(leagues);
            ordered.Sort((a, b) => a.Division.CompareTo(b.Division));

            var result = new RolloverResult { EndedYear = season.Year };

            List<LeagueTableRow> topTable = LeagueTable.Compute(ordered[0], season, _cfg.Season);
            result.ChampionClubId = topTable[0].ClubId;

            for (int i = 0; i + 1 < ordered.Count; i++)
                SwapClubs(ordered[i], ordered[i + 1], season, result);

            foreach (League league in ordered)
            {
                foreach (Club club in league.Clubs)
                {
                    foreach (Player player in club.Squad.Players)
                        player.Age++;
                }
            }

            result.NewSeason = BuildNewSeason(ordered, season.Year + 1, worldSeed);
            return result;
        }

        private void SwapClubs(League upper, League lower, Season season, RolloverResult result)
        {
            int count = _cfg.Season.PromotedRelegatedCount;

            List<LeagueTableRow> upperTable = LeagueTable.Compute(upper, season, _cfg.Season);
            List<LeagueTableRow> lowerTable = LeagueTable.Compute(lower, season, _cfg.Season);

            var goingDown = new List<Club>();
            for (int i = upperTable.Count - count; i < upperTable.Count; i++)
                goingDown.Add(upper.FindClub(upperTable[i].ClubId)!);

            var goingUp = new List<Club>();
            for (int i = 0; i < count; i++)
                goingUp.Add(lower.FindClub(lowerTable[i].ClubId)!);

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

        private Season BuildNewSeason(List<League> orderedLeagues, int year, ulong worldSeed)
        {
            var newSeason = new Season { Year = year, CurrentDay = 1 };

            int firstFixtureId = 1;
            foreach (League league in orderedLeagues)
            {
                var rng = new Pcg32(
                    worldSeed ^ ((ulong)year * YearSeedMix),
                    777UL + (ulong)league.Division);

                List<Fixture> fixtures = new FixtureGenerator(_cfg.Season).Generate(league, rng, firstFixtureId);
                firstFixtureId += fixtures.Count;
                newSeason.Fixtures.AddRange(fixtures);
            }

            return newSeason;
        }
    }
}
