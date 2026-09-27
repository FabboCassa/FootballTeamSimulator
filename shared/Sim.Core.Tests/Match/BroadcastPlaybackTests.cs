using System;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Playback of a director timeline (spec watchable-match-engine R12-R13, issue #41): the clock the
    /// MatchRenderer advances by. Cut segments cost no wall time, nothing plays below real time at 1x,
    /// 2x/4x scale the whole timeline, Skip lands on full time and a re-sim resumes from its tick.
    /// </summary>
    [TestFixture]
    public class BroadcastPlaybackTests
    {
        private const int Fpm = 120;                 // 2 frames per real second at real time
        private const double RealFramesPerSecond = Fpm / 60.0;
        private const double Eps = 1e-6;
        private const double Step = 1.0 / 60.0;      // one renderer pump at 60 fps

        /// <summary>
        /// 1x [0,20) = 10 s, cut [20,140), 2x [140,180) = 10 s, 1x [180,200) = 10 s.
        /// To the last frame (199) that is 10 + 10 + 9.5 = 29.5 s at 1x.
        /// </summary>
        private static BroadcastTimeline HandBuilt() => new BroadcastTimeline(Fpm, 200, 100,
            new[]
            {
                new BroadcastSegment(0, 20, PlaybackRate.RealTime),
                new BroadcastSegment(20, 140, PlaybackRate.Cut),
                new BroadcastSegment(140, 180, PlaybackRate.Double),
                new BroadcastSegment(180, 200, PlaybackRate.RealTime)
            },
            Array.Empty<CutSummary>());

        private const double HandBuiltSecondsAt1x = 29.5;

        private static MatchReport? _match;
        private static MatchReport? _resim;
        private const int ResimMinute = 60;

        private static MatchReport Match => _match ??= Simulate(new Pcg32(4101), withChange: false);
        private static MatchReport Resim => _resim ??= Simulate(new Pcg32(4101), withChange: true);

        private static MatchReport Simulate(Pcg32 rng, bool withChange)
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260924));
            Club homeClub = league.Clubs[0];
            Club awayClub = league.Clubs[1];
            Lineup home = LineupSelector.BestEleven(homeClub);
            Lineup away = LineupSelector.BestEleven(awayClub);
            var plan = new MatchPlan(new MatchInput(home, away));
            if (withChange)
                plan = plan.WithChange(ResimMinute, new MatchInput(home, away, HomeAttacking()));
            return new MatchEngine(new BalanceConfig()).Simulate(plan, rng);
        }

        private static MatchTactics HomeAttacking()
        {
            int famMax = new BalanceConfig().Tactics.FamiliarityMax;
            return new MatchTactics(
                new TacticContext(new Tactic(Formation.F433,
                    new TacticInstructions(Mentality.Attacking, Pressing.High, Tempo.Fast, Width.Normal)), famMax),
                TacticContext.Neutral(famMax));
        }

        private static void AssertFinishesAfter(BroadcastPlayback p, double seconds, string what)
        {
            p.Advance(seconds - Eps);
            Assert.That(p.Finished, Is.False, $"{what}: finished before {seconds} s");
            p.Advance(2 * Eps);
            Assert.That(p.Finished, Is.True, $"{what}: not finished after {seconds} s");
            Assert.That(p.Frame, Is.EqualTo(p.LastFrame), what);
        }

        // ------------------------------------------------------------------ R13: never below real time

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void NoPumpEverAdvancesSlowerThanRealTimeTimesSpeed_OnARealMatch(int speed)
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            Assert.That(t.FrameCount, Is.GreaterThan(0));
            Assert.That(t.Segments.All(s => s.Rate == PlaybackRate.Cut || (int)s.Rate >= (int)PlaybackRate.RealTime),
                "no segment may play below real time at 1x");

            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);
            double floor = RealFramesPerSecond * speed * Step * (1 - 1e-9);
            int pumps = 0;
            while (!p.Finished)
            {
                double before = p.Position;
                p.Advance(Step);
                pumps++;
                if (!p.Finished)
                    Assert.That(p.Position - before, Is.GreaterThanOrEqualTo(floor),
                        $"pump {pumps} at frame {before:F2} advanced slower than real time x{speed}");
            }

            Assert.That(pumps, Is.GreaterThan(1));
        }

        [Test]
        public void RealTimeSegment_AdvancesAtTheStreamRate_AndDoubleAtTwiceIt()
        {
            var p = new BroadcastPlayback(HandBuilt());

            p.Advance(5.0);
            Assert.That(p.Position, Is.EqualTo(10.0).Within(1e-9), "1x: 2 frames per real second");

            p.Seek(140);
            p.Advance(5.0);
            Assert.That(p.Position, Is.EqualTo(160.0).Within(1e-9), "2x: 4 frames per real second");
        }

        [Test]
        public void SpeedBelowOne_IsRejected()
        {
            var p = new BroadcastPlayback(HandBuilt());

            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetSpeed(0));
            Assert.That(p.Speed, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ 2x / 4x scale the timeline

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void Speed_ScalesTheTotalDurationExactly_HandBuilt(int speed)
        {
            var p = new BroadcastPlayback(HandBuilt());
            p.SetSpeed(speed);

            AssertFinishesAfter(p, HandBuiltSecondsAt1x / speed, $"x{speed}");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void Speed_ScalesTheTotalDurationExactly_RealMatch(int speed)
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            double at1x = t.PlaybackMilliseconds(0, t.FrameCount - 1) / 1000.0;
            Assert.That(at1x, Is.InRange(8 * 60.0, 12 * 60.0), "a watched match is about ten minutes at 1x");

            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);

            AssertFinishesAfter(p, at1x / speed, $"x{speed}");
        }

        [Test]
        public void ChangingSpeedMidMatch_OnlyScalesWhatIsLeft()
        {
            var p = new BroadcastPlayback(HandBuilt());
            p.Advance(5.0);                          // 10 frames in, 24.5 s of 1x left
            p.SetSpeed(4);

            AssertFinishesAfter(p, (HandBuiltSecondsAt1x - 5.0) / 4, "switched to 4x at 5 s");
        }

        // ------------------------------------------------------------------ cuts are jumped

        [Test]
        public void ACutSegment_IsJumped_AndCostsNoWallTime()
        {
            var p = new BroadcastPlayback(HandBuilt());

            p.Advance(9.75);
            Assert.That(p.Frame, Is.EqualTo(19));
            Assert.That(p.Fraction, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(p.Minute, Is.EqualTo(0));

            // 0.25 s finishes frame 19, the cut [20,140) is free, the other 0.25 s is one 2x frame.
            p.Advance(0.5);
            Assert.That(p.Position, Is.EqualTo(141.0).Within(1e-9));
            Assert.That(p.Minute, Is.EqualTo(1), "the clock advances instantly across the cut");
        }

        [Test]
        public void NoPump_EverRestsOnACutFrame_OnARealMatch()
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            Assert.That(t.Segments.Any(s => s.Rate == PlaybackRate.Cut), "a real match has cuts");

            var p = new BroadcastPlayback(t);
            while (!p.Finished)
            {
                p.Advance(Step);
                if (!p.Finished)
                    Assert.That(t.RateAt(p.Frame), Is.Not.EqualTo(PlaybackRate.Cut), $"rested on cut frame {p.Frame}");
            }
        }

        // ------------------------------------------------------------------ Skip

        [Test]
        public void Skip_LandsOnTheLastFrame_AtFullTime()
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            var p = new BroadcastPlayback(t);
            p.Advance(30.0);

            p.Skip();

            Assert.That(p.Finished, Is.True);
            Assert.That(p.Frame, Is.EqualTo(t.FrameCount - 1));
            Assert.That(p.Fraction, Is.EqualTo(0.0));
            Assert.That(p.Minute, Is.EqualTo(90));

            p.Advance(10.0);
            Assert.That(p.Frame, Is.EqualTo(t.FrameCount - 1), "nothing plays past full time");
        }

        [Test]
        public void AnEmptyTimeline_IsFinishedAtOnce()
        {
            var p = new BroadcastPlayback(BroadcastTimeline.Empty);

            Assert.That(p.Finished, Is.True);
            Assert.That(p.Frame, Is.EqualTo(0));
            p.Advance(1.0);
            Assert.That(p.Frame, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ re-sim resumes from its tick

        [Test]
        public void StartingFromATick_ContinuesFromThatTick()
        {
            var p = new BroadcastPlayback(HandBuilt(), startFrame: 150);
            Assert.That(p.Frame, Is.EqualTo(150));

            p.Advance(1.0);
            Assert.That(p.Position, Is.EqualTo(154.0).Within(1e-9), "four 2x frames on from 150");
        }

        [Test]
        public void StartingInsideACut_JumpsToTheNextPlayedFrame()
        {
            var p = new BroadcastPlayback(HandBuilt(), startFrame: 60);
            Assert.That(p.Frame, Is.EqualTo(60));

            p.Advance(0.5);
            Assert.That(p.Position, Is.EqualTo(142.0).Within(1e-9));
        }

        [Test]
        public void AResimulatedRemainder_ResumesFromTheInputTick_AndPlaysOnlyWhatIsLeft()
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Resim);
            int tick = ResimMinute * t.FramesPerMinute;

            var p = new BroadcastPlayback(t, startFrame: tick);
            Assert.That(p.Frame, Is.EqualTo(tick));
            Assert.That(p.Minute, Is.EqualTo(ResimMinute));

            p.Advance(Step);
            Assert.That(p.Position, Is.GreaterThan(tick), "the remainder plays forward from the input");

            var fresh = new BroadcastPlayback(t, startFrame: tick);
            double remaining = t.PlaybackMilliseconds(tick, t.FrameCount - 1) / 1000.0;
            AssertFinishesAfter(fresh, remaining, "resumed at minute 60");
        }

        [Test]
        public void Seek_RewindsAndReplaysFromTheGivenTick()
        {
            var p = new BroadcastPlayback(HandBuilt());
            p.Advance(20.0);

            p.Seek(10);
            Assert.That(p.Frame, Is.EqualTo(10));
            Assert.That(p.Finished, Is.False);

            p.Advance(6.0);
            Assert.That(p.Position, Is.EqualTo(144.0).Within(1e-9), "5 s of 1x to frame 20, the cut, then 1 s of 2x");
        }
    }
}
