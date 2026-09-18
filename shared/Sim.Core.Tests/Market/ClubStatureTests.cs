using System.Linq;
using System.Text.Json;
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
    /// Persistent club stature and intra-league wealth spread (R4 of the realistic-club-economy
    /// spec):
    ///   - stature (0-100) is assigned deterministically at generation, on a sub-stream derived
    ///     from (but never consuming) the caller's own RNG stream, correlated with the club's
    ///     generated strength rank but not equal to it;
    ///   - within a tier-1 league of at least 16 clubs, the richest club's season revenue is
    ///     2.4-3.6x the league mean and the poorest is 0.3-0.5x, with stature<->revenue rank
    ///     correlation (Spearman) at least 0.9;
    ///   - two clubs of equal strength but different stature earn different revenue;
    ///   - stature survives a JSON round-trip.
    ///
    /// The R4 revenue bands are measured on <see cref="Finances.SeasonIncome"/> as booked by the
    /// REAL <see cref="FinanceProgressor"/> path (AccrueMatchday/AccrueWeek/AwardPrizeMoney over a
    /// simulated season) — the same functions the client and the balance harness call — never a
    /// test-local hand-summed revenue array.
    ///
    /// All of this is pure/deterministic (integer math) and never touches the match engine, so
    /// the golden master is untouched - proven elsewhere by SimulationDeterminismTests staying green.
    /// </summary>
    [TestFixture]
    public class ClubStatureTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private const ulong Seed = 71_717;

        // ============================================================ generation

        [Test]
        public void Stature_IsAssignedDeterministically_WithinRange()
        {
            League a = GenerateLeague(seed: 4242, clubCount: 20);
            League b = GenerateLeague(seed: 4242, clubCount: 20);

            for (int i = 0; i < a.Clubs.Count; i++)
            {
                Assert.That(a.Clubs[i].Stature, Is.InRange(0, 100), $"club {i} stature out of [0,100]");
                Assert.That(b.Clubs[i].Stature, Is.EqualTo(a.Clubs[i].Stature),
                    "same seed must reproduce identical stature (deterministic generation)");
            }
        }

        [Test]
        public void Stature_CorrelatesWithStrengthRank_ButIsNotEqualToIt()
        {
            // Clubs are laid out strongest-first (index 0 = top of the table), so the top half
            // must, on average, carry higher stature than the bottom half - the "correlated"
            // half of R4 - while individual clubs should NOT all land on the same clean
            // rank-derived baseline - the "not equal" half.
            League league = GenerateLeague(seed: 909090, clubCount: 20);

            double topHalfMean = league.Clubs.Take(10).Average(c => (double)c.Stature);
            double bottomHalfMean = league.Clubs.Skip(10).Average(c => (double)c.Stature);
            Assert.That(topHalfMean, Is.GreaterThan(bottomHalfMean),
                "the strongest half of the league must, on average, carry higher stature than the weakest half");

            bool anyClubDeviatesFromTheCleanRankBaseline = league.Clubs
                .Select((c, i) => (Club: c, Baseline: (league.Clubs.Count - 1 - i) * 100 / (league.Clubs.Count - 1)))
                .Any(x => x.Club.Stature != x.Baseline);
            Assert.That(anyClubDeviatesFromTheCleanRankBaseline, Is.True,
                "stature must not be a clean, noise-free function of rank alone (independent noise required)");
        }

        // ============================================================ R4 core: intra-league wealth spread

        [Test]
        public void TierOneLeague_With16Clubs_RevenueSpread_MatchesRealFootballBand()
        {
            const int clubCount = 16;
            League league = GenerateLeague(seed: Seed, clubCount: clubCount);
            league.EconomicReputation = 100; // England-equivalent: full nation x division wealth, no discount

            // A controlled, deterministic stature ladder spanning the full 0-100 range (the same
            // rank-derived baseline StatureModel targets before noise) so this test measures the
            // FINANCE MODEL's response to stature precisely, independent of any one generation
            // seed's noise draw - generation itself is covered by the tests above.
            for (int i = 0; i < clubCount; i++)
                league.Clubs[i].Stature = (clubCount - 1 - i) * 100 / (clubCount - 1);

            long[] revenue = PlayFullSeasonAndReadRevenue(league);

            double mean = revenue.Average(v => (double)v);
            double richest = revenue.Max() / mean;
            double poorest = revenue.Min() / mean;
            double spearman = Spearman(league.Clubs.Select(c => (double)c.Stature).ToArray(), revenue.Select(v => (double)v).ToArray());

            TestContext.Out.WriteLine(
                $"[stature-spread] mean {Money((long)mean)}, richest {richest:F2}x, poorest {poorest:F2}x, spearman {spearman:F2}");

            Assert.That(richest, Is.InRange(2.4, 3.6), "richest club revenue must be 2.4-3.6x the league mean");
            Assert.That(poorest, Is.InRange(0.3, 0.5), "poorest club revenue must be 0.3-0.5x the league mean");
            Assert.That(spearman, Is.GreaterThanOrEqualTo(0.9), "stature<->revenue rank correlation must be >= 0.9");
        }

        // ============================================================ equal strength, different stature

        [Test]
        public void EqualStrengthClubs_WithDifferentStature_EarnDifferentRevenue()
        {
            // GateReceipts/WeeklySponsor ARE the functions AccrueMatchday/AccrueWeek call in the
            // real path (Market/FinanceProgressor.cs) - calling them directly here exercises
            // production code, not a parallel formula.
            Club richStature = new Club { Strength = 65, Stature = 100, Facilities = new Facilities { Stadium = 3 } };
            Club poorStature = new Club { Strength = 65, Stature = 0, Facilities = new Facilities { Stadium = 3 } };

            const int leagueLevel = 1;
            const int economicReputation = 100;

            long richRevenue = FinanceModel.GateReceipts(richStature, leagueLevel, economicReputation, Cfg)
                                + FinanceModel.WeeklySponsor(richStature, leagueLevel, economicReputation, Cfg);
            long poorRevenue = FinanceModel.GateReceipts(poorStature, leagueLevel, economicReputation, Cfg)
                                + FinanceModel.WeeklySponsor(poorStature, leagueLevel, economicReputation, Cfg);

            Assert.That(richRevenue, Is.Not.EqualTo(poorRevenue),
                "two clubs of equal strength but different stature must earn different revenue");
            Assert.That(richRevenue, Is.GreaterThan(poorRevenue), "higher stature must mean more revenue, all else equal");
        }

        // ============================================================ save format

        [Test]
        public void Stature_SurvivesJsonRoundTrip()
        {
            var club = new Club { Id = 1, Name = "Roundtrip FC", Stature = 87 };

            string json = JsonSerializer.Serialize(club);
            Club? reloaded = JsonSerializer.Deserialize<Club>(json);

            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded!.Stature, Is.EqualTo(87));
        }

        // ============================================================ helpers

        private static League GenerateLeague(ulong seed, int clubCount)
        {
            var options = new LeagueGenerationOptions
            {
                LeagueId = 8_800 + clubCount,
                LeagueName = "Stature Test League",
                Division = 1,
                ClubCount = clubCount,
                FirstClubId = 1,
                FirstPlayerId = 1
            };
            return new LeagueGenerator(options, Cfg).Generate(new Pcg32(seed));
        }

        /// <summary>
        /// Drives a full season through the REAL <see cref="FinanceProgressor"/> path (the same
        /// AccrueMatchday/AccrueWeek/AwardPrizeMoney calls the client and balance harness use over
        /// simulated fixtures) and returns each club's final <see cref="Finances.SeasonIncome"/>,
        /// in club (table) order.
        /// </summary>
        private static long[] PlayFullSeasonAndReadRevenue(League league)
        {
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(Seed, 777))
            };

            var fin = new FinanceProgressor(Cfg);
            fin.SeedWorld(new[] { league }); // stadium tier from strength, starting balance

            var progressor = new SeasonProgressor(Cfg);
            int days = 2 * (league.Clubs.Count - 1) * Cfg.Season.DaysBetweenRounds;
            for (int d = 0; d < days; d++)
            {
                var outcomes = progressor.AdvanceDay(league, season, Seed);
                if (outcomes.Count == 0) continue;
                fin.AccrueMatchday(new[] { league }, outcomes);
                fin.AccrueWeek(new[] { league }, season);
            }
            fin.AwardPrizeMoney(new[] { league }, season);

            return league.Clubs.Select(c => c.Finances.SeasonIncome).ToArray();
        }

        /// <summary>Spearman rank correlation. Floats are fine here - this is a test statistics helper, not persisted state.</summary>
        private static double Spearman(double[] a, double[] b)
        {
            int n = a.Length;
            int[] ra = Rank(a);
            int[] rb = Rank(b);
            double d2 = 0;
            for (int i = 0; i < n; i++)
            {
                double d = ra[i] - rb[i];
                d2 += d * d;
            }
            return 1.0 - 6.0 * d2 / (n * ((double)n * n - 1));
        }

        private static int[] Rank(double[] values)
        {
            int n = values.Length;
            int[] order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
            var ranks = new int[n];
            for (int r = 0; r < n; r++) ranks[order[r]] = r;
            return ranks;
        }

        private static string Money(long v)
        {
            if (v >= 1_000_000 || v <= -1_000_000) return $"EUR {v / 1_000_000.0:F1}M";
            if (v >= 1_000 || v <= -1_000) return $"EUR {v / 1_000.0:F0}k";
            return $"EUR {v}";
        }
    }
}
