using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Difficulty;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Difficulty
{
    /// <summary>
    /// Task 5.7 acceptance: single-player difficulty (Easy/Normal/Hard) with NO cheating AI.
    ///   - THE ✅ a win-rate harness shows a clear, monotone separation between the three levels
    ///     with IDENTICAL user behaviour (the user always fields his best XI; only the AI's lineup
    ///     competence changes): Easy > Normal > Hard user points;
    ///   - the money lever is asymmetric & honest (Easy = the user is richer, Hard = AI clubs are
    ///     richer / more aggressive in the market);
    ///   - the board-patience lever speeds up / slows down the sacking meter WITHOUT breaking the
    ///     task-5.6 "a warning season always precedes a sacking" invariant;
    ///   - the AI competence selector is deterministic and reduces exactly to the best XI at 100,
    ///     so Hard reproduces the pre-5.7 season byte-identically.
    ///
    /// All of this is opt-in (the match engine never reads difficulty), so the golden master and the
    /// existing tests are unaffected — proven by those tests staying green and by the identity test
    /// below.
    /// </summary>
    [TestFixture]
    public class DifficultyTests
    {
        private static readonly ulong[] Seeds =
            { 11, 101, 2024, 33_333, 424_242, 7_000_003, 90_210, 1_234_567 };

        private const int UserClubIndex = 9; // a mid-strength club (as in LineupStrengthTests)
        private const int SafetyCap = 600;

        // ============================================================ THE ✅: win-rate separation

        [Test]
        public void WinRate_SeparatesClearly_AcrossDifficulties_WithIdenticalUserBehaviour()
        {
            int easy = 0, normal = 0, hard = 0;
            foreach (ulong seed in Seeds)
            {
                int e = UserSeasonPoints(seed, DifficultyLevel.Easy);
                int n = UserSeasonPoints(seed, DifficultyLevel.Normal);
                int h = UserSeasonPoints(seed, DifficultyLevel.Hard);
                easy += e; normal += n; hard += h;
                TestContext.Out.WriteLine($"[difficulty-winrate] seed {seed}: Easy {e}  Normal {n}  Hard {h} pts");
            }

            TestContext.Out.WriteLine(
                $"[difficulty-winrate] TOTAL over {Seeds.Length} seasons — Easy {easy}  Normal {normal}  Hard {hard} pts " +
                $"(user fields best XI throughout; only AI competence differs)");

            Assert.That(easy, Is.GreaterThan(normal),
                "Easy must yield more user points than Normal (weaker AI opponents)");
            Assert.That(normal, Is.GreaterThan(hard),
                "Normal must yield more user points than Hard (Hard AI fields its best XI)");
            Assert.That(easy - hard, Is.GreaterThanOrEqualTo(8),
                "The Easy↔Hard gap must be clearly visible, not a rounding wobble");
        }

        /// <summary>One full season; the user club fields its best XI (no plan), AI competence = the level.</summary>
        private static int UserSeasonPoints(ulong seed, DifficultyLevel level)
        {
            var cfg = new BalanceConfig();
            League league = new LeagueGenerator().Generate(new Pcg32(seed));
            var season = new Season { Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777)) };

            Club user = league.Clubs[UserClubIndex];
            DifficultySettings s = DifficultyModel.Resolve(level, cfg);
            DifficultyContext ctx = DifficultyModel.MatchContext(user.Id, s);

            var p = new SeasonProgressor();
            int guard = 0;
            while (season.Fixtures.Any(f => !f.Played) && guard++ < SafetyCap)
                p.AdvanceDay(league, season, seed, null, null, null, ctx);

            Assert.That(season.Fixtures.All(f => f.Played), Is.True, "Season must complete within the day cap.");
            return LeagueTable.Compute(league, season).Single(r => r.ClubId == user.Id).Points;
        }

        // ============================================================ Hard == pre-5.7 season (identity)

        [Test]
        public void HardDifficulty_FieldsBestXi_ReproducingTheLegacySeasonByteIdentically()
        {
            const ulong seed = 246_801_357;

            // Legacy: no difficulty context at all.
            (League l1, Season s1) = NewWorld(seed);
            RunFullSeason(l1, s1, seed, difficulty: null);

            // Hard: competence 100 ⇒ every AI club still fields its best XI ⇒ same result.
            (League l2, Season s2) = NewWorld(seed);
            var cfg = new BalanceConfig();
            DifficultySettings hard = DifficultyModel.Resolve(DifficultyLevel.Hard, cfg);
            DifficultyContext ctx = DifficultyModel.MatchContext(l2.Clubs[UserClubIndex].Id, hard);
            RunFullSeason(l2, s2, seed, ctx);

            for (int i = 0; i < s1.Fixtures.Count; i++)
            {
                Assert.That(s2.Fixtures[i].HomeGoals, Is.EqualTo(s1.Fixtures[i].HomeGoals),
                    $"Fixture {i} home score diverged — Hard (competence 100) must equal the legacy season");
                Assert.That(s2.Fixtures[i].AwayGoals, Is.EqualTo(s1.Fixtures[i].AwayGoals),
                    $"Fixture {i} away score diverged — Hard (competence 100) must equal the legacy season");
            }
        }

        // ============================================================ competence selector

        [Test]
        public void CompetentEleven_AtFullCompetence_EqualsBestEleven_AndIsDeterministic()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(555));
            Club club = league.Clubs[0];
            PositionRole[] f = LineupSelector.DefaultFormation;

            Lineup best = LineupSelector.BestEleven(club, f);
            Lineup full = LineupSelector.CompetentEleven(club, f, 100, 3, new Pcg32(999));

            Assert.That(Ids(full), Is.EqualTo(Ids(best)),
                "Competence 100 must reproduce the best XI exactly (Hard = the pre-5.7 AI)");

            Lineup a = LineupSelector.CompetentEleven(club, f, 60, 3, new Pcg32(7));
            Lineup b = LineupSelector.CompetentEleven(club, f, 60, 3, new Pcg32(7));
            Assert.That(Ids(b), Is.EqualTo(Ids(a)), "Same seed + competence must pick the same XI (deterministic)");

            int bestAvg = AverageRating(best);
            int weakAvg = AverageRating(LineupSelector.CompetentEleven(club, f, 35, 3, new Pcg32(7)));
            TestContext.Out.WriteLine(
                $"[difficulty-competence] best-XI avg {bestAvg} vs low-competence(35) avg {weakAvg}");

            Assert.That(weakAvg, Is.LessThan(bestAvg),
                "A low-competence manager must field a genuinely weaker (real) XI");
        }

        // ============================================================ money lever (asymmetric, honest)

        [Test]
        public void Budgets_FavourTheUserOnEasy_AndTheAiOnHard()
        {
            var cfg = new BalanceConfig();
            DifficultySettings easy = DifficultyModel.Resolve(DifficultyLevel.Easy, cfg);
            DifficultySettings hard = DifficultyModel.Resolve(DifficultyLevel.Hard, cfg);

            // Strong clubs so the base budget sits well above the floor and the multiplier shows.
            League leEasy = new LeagueGenerator().Generate(new Pcg32(31_337));
            League leHard = new LeagueGenerator().Generate(new Pcg32(31_337));
            int userId = leEasy.Clubs[0].Id;   // same club id in both (same seed)
            int aiId = leEasy.Clubs[1].Id;

            DifficultyModel.SeedBudgets(new[] { leEasy }, userId, easy, cfg);
            DifficultyModel.SeedBudgets(new[] { leHard }, userId, hard, cfg);

            long userEasy = leEasy.FindClub(userId)!.TransferBudget;
            long userHard = leHard.FindClub(userId)!.TransferBudget;
            long aiEasy = leEasy.FindClub(aiId)!.TransferBudget;
            long aiHard = leHard.FindClub(aiId)!.TransferBudget;

            TestContext.Out.WriteLine(
                $"[difficulty-budget] user €{userEasy:N0} (Easy) vs €{userHard:N0} (Hard); " +
                $"AI €{aiEasy:N0} (Easy) vs €{aiHard:N0} (Hard)");

            Assert.That(userEasy, Is.GreaterThan(userHard), "The user starts richer on Easy");
            Assert.That(aiHard, Is.GreaterThan(aiEasy), "AI clubs are richer (more aggressive) on Hard");
        }

        // ============================================================ board patience

        [Test]
        public void BoardPatience_SacksFasterOnHard_ButStillWarnsFirst_AtEveryLevel()
        {
            int seasonsToSackEasy = SeasonsToSack(DifficultyLevel.Easy, out int warnEasy);
            int seasonsToSackNormal = SeasonsToSack(DifficultyLevel.Normal, out int warnNormal);
            int seasonsToSackHard = SeasonsToSack(DifficultyLevel.Hard, out int warnHard);

            TestContext.Out.WriteLine(
                $"[difficulty-board] seasons of mild underachievement to sacking — " +
                $"Easy warn@{warnEasy}/sack@{seasonsToSackEasy}, " +
                $"Normal warn@{warnNormal}/sack@{seasonsToSackNormal}, " +
                $"Hard warn@{warnHard}/sack@{seasonsToSackHard}");

            Assert.That(seasonsToSackHard, Is.LessThan(seasonsToSackEasy),
                "A demanding (Hard) board must run out of patience sooner than a forgiving (Easy) one");
            foreach (DifficultyLevel level in new[] { DifficultyLevel.Easy, DifficultyLevel.Normal, DifficultyLevel.Hard })
            {
                CareerBalance c = AdjustedCareer(level);
                Assert.That(c.ConfidenceWarningThreshold - c.MaxConfidenceDeltaPerEvaluation,
                    Is.GreaterThanOrEqualTo(c.ConfidenceSackThreshold),
                    $"{level}: warning must still precede a sacking (Warning − MaxDelta ≥ Sack)");
            }
        }

        /// <summary>Mild, persistent underachievement (finish 2 below objective) → seasons until the board sacks.</summary>
        private static int SeasonsToSack(DifficultyLevel level, out int warnSeason)
        {
            var cfg = new BalanceConfig();
            DifficultyModel.ApplyBoardPatience(cfg, DifficultyModel.Resolve(level, cfg));
            var board = new BoardModel(cfg);
            var coach = new Coach { BoardConfidence = cfg.Career.NeutralConfidence };

            const int expected = 6, actual = 8; // two positions below the objective, every season
            warnSeason = -1;
            for (int season = 1; season <= 30; season++)
            {
                board.ApplyConfidenceDelta(coach, board.SeasonEndConfidenceDelta(actual, expected));
                if (board.ShouldSack(coach.BoardConfidence)) return season;
                if (warnSeason < 0 && board.IsWarned(coach.BoardConfidence)) warnSeason = season;
            }
            return int.MaxValue;
        }

        private static CareerBalance AdjustedCareer(DifficultyLevel level)
        {
            var cfg = new BalanceConfig();
            DifficultyModel.ApplyBoardPatience(cfg, DifficultyModel.Resolve(level, cfg));
            return cfg.Career;
        }

        // ============================================================ helpers

        private static (League, Season) NewWorld(ulong seed)
        {
            League league = new LeagueGenerator().Generate(new Pcg32(seed));
            var season = new Season { Fixtures = new FixtureGenerator().Generate(league, new Pcg32(seed, 777)) };
            return (league, season);
        }

        private static void RunFullSeason(League league, Season season, ulong seed, DifficultyContext? difficulty)
        {
            var p = new SeasonProgressor();
            int guard = 0;
            while (season.Fixtures.Any(f => !f.Played) && guard++ < SafetyCap)
                p.AdvanceDay(league, season, seed, null, null, null, difficulty);
            Assert.That(season.Fixtures.All(f => f.Played), Is.True, "Season must complete within the day cap.");
        }

        private static List<int> Ids(Lineup lineup)
        {
            var ids = new List<int>();
            foreach (LineupSlot slot in lineup.Slots) ids.Add(slot.Player.Id);
            return ids;
        }

        private static int AverageRating(Lineup lineup)
        {
            if (lineup.Slots.Count == 0) return 0;
            int sum = 0;
            foreach (LineupSlot slot in lineup.Slots)
                sum += PlayerRating.OverallFor(slot.Player, slot.Role);
            return sum / lineup.Slots.Count;
        }
    }
}
