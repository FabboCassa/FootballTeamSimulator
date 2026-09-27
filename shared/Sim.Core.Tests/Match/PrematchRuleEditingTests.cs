using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The pre-match plan rule editor (issue #44): a rule may call a touchline shout, the editor
    /// only ever adds a rule the engine can act on, and it never edits the saved plan in place.
    /// </summary>
    [TestFixture]
    public class PrematchRuleEditingTests
    {
        private static readonly int FamMax = new BalanceConfig().Tactics.FamiliarityMax;

        private static Club _club = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _club = new LeagueGenerator().Generate(new Pcg32(20260927)).Clubs[1];
        }

        [Test]
        public void AShoutRule_IsAValidRule_WithOnlyTheShoutAsItsAction()
        {
            PrematchRule rule = PrematchRule.ForShout(70, ScoreSituation.Losing, TouchlineShout.AllForward);

            Assert.Multiple(() =>
            {
                Assert.That(rule.FromMinute, Is.EqualTo(70));
                Assert.That(rule.When, Is.EqualTo(ScoreSituation.Losing));
                Assert.That(rule.Margin, Is.EqualTo(1));
                Assert.That(rule.Shout, Is.EqualTo(TouchlineShout.AllForward));
                Assert.That(rule.ChangeInstructions, Is.False);
                Assert.That(rule.SubOutPlayerId, Is.EqualTo(0));
                Assert.That(rule.SubInPlayerId, Is.EqualTo(0));
                Assert.That(rule.IsValid(), Is.True);
            });
        }

        [TestCase(0, ScoreSituation.Always, TouchlineShout.PressHigh, TestName = "minute before kickoff")]
        [TestCase(91, ScoreSituation.Always, TouchlineShout.PressHigh, TestName = "minute after full time")]
        [TestCase(60, (ScoreSituation)9, TouchlineShout.PressHigh, TestName = "unknown score situation")]
        [TestCase(60, ScoreSituation.Drawing, (TouchlineShout)42, TestName = "unknown shout")]
        [TestCase(60, ScoreSituation.Drawing, TouchlineShout.None, TestName = "a rule that does nothing")]
        public void ARuleTheEngineCannotActOn_IsInvalid(int minute, ScoreSituation when, TouchlineShout shout)
        {
            var rule = new PrematchRule { FromMinute = minute, When = when, Shout = shout };
            Assert.That(rule.IsValid(), Is.False);
        }

        [Test]
        public void AZeroMargin_OrAHalfSubstitution_IsInvalid_ButATacticChangeAloneIsFine()
        {
            Assert.That(new PrematchRule { FromMinute = 60, Margin = 0, Shout = TouchlineShout.Encourage }.IsValid(), Is.False);
            Assert.That(new PrematchRule { FromMinute = 60, SubOutPlayerId = 7 }.IsValid(), Is.False);
            Assert.That(new PrematchRule { FromMinute = 60, ChangeInstructions = true }.IsValid(), Is.True);
            Assert.That(new PrematchRule { FromMinute = 60, SubOutPlayerId = 7, SubInPlayerId = 8 }.IsValid(), Is.True);
        }

        [Test]
        public void WithRule_ReturnsANewPlan_AndLeavesTheSavedOneAlone()
        {
            var saved = new PrematchPlan();
            PrematchPlan edited = saved
                .WithRule(PrematchRule.ForShout(60, ScoreSituation.Drawing, TouchlineShout.Encourage))
                .WithRule(PrematchRule.ForShout(75, ScoreSituation.Losing, TouchlineShout.AllForward));

            Assert.That(saved.Rules, Is.Empty, "the saved plan is untouched until the coach saves");
            Assert.That(edited, Is.Not.SameAs(saved));
            Assert.That(edited.Rules.Select(r => r.Shout),
                Is.EqualTo(new[] { TouchlineShout.Encourage, TouchlineShout.AllForward }), "in the order they were added");

            PrematchPlan removed = edited.WithoutRule(0);
            Assert.That(removed.Rules.Single().Shout, Is.EqualTo(TouchlineShout.AllForward));
            Assert.That(edited.Rules, Has.Count.EqualTo(2), "removing is a new plan too");
            Assert.That(removed.Rules[0], Is.Not.SameAs(edited.Rules[1]), "rules are copied, not shared");
            Assert.That(edited.WithoutRule(5).Rules, Has.Count.EqualTo(2), "an index out of range removes nothing");
        }

        [Test]
        public void CanAdd_RefusesAnInvalidRule_AndAFullPlan()
        {
            PrematchPlan plan = new PrematchPlan();
            for (int i = 0; i < PrematchPlan.MaxRules; i++)
            {
                PrematchRule next = PrematchRule.ForShout(10 + i, ScoreSituation.Always, TouchlineShout.Concentrate);
                Assert.That(plan.CanAdd(next), Is.True, $"rule {i + 1}");
                plan = plan.WithRule(next);
            }

            PrematchRule oneMore = PrematchRule.ForShout(89, ScoreSituation.Always, TouchlineShout.KeepBall);
            Assert.That(plan.CanAdd(oneMore), Is.False, "the plan is full");
            Assert.Throws<ArgumentException>(() => plan.WithRule(oneMore));

            var empty = new PrematchPlan();
            var nothing = new PrematchRule { FromMinute = 60 };
            Assert.That(empty.CanAdd(nothing), Is.False);
            Assert.That(empty.CanAdd(null), Is.False);
            Assert.Throws<ArgumentException>(() => empty.WithRule(nothing));
        }

        [Test]
        public void AnEditedShoutRule_SurvivesTheSave_AndResolvesToAShoutAction()
        {
            PrematchPlan edited = new PrematchPlan()
                .WithRule(PrematchRule.ForShout(65, ScoreSituation.Winning, TouchlineShout.KeepBall));

            PrematchPlan loaded = JsonSerializer.Deserialize<PrematchPlan>(JsonSerializer.Serialize(edited))!;
            List<MatchRule> rules = loaded.Resolve(_club, null, FamMax);

            MatchRule rule = rules.Single();
            Assert.Multiple(() =>
            {
                Assert.That(rule.FromMinute, Is.EqualTo(65));
                Assert.That(rule.When, Is.EqualTo(ScoreSituation.Winning));
                Assert.That(rule.Action.Shout, Is.EqualTo(TouchlineShout.KeepBall));
                Assert.That(rule.Action.ChangesInput, Is.False);
            });
        }
    }
}
