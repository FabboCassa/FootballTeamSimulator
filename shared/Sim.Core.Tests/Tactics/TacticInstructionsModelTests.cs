using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Tactics
{
    /// <summary>
    /// Issue #89 (spec R16): the expanded instruction model, its v11 mapping, and its
    /// serialisable plan. The engine effects of the new axes are not part of this model.
    /// </summary>
    [TestFixture]
    public class TacticInstructionsModelTests
    {
        private static readonly string[] R16Axes =
        {
            // In possession
            nameof(TacticInstructions.Mentality), nameof(TacticInstructions.PassingDirectness),
            nameof(TacticInstructions.Tempo), nameof(TacticInstructions.Width),
            nameof(TacticInstructions.BuildUp), nameof(TacticInstructions.FinalThirdApproach),
            nameof(TacticInstructions.ShotSelection), nameof(TacticInstructions.DribbleFrequency),
            // Transitions
            nameof(TacticInstructions.OnBallLost), nameof(TacticInstructions.OnBallWon),
            // Out of possession
            nameof(TacticInstructions.LineOfEngagement), nameof(TacticInstructions.DefensiveLine),
            nameof(TacticInstructions.Pressing), nameof(TacticInstructions.MarkingScheme),
            nameof(TacticInstructions.PressDirection)
        };

        private static FieldInfo[] AxisFields() =>
            typeof(TacticInstructions).GetFields(BindingFlags.Public | BindingFlags.Instance);

        // An instruction set with every axis off its identity, so a lost axis shows up.
        private static TacticInstructions AllOffIdentity() => new TacticInstructions(
            Mentality.Attacking, PassingDirectness.Direct, Tempo.Fast, Width.Wide,
            BuildUp.GoalKickLong, FinalThirdApproach.CrossEarly, ShotSelection.ShootOnSight,
            DribbleFrequency.More, OnBallLost.CounterPress, OnBallWon.Counter,
            LineOfEngagement.High, DefensiveLine.VeryHigh, Pressing.High,
            MarkingScheme.ManOriented, PressDirection.ForceInside);

        [Test]
        public void Model_HasExactlyTheR16Axes()
        {
            string[] fields = AxisFields().Select(f => f.Name).OrderBy(n => n).ToArray();
            Assert.That(fields, Is.EqualTo(R16Axes.OrderBy(n => n).ToArray()));
        }

        [Test]
        public void EveryAxis_HasThreeToFiveLevels_AndItsMiddleLevelIsTheIdentity()
        {
            object neutral = TacticInstructions.Neutral;
            foreach (FieldInfo f in AxisFields())
            {
                Assert.That(f.FieldType.IsEnum, Is.True, f.Name);
                int[] values = Enum.GetValues(f.FieldType).Cast<int>().OrderBy(v => v).ToArray();
                Assert.That(values.Length, Is.InRange(3, 5), f.Name);
                Assert.That(values.Length % 2, Is.EqualTo(1), $"{f.Name} needs a middle level");
                Assert.That(values, Is.EqualTo(Enumerable.Range(0, values.Length).ToArray()),
                    $"{f.Name} levels must be contiguous from 0");
                Assert.That((int)f.GetValue(neutral)!, Is.EqualTo(values.Length / 2),
                    $"Neutral must sit on the middle level of {f.Name}");
            }
        }

        [Test]
        public void V11Mapping_EachAxisLandsOnItsR16Counterpart_AndEveryOtherAxisStaysAtIdentity()
        {
            TacticInstructions n = TacticInstructions.Neutral;
            foreach (Mentality m in Enum.GetValues(typeof(Mentality)))
            foreach (Pressing p in Enum.GetValues(typeof(Pressing)))
            foreach (Tempo t in Enum.GetValues(typeof(Tempo)))
            foreach (Width w in Enum.GetValues(typeof(Width)))
            {
                TacticInstructions i = TacticInstructions.FromV11(m, p, t, w);
                string tag = $"{m}/{p}/{t}/{w}";
                Assert.That(i.Mentality, Is.EqualTo(m), tag);
                Assert.That(i.Pressing, Is.EqualTo(p), tag);
                Assert.That(i.Tempo, Is.EqualTo(t), tag);
                Assert.That(i.Width, Is.EqualTo(w), tag);
                Assert.That(i.PassingDirectness, Is.EqualTo(n.PassingDirectness), tag);
                Assert.That(i.BuildUp, Is.EqualTo(n.BuildUp), tag);
                Assert.That(i.FinalThirdApproach, Is.EqualTo(n.FinalThirdApproach), tag);
                Assert.That(i.ShotSelection, Is.EqualTo(n.ShotSelection), tag);
                Assert.That(i.DribbleFrequency, Is.EqualTo(n.DribbleFrequency), tag);
                Assert.That(i.OnBallLost, Is.EqualTo(n.OnBallLost), tag);
                Assert.That(i.OnBallWon, Is.EqualTo(n.OnBallWon), tag);
                Assert.That(i.LineOfEngagement, Is.EqualTo(n.LineOfEngagement), tag);
                Assert.That(i.DefensiveLine, Is.EqualTo(n.DefensiveLine), tag);
                Assert.That(i.MarkingScheme, Is.EqualTo(n.MarkingScheme), tag);
                Assert.That(i.PressDirection, Is.EqualTo(n.PressDirection), tag);
                Assert.That(new TacticInstructions(m, p, t, w), Is.EqualTo(i),
                    "The four-axis constructor is the v11 mapping");
            }
        }

        [Test]
        public void V11Mapping_OfTheBalancedSet_IsTheNeutralIdentity()
        {
            TacticInstructions balanced =
                TacticInstructions.FromV11(Mentality.Balanced, Pressing.Medium, Tempo.Normal, Width.Normal);
            Assert.That(balanced, Is.EqualTo(TacticInstructions.Neutral));
            Assert.That(balanced.GetHashCode(), Is.EqualTo(TacticInstructions.Neutral.GetHashCode()));
        }

        [Test]
        public void Equality_SeesEveryExpandedAxis()
        {
            TacticInstructions full = AllOffIdentity();
            TacticInstructions n = TacticInstructions.Neutral;
            object boxedNeutral = n;
            foreach (FieldInfo f in AxisFields())
            {
                // Neutral with this one axis taken from the off-identity set.
                object probe = n;
                f.SetValue(probe, f.GetValue(full));
                Assert.That(((TacticInstructions)probe).Equals(n), Is.False, f.Name);
                Assert.That(boxedNeutral.Equals(probe), Is.False, f.Name);
            }
        }

        [Test]
        public void TacticPlan_RoundTripsEveryAxis()
        {
            var tactic = new Tactic(Formation.F352, AllOffIdentity());
            Assert.That(TacticPlan.FromTactic(tactic).ToTactic(), Is.EqualTo(tactic));
        }

        [Test]
        public void TacticPlan_JsonRoundTripsEveryAxis()
        {
            var tactic = new Tactic(Formation.F4231, AllOffIdentity());
            string json = JsonSerializer.Serialize(TacticPlan.FromTactic(tactic));
            TacticPlan back = JsonSerializer.Deserialize<TacticPlan>(json)!;
            Assert.That(back.ToTactic(), Is.EqualTo(tactic));
        }

        [Test]
        public void TacticPlan_StoredBeforeTheExpansion_ReadsAsItsV11Mapping()
        {
            // A plan as saves and the server stored it before the expanded axes existed.
            const string legacy = "{\"Formation\":2,\"Mentality\":2,\"Pressing\":0,\"Tempo\":2,\"Width\":0}";
            TacticPlan plan = JsonSerializer.Deserialize<TacticPlan>(legacy)!;
            Assert.That(plan.ToTactic(), Is.EqualTo(new Tactic((Formation)2,
                TacticInstructions.FromV11(Mentality.Attacking, Pressing.Low, Tempo.Fast, Width.Narrow))));
        }

        [Test]
        public void TacticPlan_KeyOfAV11Plan_IsUnchanged()
        {
            // Familiarity is stored under this key in saves; the expansion must not orphan it.
            var plan = TacticPlan.FromTactic(new Tactic(Formation.F442,
                TacticInstructions.FromV11(Mentality.Attacking, Pressing.Low, Tempo.Fast, Width.Normal)));
            Assert.That(plan.Key(), Is.EqualTo($"{(int)Formation.F442}:2021"));
        }
    }
}
