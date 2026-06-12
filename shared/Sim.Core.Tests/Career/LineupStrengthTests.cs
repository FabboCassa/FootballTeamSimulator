using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 2.6 acceptance: lineup strength must matter. Two identical worlds
    /// run a full season; in one, the chosen club fields its worst eleven all
    /// season. Deterministic per seed (no statistical flakiness): if this
    /// fails after a balance change, re-judge with the match harness.
    /// </summary>
    [TestFixture]
    public class LineupStrengthTests
    {
        private const ulong WorldSeed = 246813579;
        private const int SafetyCap = 400;

        [Test]
        public void BenchedEleven_ScoresFewerPointsOverASeason()
        {
            int clubId;
            int bestPoints = RunFullSeason(false, out clubId);
            int worstPoints = RunFullSeason(true, out _);

            TestContext.WriteLine($"Club {clubId}: best XI {bestPoints} pts, worst XI {worstPoints} pts over a season.");

            Assert.That(worstPoints, Is.LessThan(bestPoints),
                $"Worst XI ({worstPoints} pts) should finish below best XI ({bestPoints} pts).");
        }

        private static int RunFullSeason(bool useWorstEleven, out int clubId)
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            var progressor = new SeasonProgressor();

            Club club = league.Clubs[9]; // mid-strength club
            clubId = club.Id;

            Dictionary<int, LineupPlan> plans = null;
            if (useWorstEleven)
                plans = new Dictionary<int, LineupPlan> { [club.Id] = WorstEleven(club) };

            int guard = 0;
            while (season.Fixtures.Any(f => !f.Played) && guard++ < SafetyCap)
                progressor.AdvanceDay(league, season, WorldSeed, plans);

            Assert.That(season.Fixtures.All(f => f.Played), Is.True, "Season must complete within the day cap.");

            List<LeagueTableRow> table = LeagueTable.Compute(league, season);
            return table.Single(r => r.ClubId == club.Id).Points;
        }

        /// <summary>Greedy lowest-rated eleven for the default formation (the "everyone benched" XI).</summary>
        private static LineupPlan WorstEleven(Club club)
        {
            var plan = new LineupPlan { ClubId = club.Id };
            var used = new HashSet<int>();

            foreach (PositionRole role in LineupSelector.DefaultFormation)
            {
                Player? worst = null;
                int worstRating = int.MaxValue;

                foreach (Player candidate in club.Squad.Players)
                {
                    if (used.Contains(candidate.Id))
                        continue;

                    int rating = PlayerRating.OverallFor(candidate, role);
                    if (rating < worstRating)
                    {
                        worstRating = rating;
                        worst = candidate;
                    }
                }

                used.Add(worst!.Id);
                plan.Slots.Add(new LineupPlanSlot { Role = role, PlayerId = worst.Id });
            }

            return plan;
        }
    }
}
