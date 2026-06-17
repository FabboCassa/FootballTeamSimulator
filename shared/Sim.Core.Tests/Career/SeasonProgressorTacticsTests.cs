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
    /// Task 3.3 — tactics flow through the season progressor: the user's tactic
    /// changes their own matches, AI matches stay byte-identical, and a neutral
    /// fully-familiar tactic is the identity (no behaviour change).
    /// </summary>
    [TestFixture]
    public class SeasonProgressorTacticsTests
    {
        private const ulong WorldSeed = 424242;
        private const int Days = 7 * 38; // a full double round-robin (20 clubs).

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
            Season season, League league, IReadOnlyDictionary<int, TacticContext>? tactics)
        {
            var progressor = new SeasonProgressor();
            for (int i = 0; i < Days; i++)
                progressor.AdvanceDay(league, season, WorldSeed, null, tactics);
        }

        private static TacticContext Ctx(Formation f, Mentality m, Pressing p, Tempo t, Width w, int familiarity) =>
            new TacticContext(new Tactic(f, new TacticInstructions(m, p, t, w)), familiarity);

        [Test]
        public void NeutralFullyFamiliarTactic_IsIdentity_AtSeasonLevel()
        {
            (League baseLeague, Season baseSeason) = NewWorld();
            (League tacLeague, Season tacSeason) = NewWorld();
            int clubId = baseLeague.Clubs[0].Id;

            RunSeason(baseSeason, baseLeague, null);
            RunSeason(tacSeason, tacLeague, new Dictionary<int, TacticContext>
            {
                [clubId] = TacticContext.Neutral(FamMax)
            });

            for (int i = 0; i < baseSeason.Fixtures.Count; i++)
            {
                Fixture a = baseSeason.Fixtures[i];
                Fixture b = tacSeason.Fixtures[i];
                Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                    $"Neutral fully-familiar tactic changed fixture {a.Id} — should be identity.");
            }
        }

        [Test]
        public void UserTactic_LeavesAiMatchesByteIdentical()
        {
            (League baseLeague, Season baseSeason) = NewWorld();
            (League tacLeague, Season tacSeason) = NewWorld();
            int clubId = tacLeague.Clubs[0].Id;

            RunSeason(baseSeason, baseLeague, null);
            RunSeason(tacSeason, tacLeague, new Dictionary<int, TacticContext>
            {
                // A strongly attacking tactic, fresh (familiarity 0).
                [clubId] = Ctx(Formation.F433, Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide, 0)
            });

            int changed = 0;
            for (int i = 0; i < baseSeason.Fixtures.Count; i++)
            {
                Fixture a = baseSeason.Fixtures[i];
                Fixture b = tacSeason.Fixtures[i];
                bool involvesUser = a.Involves(clubId);

                if (!involvesUser)
                    Assert.That((b.HomeGoals, b.AwayGoals), Is.EqualTo((a.HomeGoals, a.AwayGoals)),
                        $"AI-only fixture {a.Id} must be unaffected by another club's tactic.");
                else if ((b.HomeGoals, b.AwayGoals) != (a.HomeGoals, a.AwayGoals))
                    changed++;
            }

            Assert.That(changed, Is.GreaterThan(0),
                "The user's tactic should change at least one of their own matches.");
        }

        [Test]
        public void AttackingTactic_ScoresMore_AndConcedesMore_ThanDefensive()
        {
            (League attLeague, Season attSeason) = NewWorld();
            (League defLeague, Season defSeason) = NewWorld();
            int clubId = attLeague.Clubs[0].Id;

            // Full familiarity both sides so the comparison isolates the
            // attack/defense trade-off, not the unfamiliarity malus.
            RunSeason(attSeason, attLeague, new Dictionary<int, TacticContext>
            {
                [clubId] = Ctx(Formation.F433, Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Normal, FamMax)
            });
            RunSeason(defSeason, defLeague, new Dictionary<int, TacticContext>
            {
                [clubId] = Ctx(Formation.F433, Mentality.Defensive, Pressing.Low, Tempo.Slow, Width.Normal, FamMax)
            });

            (int gf, int ga) att = Totals(attSeason, clubId);
            (int gf, int ga) def = Totals(defSeason, clubId);

            TestContext.Out.WriteLine(
                $"[tactics] attacking GF/GA = {att.gf}/{att.ga} | defensive GF/GA = {def.gf}/{def.ga}");

            Assert.That(att.gf, Is.GreaterThan(def.gf), "Attacking tactic should score more over a season.");
            Assert.That(att.ga, Is.GreaterThan(def.ga), "Attacking tactic should concede more over a season.");
        }

        private static (int gf, int ga) Totals(Season season, int clubId)
        {
            int gf = 0, ga = 0;
            foreach (Fixture f in season.Fixtures)
            {
                if (!f.Played || !f.Involves(clubId))
                    continue;
                bool home = f.HomeClubId == clubId;
                gf += home ? f.HomeGoals : f.AwayGoals;
                ga += home ? f.AwayGoals : f.HomeGoals;
            }

            return (gf, ga);
        }
    }
}
