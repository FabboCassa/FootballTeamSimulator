using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Touchline shouts (watchable-match spec R11, issue #36). A shout is a match input injected
    /// at a minute, like a tactic change: it lasts a while, the coach's voice then needs a rest,
    /// and "Encourage" wears thin when it is repeated. Its magnitudes are config, and it reaches
    /// the pitch only through the movement tactics — so a match with no shout is untouched.
    /// </summary>
    [TestFixture]
    public class TouchlineShoutTests
    {
        private static readonly int FamMax = new BalanceConfig().Tactics.FamiliarityMax;

        private static Club _home = null!;
        private static Club _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            _home = league.Clubs[9];
            _away = league.Clubs[10];
        }

        private static MatchInput Kickoff() =>
            new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away));

        // ------------------------------------------------------------------ duration and cooldown

        [Test]
        public void AShout_LastsTenMinutes_ThenExpires()
        {
            ShoutBalance cfg = new BalanceConfig().Match.Shouts;
            Assert.That(cfg.DurationMinutes, Is.EqualTo(10));
            Assert.That(cfg.CooldownMinutes, Is.EqualTo(15));

            var shouts = new TouchlineShouts(cfg.DurationMinutes, cfg.CooldownMinutes);
            Assert.That(shouts.Call(true, TouchlineShout.PressHigh, 30), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(shouts.Active(true, 30), Is.EqualTo(TouchlineShout.PressHigh));
                Assert.That(shouts.Active(true, 39), Is.EqualTo(TouchlineShout.PressHigh));
                Assert.That(shouts.Active(true, 40), Is.EqualTo(TouchlineShout.None), "ten minutes, then it is gone");
                Assert.That(shouts.Active(false, 30), Is.EqualTo(TouchlineShout.None), "the other bench said nothing");
            });
        }

        [Test]
        public void AShout_DuringTheCooldown_IsRefused()
        {
            var shouts = new TouchlineShouts(10, 15);

            Assert.That(shouts.Call(true, TouchlineShout.KeepBall, 20), Is.True);
            Assert.That(shouts.Call(true, TouchlineShout.AllForward, 34), Is.False, "fourteen minutes is inside the cooldown");
            Assert.That(shouts.Active(true, 34), Is.EqualTo(TouchlineShout.None), "the refused shout is not heard");
            Assert.That(shouts.Call(false, TouchlineShout.PressHigh, 21), Is.True, "each bench has its own voice");
            Assert.That(shouts.Call(true, TouchlineShout.AllForward, 35), Is.True, "fifteen minutes later it is heard");
            Assert.That(shouts.Active(true, 35), Is.EqualTo(TouchlineShout.AllForward));
        }

        [Test]
        public void NoShout_IsNotACall_AndStartsNoCooldown()
        {
            var shouts = new TouchlineShouts(10, 15);

            Assert.That(shouts.Call(true, TouchlineShout.None, 10), Is.False);
            Assert.That(shouts.Call(true, (TouchlineShout)42, 10), Is.False);
            Assert.That(shouts.Call(true, TouchlineShout.Concentrate, 10), Is.True);
        }

        // ------------------------------------------------------------------ magnitudes

        [Test]
        public void Encourage_WeakensWhenRepeated()
        {
            MatchBalance cfg = new BalanceConfig().Match;
            int first = ShoutEffect.Of(TouchlineShout.Encourage, 0, cfg).PressureFeltPercent;
            int second = ShoutEffect.Of(TouchlineShout.Encourage, 1, cfg).PressureFeltPercent;
            int third = ShoutEffect.Of(TouchlineShout.Encourage, 2, cfg).PressureFeltPercent;

            Assert.That(first, Is.LessThan(100), "the first encouragement takes pressure off");
            Assert.That(second, Is.GreaterThan(first).And.LessThan(100), "the second still helps, less");
            Assert.That(third, Is.GreaterThan(second), "and the third less again");

            var shouts = new TouchlineShouts(10, 15);
            shouts.Call(true, TouchlineShout.Encourage, 10);
            Assert.That(shouts.Repeats(true), Is.EqualTo(0));
            shouts.Call(true, TouchlineShout.PressHigh, 25);
            shouts.Call(true, TouchlineShout.Encourage, 40);
            Assert.That(shouts.Repeats(true), Is.EqualTo(1), "one encouragement heard before this one");
            Assert.That(shouts.Repeats(false), Is.EqualTo(0));
        }

        [Test]
        public void NoShout_IsTheIdentity()
        {
            ShoutEffect none = ShoutEffect.Of(TouchlineShout.None, 0, new BalanceConfig().Match);
            ShoutEffect identity = ShoutEffect.None;

            foreach (ShoutEffect e in new[] { none, identity })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(e.LinePushDm, Is.EqualTo(0));
                    Assert.That(e.FrontLineGapPercent, Is.EqualTo(100));
                    Assert.That(e.Supporters, Is.EqualTo(0));
                    Assert.That(e.PressReachPercent, Is.EqualTo(100));
                    Assert.That(e.PressTriggerDepthDm, Is.EqualTo(0));
                    Assert.That(e.SecondPressPercent, Is.EqualTo(100));
                    Assert.That(e.PressStandOffPercent, Is.EqualTo(100));
                    Assert.That(e.HoldPercent, Is.EqualTo(100));
                    Assert.That(e.ForwardBias, Is.EqualTo(0));
                    Assert.That(e.ShotAppetitePercent, Is.EqualTo(100));
                    Assert.That(e.FatiguePercent, Is.EqualTo(100));
                    Assert.That(e.PressureFeltPercent, Is.EqualTo(100));
                    Assert.That(e.OwnHalfPressureFeltPercent, Is.EqualTo(100));
                });
            }
        }

        [Test]
        public void EachShout_PullsItsOwnLever_FromConfig()
        {
            var cfg = new BalanceConfig().Match;
            ShoutEffect press = ShoutEffect.Of(TouchlineShout.PressHigh, 0, cfg);
            ShoutEffect keep = ShoutEffect.Of(TouchlineShout.KeepBall, 0, cfg);
            ShoutEffect forward = ShoutEffect.Of(TouchlineShout.AllForward, 0, cfg);
            ShoutEffect focus = ShoutEffect.Of(TouchlineShout.Concentrate, 0, cfg);

            Assert.Multiple(() =>
            {
                Assert.That(press.PressReachPercent, Is.GreaterThan(100), "press high: pressing up");
                Assert.That(press.PressTriggerDepthDm, Is.GreaterThan(0));
                Assert.That(press.FatiguePercent, Is.GreaterThan(100), "press high: fatigue up");
                Assert.That(keep.HoldPercent, Is.GreaterThan(100), "keep the ball: tempo down");
                Assert.That(keep.ShotAppetitePercent, Is.LessThan(100), "keep the ball: risk down");
                Assert.That(forward.LinePushDm, Is.GreaterThan(0), "all forward: the block goes up");
                Assert.That(forward.Supporters, Is.GreaterThan(0));
                Assert.That(focus.OwnHalfPressureFeltPercent, Is.LessThan(100), "concentrate: fewer errors at the back");
                Assert.That(focus.ShotAppetitePercent, Is.LessThan(100), "concentrate: slightly more cautious");
            });

            cfg.Shouts.AllForwardLinePushDm = 37;
            Assert.That(ShoutEffect.Of(TouchlineShout.AllForward, 0, cfg).LinePushDm, Is.EqualTo(37),
                "the magnitude is the config's");
        }

        // ------------------------------------------------------------------ the feed

        [Test]
        public void Feed_HearsAShoutFromItsMinute_RefusesOneInTheCooldown_AndLetsItExpire()
        {
            MatchPlan plan = new MatchPlan(Kickoff())
                .WithChange(20, Kickoff().WithShout(true, TouchlineShout.PressHigh))
                .WithChange(25, Kickoff().WithShout(true, TouchlineShout.AllForward));
            var feed = new MatchInputFeed(plan, null, null, FamMax, new BalanceConfig().Match);

            for (int minute = 1; minute < 20; minute++)
            {
                feed.Advance(minute, 0, 0);
                Assert.That(feed.ShoutsChanged, Is.False, $"minute {minute}");
            }

            feed.Advance(20, 0, 0);
            Assert.That(feed.ShoutsChanged, Is.True);
            Assert.That(feed.ActiveShout(true), Is.EqualTo(TouchlineShout.PressHigh));
            Assert.That(feed.Effect(true).PressReachPercent, Is.GreaterThan(100));
            Assert.That(feed.Effect(false).PressReachPercent, Is.EqualTo(100));

            for (int minute = 21; minute < 30; minute++) feed.Advance(minute, 0, 0);
            Assert.That(feed.ActiveShout(true), Is.EqualTo(TouchlineShout.PressHigh), "the minute-25 shout was refused");
            Assert.That(feed.Shouts, Has.Count.EqualTo(1));
            Assert.That(feed.Shouts[0].Minute, Is.EqualTo(20));

            feed.Advance(30, 0, 0);
            Assert.That(feed.ShoutsChanged, Is.True, "expiry is a change too");
            Assert.That(feed.ActiveShout(true), Is.EqualTo(TouchlineShout.None));
        }

        [Test]
        public void PrematchRule_WithAShout_ResolvesToAShoutAction_AndFiresWithoutChangingTheInput()
        {
            var prematch = new PrematchPlan
            {
                Rules = { new PrematchRule { FromMinute = 70, When = ScoreSituation.Losing, Shout = TouchlineShout.AllForward } }
            };
            List<MatchRule> rules = prematch.Resolve(_home, null, FamMax);

            Assert.That(rules, Has.Count.EqualTo(1), "a shout alone is an action");
            Assert.That(rules[0].Action.Shout, Is.EqualTo(TouchlineShout.AllForward));
            Assert.That(rules[0].Action.IsEmpty, Is.False);

            var feed = new MatchInputFeed(new MatchPlan(Kickoff()), rules, null, FamMax, new BalanceConfig().Match);
            MatchInput before = feed.Current;
            for (int minute = 1; minute < 70; minute++) feed.Advance(minute, 0, 1);
            Assert.That(feed.ActiveShout(true), Is.EqualTo(TouchlineShout.None));

            bool changed = feed.Advance(70, 0, 1);
            Assert.That(changed, Is.False, "a shout is not a new lineup or tactic");
            Assert.That(feed.Current, Is.SameAs(before));
            Assert.That(feed.ShoutsChanged, Is.True);
            Assert.That(feed.ActiveShout(true), Is.EqualTo(TouchlineShout.AllForward));
        }

        [Test]
        public void PrematchPlan_WithAShout_RoundTripsThroughJson_AndAnOlderPlanHasNone()
        {
            var plan = new PrematchPlan
            {
                Rules = { new PrematchRule { FromMinute = 60, When = ScoreSituation.Drawing, Shout = TouchlineShout.Encourage } }
            };

            PrematchPlan back = JsonSerializer.Deserialize<PrematchPlan>(JsonSerializer.Serialize(plan))!;
            Assert.That(back.Rules.Single().Shout, Is.EqualTo(TouchlineShout.Encourage));
            Assert.That(back.Rules.Single().FromMinute, Is.EqualTo(60));

            PrematchPlan older = JsonSerializer.Deserialize<PrematchPlan>(
                "{\"Rules\":[{\"FromMinute\":60,\"When\":2,\"ChangeInstructions\":true}]}")!;
            Assert.That(older.Rules.Single().Shout, Is.EqualTo(TouchlineShout.None));
        }

        // ------------------------------------------------------------------ on the pitch

        [TestCase(MatchBrainVersion.V10)]
        [TestCase(MatchBrainVersion.V11)]
        public void AShout_IsAnEvent_AndMovesThePitchOnlyFromItsMinute(MatchBrainVersion brain)
        {
            var cfg = new BalanceConfig();
            cfg.Match.Brain = brain;
            const int minute = 40;

            MatchReport baseline = new MatchEngine(cfg).Simulate(new MatchPlan(Kickoff()), new Pcg32(4242));
            MatchReport shouted = new MatchEngine(cfg).Simulate(
                new MatchPlan(Kickoff()).WithChange(minute, Kickoff().WithShout(true, TouchlineShout.AllForward)),
                new Pcg32(4242));

            MatchEvent shout = shouted.Events.Single(e => e.Type == MatchEventType.Shout);
            Assert.Multiple(() =>
            {
                Assert.That(shout.Minute, Is.EqualTo(minute));
                Assert.That(shout.ClubId, Is.EqualTo(_home.Id));
                Assert.That(shout.Shout, Is.EqualTo(TouchlineShout.AllForward));
                Assert.That(baseline.Events.Any(e => e.Type == MatchEventType.Shout), Is.False);
            });

            int prefix = minute * shouted.Positions!.TicksPerMinute * shouted.Positions.PlayerCount * 2;
            Assert.That(shouted.Positions.HomeXY.Take(prefix), Is.EqualTo(baseline.Positions!.HomeXY.Take(prefix)),
                "everything before the shout is the same match");
            Assert.That(shouted.Positions.HomeXY, Is.Not.EqualTo(baseline.Positions.HomeXY),
                "the shout reaches the pitch through the movement tactics");
            Assert.That(Before(shouted, minute), Is.EqualTo(Before(baseline, minute)));
        }

        [Test]
        public void AShout_WithIdentityMagnitudes_LeavesThePitchAlone()
        {
            // The plumbing itself (the call, the event, the tactic refresh) consumes no randomness:
            // with its magnitudes set to the identity, a shout is only an entry on the timeline.
            var cfg = new BalanceConfig();
            ShoutBalance s = cfg.Match.Shouts;
            s.AllForwardLinePushDm = 0;
            s.AllForwardSupporters = 0;
            s.AllForwardFrontLineGapPercent = 100;
            s.AllForwardShotAppetitePercent = 100;

            MatchReport baseline = new MatchEngine(cfg).Simulate(new MatchPlan(Kickoff()), new Pcg32(99));
            MatchReport shouted = new MatchEngine(cfg).Simulate(
                new MatchPlan(Kickoff()).WithChange(30, Kickoff().WithShout(false, TouchlineShout.AllForward)),
                new Pcg32(99));

            Assert.That(shouted.Positions!.HomeXY, Is.EqualTo(baseline.Positions!.HomeXY));
            Assert.That(shouted.Positions.AwayXY, Is.EqualTo(baseline.Positions.AwayXY));
            Assert.That(shouted.Events.Where(e => e.Type != MatchEventType.Shout).Select(Key),
                Is.EqualTo(baseline.Events.Select(Key)));
            Assert.That(shouted.Events.Single(e => e.Type == MatchEventType.Shout).ClubId, Is.EqualTo(_away.Id));
        }

        [Test]
        public void TheFastPath_WritesTheShoutOnTheTimelineToo()
        {
            var engine = new MatchEngine(generatePositions: false);
            MatchReport report = engine.Simulate(
                new MatchPlan(Kickoff()).WithChange(55, Kickoff().WithShout(true, TouchlineShout.Concentrate)),
                new Pcg32(7));

            MatchEvent shout = report.Events.Single(e => e.Type == MatchEventType.Shout);
            Assert.That(shout.Minute, Is.EqualTo(55));
            Assert.That(shout.Shout, Is.EqualTo(TouchlineShout.Concentrate));
            Assert.That(report.Events.Select(e => e.Minute), Is.Ordered, "the timeline stays in minute order");
        }

        [Test]
        public void WhichShout_WasCalled_IsPartOfTheReportHash()
        {
            var a = new MatchReport { HomeClubId = 1, AwayClubId = 2 };
            var b = new MatchReport { HomeClubId = 1, AwayClubId = 2 };
            a.Events.Add(new MatchEvent { Minute = 30, Type = MatchEventType.Shout, ClubId = 1, Shout = TouchlineShout.PressHigh });
            b.Events.Add(new MatchEvent { Minute = 30, Type = MatchEventType.Shout, ClubId = 1, Shout = TouchlineShout.KeepBall });

            Assert.That(MatchReportHasher.Hash(a), Is.Not.EqualTo(MatchReportHasher.Hash(b)));
        }

        private static List<string> Before(MatchReport report, int minute) =>
            report.Events.Where(e => e.Minute < minute).Select(Key).ToList();

        private static string Key(MatchEvent e) => $"{e.Minute}:{e.Type}:{e.ClubId}:{e.PlayerId}";
    }
}
