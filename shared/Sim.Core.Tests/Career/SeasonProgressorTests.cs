using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    [TestFixture]
    public class SeasonProgressorTests
    {
        private const ulong WorldSeed = 987654321;

        private static (League league, Season season) NewWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            return (league, season);
        }

        private static List<MatchOutcome> AdvanceDays(
            SeasonProgressor progressor,
            League league,
            Season season,
            int days,
            IReadOnlyDictionary<int, LineupPlan>? plans = null)
        {
            var outcomes = new List<MatchOutcome>();
            for (int i = 0; i < days; i++)
                outcomes.AddRange(progressor.AdvanceDay(league, season, WorldSeed, plans));
            return outcomes;
        }

        // ------------------------------------------------------------ the watched fixture (13.1)

        [Test]
        public void OnlyTheWatchedClubsFixture_CarriesTheMovementStream()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();
            int firstMatchDay = new SeasonBalance().FirstMatchDay;
            int watched = league.Clubs[3].Id;

            // The season starts on day 1, so the (firstMatchDay - 1)th advance is round one.
            AdvanceDays(progressor, league, season, firstMatchDay - 2);
            List<MatchOutcome> day = progressor.AdvanceDay(
                league, season, WorldSeed, null, null, null, null, watched);

            Assert.That(day, Is.Not.Empty, "the first matchday must play fixtures");

            List<MatchOutcome> hisMatch = day.Where(o => o.Fixture.Involves(watched)).ToList();
            Assert.That(hisMatch, Has.Count.EqualTo(1), "a club plays once a matchday");
            Assert.That(hisMatch[0].Report.Positions, Is.Not.Null,
                "the fixture the coach can watch MUST carry its replay — this is what a blank pitch looks like");
            Assert.That(hisMatch[0].Report.Positions!.TickCount, Is.GreaterThan(1));

            foreach (MatchOutcome other in day.Where(o => !o.Fixture.Involves(watched)))
                Assert.That(other.Report.Positions, Is.Null,
                    "nobody watches an AI fixture; building a stream for it is waste");
        }

        [Test]
        public void WatchingAFixture_PlaysItOnThePitch_AndLeavesEveryOtherFixtureAlone()
        {
            int watchedId;
            {
                (League probe, Season _) = NewWorld();
                watchedId = probe.Clubs[3].Id;
            }

            // The scoreline and the timeline of every fixture of the day, fixture by fixture.
            List<string> Play(int? watched)
            {
                (League league, Season season) = NewWorld();
                var progressor = new SeasonProgressor();
                int firstMatchDay = new SeasonBalance().FirstMatchDay;
                AdvanceDays(progressor, league, season, firstMatchDay - 2);

                List<MatchOutcome> day = progressor.AdvanceDay(
                    league, season, WorldSeed, null, null, null, null, watched);

                return day.Select(o =>
                    $"{o.Fixture.Id}:{o.Report.HomeGoals}-{o.Report.AwayGoals}:" +
                    string.Join(",", o.Report.Events.Select(e => $"{e.Minute}/{(int)e.Type}/{e.PlayerId}")))
                    .ToList();
            }

            List<string> unwatched = Play(null);
            List<string> watchedDay = Play(watchedId);

            // ENGINE PHASE 6 REDREW THIS TEST, and it is worth saying why. Until the causality was
            // inverted, watching a fixture could not move it by one goal: the picture was a
            // re-enactment of a result the minute model had already decided, and this test said so.
            // Now the fixture the coach watches is PLAYED — twenty-two agents, a ball and a
            // referee — and the result is whatever that match produced. So the claim changes shape:
            // the watched fixture may differ, and EVERY OTHER FIXTURE OF THE DAY MAY NOT. That is
            // the real contract, because it is what keeps a league table the same table whether or
            // not the coach happened to be looking.
            Assert.That(watchedDay, Has.Count.EqualTo(unwatched.Count), "the same fixtures must be played");

            int differences = 0;
            for (int i = 0; i < unwatched.Count; i++)
            {
                bool isWatched = unwatched[i] != watchedDay[i];
                if (isWatched) differences++;
            }

            Assert.That(differences, Is.LessThanOrEqualTo(1),
                "watching one fixture must not move any OTHER fixture of the matchday");
        }

        [Test]
        public void QuietDays_PlayNothing_AndAdvanceTheCalendar()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();

            List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, WorldSeed);

            Assert.That(outcomes, Is.Empty);
            Assert.That(season.CurrentDay, Is.EqualTo(2));
        }

        [Test]
        public void FirstMatchDay_PlaysExactlyRoundOne()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();
            int firstMatchDay = new SeasonBalance().FirstMatchDay;

            List<MatchOutcome> outcomes = AdvanceDays(progressor, league, season, firstMatchDay - 1);

            Assert.That(outcomes, Has.Count.EqualTo(10), "20 clubs => 10 matches on matchday 1.");
            Assert.That(outcomes.All(o => o.Fixture.Round == 1 && o.Fixture.Played), Is.True);
            Assert.That(season.Fixtures.Count(f => f.Played), Is.EqualTo(10));
        }

        [Test]
        public void Results_AreDeterministicPerWorldSeed()
        {
            (League leagueA, Season seasonA) = NewWorld();
            (League leagueB, Season seasonB) = NewWorld();
            var progressor = new SeasonProgressor();

            AdvanceDays(progressor, leagueA, seasonA, 14);
            AdvanceDays(progressor, leagueB, seasonB, 14);

            for (int i = 0; i < seasonA.Fixtures.Count; i++)
            {
                Fixture a = seasonA.Fixtures[i];
                Fixture b = seasonB.Fixtures[i];
                Assert.That((b.Played, b.HomeGoals, b.AwayGoals),
                    Is.EqualTo((a.Played, a.HomeGoals, a.AwayGoals)),
                    $"Fixture {a.Id} diverged between identical worlds.");
            }

            Assert.That(seasonA.Fixtures.Count(f => f.Played), Is.EqualTo(20),
                "Two matchdays should have been played in 14 days.");
        }

        [Test]
        public void Scores_StayWithinSaneBounds()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();

            List<MatchOutcome> outcomes = AdvanceDays(progressor, league, season, 14);

            foreach (Fixture f in outcomes.Select(o => o.Fixture))
            {
                Assert.That(f.HomeGoals, Is.InRange(0, 12));
                Assert.That(f.AwayGoals, Is.InRange(0, 12));
            }
        }

        [Test]
        public void LeagueTable_TotalsAreConsistent_AfterTwoMatchdays()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();

            AdvanceDays(progressor, league, season, 14);

            List<LeagueTableRow> table = LeagueTable.Compute(league, season);

            Assert.That(table.Sum(r => r.Played), Is.EqualTo(40), "20 played fixtures => 40 club-appearances.");
            Assert.That(table.Sum(r => r.GoalsFor), Is.EqualTo(table.Sum(r => r.GoalsAgainst)));
            Assert.That(table.Sum(r => r.Wins), Is.EqualTo(table.Sum(r => r.Losses)));
            Assert.That(table.All(r => r.Played == 2), Is.True);
        }

        [Test]
        public void ScorerTallies_MatchTotalGoals()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();

            AdvanceDays(progressor, league, season, 14);

            int goalsFromFixtures = season.Fixtures.Where(f => f.Played).Sum(f => f.HomeGoals + f.AwayGoals);

            Assert.That(season.Scorers.Sum(t => t.Goals), Is.EqualTo(goalsFromFixtures));
            Assert.That(season.Scorers.All(t => t.Goals > 0), Is.True);
            Assert.That(season.Scorers.Select(t => t.PlayerId).Distinct().Count(),
                Is.EqualTo(season.Scorers.Count), "One tally per player.");
            Assert.That(season.Scorers.All(t => league.FindPlayer(t.PlayerId) != null), Is.True,
                "Every scorer must exist in the league.");
        }

        [Test]
        public void LineupPlan_IsUsedForThatClub()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();
            Club club = league.Clubs[0];

            // A distinctive XI: first 11 players by id, regardless of fit.
            var plan = new LineupPlan { ClubId = club.Id };
            List<Player> byId = club.Squad.Players.OrderBy(p => p.Id).ToList();
            for (int i = 0; i < Lineup.Size; i++)
                plan.Slots.Add(new LineupPlanSlot
                {
                    Role = LineupSelector.DefaultFormation[i],
                    PlayerId = byId[i].Id
                });

            var plans = new Dictionary<int, LineupPlan> { [club.Id] = plan };
            var planIds = plan.Slots.Select(s => s.PlayerId).ToHashSet();

            List<MatchOutcome> outcomes = AdvanceDays(progressor, league, season, 7, plans);
            MatchOutcome userMatch = outcomes.Single(o => o.Fixture.Involves(club.Id));

            var clubEvents = userMatch.Report.Events.Where(e => e.ClubId == club.Id).ToList();
            Assert.That(clubEvents, Is.Not.Empty, "Expected at least one chance for the club over a match.");
            Assert.That(clubEvents.All(e => planIds.Contains(e.PlayerId)), Is.True,
                "Every event of the planned club must involve a planned player.");
        }

        [Test]
        public void InvalidLineupPlan_FallsBackToBestEleven()
        {
            (League league, Season season) = NewWorld();
            var progressor = new SeasonProgressor();
            Club club = league.Clubs[0];

            var plan = new LineupPlan { ClubId = club.Id };
            for (int i = 0; i < Lineup.Size; i++)
                plan.Slots.Add(new LineupPlanSlot
                {
                    Role = LineupSelector.DefaultFormation[i],
                    PlayerId = 99000 + i // not in the squad
                });

            var plans = new Dictionary<int, LineupPlan> { [club.Id] = plan };

            List<MatchOutcome> outcomes = AdvanceDays(progressor, league, season, 7, plans);
            MatchOutcome userMatch = outcomes.Single(o => o.Fixture.Involves(club.Id));

            Assert.That(userMatch.Fixture.Played, Is.True, "Invalid plan must not prevent the match.");
        }
    }
}
