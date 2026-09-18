using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Season-end stature evolution (R5 of the realistic-club-economy spec):
    ///   - stature only ever changes via <see cref="StatureProgressor.ApplySeasonEnd"/>, from finish
    ///     vs expectation, promotion/relegation and an outright title;
    ///   - a single season's change never exceeds <see cref="StatureBalance.MaxDeltaPerSeason"/>;
    ///   - a club winning its league every season needs at least 5 seasons to climb from a
    ///     league-median stature to a top-3 one;
    ///   - the whole thing is a pure function of its inputs, so it is deterministic for a given seed.
    ///
    /// Pure/deterministic (integer math, NO RNG) and never touched by the match engine or
    /// generation, so the golden master and every existing determinism test are unaffected.
    /// </summary>
    [TestFixture]
    public class StatureProgressorTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static StatureBalance Balance => Cfg.Stature;

        // ============================================================ what drives the change

        [Test]
        public void ApplySeasonEnd_MeetingExpectation_NoPromotionRelegationOrTitle_LeavesStatureUnchanged()
        {
            var club = new Club { Stature = 50 };
            var progressor = new StatureProgressor(Cfg);

            // Finished exactly where expected (mid-table), no movement between divisions, no title.
            progressor.ApplySeasonEnd(club, actualPosition: 8, expectedPosition: 8, promoted: false, relegated: false);

            Assert.That(club.Stature, Is.EqualTo(50), "meeting expectation with no title/promotion/relegation must not move stature");
        }

        [Test]
        public void ApplySeasonEnd_OverachievingExpectation_RaisesStature()
        {
            var club = new Club { Stature = 50 };
            var progressor = new StatureProgressor(Cfg);

            progressor.ApplySeasonEnd(club, actualPosition: 4, expectedPosition: 10, promoted: false, relegated: false);

            Assert.That(club.Stature, Is.GreaterThan(50), "finishing well above the expected position must raise stature");
        }

        [Test]
        public void ApplySeasonEnd_UnderachievingExpectation_LowersStature()
        {
            var club = new Club { Stature = 50 };
            var progressor = new StatureProgressor(Cfg);

            progressor.ApplySeasonEnd(club, actualPosition: 16, expectedPosition: 6, promoted: false, relegated: false);

            Assert.That(club.Stature, Is.LessThan(50), "finishing well below the expected position must lower stature");
        }

        [Test]
        public void ApplySeasonEnd_WinningTheTitle_AddsABonusOnTopOfThePositionDelta()
        {
            var progressor = new StatureProgressor(Cfg);

            // Same gap-vs-expectation (2 positions better than expected) for both, only one is a title.
            var titleWinner = new Club { Stature = 50 };
            progressor.ApplySeasonEnd(titleWinner, actualPosition: 1, expectedPosition: 3, promoted: false, relegated: false);

            var runnerUp = new Club { Stature = 50 };
            progressor.ApplySeasonEnd(runnerUp, actualPosition: 2, expectedPosition: 4, promoted: false, relegated: false);

            Assert.That(titleWinner.Stature, Is.GreaterThan(runnerUp.Stature),
                "winning the division outright must add a bonus beyond the equivalent finish-vs-expectation gap");
        }

        [Test]
        public void ApplySeasonEnd_Promotion_AddsABonus_AndRelegation_SubtractsAPenalty()
        {
            var progressor = new StatureProgressor(Cfg);

            var baseline = new Club { Stature = 50 };
            progressor.ApplySeasonEnd(baseline, actualPosition: 8, expectedPosition: 8, promoted: false, relegated: false);

            var promoted = new Club { Stature = 50 };
            progressor.ApplySeasonEnd(promoted, actualPosition: 8, expectedPosition: 8, promoted: true, relegated: false);

            var relegated = new Club { Stature = 50 };
            progressor.ApplySeasonEnd(relegated, actualPosition: 8, expectedPosition: 8, promoted: false, relegated: true);

            Assert.That(promoted.Stature, Is.GreaterThan(baseline.Stature), "promotion must add stature beyond the plain finish-vs-expectation delta");
            Assert.That(relegated.Stature, Is.LessThan(baseline.Stature), "relegation must subtract stature beyond the plain finish-vs-expectation delta");
        }

        [Test]
        public void ApplySeasonEnd_ClampsResultToZeroToOneHundredRange()
        {
            var progressor = new StatureProgressor(Cfg);

            var alreadyTop = new Club { Stature = 100 };
            progressor.ApplySeasonEnd(alreadyTop, actualPosition: 1, expectedPosition: 1, promoted: false, relegated: false);
            Assert.That(alreadyTop.Stature, Is.EqualTo(100), "stature must never exceed 100");

            var alreadyBottom = new Club { Stature = 0 };
            progressor.ApplySeasonEnd(alreadyBottom, actualPosition: 20, expectedPosition: 1, promoted: false, relegated: true);
            Assert.That(alreadyBottom.Stature, Is.EqualTo(0), "stature must never drop below 0");
        }

        // ============================================================ the cap

        [Test]
        public void SeasonEndDelta_NeverExceedsTheConfiguredCapInEitherDirection()
        {
            var progressor = new StatureProgressor(Cfg);
            int max = Balance.MaxDeltaPerSeason;

            // A wide grid: every finishing position 1..20 against every expected position 1..20,
            // crossed with promoted/relegated, covers the full input space this pure function sees.
            for (int actual = 1; actual <= 20; actual++)
            {
                for (int expected = 1; expected <= 20; expected++)
                {
                    foreach (bool promoted in new[] { false, true })
                    {
                        foreach (bool relegated in new[] { false, true })
                        {
                            int delta = progressor.SeasonEndDelta(actual, expected, promoted, relegated);
                            Assert.That(delta, Is.InRange(-max, max),
                                $"delta out of cap for actual={actual}, expected={expected}, promoted={promoted}, relegated={relegated}");
                        }
                    }
                }
            }
        }

        // ============================================================ league-median -> top-3 in >= 5 seasons

        [Test]
        public void ClubWinningItsLeagueEverySeason_NeedsAtLeastFiveSeasons_ToReachTopThreeStature()
        {
            // A real generated 20-club league (same generator/seed the other stature tests use):
            // gives us a genuine league-median stature and a genuine top-3 stature, rather than
            // invented numbers.
            League league = GenerateLeague(seed: 4242, clubCount: 20);
            int[] statures = league.Clubs.Select(c => c.Stature).OrderBy(s => s).ToArray();
            int medianStature = (statures[9] + statures[10]) / 2; // 20 clubs: average of the two middle ranks
            int topThreeStature = statures[statures.Length - 3]; // 3rd-highest stature in the league

            TestContext.Out.WriteLine($"[stature-climb] median {medianStature}, top-3 threshold {topThreeStature}");
            Assert.That(topThreeStature, Is.GreaterThan(medianStature), "test precondition: top-3 must be above the median in this league");

            var club = new Club { Stature = medianStature };
            var progressor = new StatureProgressor(Cfg);

            // The club wins the title every season, having been expected only to finish mid-table
            // (a genuine over-performer, not a club already expected to win).
            const int expectedPosition = 10;

            for (int season = 1; season <= 4; season++)
            {
                progressor.ApplySeasonEnd(club, actualPosition: 1, expectedPosition: expectedPosition, promoted: false, relegated: false);
                Assert.That(club.Stature, Is.LessThan(topThreeStature),
                    $"a club must not reach top-3 stature in fewer than 5 seasons of title-winning (season {season})");
            }

            // It does eventually get there under the cap - proves the mechanism moves, not just caps forever.
            int seasonsToReachTopThree = 4;
            while (club.Stature < topThreeStature && seasonsToReachTopThree < 30)
            {
                progressor.ApplySeasonEnd(club, actualPosition: 1, expectedPosition: expectedPosition, promoted: false, relegated: false);
                seasonsToReachTopThree++;
            }

            TestContext.Out.WriteLine($"[stature-climb] reached top-3 stature after {seasonsToReachTopThree} seasons");
            Assert.That(seasonsToReachTopThree, Is.GreaterThanOrEqualTo(5));
            Assert.That(club.Stature, Is.GreaterThanOrEqualTo(topThreeStature), "the climb must actually complete within a reasonable number of seasons");
        }

        // ============================================================ determinism

        [Test]
        public void SeasonEndDelta_IsPureAndDeterministic_ForTheSameInputs()
        {
            var progressor = new StatureProgressor(Cfg);

            int first = progressor.SeasonEndDelta(actualPosition: 3, expectedPosition: 9, promoted: true, relegated: false);
            int second = progressor.SeasonEndDelta(actualPosition: 3, expectedPosition: 9, promoted: true, relegated: false);

            Assert.That(second, Is.EqualTo(first), "the same inputs must always produce the same stature delta");
        }

        [Test]
        public void ApplySeasonEnd_OverMultipleSeasons_IsDeterministic_ForTheSameSeed()
        {
            League leagueA = GenerateLeague(seed: 909090, clubCount: 20);
            League leagueB = GenerateLeague(seed: 909090, clubCount: 20);

            var progressorA = new StatureProgressor(Cfg);
            var progressorB = new StatureProgressor(Cfg);

            for (int i = 0; i < leagueA.Clubs.Count; i++)
            {
                Club a = leagueA.Clubs[i];
                Club b = leagueB.Clubs[i];

                // Three simulated seasons of varied, but identically-derived, results per club.
                for (int season = 0; season < 3; season++)
                {
                    int actual = 1 + (i + season) % leagueA.Clubs.Count;
                    int expected = 1 + i;
                    bool promoted = actual <= 3 && i > 5;
                    bool relegated = actual >= leagueA.Clubs.Count - 2 && i < 5;

                    progressorA.ApplySeasonEnd(a, actual, expected, promoted, relegated);
                    progressorB.ApplySeasonEnd(b, actual, expected, promoted, relegated);
                }
            }

            for (int i = 0; i < leagueA.Clubs.Count; i++)
            {
                Assert.That(leagueB.Clubs[i].Stature, Is.EqualTo(leagueA.Clubs[i].Stature),
                    $"club {i}: same seed and same sequence of season-end results must produce identical stature");
            }
        }

        // ============================================================ helpers

        private static League GenerateLeague(ulong seed, int clubCount)
        {
            var options = new LeagueGenerationOptions
            {
                LeagueId = 9_900 + clubCount,
                LeagueName = "Stature Progressor Test League",
                Division = 1,
                ClubCount = clubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            };
            return new LeagueGenerator(options, Cfg).Generate(new Pcg32(seed));
        }
    }
}
