using NUnit.Framework;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Tests.Condition
{
    /// <summary>
    /// Task 4.5 acceptance: the lightweight support actions (praise / encourage /
    /// motivate / criticize / rest) move morale (and where relevant form/fitness) "as
    /// configured", and spamming is ineffective — both softly (context sensitivity
    /// saturates the effect) and hard (a per-player cooldown blocks repeats).
    ///
    /// All effects are pure integer functions of the current condition and config (no
    /// RNG), and nothing here is called by the engine, so the existing golden masters
    /// and 130 tests are unaffected.
    /// </summary>
    [TestFixture]
    public class SupportActionTests
    {
        private static readonly BalanceConfig _cfg = new BalanceConfig();
        private static SupportBalance S => _cfg.Support;

        private static PlayerCondition Cond(int form, int morale, int fitness)
            => new PlayerCondition { Form = form, Morale = morale, Fitness = fitness };

        // ---------------------------------------------------------- "as configured"

        [Test]
        public void EachAction_MovesConditionInTheConfiguredDirection_AtAFavourableContext()
        {
            // Praise a neutral-morale player who has played well (form 60) → morale up.
            Assert.That(SupportActionModel.Resolve(SupportAction.Praise, Cond(60, 50, 100), S).MoraleDelta,
                Is.GreaterThan(0), "Praise should lift morale");

            // Encourage a player who is down → morale up.
            Assert.That(SupportActionModel.Resolve(SupportAction.Encourage, Cond(40, 25, 100), S).MoraleDelta,
                Is.GreaterThan(0), "Encourage should lift morale");

            // Motivate a mid-morale player → morale up and a form nudge.
            var motivate = SupportActionModel.Resolve(SupportAction.Motivate, Cond(50, 50, 100), S);
            Assert.That(motivate.MoraleDelta, Is.GreaterThan(0), "Motivate should lift morale");
            Assert.That(motivate.FormDelta, Is.GreaterThan(0), "Motivate should nudge form");

            // Criticize → morale down (and a form spark for a confident player).
            var criticize = SupportActionModel.Resolve(SupportAction.Criticize, Cond(50, 70, 100), S);
            Assert.That(criticize.MoraleDelta, Is.LessThan(0), "Criticize should lower morale");
            Assert.That(criticize.FormDelta, Is.GreaterThan(0), "Criticize should spark form for a confident player");

            // Rest a tired player → fitness up, small morale up, and it costs training.
            var rest = SupportActionModel.Resolve(SupportAction.Rest, Cond(50, 50, 60), S);
            Assert.That(rest.FitnessDelta, Is.GreaterThan(0), "Rest should recover fitness");
            Assert.That(rest.MoraleDelta, Is.GreaterThan(0), "Rest should give a small morale lift");
            Assert.That(rest.SkipsTraining, Is.True, "Rest costs the week's training");
        }

        [Test]
        public void Magnitudes_MatchTheConfigAtFullContext()
        {
            // Encourage at rock-bottom morale = full context → exactly the configured boost.
            Assert.That(SupportActionModel.Resolve(SupportAction.Encourage, Cond(50, 0, 100), S).MoraleDelta,
                Is.EqualTo(S.EncourageMoraleBoost));

            // Praise at neutral morale + neutral form = full context → exactly the configured boost.
            Assert.That(SupportActionModel.Resolve(SupportAction.Praise, Cond(50, 50, 100), S).MoraleDelta,
                Is.EqualTo(S.PraiseMoraleBoost));

            // Motivate at neutral morale = peak context → exactly the configured boost + form nudge.
            var motivate = SupportActionModel.Resolve(SupportAction.Motivate, Cond(50, 50, 100), S);
            Assert.That(motivate.MoraleDelta, Is.EqualTo(S.MotivateMoraleBoost));
            Assert.That(motivate.FormDelta, Is.EqualTo(S.MotivateFormNudge));

            // Rest at zero fitness = full context → exactly the configured recovery.
            Assert.That(SupportActionModel.Resolve(SupportAction.Rest, Cond(50, 50, 0), S).FitnessDelta,
                Is.EqualTo(S.RestFitnessRecovery));
        }

        // ---------------------------------------------------------- context sensitivity (soft anti-spam)

        [Test]
        public void Praise_SaturatesToNothingNearTheMoraleCeiling()
        {
            int atNeutral = SupportActionModel.Resolve(SupportAction.Praise, Cond(50, 50, 100), S).MoraleDelta;
            int atCeiling = SupportActionModel.Resolve(SupportAction.Praise, Cond(50, 100, 100), S).MoraleDelta;

            Assert.That(atNeutral, Is.GreaterThan(0));
            Assert.That(atCeiling, Is.EqualTo(0), "Praising a maxed-out player does nothing — repeated praise is wasted");
        }

        [Test]
        public void Praise_OfAnOutOfFormPlayer_RingsHollow()
        {
            int goodForm = SupportActionModel.Resolve(SupportAction.Praise, Cond(80, 50, 100), S).MoraleDelta;
            int poorForm = SupportActionModel.Resolve(SupportAction.Praise, Cond(10, 50, 100), S).MoraleDelta;
            Assert.That(poorForm, Is.LessThan(goodForm), "Praise should mean less after a poor run");
        }

        [Test]
        public void Encourage_IsStrongerTheLowerTheMorale()
        {
            int down = SupportActionModel.Resolve(SupportAction.Encourage, Cond(50, 20, 100), S).MoraleDelta;
            int happy = SupportActionModel.Resolve(SupportAction.Encourage, Cond(50, 70, 100), S).MoraleDelta;
            Assert.That(down, Is.GreaterThan(happy), "Encouragement helps a player who is down more than a happy one");
        }

        [Test]
        public void Criticize_HurtsTheFragileMore_AndSparksTheConfident()
        {
            var fragile = SupportActionModel.Resolve(SupportAction.Criticize, Cond(50, 20, 100), S);
            var confident = SupportActionModel.Resolve(SupportAction.Criticize, Cond(50, 90, 100), S);

            // The morale hit lands harder on the already-fragile player (more negative).
            Assert.That(fragile.MoraleDelta, Is.LessThan(confident.MoraleDelta),
                "Criticism stings a low-morale player more — a telegraphed, capped mistake");

            // The form wake-up is converted by the confident player, barely by the fragile one.
            Assert.That(confident.FormDelta, Is.GreaterThan(fragile.FormDelta),
                "A confident player converts criticism into a form spark");
        }

        [Test]
        public void Rest_RecoversLessWhenAlreadyFresh()
        {
            int tired = SupportActionModel.Resolve(SupportAction.Rest, Cond(50, 50, 40), S).FitnessDelta;
            int fresh = SupportActionModel.Resolve(SupportAction.Rest, Cond(50, 50, 100), S).FitnessDelta;
            Assert.That(tired, Is.GreaterThan(0));
            Assert.That(fresh, Is.EqualTo(0), "Resting an already-fresh player recovers nothing");
        }

        // ---------------------------------------------------------- cooldown (hard anti-spam) — THE ✅

        [Test]
        public void Cooldown_BlocksAReUse_ThenAllowsItAfterExpiry()
        {
            var log = new SupportActionLog();
            var condition = Cond(60, 40, 100); // praise has real effect here
            const int playerId = 7;

            // Day 0: first praise lands.
            var first = SupportActionModel.TryApply(condition, SupportAction.Praise, log, playerId, 0, S);
            Assert.That(first.Applied, Is.True);
            Assert.That(first.MoraleDelta, Is.GreaterThan(0));
            int moraleAfterFirst = condition.Morale;

            // Mid-cooldown: a second praise is refused and changes nothing (spam is ineffective).
            int midDay = S.PraiseCooldownDays - 1;
            var spam = SupportActionModel.TryApply(condition, SupportAction.Praise, log, playerId, midDay, S);
            Assert.That(spam.Applied, Is.False, "Re-using praise inside its cooldown must be blocked");
            Assert.That(spam.MoraleDelta, Is.EqualTo(0));
            Assert.That(condition.Morale, Is.EqualTo(moraleAfterFirst), "Blocked action must not move condition");

            // After the cooldown elapses: praise is available again.
            var later = SupportActionModel.TryApply(condition, SupportAction.Praise, log, playerId, S.PraiseCooldownDays, S);
            Assert.That(later.Applied, Is.True, "Praise becomes available once the cooldown has passed");
        }

        [Test]
        public void Cooldowns_AreIndependentPerActionAndPerPlayer()
        {
            var log = new SupportActionLog();
            var p7 = Cond(50, 40, 100);
            var p9 = Cond(50, 40, 100);

            // Praise player 7 on day 0.
            Assert.That(SupportActionModel.TryApply(p7, SupportAction.Praise, log, 7, 0, S).Applied, Is.True);

            // Same day: a *different* action on the same player is allowed.
            Assert.That(SupportActionModel.TryApply(p7, SupportAction.Encourage, log, 7, 0, S).Applied, Is.True);
            // ...and the same action on a *different* player is allowed.
            Assert.That(SupportActionModel.TryApply(p9, SupportAction.Praise, log, 9, 0, S).Applied, Is.True);
            // ...but praising player 7 again is still blocked.
            Assert.That(SupportActionModel.TryApply(p7, SupportAction.Praise, log, 7, 0, S).Applied, Is.False);
        }

        // ---------------------------------------------------------- determinism

        [Test]
        public void Resolve_IsDeterministic_NoRandomness()
        {
            var a = SupportActionModel.Resolve(SupportAction.Criticize, Cond(63, 41, 88), S);
            var b = SupportActionModel.Resolve(SupportAction.Criticize, Cond(63, 41, 88), S);
            Assert.That(a.MoraleDelta, Is.EqualTo(b.MoraleDelta));
            Assert.That(a.FormDelta, Is.EqualTo(b.FormDelta));
            Assert.That(a.FitnessDelta, Is.EqualTo(b.FitnessDelta));
        }
    }
}
