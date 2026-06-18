using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 3.5 — a club's conditional pre-match plan executes automatically when
    /// its fixtures are advanced past headless (the "skipped / unwatched" path):
    /// the club's results change while every other fixture stays byte-identical,
    /// and the headless run is deterministic.
    /// </summary>
    [TestFixture]
    public class SeasonProgressorPlanTests
    {
        private const ulong WorldSeed = 909090;
        private const int Days = 7 * 38;

        private static readonly int FamMax = new BalanceConfig().Tactics.FamiliarityMax;

        private static (League league, Season season) NewWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            return (league, season);
        }

        private static void RunSeason(
            Season season, League league, IReadOnlyDictionary<int, IReadOnlyList<MatchRule>>? rules)
        {
            var progressor = new SeasonProgressor();
            for (int i = 0; i < Days; i++)
                progressor.AdvanceDay(league, season, WorldSeed, null, null, rules);
        }

        /// <summary>"While not winning, attack" — true at 0-0 kickoff, so it fires in every match at minute 1.</summary>
        private static MatchRule AlwaysFires() =>
            new MatchRule(1, ScoreSituation.NotWinning,
                new RuleAction(new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Normal), FamMax));

        [Test]
        public void Plan_ExecutesHeadless_ChangesOwnMatches_LeavesOthersByteIdentical()
        {
            (League baseLeague, Season baseSeason) = NewWorld();
            (League planLeague, Season planSeason) = NewWorld();
            int clubId = planLeague.Clubs[0].Id;

            RunSeason(baseSeason, baseLeague, null);
            RunSeason(planSeason, planLeague, new Dictionary<int, IReadOnlyList<MatchRule>>
            {
                [clubId] = new[] { AlwaysFires() }
            });

            int changed = 0;
            for (int i = 0; i < baseSeason.Fixtures.Count; i++)
            {
                Fixture a = baseSeason.Fixtures[i];
                Fixture b = planSeason.Fixtures[i];

                if (a.Involves(clubId))
                {
                    if (b.HomeGoals != a.HomeGoals || b.AwayGoals != a.AwayGoals)
                        changed++;
                }
                else
                {
                    Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                        $"Fixture {a.Id} does not involve the planning club and must be byte-identical.");
                }
            }

            TestContext.Out.WriteLine($"[3.5] headless plan changed {changed} of the club's fixtures.");
            Assert.That(changed, Is.GreaterThan(0),
                "A plan that fires every match must change at least some of the club's headless results.");
        }

        [Test]
        public void HeadlessPlanRun_IsDeterministic()
        {
            (League l1, Season s1) = NewWorld();
            (League l2, Season s2) = NewWorld();
            int clubId = l1.Clubs[0].Id;

            var rules = new Dictionary<int, IReadOnlyList<MatchRule>> { [clubId] = new[] { AlwaysFires() } };
            RunSeason(s1, l1, rules);
            RunSeason(s2, l2, new Dictionary<int, IReadOnlyList<MatchRule>> { [l2.Clubs[0].Id] = new[] { AlwaysFires() } });

            for (int i = 0; i < s1.Fixtures.Count; i++)
            {
                Fixture a = s1.Fixtures[i];
                Fixture b = s2.Fixtures[i];
                Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                    $"Fixture {a.Id}: headless plan execution must be deterministic across runs.");
            }
        }
    }
}
