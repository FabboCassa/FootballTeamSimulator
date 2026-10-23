using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Issue #99 (spec R21): the edits behind the Tattiche screen. Picking a style loads its
    /// preset, tweaking an instruction marks the style custom, and every edit returns a new plan.
    /// </summary>
    [TestFixture]
    public class TacticEditorTests
    {
        [Test]
        public void PickingAStyle_LoadsItsPreset_KeepsTheFormation_AndIsNotCustom()
        {
            TacticPlan start = TacticEditor.WithFormation(TacticPlan.Neutral(), Formation.F343);

            TacticPlan tiki = TacticEditor.WithStyle(start, PlayingStyle.TikiTaka);

            Assert.Multiple(() =>
            {
                Assert.That(tiki.Style, Is.EqualTo(PlayingStyle.TikiTaka));
                Assert.That(tiki.Formation, Is.EqualTo(Formation.F343));
                Assert.That(tiki.ToTactic().Instructions, Is.EqualTo(PlayingStyles.Get(PlayingStyle.TikiTaka).Instructions));
                Assert.That(TacticEditor.IsCustom(tiki), Is.False);
                Assert.That(start.Style, Is.EqualTo(PlayingStyle.Balanced), "the source plan is not mutated");
            });
        }

        [Test]
        public void TweakingAnInstruction_KeepsTheStyle_AndMarksItCustom()
        {
            TacticPlan tiki = TacticEditor.WithStyle(TacticPlan.Neutral(), PlayingStyle.TikiTaka);

            TacticPlan tweaked = TacticEditor.WithInstruction(tiki, InstructionAxis.Tempo, (int)Tempo.Fast);

            TacticInstructions preset = PlayingStyles.Get(PlayingStyle.TikiTaka).Instructions;
            Assert.Multiple(() =>
            {
                Assert.That(tweaked.Style, Is.EqualTo(PlayingStyle.TikiTaka));
                Assert.That(TacticEditor.IsCustom(tweaked), Is.True);
                Assert.That(tweaked.Tempo, Is.EqualTo(Tempo.Fast));
                Assert.That(tweaked.Pressing, Is.EqualTo(preset.Pressing), "untouched axes keep the preset");
                Assert.That(tweaked.ToStylePlan().Overrides.Count(), Is.EqualTo(1));
                Assert.That(tiki.Tempo, Is.EqualTo(preset.Tempo), "the source plan is not mutated");
                Assert.That(TacticEditor.IsCustom(tiki), Is.False);
            });
        }

        [Test]
        public void TweakingBackToThePresetLevel_ClearsCustom()
        {
            TacticPlan tiki = TacticEditor.WithStyle(TacticPlan.Neutral(), PlayingStyle.TikiTaka);
            TacticPlan tweaked = TacticEditor.WithInstruction(tiki, InstructionAxis.DefensiveLine, (int)DefensiveLine.VeryDeep);

            TacticPlan back = TacticEditor.WithInstruction(tweaked, InstructionAxis.DefensiveLine, (int)DefensiveLine.High);

            Assert.That(TacticEditor.IsCustom(tweaked), Is.True);
            Assert.That(TacticEditor.IsCustom(back), Is.False);
        }

        [Test]
        public void RepickingAStyle_DropsTheTweaks()
        {
            TacticPlan custom = TacticEditor.WithInstruction(
                TacticEditor.WithStyle(TacticPlan.Neutral(), PlayingStyle.Catenaccio),
                InstructionAxis.Mentality, (int)Mentality.Attacking);

            TacticPlan reset = TacticEditor.WithStyle(custom, PlayingStyle.Catenaccio);
            TacticPlan other = TacticEditor.WithStyle(custom, PlayingStyle.Gegenpress);

            Assert.Multiple(() =>
            {
                Assert.That(TacticEditor.IsCustom(reset), Is.False);
                Assert.That(reset.Mentality, Is.EqualTo(Mentality.Defensive));
                Assert.That(TacticEditor.IsCustom(other), Is.False);
                Assert.That(other.ToTactic().Instructions, Is.EqualTo(PlayingStyles.Get(PlayingStyle.Gegenpress).Instructions));
            });
        }

        [Test]
        public void ChangingTheFormation_KeepsStyleAndTweaks()
        {
            TacticPlan custom = TacticEditor.WithInstruction(
                TacticEditor.WithStyle(TacticPlan.Neutral(), PlayingStyle.WingPlay),
                InstructionAxis.MarkingScheme, (int)MarkingScheme.ManOriented);

            TacticPlan moved = TacticEditor.WithFormation(custom, Formation.F4411);

            Assert.Multiple(() =>
            {
                Assert.That(moved.Formation, Is.EqualTo(Formation.F4411));
                Assert.That(moved.Style, Is.EqualTo(PlayingStyle.WingPlay));
                Assert.That(moved.MarkingScheme, Is.EqualTo(MarkingScheme.ManOriented));
                Assert.That(TacticEditor.IsCustom(moved), Is.True);
                Assert.That(custom.Formation, Is.EqualTo(Formation.F433), "the source plan is not mutated");
            });
        }

        [Test]
        public void Axes_ListEveryInstructionOnce_GroupedByPhase()
        {
            InstructionAxis[] all = Enum.GetValues(typeof(InstructionAxis)).Cast<InstructionAxis>().ToArray();
            Assert.That(TacticEditor.Axes, Is.EquivalentTo(all));
            Assert.That(all.Length, Is.EqualTo(typeof(TacticInstructions).GetFields().Length));

            InstructionPhase[] phases = TacticEditor.Axes.Select(TacticEditor.PhaseOf).ToArray();
            Assert.That(phases, Is.Ordered, "the screen shows one block per phase");
            Assert.That(TacticEditor.PhaseOf(InstructionAxis.OnBallLost), Is.EqualTo(InstructionPhase.Transitions));
            Assert.That(TacticEditor.PhaseOf(InstructionAxis.Pressing), Is.EqualTo(InstructionPhase.OutOfPossession));
            Assert.That(TacticEditor.PhaseOf(InstructionAxis.Width), Is.EqualTo(InstructionPhase.InPossession));
        }

        [Test]
        public void EveryAxis_SetsAndReadsEachOfItsLevels_AndRejectsOthers()
        {
            Assert.Multiple(() =>
            {
                foreach (InstructionAxis axis in TacticEditor.Axes)
                {
                    int count = TacticEditor.LevelCount(axis);
                    Assert.That(count, Is.EqualTo(Enum.GetValues(TacticEditor.LevelType(axis)).Length), $"{axis} levels");

                    for (int level = 0; level < count; level++)
                    {
                        TacticPlan plan = TacticEditor.WithInstruction(TacticPlan.Neutral(), axis, level);
                        Assert.That(TacticEditor.LevelOf(plan, axis), Is.EqualTo(level), $"{axis}={level}");
                        Assert.That(plan.Formation, Is.EqualTo(Formation.F433));
                    }

                    Assert.Throws<ArgumentOutOfRangeException>(() => TacticEditor.WithInstruction(TacticPlan.Neutral(), axis, -1));
                    Assert.Throws<ArgumentOutOfRangeException>(() => TacticEditor.WithInstruction(TacticPlan.Neutral(), axis, count));
                }
            });
            Assert.That(TacticEditor.LevelCount(InstructionAxis.DefensiveLine), Is.EqualTo(5));
        }

        [Test]
        public void EveryAxis_WritesOnlyItsOwnField()
        {
            TacticInstructions neutral = TacticInstructions.Neutral;
            foreach (InstructionAxis axis in TacticEditor.Axes)
            {
                int nonNeutral = TacticEditor.LevelOf(TacticPlan.Neutral(), axis) == 0 ? 1 : 0;
                TacticPlan plan = TacticEditor.WithInstruction(TacticPlan.Neutral(), axis, nonNeutral);

                foreach (InstructionAxis other in TacticEditor.Axes.Where(a => a != axis))
                    Assert.That(TacticEditor.LevelOf(plan, other), Is.EqualTo(TacticEditor.LevelOf(TacticPlan.Neutral(), other)),
                        $"setting {axis} moved {other}");
                Assert.That(plan.ToTactic().Instructions, Is.Not.EqualTo(neutral), $"{axis} changed nothing");
            }
        }

        [Test]
        public void ACustomPlan_SurvivesTheStoredStylePlanRoundTrip()
        {
            TacticPlan custom = TacticEditor.WithInstruction(
                TacticEditor.WithStyle(TacticPlan.Neutral(), PlayingStyle.ParkTheBus),
                InstructionAxis.ShotSelection, (int)ShotSelection.ShootOnSight);

            TacticPlan reloaded = TacticPlan.FromStyle(custom.Formation, custom.ToStylePlan());

            Assert.That(reloaded.ToTactic(), Is.EqualTo(custom.ToTactic()));
            Assert.That(reloaded.Style, Is.EqualTo(PlayingStyle.ParkTheBus));
            Assert.That(TacticEditor.IsCustom(reloaded), Is.True);
        }
    }
}
