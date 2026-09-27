using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// The live shout panel (watchable-match spec R11, issue #44): five buttons, a cooldown
    /// indicator and the remaining effect time, all read off what the engine actually heard — so
    /// the panel can never promise a shout the engine would refuse.
    /// </summary>
    [TestFixture]
    public class ShoutBoardTests
    {
        private const int Home = 11;
        private const int Away = 22;

        private static readonly ShoutBalance Cfg = new BalanceConfig().Match.Shouts;

        private static Club _home = null!;
        private static Club _away = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260926));
            _home = league.Clubs[2];
            _away = league.Clubs[5];
        }

        private static MatchReport ReportWith(params (int Minute, int Club, TouchlineShout Shout)[] shouts)
        {
            var report = new MatchReport { HomeClubId = Home, AwayClubId = Away };
            foreach ((int minute, int club, TouchlineShout shout) in shouts)
                report.Events.Add(new MatchEvent { Minute = minute, Type = MatchEventType.Shout, ClubId = club, Shout = shout });
            return report;
        }

        [Test]
        public void TheFiveShouts_AreOnThePanel_InAFixedOrder()
        {
            Assert.That(ShoutBoard.Choices, Is.EqualTo(new[]
            {
                TouchlineShout.PressHigh, TouchlineShout.KeepBall, TouchlineShout.AllForward,
                TouchlineShout.Encourage, TouchlineShout.Concentrate
            }));
        }

        [Test]
        public void ABenchThatHasNotShouted_CanShout_AndHasNothingRunning()
        {
            ShoutBoard board = ShoutBoard.Read(ReportWith(), Home, 50, Cfg);

            Assert.Multiple(() =>
            {
                Assert.That(board.Active, Is.EqualTo(TouchlineShout.None));
                Assert.That(board.EffectMinutesLeft, Is.EqualTo(0));
                Assert.That(board.CooldownMinutesLeft, Is.EqualTo(0));
                Assert.That(board.CanShout, Is.True);
            });
            Assert.That(ShoutBoard.Read(null, Home, 50, Cfg).CanShout, Is.True, "no report yet is no shout yet");
        }

        [Test]
        public void AHeardShout_CountsDownItsEffect_ThenItsCooldown()
        {
            MatchReport report = ReportWith((30, Home, TouchlineShout.PressHigh));

            ShoutBoard running = ShoutBoard.Read(report, Home, 31, Cfg);
            Assert.Multiple(() =>
            {
                Assert.That(running.Active, Is.EqualTo(TouchlineShout.PressHigh));
                Assert.That(running.EffectMinutesLeft, Is.EqualTo(9), "ten minutes from 30', one gone");
                Assert.That(running.CooldownMinutesLeft, Is.EqualTo(14), "fifteen minutes from 30', one gone");
                Assert.That(running.CanShout, Is.False);
            });

            ShoutBoard expired = ShoutBoard.Read(report, Home, 40, Cfg);
            Assert.Multiple(() =>
            {
                Assert.That(expired.Active, Is.EqualTo(TouchlineShout.None), "the effect is over at 40'");
                Assert.That(expired.EffectMinutesLeft, Is.EqualTo(0));
                Assert.That(expired.CooldownMinutesLeft, Is.EqualTo(5));
                Assert.That(expired.CanShout, Is.False, "but the voice still needs a rest");
            });

            ShoutBoard rested = ShoutBoard.Read(report, Home, 45, Cfg);
            Assert.That(rested.CanShout, Is.True, "fifteen minutes later the bench is heard again");
            Assert.That(rested.CooldownMinutesLeft, Is.EqualTo(0));
        }

        [Test]
        public void TheOtherBench_AndShoutsNotYetReached_DoNotCount()
        {
            MatchReport report = ReportWith((20, Away, TouchlineShout.AllForward), (70, Home, TouchlineShout.KeepBall));

            ShoutBoard board = ShoutBoard.Read(report, Home, 25, Cfg);
            Assert.That(board.Active, Is.EqualTo(TouchlineShout.None), "the away bench's shout is not ours");
            Assert.That(board.CanShout, Is.True, "and a plan shout at 70' has not been reached at 25'");

            Assert.That(ShoutBoard.Read(report, Away, 25, Cfg).Active, Is.EqualTo(TouchlineShout.AllForward));
            Assert.That(ShoutBoard.Read(report, Home, 70, Cfg).Active, Is.EqualTo(TouchlineShout.None),
                "a shout at the very minute being judged is still in the future of that minute");
        }

        [Test]
        public void TheTimings_AreTheConfigs()
        {
            var cfg = new ShoutBalance { DurationMinutes = 4, CooldownMinutes = 6 };
            ShoutBoard board = ShoutBoard.Read(ReportWith((10, Home, TouchlineShout.Encourage)), Home, 11, cfg);

            Assert.That(board.EffectMinutesLeft, Is.EqualTo(3));
            Assert.That(board.CooldownMinutesLeft, Is.EqualTo(5));
        }

        [Test]
        public void TouchlineShouts_ReportTheTimeLeft_AndCooldownMeetsTheRefusal()
        {
            var voice = new TouchlineShouts(10, 15);
            Assert.That(voice.MinutesLeft(true, 5), Is.EqualTo(0));
            Assert.That(voice.CooldownLeft(true, 5), Is.EqualTo(0));

            voice.Call(true, TouchlineShout.Concentrate, 20);
            Assert.Multiple(() =>
            {
                Assert.That(voice.MinutesLeft(true, 20), Is.EqualTo(10));
                Assert.That(voice.MinutesLeft(true, 29), Is.EqualTo(1));
                Assert.That(voice.MinutesLeft(true, 30), Is.EqualTo(0));
                Assert.That(voice.MinutesLeft(false, 25), Is.EqualTo(0));
                Assert.That(voice.CooldownLeft(true, 34), Is.EqualTo(1));
                Assert.That(voice.CooldownLeft(true, 35), Is.EqualTo(0));
            });

            Assert.That(voice.Call(true, TouchlineShout.PressHigh, 34), Is.False, "one minute of cooldown left: refused");
            Assert.That(voice.Call(true, TouchlineShout.PressHigh, 35), Is.True, "none left: heard");
        }

        [Test]
        public void APressedShout_IsInjectedAsAnInput_AndReachesTheCommentaryAndThePanel()
        {
            // What the watch screen does when the coach presses a button and applies it: the
            // current input, with the shout, from the minute after the pause.
            const int from = 58;
            MatchInput kickoff = new MatchInput(LineupSelector.BestEleven(_home), LineupSelector.BestEleven(_away));
            MatchPlan plan = new MatchPlan(kickoff).WithChange(from, kickoff.WithShout(true, TouchlineShout.PressHigh));

            MatchReport report = new MatchEngine().Simulate(plan, new Pcg32(4411));

            MatchEvent heard = report.Events.Single(e => e.Type == MatchEventType.Shout);
            Assert.That(heard.Minute, Is.EqualTo(from));
            Assert.That(heard.ClubId, Is.EqualTo(_home.Id));

            BroadcastTimeline timeline = new BroadcastDirector().Build(report);
            CommentaryLine line = CommentaryBuilder.Build(report, timeline).Single(l => l.Icon == CommentaryIcon.Shout);
            Assert.That(line.Minute, Is.EqualTo(from));
            Assert.That(line.Steps.Single().Key, Is.EqualTo(CommentaryKeys.ShoutPressHigh));
            Assert.That(line.Steps.Single().Home, Is.True);

            ShoutBoard board = ShoutBoard.Read(report, _home.Id, from + 1, Cfg);
            Assert.That(board.Active, Is.EqualTo(TouchlineShout.PressHigh));
            Assert.That(board.EffectMinutesLeft, Is.EqualTo(Cfg.DurationMinutes - 1));
            Assert.That(board.CanShout, Is.False);
            Assert.That(ShoutBoard.Read(report, _away.Id, from + 1, Cfg).CanShout, Is.True);
        }
    }
}
