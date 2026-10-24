using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Career;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Career
{
    /// <summary>
    /// R24 of the real-match spec: the fast model reads the whole R16 instruction set and the
    /// formation (<see cref="QuickTacticPercent"/>), so background matches react to styles and
    /// formations. Every table keeps the identity on its neutral level (4-3-3 for formations), so
    /// a Balanced 4-3-3 side plays exactly the v12 fast model. The full-engine comparison is
    /// <see cref="FastModelStyleHarnessTests"/>.
    /// </summary>
    [TestFixture]
    public class QuickTacticPercentTests
    {
        private const ulong Seed = 20261023UL;
        private const int Strength = 66;
        private static readonly Tactic Neutral = Tactic.Neutral;

        [Test]
        public void NeutralAgainstNeutral_IsTheIdentity()
        {
            Assert.That(QuickTacticPercent.Of(Neutral, Neutral, new BalanceConfig().World), Is.EqualTo(100));
        }

        [Test]
        public void EveryTable_HasOneEntryPerLevel_AndTheIdentityOnTheNeutralLevel()
        {
            WorldBalance w = new BalanceConfig().World;
            foreach ((string name, int[] table, int levels, int identity) in Tables(w))
            {
                Assert.That(table.Length, Is.EqualTo(levels), name);
                Assert.That(table[identity], Is.EqualTo(0), name);
            }
        }

        [Test]
        public void AGoalsForEntry_MovesTheSidesOwnPercent_AndAGoalsAgainstEntry_TheOpponents()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickShotSelectionGoalsForPercent = new[] { 0, 0, 12 };
            cfg.World.QuickShotSelectionGoalsAgainstPercent = new[] { 0, 0, 0 };
            cfg.World.QuickDefensiveLineGoalsForPercent = new[] { 0, 0, 0, 0, 0 };
            cfg.World.QuickDefensiveLineGoalsAgainstPercent = new[] { -9, 0, 0, 0, 0 };
            var shooter = new Tactic(Formation.F433, With(ShotSelection.ShootOnSight, DefensiveLine.VeryDeep));

            Assert.That(QuickTacticPercent.Of(shooter, Neutral, cfg.World), Is.EqualTo(112), "own goals for");
            Assert.That(QuickTacticPercent.Of(Neutral, shooter, cfg.World), Is.EqualTo(91), "the opponent's goals");
        }

        [Test]
        public void TheFormationTables_AreReadForTheSidesOwnShape()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickFormationGoalsForPercent = new int[Formations.Catalogue.Length];
            cfg.World.QuickFormationGoalsAgainstPercent = new int[Formations.Catalogue.Length];
            cfg.World.QuickFormationGoalsForPercent[(int)Formation.F424] = 7;
            cfg.World.QuickFormationGoalsAgainstPercent[(int)Formation.F424] = 15;
            var f424 = new Tactic(Formation.F424);

            Assert.That(QuickTacticPercent.Of(f424, Neutral, cfg.World), Is.EqualTo(107));
            Assert.That(QuickTacticPercent.Of(Neutral, f424, cfg.World), Is.EqualTo(115));
        }

        [Test]
        public void ACataloguePreset_ReadsItsStyleEntry_NotTheSumOfItsAxes()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickStyleGoalsForPercent = new int[PlayingStyles.All.Count];
            cfg.World.QuickStyleGoalsAgainstPercent = new int[PlayingStyles.All.Count];
            cfg.World.QuickStyleGoalsForPercent[(int)PlayingStyle.Gegenpress] = 30;
            cfg.World.QuickStyleGoalsAgainstPercent[(int)PlayingStyle.Gegenpress] = 50;
            var gegenpress = new Tactic(Formation.F433, PlayingStyles.Get(PlayingStyle.Gegenpress).Instructions);

            Assert.That(QuickTacticPercent.Of(gegenpress, Neutral, cfg.World), Is.EqualTo(130));
            Assert.That(QuickTacticPercent.Of(Neutral, gegenpress, cfg.World), Is.EqualTo(150));
        }

        [Test]
        public void ATweakedPreset_AddsTheSingleAxisDifferenceOfTheChangedAxis()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickStyleGoalsForPercent = new int[PlayingStyles.All.Count];
            cfg.World.QuickStyleGoalsForPercent[(int)PlayingStyle.TikiTaka] = -18;
            cfg.World.QuickShotSelectionGoalsForPercent = new[] { -11, 0, 11 };
            var tweaked = StylePlan.Of(PlayingStyle.TikiTaka);
            tweaked.Overrides.ShotSelection = ShotSelection.ShootOnSight;   // the preset is Patient

            int percent = QuickTacticPercent.Of(tweaked.ToTactic(Formation.F433), Neutral, cfg.World);

            Assert.That(percent, Is.EqualTo(100 - 18 + (11 - -11)));
        }

        [Test]
        public void NearestStyle_IsThePresetWithTheFewestChangedAxes_AndBalancedWinsTies()
        {
            Assert.That(QuickTacticPercent.NearestStyle(TacticInstructions.Neutral), Is.EqualTo((int)PlayingStyle.Balanced));
            foreach (StyleDefinition def in PlayingStyles.All)
                Assert.That(QuickTacticPercent.NearestStyle(def.Instructions), Is.EqualTo((int)def.Style), def.Name);

            var v11 = new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Wide);
            Assert.That(QuickTacticPercent.NearestStyle(v11), Is.EqualTo((int)PlayingStyle.Balanced), "a v11 set reads the axis tables");
        }

        [Test]
        public void AMissingOrShortTable_ReadsAsTheIdentity()
        {
            var cfg = new BalanceConfig();
            cfg.World.QuickMarkingSchemeGoalsForPercent = null!;
            cfg.World.QuickFormationGoalsForPercent = new[] { 0 };
            var tactic = new Tactic(Formation.F3511, With(MarkingScheme.ManOriented));
            var reference = new Tactic(Formation.F433, With(MarkingScheme.Mixed));

            int ownWithout = QuickTacticPercent.Of(tactic, Neutral, cfg.World);
            Assert.That(ownWithout, Is.EqualTo(QuickTacticPercent.Of(reference, Neutral, cfg.World)));
        }

        [Test]
        public void ExpectedGoals_OfA433Tactic_AreThoseOfItsInstructions()
        {
            var cfg = new BalanceConfig();
            foreach (StyleDefinition home in PlayingStyles.All)
            foreach (StyleDefinition away in new[] { PlayingStyles.Get(PlayingStyle.Balanced), PlayingStyles.Get(PlayingStyle.ParkTheBus) })
            {
                QuickResultResolver.ExpectedGoals(70, 62, cfg, home.Instructions, away.Instructions, out double h1, out double a1);
                QuickResultResolver.ExpectedGoals(70, 62, cfg,
                    new Tactic(Formation.F433, home.Instructions), new Tactic(Formation.F433, away.Instructions), out double h2, out double a2);
                Assert.That((h2, a2), Is.EqualTo((h1, a1)), home.Name + " vs " + away.Name);
            }
        }

        [Test]
        public void ABalanced433Side_IsBitIdenticalToNoTactic()
        {
            var cfg = new BalanceConfig();
            for (int id = 1; id <= 200; id++)
            {
                var none = new Fixture { Id = id };
                var balanced = new Fixture { Id = id };
                QuickResultResolver.Resolve(none, 70, 62, Seed, cfg);
                QuickResultResolver.Resolve(balanced, 70, 62, Seed, cfg, StylePlan.Of(PlayingStyle.Balanced).ToTactic(Formation.F433), Neutral);
                Assert.That((balanced.HomeGoals, balanced.AwayGoals), Is.EqualTo((none.HomeGoals, none.AwayGoals)), $"fixture {id}");
            }
        }

        /// <summary>
        /// The sign of each shipped style's shift (in its first suggested formation, against Balanced 4-3-3): the
        /// full engine's, measured by <see cref="FastModelStyleHarnessTests"/>. 0 where the engine's shift is inside
        /// two standard errors, so only the fast model's own reading is free there.
        /// </summary>
        [TestCaseSource(nameof(StyleSigns))]
        public void ShippedTables_ShiftEachStyleTheWayTheFullEngineDoes(PlayingStyle style, int goalsFor, int goalsAgainst)
        {
            var cfg = new BalanceConfig();
            StyleDefinition def = PlayingStyles.Get(style);
            var tactic = new Tactic(def.SuggestedFormations[0], def.Instructions);
            QuickResultResolver.ExpectedGoals(Strength, Strength, cfg, Neutral, Neutral, out double home, out double away);

            QuickResultResolver.ExpectedGoals(Strength, Strength, cfg, tactic, Neutral, out double h, out double a);
            if (goalsFor != 0) Assert.That(Math.Sign(h - home), Is.EqualTo(goalsFor), "goals for");
            if (goalsAgainst != 0) Assert.That(Math.Sign(a - away), Is.EqualTo(goalsAgainst), "goals against");
        }

        private static IEnumerable<TestCaseData> StyleSigns() => FastModelStyleHarnessTests.EngineSigns
            .Select(s => new TestCaseData(s.Style, s.GoalsFor, s.GoalsAgainst).SetName($"Style_{s.Style}"));

        [Test]
        public void BackgroundProgressor_PassesEachClubsFormation()
        {
            World plain = Generate(), shaped = Generate();
            Club club = shaped.BackgroundSeason.Fixtures.Select(f => shaped.FindClub(f.HomeClubId)).First(c => c != null)!;
            var cfg = new BalanceConfig();
            cfg.World.QuickFormationGoalsForPercent = new int[Formations.Catalogue.Length];
            cfg.World.QuickFormationGoalsForPercent[(int)Formation.F424] = 40;
            var progressor = new BackgroundLeagueProgressor(cfg);

            progressor.AdvanceTo(plain, int.MaxValue, Seed);
            progressor.AdvanceTo(shaped, int.MaxValue, Seed, new Dictionary<int, Tactic> { [club.Id] = new Tactic(Formation.F424) });

            int before = 0, after = 0;
            for (int i = 0; i < plain.BackgroundSeason.Fixtures.Count; i++)
            {
                Fixture p = plain.BackgroundSeason.Fixtures[i], t = shaped.BackgroundSeason.Fixtures[i];
                if (p.HomeClubId != club.Id && p.AwayClubId != club.Id)
                {
                    Assert.That((t.HomeGoals, t.AwayGoals), Is.EqualTo((p.HomeGoals, p.AwayGoals)), "other clubs' fixtures are untouched");
                    continue;
                }

                before += p.HomeClubId == club.Id ? p.HomeGoals : p.AwayGoals;
                after += t.HomeClubId == club.Id ? t.HomeGoals : t.AwayGoals;
            }

            Assert.That(after, Is.GreaterThan(before), "the club scores more in its 4-2-4 over a season");
        }

        private static TacticInstructions With(ShotSelection shot, DefensiveLine line)
        {
            TacticInstructions n = TacticInstructions.Neutral;
            return new TacticInstructions(n.Mentality, n.PassingDirectness, n.Tempo, n.Width, n.BuildUp, n.FinalThirdApproach,
                shot, n.DribbleFrequency, n.OnBallLost, n.OnBallWon, n.LineOfEngagement, line, n.Pressing, n.MarkingScheme,
                n.PressDirection);
        }

        private static TacticInstructions With(MarkingScheme marking)
        {
            TacticInstructions n = TacticInstructions.Neutral;
            return new TacticInstructions(n.Mentality, n.PassingDirectness, n.Tempo, n.Width, n.BuildUp, n.FinalThirdApproach,
                n.ShotSelection, n.DribbleFrequency, n.OnBallLost, n.OnBallWon, n.LineOfEngagement, n.DefensiveLine, n.Pressing,
                marking, n.PressDirection);
        }

        private static World Generate()
        {
            var scope = new WorldScope { Size = DatabaseSize.Medium };
            scope.Playable.Add(new PlayableNation { Code = "ITA", PlayableTiers = 1 });
            return new WorldGenerator(new WorldGenerationOptions { Scope = scope }, new BalanceConfig()).Generate(Seed);
        }

        private static int Levels<T>() where T : struct, Enum => Enum.GetValues(typeof(T)).Length;

        private static IEnumerable<(string, int[], int, int)> Tables(WorldBalance w)
        {
            TacticInstructions n = TacticInstructions.Neutral;
            return new (string, int[], int, int)[]
            {
                ("mentality GF", w.QuickMentalityGoalsForPercent, Levels<Mentality>(), (int)n.Mentality),
                ("mentality GA", w.QuickMentalityGoalsAgainstPercent, Levels<Mentality>(), (int)n.Mentality),
                ("passing GF", w.QuickPassingDirectnessGoalsForPercent, Levels<PassingDirectness>(), (int)n.PassingDirectness),
                ("passing GA", w.QuickPassingDirectnessGoalsAgainstPercent, Levels<PassingDirectness>(), (int)n.PassingDirectness),
                ("tempo GF", w.QuickTempoGoalsForPercent, Levels<Tempo>(), (int)n.Tempo),
                ("tempo GA", w.QuickTempoGoalsAgainstPercent, Levels<Tempo>(), (int)n.Tempo),
                ("width GF", w.QuickWidthGoalsForPercent, Levels<Width>(), (int)n.Width),
                ("width GA", w.QuickWidthGoalsAgainstPercent, Levels<Width>(), (int)n.Width),
                ("build-up GF", w.QuickBuildUpGoalsForPercent, Levels<BuildUp>(), (int)n.BuildUp),
                ("build-up GA", w.QuickBuildUpGoalsAgainstPercent, Levels<BuildUp>(), (int)n.BuildUp),
                ("final third GF", w.QuickFinalThirdGoalsForPercent, Levels<FinalThirdApproach>(), (int)n.FinalThirdApproach),
                ("final third GA", w.QuickFinalThirdGoalsAgainstPercent, Levels<FinalThirdApproach>(), (int)n.FinalThirdApproach),
                ("shot selection GF", w.QuickShotSelectionGoalsForPercent, Levels<ShotSelection>(), (int)n.ShotSelection),
                ("shot selection GA", w.QuickShotSelectionGoalsAgainstPercent, Levels<ShotSelection>(), (int)n.ShotSelection),
                ("dribbling GF", w.QuickDribbleGoalsForPercent, Levels<DribbleFrequency>(), (int)n.DribbleFrequency),
                ("dribbling GA", w.QuickDribbleGoalsAgainstPercent, Levels<DribbleFrequency>(), (int)n.DribbleFrequency),
                ("ball lost GF", w.QuickOnBallLostGoalsForPercent, Levels<OnBallLost>(), (int)n.OnBallLost),
                ("ball lost GA", w.QuickOnBallLostGoalsAgainstPercent, Levels<OnBallLost>(), (int)n.OnBallLost),
                ("ball won GF", w.QuickOnBallWonGoalsForPercent, Levels<OnBallWon>(), (int)n.OnBallWon),
                ("ball won GA", w.QuickOnBallWonGoalsAgainstPercent, Levels<OnBallWon>(), (int)n.OnBallWon),
                ("engagement GF", w.QuickLineOfEngagementGoalsForPercent, Levels<LineOfEngagement>(), (int)n.LineOfEngagement),
                ("engagement GA", w.QuickLineOfEngagementGoalsAgainstPercent, Levels<LineOfEngagement>(), (int)n.LineOfEngagement),
                ("defensive line GF", w.QuickDefensiveLineGoalsForPercent, Levels<DefensiveLine>(), (int)n.DefensiveLine),
                ("defensive line GA", w.QuickDefensiveLineGoalsAgainstPercent, Levels<DefensiveLine>(), (int)n.DefensiveLine),
                ("pressing GF", w.QuickPressingGoalsForPercent, Levels<Pressing>(), (int)n.Pressing),
                ("pressing GA", w.QuickPressingGoalsAgainstPercent, Levels<Pressing>(), (int)n.Pressing),
                ("marking GF", w.QuickMarkingSchemeGoalsForPercent, Levels<MarkingScheme>(), (int)n.MarkingScheme),
                ("marking GA", w.QuickMarkingSchemeGoalsAgainstPercent, Levels<MarkingScheme>(), (int)n.MarkingScheme),
                ("press direction GF", w.QuickPressDirectionGoalsForPercent, Levels<PressDirection>(), (int)n.PressDirection),
                ("press direction GA", w.QuickPressDirectionGoalsAgainstPercent, Levels<PressDirection>(), (int)n.PressDirection),
                ("style GF", w.QuickStyleGoalsForPercent, Levels<PlayingStyle>(), (int)PlayingStyle.Balanced),
                ("style GA", w.QuickStyleGoalsAgainstPercent, Levels<PlayingStyle>(), (int)PlayingStyle.Balanced),
                ("formation GF", w.QuickFormationGoalsForPercent, Levels<Formation>(), (int)Tactic.Neutral.Formation),
                ("formation GA", w.QuickFormationGoalsAgainstPercent, Levels<Formation>(), (int)Tactic.Neutral.Formation)
            };
        }
    }
}
