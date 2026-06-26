using System.Collections.Generic;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Market;
using Sim.Core.Random;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// Task 5.6 acceptance: coach career (board objectives, reputation, sackings, job offers,
    /// whole-world hiring carousel).
    ///   - THE ✅ overachieve ⇒ reputation rises and better offers arrive;
    ///   - THE ✅ persistent underachievement ⇒ a WARNING season always precedes the sacking
    ///     (structurally guaranteed by the capped per-evaluation confidence change);
    ///   - an offer always targets a real club with a usable squad and a transfer budget (so the
    ///     host can move the user there);
    ///   - a sacked AI coach is replaced; the whole-world carousel is deterministic and not frozen.
    ///
    /// All of this is opt-in (the match engine / SeasonProgressor never call it), so the golden
    /// master and the existing tests are unaffected — proven by those tests staying green.
    /// </summary>
    [TestFixture]
    public class CoachCareerTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static CareerBalance Career => Cfg.Career;
        private const ulong WorldSeed = 5_006_006;

        // ============================================================ seeding sanity

        [Test]
        public void SeedWorld_GivesStrongerClubsHigherReputation_AndHarderObjectives()
        {
            League league = NewLeague();
            new CoachCareerProgressor(Cfg).SeedWorld(new[] { league });

            Club strongest = ClubByStrengthRank(league, rank: 1);
            Club weakest = ClubByStrengthRank(league, rank: league.Clubs.Count);

            TestContext.Out.WriteLine(
                $"[career-seed] strongest {strongest.ShortName}: rep {strongest.Coach.Reputation}, " +
                $"objective {strongest.Coach.ObjectiveExpectedPosition}; weakest {weakest.ShortName}: " +
                $"rep {weakest.Coach.Reputation}, objective {weakest.Coach.ObjectiveExpectedPosition}");

            Assert.That(strongest.Coach.Reputation, Is.GreaterThan(weakest.Coach.Reputation),
                "A stronger club's coach must be seeded with a higher reputation");
            Assert.That(strongest.Coach.ObjectiveExpectedPosition, Is.LessThan(weakest.Coach.ObjectiveExpectedPosition),
                "A stronger club must be expected to finish higher (a lower position number)");
        }

        // ============================================================ THE ✅: overachieve → better offers

        [Test]
        public void Overachieving_RaisesReputation_AndBetterOffersArrive()
        {
            League league = NewLeague();
            var career = new CoachCareerProgressor(Cfg);
            career.SeedWorld(new[] { league });
            new BudgetModel(Cfg).SeedBudgets(new[] { league });

            // The user takes the WEAKEST club (low reputation, modest objective) — lots of headroom.
            Club userClub = ClubByStrengthRank(league, rank: league.Clubs.Count);
            userClub.Coach.IsHuman = true;
            var jobs = new JobMarket(Cfg);

            List<JobOffer> before = jobs.GenerateUserOffers(userClub.Coach, userClub.Id, new[] { league });
            int repBefore = userClub.Coach.Reputation;
            int ceilingBefore = MaxRequired(before);

            // Win the league four seasons running while the board only expected mid/lower table.
            var rep = new ReputationModel(Cfg);
            for (int s = 0; s < 4; s++)
                rep.ApplySeasonEnd(userClub.Coach, actualPosition: 1, expectedPosition: userClub.Coach.ObjectiveExpectedPosition, division: league.Division);

            List<JobOffer> after = jobs.GenerateUserOffers(userClub.Coach, userClub.Id, new[] { league });
            int repAfter = userClub.Coach.Reputation;
            int ceilingAfter = MaxRequired(after);

            TestContext.Out.WriteLine(
                $"[career-offers] reputation {repBefore} -> {repAfter}; offers {before.Count} (best stature {ceilingBefore}) " +
                $"-> {after.Count} (best stature {ceilingAfter})");

            Assert.That(repAfter, Is.GreaterThan(repBefore), "Overachieving must raise reputation");
            Assert.That(after.Count, Is.GreaterThanOrEqualTo(before.Count),
                "A more reputable coach must attract at least as many clubs");
            Assert.That(ceilingAfter, Is.GreaterThan(ceilingBefore),
                "A more reputable coach must now be courted by a more prestigious club (the ✅)");
        }

        // ============================================================ THE ✅: warning before sacking

        [Test]
        public void PersistentUnderachievement_Warns_BeforeItSacks()
        {
            // Structural invariant: from the warning line, one capped evaluation cannot reach the sack line.
            Assert.That(Career.ConfidenceWarningThreshold - Career.MaxConfidenceDeltaPerEvaluation,
                Is.GreaterThanOrEqualTo(Career.ConfidenceSackThreshold),
                "Config must guarantee a warning season precedes a sacking (Warning − MaxDelta ≥ Sack)");

            var board = new BoardModel(Cfg);
            var coach = new Coach { BoardConfidence = Career.NeutralConfidence };

            const int expected = 3;   // ambitious objective
            const int actual = 18;    // keeps finishing near the bottom

            int warnSeason = -1, sackSeason = -1;
            for (int season = 1; season <= 10 && sackSeason < 0; season++)
            {
                board.ApplyConfidenceDelta(coach, board.SeasonEndConfidenceDelta(actual, expected));

                if (board.ShouldSack(coach.BoardConfidence)) { if (sackSeason < 0) sackSeason = season; }
                else if (board.IsWarned(coach.BoardConfidence)) { if (warnSeason < 0) warnSeason = season; }
            }

            TestContext.Out.WriteLine(
                $"[career-sack] confidence reached the warning band at season {warnSeason}, sacked at season {sackSeason}");

            Assert.That(sackSeason, Is.GreaterThan(0), "Persistent underachievement must eventually be sacked");
            Assert.That(warnSeason, Is.GreaterThan(0), "A warning must be issued");
            Assert.That(warnSeason, Is.LessThan(sackSeason), "The warning must come BEFORE the sacking (the ✅)");
        }

        // ============================================================ an offer is a usable club

        [Test]
        public void AJobOffer_AlwaysTargetsARealClub_WithASquadAndBudget()
        {
            League league = NewLeague();
            var career = new CoachCareerProgressor(Cfg);
            career.SeedWorld(new[] { league });
            new BudgetModel(Cfg).SeedBudgets(new[] { league });

            // A well-regarded coach at a modest club → real, better clubs court him.
            Club userClub = ClubByStrengthRank(league, rank: league.Clubs.Count);
            userClub.Coach.Reputation = 95;

            List<JobOffer> offers = new JobMarket(Cfg).GenerateUserOffers(userClub.Coach, userClub.Id, new[] { league });
            Assert.That(offers, Is.Not.Empty, "A renowned coach at a small club must receive offers");

            foreach (JobOffer offer in offers)
            {
                Club? target = league.FindClub(offer.ClubId);
                Assert.That(target, Is.Not.Null, $"Offer references a non-existent club {offer.ClubId}");
                Assert.That(target!.Squad.Players.Count, Is.GreaterThanOrEqualTo(11),
                    "An offered club must have a fieldable squad the user inherits");
                Assert.That(target.TransferBudget, Is.GreaterThan(0),
                    "An offered club must come with a transfer budget");
                Assert.That(offer.ClubId, Is.Not.EqualTo(userClub.Id), "A club never offers the user his own job");
            }
        }

        // ============================================================ a sacked AI coach is replaced

        [Test]
        public void ASackedAiCoach_IsReplaced_ByANeutralConfidenceCoach()
        {
            (League league, Season season) = NewWorld();
            var career = new CoachCareerProgressor(Cfg);
            career.SeedWorld(new[] { league });

            Club userClub = league.Clubs[0];
            userClub.Coach.IsHuman = true;

            // Drive one AI coach below the sack line; play the season for a real final table.
            // Confidence 0: even a maximum positive season-end swing (capped at MaxConfidenceDelta)
            // cannot lift it to the sack threshold, so the sacking is guaranteed regardless of result.
            Club doomed = league.Clubs[1];
            Coach oldCoach = doomed.Coach;
            oldCoach.BoardConfidence = 0;

            PlaySeason(new SeasonProgressor(), league, season);
            CareerSeasonReport report = career.EvolveSeasonEnd(new[] { league }, season, userClub.Id);

            TestContext.Out.WriteLine(
                $"[career-carousel] sacked {report.SackedAiClubIds.Count} AI coaches, made {report.Hires.Count} hires");

            Assert.That(report.SackedAiClubIds, Does.Contain(doomed.Id), "The failing AI coach must be sacked");
            Assert.That(doomed.Coach, Is.Not.SameAs(oldCoach), "The vacant bench must get a different coach");
            Assert.That(doomed.Coach.BoardConfidence, Is.EqualTo(Career.NeutralConfidence),
                "A newly-appointed coach starts on neutral board confidence");
            Assert.That(report.Hires.Exists(h => h.ClubId == doomed.Id), Is.True, "The hire must be recorded");
        }

        // ============================================================ whole-world carousel determinism

        [Test]
        public void WholeWorldCarousel_IsDeterministic_AndNotFrozen()
        {
            const int seasons = 6;
            (List<League> a, int sackedA) = RunCareer(seasons);
            (List<League> b, int sackedB) = RunCareer(seasons);

            League la = a[0], lb = b[0];
            var reputations = new HashSet<int>();
            for (int i = 0; i < la.Clubs.Count; i++)
            {
                Coach ca = la.Clubs[i].Coach, cb = lb.Clubs[i].Coach;
                Assert.That(cb.Reputation, Is.EqualTo(ca.Reputation),
                    $"Club index {i} ended with a different reputation across identical runs (non-deterministic)");
                Assert.That(cb.BoardConfidence, Is.EqualTo(ca.BoardConfidence),
                    $"Club index {i} ended with a different confidence across identical runs");
                reputations.Add(ca.Reputation);
            }

            TestContext.Out.WriteLine(
                $"[career-world] {seasons} seasons: {sackedA} AI sackings, {reputations.Count} distinct coach reputations");

            Assert.That(sackedA, Is.EqualTo(sackedB), "The carousel must sack identically across identical runs");
            Assert.That(reputations.Count, Is.GreaterThan(1),
                "Reputations must spread out over time (the world is not frozen)");
        }

        // ============================================================ helpers

        private static (List<League>, int totalSacked) RunCareer(int seasons)
        {
            (League league, Season season) = NewWorld();
            var leagues = new List<League> { league };
            var career = new CoachCareerProgressor(Cfg);
            career.SeedWorld(leagues);

            int userClubId = league.Clubs[0].Id;
            league.Clubs[0].Coach.IsHuman = true;

            var p = new SeasonProgressor();
            int totalSacked = 0;
            for (int s = 0; s < seasons; s++)
            {
                PlaySeason(p, league, season);
                CareerSeasonReport report = career.EvolveSeasonEnd(leagues, season, userClubId);
                totalSacked += report.SackedAiClubIds.Count;

                RolloverResult roll = new SeasonRollover(Cfg).EndSeason(leagues, season, WorldSeed);
                season = roll.NewSeason;
                career.AssignObjectives(leagues);
            }

            return (leagues, totalSacked);
        }

        private static League NewLeague() => new LeagueGenerator().Generate(new Pcg32(WorldSeed));

        private static (League, Season) NewWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(WorldSeed));
            var season = new Season
            {
                Fixtures = new FixtureGenerator().Generate(league, new Pcg32(WorldSeed, 777))
            };
            return (league, season);
        }

        private static void PlaySeason(SeasonProgressor p, League league, Season season)
        {
            int guard = 0;
            while (HasUnplayed(season) && guard++ < 600)
                p.AdvanceDay(league, season, WorldSeed);
            Assert.That(HasUnplayed(season), Is.False, "Season must finish for the career evaluation");
        }

        private static bool HasUnplayed(Season season)
        {
            foreach (Fixture f in season.Fixtures)
                if (!f.Played) return true;
            return false;
        }

        private static Club ClubByStrengthRank(League league, int rank)
        {
            foreach (Club club in league.Clubs)
                if (BoardModel.StrengthRank(club, league) == rank)
                    return club;
            return league.Clubs[0];
        }

        private static int MaxRequired(List<JobOffer> offers)
        {
            int max = 0;
            foreach (JobOffer o in offers)
                if (o.RequiredReputation > max) max = o.RequiredReputation;
            return max;
        }
    }
}
