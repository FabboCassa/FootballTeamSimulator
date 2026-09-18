using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Market
{
    /// <summary>
    /// Estimated finances for every club, including data-only ones (R6 of the
    /// realistic-club-economy spec):
    ///   - gate, commercial (sponsor) and prize income all scale by nation x division x stature
    ///     (R1-R4 for gate/sponsor; prize gets the same nation x division scaling plus its own,
    ///     gentler stature premium - see <see cref="FinanceModel.PrizeStatureMultiplierPermille"/>);
    ///   - a data-only club (no fixtures, no table) gets an estimated ANNUAL revenue paid out in
    ///     equal weekly instalments, with no real gate or prize;
    ///   - every club's season revenue is exactly the sum of its booked components;
    ///   - a data-only club's mean revenue lands within +-15% of a playable club of the same
    ///     nation, division and stature;
    ///   - FinanceProgressor seeds and accrues finances across a whole World, at every detail level.
    ///
    /// All of this is pure/deterministic (integer math) and never touches the match engine, so the
    /// golden master is untouched - proven elsewhere by SimulationDeterminismTests staying green.
    /// </summary>
    [TestFixture]
    public class EstimatedFinanceTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        // ============================================================ R6: gate/commercial/prize scaling

        [Test]
        public void GateAndSponsor_ScaleByNationDivisionAndStature()
        {
            Club poorStature = new Club { Strength = 65, Stature = 0, Facilities = new Facilities { Stadium = 3 } };
            Club richStature = new Club { Strength = 65, Stature = 100, Facilities = new Facilities { Stadium = 3 } };

            Assert.That(FinanceModel.GateReceipts(richStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.GateReceipts(poorStature, 1, 100, Cfg)), "gate must scale with stature");
            Assert.That(FinanceModel.WeeklySponsor(richStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.WeeklySponsor(poorStature, 1, 100, Cfg)), "sponsor must scale with stature");

            Assert.That(FinanceModel.GateReceipts(poorStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.GateReceipts(poorStature, 1, 50, Cfg)), "gate must scale with nation wealth");
            Assert.That(FinanceModel.WeeklySponsor(poorStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.WeeklySponsor(poorStature, 1, 50, Cfg)), "sponsor must scale with nation wealth");

            Assert.That(FinanceModel.GateReceipts(poorStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.GateReceipts(poorStature, 2, 100, Cfg)), "gate must scale with division wealth");
            Assert.That(FinanceModel.WeeklySponsor(poorStature, 1, 100, Cfg),
                Is.GreaterThan(FinanceModel.WeeklySponsor(poorStature, 2, 100, Cfg)), "sponsor must scale with division wealth");
        }

        [Test]
        public void PrizeMoney_ScalesByNationAndDivision()
        {
            const int stature = 50;
            Assert.That(FinanceModel.PrizeMoney(1, 20, 1, 100, stature, Cfg),
                Is.GreaterThan(FinanceModel.PrizeMoney(1, 20, 1, 50, stature, Cfg)), "prize must scale with nation wealth");
            Assert.That(FinanceModel.PrizeMoney(1, 20, 1, 100, stature, Cfg),
                Is.GreaterThan(FinanceModel.PrizeMoney(1, 20, 2, 100, stature, Cfg)), "prize must scale with division wealth");
        }

        [Test]
        public void PrizeMoney_ScalesByStature_AtEqualPositionNationAndDivision()
        {
            // Same finishing position, nation and division: only stature differs, so this fails if
            // production PrizeMoney ever drops (or ignores) the stature multiplier.
            long poorStaturePrize = FinanceModel.PrizeMoney(1, 20, 1, 100, 0, Cfg);
            long richStaturePrize = FinanceModel.PrizeMoney(1, 20, 1, 100, 100, Cfg);
            Assert.That(richStaturePrize, Is.GreaterThan(poorStaturePrize), "prize must scale with the club's own stature");
        }

        [Test]
        public void EstimatedAnnualRevenue_CombiningGateSponsorAndPrize_ScalesByNationDivisionAndStature()
        {
            Club poorStature = new Club { Strength = 65, Stature = 0, Facilities = new Facilities { Stadium = 3 } };
            Club richStature = new Club { Strength = 65, Stature = 100, Facilities = new Facilities { Stadium = 3 } };
            const int clubCount = 20;

            long rich = FinanceModel.EstimatedAnnualRevenue(richStature, 1, 100, clubCount, Cfg);
            long poor = FinanceModel.EstimatedAnnualRevenue(poorStature, 1, 100, clubCount, Cfg);
            Assert.That(rich, Is.GreaterThan(poor), "combined gate+sponsor+prize estimate must scale with stature");

            long richNation = FinanceModel.EstimatedAnnualRevenue(poorStature, 1, 100, clubCount, Cfg);
            long poorNation = FinanceModel.EstimatedAnnualRevenue(poorStature, 1, 50, clubCount, Cfg);
            Assert.That(richNation, Is.GreaterThan(poorNation), "combined estimate must scale with nation wealth");

            long tier1 = FinanceModel.EstimatedAnnualRevenue(poorStature, 1, 100, clubCount, Cfg);
            long tier2 = FinanceModel.EstimatedAnnualRevenue(poorStature, 2, 100, clubCount, Cfg);
            Assert.That(tier1, Is.GreaterThan(tier2), "combined estimate must scale with division wealth");
        }

        // ============================================================ R6: data-only weekly revenue, no gate/prize

        [Test]
        public void DataOnlyClubs_EarnEstimatedWeeklyRevenue_WithNoGateOrPrize()
        {
            League league = GenerateLeague(seed: 5150, clubCount: 12, tier: 2, economicReputation: 70);
            league.DetailLevel = LeagueDetailLevel.DataOnly;

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });

            Club club = league.Clubs[0];
            long balanceAfterSeeding = club.Finances.Balance;

            const int weeks = 10;
            for (int week = 0; week < weeks; week++)
                fin.AccrueDataOnlyWeek(new[] { league });

            Assert.That(club.Finances.SeasonEstimatedIncome, Is.GreaterThan(0), "a data-only club must earn estimated revenue");
            Assert.That(club.Finances.Balance, Is.EqualTo(balanceAfterSeeding + club.Finances.SeasonEstimatedIncome),
                "the estimated revenue must actually be credited to the operating balance");
            Assert.That(club.Finances.SeasonGateIncome, Is.Zero, "data-only clubs never book real gate income (no fixtures)");
            Assert.That(club.Finances.SeasonPrizeIncome, Is.Zero, "data-only clubs never book real prize income (no table)");
            Assert.That(club.Finances.SeasonSponsorIncome, Is.Zero, "data-only clubs never book real sponsor income (AccrueWeek never runs for them)");

            long expectedWeekly = FinanceModel.EstimatedWeeklyRevenue(club, league.Division, league.EconomicReputation, league.Clubs.Count, Cfg);
            Assert.That(club.Finances.SeasonEstimatedIncome, Is.EqualTo(expectedWeekly * weeks),
                "every week must credit the same, deterministic instalment");
        }

        // ============================================================ R6: season income = sum of components

        [Test]
        public void SeasonIncome_EqualsSumOfItsComponents_ForPlayableAndDataOnlyClubs()
        {
            League playable = GenerateLeague(seed: 3131, clubCount: 10, tier: 1, economicReputation: 90);
            long[] revenue = PlayFullSeasonAndReadRevenue(playable, seed: 3131);
            Assert.That(revenue.Sum(), Is.GreaterThan(0));

            foreach (Club club in playable.Clubs)
            {
                Finances f = club.Finances;
                Assert.That(f.SeasonIncome, Is.EqualTo(f.SeasonGateIncome + f.SeasonSponsorIncome + f.SeasonPrizeIncome + f.SeasonEstimatedIncome),
                    $"club {club.Id}: season income must equal the sum of its booked components");
                Assert.That(f.SeasonEstimatedIncome, Is.Zero, "a playable club never books estimated income");
            }

            League dataOnly = GenerateLeague(seed: 3232, clubCount: 10, tier: 1, economicReputation: 90);
            dataOnly.DetailLevel = LeagueDetailLevel.DataOnly;
            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { dataOnly });
            for (int w = 0; w < 18; w++)
                fin.AccrueDataOnlyWeek(new[] { dataOnly });

            foreach (Club club in dataOnly.Clubs)
            {
                Finances f = club.Finances;
                Assert.That(f.SeasonIncome, Is.EqualTo(f.SeasonGateIncome + f.SeasonSponsorIncome + f.SeasonPrizeIncome + f.SeasonEstimatedIncome),
                    $"club {club.Id}: season income must equal the sum of its booked components");
                Assert.That(f.SeasonIncome, Is.EqualTo(f.SeasonEstimatedIncome),
                    "a data-only club's whole season income is its estimated income");
            }
        }

        // ============================================================ R6: data-only vs playable mean revenue

        [Test]
        public void DataOnlyClub_MeanRevenue_IsWithin15PercentOfMatchingPlayableClub()
        {
            const int clubCount = 16;
            const ulong seed = 424_242;
            const int tier = 1;
            const int economicReputation = 87;

            League playable = GenerateLeague(seed, clubCount, tier, economicReputation);
            long[] playableRevenue = PlayFullSeasonAndReadRevenue(playable, seed);
            double playableMean = playableRevenue.Average(v => (double)v);

            // Same seed, same tier/clubCount => byte-identical clubs (strength, stature) to the
            // playable league above; only the detail level (and so how income is booked) differs.
            League dataOnly = GenerateLeague(seed, clubCount, tier, economicReputation);
            dataOnly.DetailLevel = LeagueDetailLevel.DataOnly;

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { dataOnly });
            int seasonWeeks = 2 * (clubCount - 1);
            for (int w = 0; w < seasonWeeks; w++)
                fin.AccrueDataOnlyWeek(new[] { dataOnly });

            double dataOnlyMean = dataOnly.Clubs.Average(c => (double)c.Finances.SeasonIncome);
            double ratio = dataOnlyMean / playableMean;

            TestContext.Out.WriteLine(
                $"[estimated-finance] playable mean {Money((long)playableMean)}, data-only mean {Money((long)dataOnlyMean)}, ratio {ratio:P1}");

            Assert.That(ratio, Is.InRange(0.85, 1.15),
                "a data-only club's mean revenue must land within +-15% of a playable club of the same nation, division and stature");
        }

        // ============================================================ R6: FinanceProgressor over a whole World

        [Test]
        public void FinanceProgressor_SeedsAndAccrues_AcrossWholeWorld_AllDetailLevels()
        {
            const ulong seed = 909_090_909UL;
            var scope = new WorldScope { Size = DatabaseSize.Large };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 1 });
            World world = new WorldGenerator(new WorldGenerationOptions { Scope = scope }, Cfg).Generate(seed);

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(world);

            foreach (Nation nation in world.Nations)
            {
                foreach (League league in nation.Leagues)
                {
                    foreach (Club club in league.Clubs)
                    {
                        Assert.That(club.Facilities.Stadium, Is.GreaterThanOrEqualTo(1),
                            $"club {club.Id} ({league.DetailLevel}) must have a seeded stadium tier");
                        Assert.That(club.Finances.Balance, Is.GreaterThan(0),
                            $"club {club.Id} ({league.DetailLevel}) must have a seeded starting balance");
                    }
                }
            }

            List<League> playable = world.PlayableLeagues();
            List<League> background = world.LeaguesAt(LeagueDetailLevel.Background);
            List<League> dataOnly = world.LeaguesAt(LeagueDetailLevel.DataOnly);
            Assert.That(playable, Is.Not.Empty);
            Assert.That(background, Is.Not.Empty);
            Assert.That(dataOnly, Is.Not.Empty);

            Season careerSeason = BuildCareerSeason(world, seed);
            var seasonProgressor = new SeasonProgressor(Cfg);
            var backgroundProgressor = new BackgroundLeagueProgressor(Cfg);
            var alreadyBooked = new HashSet<int>();

            int lastPlayableDay = careerSeason.Fixtures.Count == 0 ? 0 : careerSeason.Fixtures.Max(f => f.Day);
            int lastBackgroundDay = BackgroundLeagueProgressor.LastDay(world);
            int lastDay = Math.Max(lastPlayableDay, lastBackgroundDay);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int day = 1; day <= lastDay; day++)
            {
                List<MatchOutcome> outcomes = seasonProgressor.AdvanceDay(playable, careerSeason, seed);
                if (outcomes.Count > 0)
                {
                    fin.AccrueMatchday(playable, outcomes);
                    fin.AccrueWeek(playable, careerSeason);
                }

                backgroundProgressor.AdvanceTo(world, day, seed);
                List<Fixture> playedToday = world.BackgroundSeason.Fixtures
                    .Where(f => f.Played && f.Day == day && alreadyBooked.Add(f.Id))
                    .ToList();
                if (playedToday.Count > 0)
                {
                    fin.AccrueMatchday(background, playedToday);
                    fin.AccrueWeek(background, world.BackgroundSeason);
                }

                if (day % Cfg.Season.DaysBetweenRounds == 0)
                    fin.AccrueDataOnlyWeek(dataOnly);
            }
            stopwatch.Stop();
            TestContext.Out.WriteLine(
                $"[estimated-finance-world] {world.ClubCount()} clubs, {lastDay} days in {stopwatch.ElapsedMilliseconds} ms");

            foreach (League league in playable)
            {
                foreach (Club club in league.Clubs)
                    Assert.That(club.Finances.SeasonIncome, Is.GreaterThan(0), $"playable club {club.Id} must have accrued real income");
            }

            foreach (League league in background)
            {
                foreach (Club club in league.Clubs)
                    Assert.That(club.Finances.SeasonGateIncome + club.Finances.SeasonSponsorIncome, Is.GreaterThan(0),
                        $"background club {club.Id} must have accrued real income");
            }

            foreach (League league in dataOnly)
            {
                foreach (Club club in league.Clubs)
                {
                    Assert.That(club.Finances.SeasonEstimatedIncome, Is.GreaterThan(0), $"data-only club {club.Id} must have accrued estimated income");
                    Assert.That(club.Finances.SeasonGateIncome, Is.Zero, $"data-only club {club.Id} must never book gate income");
                    Assert.That(club.Finances.SeasonPrizeIncome, Is.Zero, $"data-only club {club.Id} must never book prize income");
                }
            }
        }

        // ============================================================ helpers

        private static League GenerateLeague(ulong seed, int clubCount, int tier, int economicReputation)
        {
            var options = new LeagueGenerationOptions
            {
                LeagueId = 7_700 + tier * 100 + clubCount,
                LeagueName = "Estimated Finance Test League",
                Division = tier,
                ClubCount = clubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            };
            League league = new LeagueGenerator(options, Cfg).Generate(new Pcg32(seed));
            league.EconomicReputation = economicReputation;
            return league;
        }

        /// <summary>Drives a full season through the REAL FinanceProgressor path and returns each club's final SeasonIncome, in club (table) order.</summary>
        private static long[] PlayFullSeasonAndReadRevenue(League league, ulong seed)
        {
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777))
            };

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league });

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (league.Clubs.Count - 1) * Cfg.Season.DaysBetweenRounds;
            for (int d = 0; d < days; d++)
            {
                List<MatchOutcome> outcomes = progressor.AdvanceDay(league, season, seed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);

            return league.Clubs.Select(c => c.Finances.SeasonIncome).ToArray();
        }

        private static Season BuildCareerSeason(World world, ulong seed)
        {
            var season = new Season { Year = 1, CurrentDay = 1 };
            var generator = new FixtureGenerator(Cfg.Season);
            int nextId = 1;

            foreach (League league in world.PlayableLeagues())
            {
                List<Fixture> fixtures = generator.Generate(league, new Pcg32(seed, 20_000UL + (ulong)league.Id), nextId);
                nextId += fixtures.Count;
                season.Fixtures.AddRange(fixtures);
            }

            return season;
        }

        private static string Money(long v)
        {
            if (v >= 1_000_000 || v <= -1_000_000) return $"EUR {v / 1_000_000.0:F1}M";
            if (v >= 1_000 || v <= -1_000) return $"EUR {v / 1_000.0:F0}k";
            return $"EUR {v}";
        }
    }
}
