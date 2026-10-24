using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Issue #93 (spec R18): the style catalogue, each style a named preset over the R16
    /// instructions, and the style + overrides plan a tweaked style is stored as.
    /// </summary>
    [TestFixture]
    public class PlayingStyleCatalogueTests
    {
        private static readonly PlayingStyle[] R18Styles =
        {
            PlayingStyle.TikiTaka, PlayingStyle.PositionalPlay, PlayingStyle.Gegenpress,
            PlayingStyle.VerticalTikiTaka, PlayingStyle.ControlPossession, PlayingStyle.WingPlay,
            PlayingStyle.RouteOne, PlayingStyle.Direct, PlayingStyle.FluidCounter,
            PlayingStyle.DirectCounter, PlayingStyle.MidBlock, PlayingStyle.Catenaccio,
            PlayingStyle.ParkTheBus, PlayingStyle.ManOrientedPress, PlayingStyle.Balanced
        };

        private static FieldInfo[] AxisFields() =>
            typeof(TacticInstructions).GetFields(BindingFlags.Public | BindingFlags.Instance);

        [Test]
        public void Catalogue_HoldsExactlyTheR18Styles_OncePerStyle()
        {
            Assert.That(PlayingStyles.All.Select(s => s.Style), Is.EquivalentTo(R18Styles));
            Assert.That(Enum.GetValues(typeof(PlayingStyle)).Cast<PlayingStyle>(), Is.EquivalentTo(R18Styles));
            foreach (PlayingStyle style in R18Styles)
                Assert.That(PlayingStyles.Get(style).Style, Is.EqualTo(style));
        }

        [Test]
        public void EveryStyle_HasAUniqueName()
        {
            string[] names = PlayingStyles.All.Select(s => s.Name).ToArray();
            Assert.That(names, Has.All.Not.Empty);
            Assert.That(names, Is.Unique);
        }

        [Test]
        public void EveryStyle_ResolvesToValidInstructions()
        {
            foreach (StyleDefinition def in PlayingStyles.All)
            {
                object instructions = def.Instructions;
                foreach (FieldInfo f in AxisFields())
                    Assert.That(Enum.IsDefined(f.FieldType, f.GetValue(instructions)!), Is.True,
                        $"{def.Style}.{f.Name} is not a defined level");
                Assert.That(StylePlan.Of(def.Style).Resolve(), Is.EqualTo(def.Instructions), def.Style.ToString());
            }
        }

        [Test]
        public void EveryStyle_IsADistinctPreset()
        {
            TacticInstructions[] presets = PlayingStyles.All.Select(s => s.Instructions).ToArray();
            Assert.That(presets, Is.Unique);
        }

        [Test]
        public void Balanced_IsTheNeutralIdentity_AndEveryOtherStyleMovesOffIt()
        {
            Assert.That(PlayingStyles.Get(PlayingStyle.Balanced).Instructions, Is.EqualTo(TacticInstructions.Neutral));
            foreach (StyleDefinition def in PlayingStyles.All.Where(s => s.Style != PlayingStyle.Balanced))
                Assert.That(def.Instructions, Is.Not.EqualTo(TacticInstructions.Neutral), def.Style.ToString());
        }

        [Test]
        public void EveryStyle_SuggestsAtLeastOneExistingFormation_WithoutRepeats()
        {
            foreach (StyleDefinition def in PlayingStyles.All)
            {
                Assert.That(def.SuggestedFormations, Is.Not.Empty, def.Style.ToString());
                Assert.That(def.SuggestedFormations, Is.Unique, def.Style.ToString());
                Assert.That(def.SuggestedFormations, Is.SubsetOf(Formations.All), def.Style.ToString());
            }
        }

        [Test]
        public void Styles_AreIndependentOfFormation()
        {
            foreach (StyleDefinition def in PlayingStyles.All)
            foreach (Formation formation in Formations.All)
            {
                Tactic tactic = StylePlan.Of(def.Style).ToTactic(formation);
                Assert.That(tactic.Formation, Is.EqualTo(formation));
                Assert.That(tactic.Instructions, Is.EqualTo(def.Instructions), $"{def.Style} in {formation}");
            }
        }

        [Test]
        public void UntweakedStyle_IsNotCustom_AndStoresNoOverrides()
        {
            foreach (PlayingStyle style in R18Styles)
            {
                StylePlan plan = StylePlan.FromInstructions(style, PlayingStyles.Get(style).Instructions);
                Assert.That(plan.Style, Is.EqualTo(style));
                Assert.That(plan.IsCustom(), Is.False, style.ToString());
                Assert.That(plan.Overrides.Count(), Is.EqualTo(0), style.ToString());
            }
        }

        [Test]
        public void TweakedStyle_IsStoredAsStylePlusOnlyTheChangedAxes_AndReportedAsCustom()
        {
            TacticInstructions preset = PlayingStyles.Get(PlayingStyle.Gegenpress).Instructions;
            TacticInstructions tweaked = new TacticInstructions(
                preset.Mentality, preset.PassingDirectness, Tempo.Slow, preset.Width,
                preset.BuildUp, preset.FinalThirdApproach, preset.ShotSelection, preset.DribbleFrequency,
                preset.OnBallLost, preset.OnBallWon,
                preset.LineOfEngagement, DefensiveLine.VeryDeep, preset.Pressing,
                preset.MarkingScheme, preset.PressDirection);

            StylePlan plan = StylePlan.FromInstructions(PlayingStyle.Gegenpress, tweaked);

            Assert.That(plan.Style, Is.EqualTo(PlayingStyle.Gegenpress));
            Assert.That(plan.IsCustom(), Is.True);
            Assert.That(plan.Overrides.Count(), Is.EqualTo(2));
            Assert.That(plan.Overrides.Tempo, Is.EqualTo(Tempo.Slow));
            Assert.That(plan.Overrides.DefensiveLine, Is.EqualTo(DefensiveLine.VeryDeep));
            Assert.That(plan.Overrides.Mentality, Is.Null, "unchanged axes follow the style");
            Assert.That(plan.Resolve(), Is.EqualTo(tweaked));
        }

        [Test]
        public void EverySingleAxisTweak_OfEveryStyle_IsCustom_AndResolvesBack()
        {
            foreach (StyleDefinition def in PlayingStyles.All)
            foreach (FieldInfo f in AxisFields())
            {
                int levels = Enum.GetValues(f.FieldType).Length;
                object probe = def.Instructions;
                int current = (int)f.GetValue(probe)!;
                f.SetValue(probe, Enum.ToObject(f.FieldType, (current + 1) % levels));
                var tweaked = (TacticInstructions)probe;

                StylePlan plan = StylePlan.FromInstructions(def.Style, tweaked);
                string tag = $"{def.Style}.{f.Name}";
                Assert.That(plan.IsCustom(), Is.True, tag);
                Assert.That(plan.Overrides.Count(), Is.EqualTo(1), tag);
                Assert.That(plan.Resolve(), Is.EqualTo(tweaked), tag);
            }
        }

        [Test]
        public void OverrideEqualToThePreset_IsNotCustom()
        {
            TacticInstructions preset = PlayingStyles.Get(PlayingStyle.Catenaccio).Instructions;
            var plan = new StylePlan
            {
                Style = PlayingStyle.Catenaccio,
                Overrides = new StyleOverrides { MarkingScheme = preset.MarkingScheme }
            };
            Assert.That(plan.IsCustom(), Is.False);
            Assert.That(plan.Resolve(), Is.EqualTo(preset));
        }

        [Test]
        public void StylePlan_JsonRoundTripsStyleAndOverrides()
        {
            TacticInstructions preset = PlayingStyles.Get(PlayingStyle.WingPlay).Instructions;
            var plan = new StylePlan
            {
                Style = PlayingStyle.WingPlay,
                Overrides = new StyleOverrides
                {
                    Pressing = Pressing.High,
                    OnBallLost = OnBallLost.CounterPress,
                    PressDirection = PressDirection.ForceInside
                }
            };

            string json = JsonSerializer.Serialize(plan);
            StylePlan back = JsonSerializer.Deserialize<StylePlan>(json)!;

            Assert.That(back.Style, Is.EqualTo(PlayingStyle.WingPlay));
            Assert.That(back.Overrides.Pressing, Is.EqualTo(Pressing.High));
            Assert.That(back.Overrides.OnBallLost, Is.EqualTo(OnBallLost.CounterPress));
            Assert.That(back.Overrides.PressDirection, Is.EqualTo(PressDirection.ForceInside));
            Assert.That(back.Overrides.Count(), Is.EqualTo(3));
            Assert.That(back.IsCustom(), Is.True);
            Assert.That(back.Resolve(), Is.EqualTo(plan.Resolve()));
            Assert.That(back.Resolve(), Is.Not.EqualTo(preset));
        }

        [Test]
        public void StylePlan_StoredWithTheStyleOnly_ReadsAsThePreset()
        {
            string json = $"{{\"Style\":{(int)PlayingStyle.ParkTheBus}}}";
            StylePlan plan = JsonSerializer.Deserialize<StylePlan>(json)!;
            Assert.That(plan.IsCustom(), Is.False);
            Assert.That(plan.Resolve(), Is.EqualTo(PlayingStyles.Get(PlayingStyle.ParkTheBus).Instructions));
        }

        [Test]
        public void DefaultStylePlan_IsBalanced_AndResolvesToTheNeutralIdentity()
        {
            var plan = new StylePlan();
            Assert.That(plan.Style, Is.EqualTo(PlayingStyle.Balanced));
            Assert.That(plan.IsCustom(), Is.False);
            Assert.That(plan.Resolve(), Is.EqualTo(TacticInstructions.Neutral));
        }
    }
}
