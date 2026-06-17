using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 3.4 host helpers: a public per-fixture RNG (so a host can re-simulate
    /// a match exactly) and record/revert of a report's result, used to replace a
    /// provisional user-match result with the post-intervention one.
    /// </summary>
    [TestFixture]
    public class SeasonProgressorResultTests
    {
        [Test]
        public void FixtureRng_IsDeterministic_PerSeedAndFixture()
        {
            Pcg32 a = SeasonProgressor.FixtureRng(12345, 7);
            Pcg32 b = SeasonProgressor.FixtureRng(12345, 7);
            Pcg32 c = SeasonProgressor.FixtureRng(12345, 8);

            bool sameStream = true, differentStream = false;
            for (int i = 0; i < 20; i++)
            {
                uint x = a.NextUInt();
                if (x != b.NextUInt()) sameStream = false;
                if (x != c.NextUInt()) differentStream = true;
            }

            Assert.That(sameStream, Is.True, "Same (seed, fixture id) must give the same RNG stream.");
            Assert.That(differentStream, Is.True, "A different fixture id must give a different stream.");
        }

        [Test]
        public void Record_ThenRevert_LeavesScorersEmpty()
        {
            var season = new Season();
            var fixture = new Fixture { Id = 1, HomeClubId = 10, AwayClubId = 20 };
            MatchReport report = Report(10, 20, (10, 101), (10, 102), (20, 201));

            SeasonProgressor.RecordResult(season, fixture, report);

            Assert.That(fixture.Played, Is.True);
            Assert.That((fixture.HomeGoals, fixture.AwayGoals), Is.EqualTo((2, 1)));
            Assert.That(season.Scorers.Count, Is.EqualTo(3));

            SeasonProgressor.RevertResult(season, fixture, report);

            Assert.That(season.Scorers, Is.Empty, "Reverting must remove all tallies it added.");
        }

        [Test]
        public void Revert_ThenRecord_ReplacesResult_AndKeepsTallyInvariant()
        {
            var season = new Season();
            var fixture = new Fixture { Id = 1, HomeClubId = 10, AwayClubId = 20 };
            MatchReport provisional = Report(10, 20, (10, 101), (10, 101), (20, 201)); // 101 scored twice
            MatchReport final = Report(10, 20, (10, 101), (20, 201), (20, 202));        // intervention changed it

            SeasonProgressor.RecordResult(season, fixture, provisional);
            SeasonProgressor.RevertResult(season, fixture, provisional);
            SeasonProgressor.RecordResult(season, fixture, final);

            Assert.That((fixture.HomeGoals, fixture.AwayGoals), Is.EqualTo((1, 2)));
            foreach (ScorerTally t in season.Scorers)
                Assert.That(t.Goals, Is.GreaterThan(0), "No zeroed tally may survive (table invariant).");

            int totalGoals = 0;
            foreach (ScorerTally t in season.Scorers) totalGoals += t.Goals;
            Assert.That(totalGoals, Is.EqualTo(3), "Tallies must match the final result's goal count.");
        }

        private static MatchReport Report(int homeClub, int awayClub, params (int clubId, int playerId)[] goals)
        {
            var report = new MatchReport { HomeClubId = homeClub, AwayClubId = awayClub };
            int minute = 1;
            foreach ((int clubId, int playerId) in goals)
            {
                report.Events.Add(new MatchEvent
                {
                    Minute = minute++,
                    Type = MatchEventType.Goal,
                    ClubId = clubId,
                    PlayerId = playerId
                });
                if (clubId == homeClub) report.HomeGoals++; else report.AwayGoals++;
            }

            return report;
        }
    }
}
