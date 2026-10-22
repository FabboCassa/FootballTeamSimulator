using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// The real-match spec's R17 formation catalogue: every formation of the R1 catalogue
    /// (docs/research/football-reference.md §4) has 11 valid slots, its in- and out-of-possession
    /// shape, and a lineup a club can field.
    /// </summary>
    [TestFixture]
    public class FormationCatalogueTests
    {
        // The R1 catalogue, in its table order: R17's list plus the two shapes R1 adds.
        private static readonly string[] R1Names =
        {
            "4-4-2", "4-4-2 diamond", "4-1-2-1-2", "4-4-1-1", "4-3-3", "4-3-3 holding", "4-2-3-1",
            "4-1-4-1", "4-5-1", "4-3-2-1", "4-2-2-2", "4-3-1-2", "4-1-3-2", "4-2-4", "4-6-0",
            "3-5-2", "3-4-3", "3-4-2-1", "3-4-1-2", "3-1-4-2", "3-5-1-1",
            "5-3-2", "5-4-1", "5-2-3", "5-2-1-2", "3-2-4-1", "5-2-2-1"
        };

        private static Club _club = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260615));
            _club = league.Clubs[9];
        }

        [Test]
        public void Catalogue_HoldsEveryR1Formation_Once()
        {
            string[] names = Formations.Catalogue.Select(Formations.Name).ToArray();

            Assert.That(names, Is.EqualTo(R1Names));
            Assert.That(Formations.Catalogue.Distinct().Count(), Is.EqualTo(Formations.Catalogue.Length));
            Assert.That(Formations.Catalogue, Is.SupersetOf(Formations.All), "the six original shapes stay in");
        }

        [Test]
        public void OriginalValues_StayStableForSaves()
        {
            Assert.That((int)Formation.F433, Is.EqualTo(0));
            Assert.That((int)Formation.F442, Is.EqualTo(1));
            Assert.That((int)Formation.F352, Is.EqualTo(2));
            Assert.That((int)Formation.F4231, Is.EqualTo(3));
            Assert.That((int)Formation.F532, Is.EqualTo(4));
            Assert.That((int)Formation.F343, Is.EqualTo(5));
            Assert.That(Formations.Catalogue.Select(f => (int)f).OrderBy(v => v),
                Is.EqualTo(Enumerable.Range(0, Formations.Catalogue.Length)), "new values are appended, no gaps");
        }

        [Test]
        public void OriginalSlots_AreUnchanged()
        {
            // The golden master plays these role lists; the catalogue must not touch them.
            const PositionRole GK = PositionRole.Goalkeeper, CB = PositionRole.CentreBack, FB = PositionRole.FullBack,
                DM = PositionRole.DefensiveMidfielder, CM = PositionRole.CentralMidfielder,
                AM = PositionRole.AttackingMidfielder, W = PositionRole.Winger, ST = PositionRole.Striker;

            Assert.That(Formations.Roles(Formation.F433), Is.EqualTo(new[] { GK, CB, CB, FB, FB, DM, CM, CM, W, W, ST }));
            Assert.That(Formations.Roles(Formation.F442), Is.EqualTo(new[] { GK, CB, CB, FB, FB, CM, CM, CM, CM, ST, ST }));
            Assert.That(Formations.Roles(Formation.F352), Is.EqualTo(new[] { GK, CB, CB, CB, CM, CM, CM, CM, CM, ST, ST }));
            Assert.That(Formations.Roles(Formation.F4231), Is.EqualTo(new[] { GK, CB, CB, FB, FB, DM, DM, AM, W, W, ST }));
            Assert.That(Formations.Roles(Formation.F532), Is.EqualTo(new[] { GK, CB, CB, CB, FB, FB, CM, CM, CM, ST, ST }));
            Assert.That(Formations.Roles(Formation.F343), Is.EqualTo(new[] { GK, CB, CB, CB, CM, CM, CM, CM, W, W, ST }));
        }

        [Test]
        public void EveryFormation_HasElevenValidSlots()
        {
            foreach (Formation f in Formations.Catalogue)
            {
                PositionRole[] roles = Formations.Roles(f);
                string name = Formations.Name(f);

                Assert.That(roles.Length, Is.EqualTo(11), $"{name}: 11 slots");
                Assert.That(roles[0], Is.EqualTo(PositionRole.Goalkeeper), $"{name}: the keeper is slot 0");
                Assert.That(roles.Count(r => r == PositionRole.Goalkeeper), Is.EqualTo(1), $"{name}: one keeper");
                Assert.That(roles.All(r => Enum.IsDefined(typeof(PositionRole), r)), Is.True, $"{name}: known roles");

                // The back line is the name's first number: wing-backs of a back three count as
                // midfielders (as in the original 3-5-2), those of a back five as full-backs.
                int defenders = roles.Count(r => r == PositionRole.CentreBack || r == PositionRole.FullBack);
                Assert.That(defenders, Is.EqualTo(name[0] - '0'), $"{name}: back line");
                Assert.That(roles.Count(r => r == PositionRole.CentreBack), Is.InRange(2, 3), $"{name}: centre-backs");
            }
        }

        [Test]
        public void EveryFormation_HasItsR1InAndOutOfPossessionShape()
        {
            foreach (Formation f in Formations.Catalogue)
            {
                string name = Formations.Name(f);
                AssertOutfieldShape(Formations.InPossession(f), $"{name} in possession");
                AssertOutfieldShape(Formations.OutOfPossession(f), $"{name} out of possession");
            }

            // Spot checks against the R1 table.
            Assert.That(Formations.InPossession(Formation.F433), Is.EqualTo("2-3-5"));
            Assert.That(Formations.OutOfPossession(Formation.F433), Is.EqualTo("4-5-1"));
            Assert.That(Formations.OutOfPossession(Formation.F352), Is.EqualTo("5-3-2"));
            Assert.That(Formations.InPossession(Formation.F442Diamond), Is.EqualTo("2-3-3-2"));
            Assert.That(Formations.InPossession(Formation.F541), Is.EqualTo("3-6-1"));
            Assert.That(Formations.OutOfPossession(Formation.F3241), Is.EqualTo("4-4-2"));
        }

        [Test]
        public void EveryFormation_CanFieldItsBestEleven()
        {
            foreach (Formation f in Formations.Catalogue)
            {
                Lineup lineup = LineupSelector.BestEleven(_club, f);

                Assert.DoesNotThrow(() => lineup.Validate(), $"{Formations.Name(f)} must be a legal lineup");
                Assert.That(lineup.Slots.Select(s => s.Role), Is.EqualTo(Formations.Roles(f)), Formations.Name(f));
                Assert.That(lineup.Slots.Select(s => s.Player.Id).Distinct().Count(), Is.EqualTo(11), Formations.Name(f));
                Assert.That(lineup.Slots[0].Player.Role, Is.EqualTo(PositionRole.Goalkeeper),
                    $"{Formations.Name(f)}: a real keeper in goal");
            }
        }

        [Test]
        public void EveryFormation_CanFieldACompetentEleven()
        {
            foreach (Formation f in Formations.Catalogue)
            {
                Lineup lineup = LineupSelector.CompetentEleven(
                    _club, Formations.Roles(f), competencePercent: 60, maxSlips: 2, new Pcg32(7));

                Assert.DoesNotThrow(() => lineup.Validate(), $"{Formations.Name(f)} must be a legal lineup");
                Assert.That(lineup.Slots.Select(s => s.Role), Is.EqualTo(Formations.Roles(f)), Formations.Name(f));
            }
        }

        [Test]
        public void EveryFormation_PlaysAWatchedMatch()
        {
            var cfg = new BalanceConfig();
            foreach (Formation f in Formations.Catalogue)
            {
                Lineup lineup = LineupSelector.BestEleven(_club, f);
                var side = new TacticContext(new Tactic(f), cfg.Tactics.FamiliarityMax);
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);

                MatchReport report = engine.Simulate(lineup, lineup, new Pcg32(92), new MatchTactics(side, side));

                Assert.That(report.Positions, Is.Not.Null, Formations.Name(f));
                Assert.That(report.HomeGoals + report.AwayGoals, Is.InRange(0, 15), Formations.Name(f));
            }
        }

        [Test]
        public void UnknownFormation_IsRejected()
        {
            var unknown = (Formation)Formations.Catalogue.Length;
            Assert.Throws<ArgumentOutOfRangeException>(() => Formations.Roles(unknown));
            Assert.Throws<ArgumentOutOfRangeException>(() => Formations.Name(unknown));
        }

        private static void AssertOutfieldShape(string shape, string what)
        {
            int[] lines = shape.Split('-').Select(int.Parse).ToArray();
            Assert.That(lines.Length, Is.InRange(3, 5), $"{what} '{shape}': lines");
            Assert.That(lines.All(n => n >= 1), Is.True, $"{what} '{shape}': every line is manned");
            Assert.That(lines.Sum(), Is.EqualTo(10), $"{what} '{shape}': ten outfield players");
        }
    }
}
