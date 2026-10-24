using System;
using System.Collections.Generic;
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
    /// Playback of a director timeline (spec real-match-and-playing-styles R12, issues #41 and #85):
    /// the clock the MatchRenderer advances by. Cut segments cost no wall time, nothing plays below
    /// 1.3x real time at 1x, 2x/4x scale every rate, Skip lands on full time and a re-sim resumes
    /// from its tick.
    /// </summary>
    [TestFixture]
    public class BroadcastPlaybackTests
    {
        private const int Fpm = 120;                 // 2 frames per real second at real time
        private const double RealFramesPerSecond = Fpm / 60.0;
        private const double LiveFactor = 1.3;
        private const double Eps = 1e-6;
        private const double Step = 1.0 / 60.0;      // one renderer pump at 60 fps

        /// <summary>
        /// Live (2.6 frames/s) [0,26) = 10 s, cut [26,140), dead time (4 frames/s) [140,180) = 10 s,
        /// live [180,207). To the last frame (206) that is 10 + 10 + 10 = 30 s at 1x.
        /// </summary>
        private static BroadcastTimeline HandBuilt() => new BroadcastTimeline(Fpm, 207, 100,
            new[]
            {
                new BroadcastSegment(0, 26, PlaybackRate.Live),
                new BroadcastSegment(26, 140, PlaybackRate.Cut),
                new BroadcastSegment(140, 180, PlaybackRate.DeadTime),
                new BroadcastSegment(180, 207, PlaybackRate.Live)
            },
            Array.Empty<CutSummary>());

        private const double HandBuiltSecondsAt1x = 30.0;

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

        // ------------------------------------------------------------------ R12: never below 1.3x real time

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void NoPumpEverAdvancesSlowerThanLivePlayTimesSpeed_OnARealMatch(int speed)
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            Assert.That(t.FrameCount, Is.GreaterThan(0));
            Assert.That(t.Segments.All(s => s.Rate == PlaybackRate.Cut || s.Rate.Factor() >= LiveFactor),
                "no segment may play below 1.3x real time at 1x");

            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);
            double floor = RealFramesPerSecond * LiveFactor * speed * Step * (1 - 1e-9);
            int pumps = 0;
            while (!p.Finished)
            {
                double before = p.Position;
                p.Advance(Step);
                pumps++;
                if (!p.Finished)
                    Assert.That(p.Position - before, Is.GreaterThanOrEqualTo(floor),
                        $"pump {pumps} at frame {before:F2} advanced slower than 1.3x real time x{speed}");
            }

            Assert.That(pumps, Is.GreaterThan(1));
        }

        [Test]
        public void LiveSegment_AdvancesAtOnePointThreeTimesTheStreamRate_AndDeadTimeAtTwiceIt()
        {
            var p = new BroadcastPlayback(HandBuilt());

            p.Advance(5.0);
            Assert.That(p.Position, Is.EqualTo(13.0).Within(1e-9), "live: 2.6 frames per real second");

            p.Seek(140);
            p.Advance(5.0);
            Assert.That(p.Position, Is.EqualTo(160.0).Within(1e-9), "dead time: 4 frames per real second");
        }

        [Test]
        public void TwoAndFourX_ScaleEveryRate()
        {
            foreach (int speed in new[] { 2, 4 })
            {
                var p = new BroadcastPlayback(HandBuilt());
                p.SetSpeed(speed);

                p.Advance(1.0);
                Assert.That(p.Position, Is.EqualTo(2.6 * speed).Within(1e-9), $"live x{speed}");

                p.Seek(140);
                p.Advance(1.0);
                Assert.That(p.Position, Is.EqualTo(140.0 + 4.0 * speed).Within(1e-9), $"dead time x{speed}");
            }
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
            double at1x = t.PlaybackSeconds(0, t.FrameCount - 1);
            Assert.That(at1x, Is.InRange(8 * 60.0, 12 * 60.0), "a watched match is about ten minutes at 1x");

            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);

            AssertFinishesAfter(p, at1x / speed, $"x{speed}");
        }

        [Test]
        public void ChangingSpeedMidMatch_OnlyScalesWhatIsLeft()
        {
            var p = new BroadcastPlayback(HandBuilt());
            p.Advance(5.0);                          // 13 frames in, 25 s of 1x left
            p.SetSpeed(4);

            AssertFinishesAfter(p, (HandBuiltSecondsAt1x - 5.0) / 4, "switched to 4x at 5 s");
        }

        // ------------------------------------------------------------------ cuts are jumped

        [Test]
        public void ACutSegment_IsJumped_AndCostsNoWallTime()
        {
            var p = new BroadcastPlayback(HandBuilt());

            p.Advance(9.75);
            Assert.That(p.Frame, Is.EqualTo(25));
            Assert.That(p.Fraction, Is.EqualTo(0.35).Within(1e-9));
            Assert.That(p.Minute, Is.EqualTo(0));

            // 0.25 s finishes frame 25, the cut [26,140) is free, the other 0.25 s is one dead-time frame.
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
            Assert.That(p.Position, Is.EqualTo(154.0).Within(1e-9), "four dead-time frames on from 150");
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
            double remaining = t.PlaybackSeconds(tick, t.FrameCount - 1);
            AssertFinishesAfter(fresh, remaining, "resumed at minute 60");
        }

        [Test]
        public void Seek_RewindsAndReplaysFromTheGivenTick()
        {
            var p = new BroadcastPlayback(HandBuilt());
            p.Advance(20.0);

            p.Seek(13);
            Assert.That(p.Frame, Is.EqualTo(13));
            Assert.That(p.Finished, Is.False);

            p.Advance(6.0);
            Assert.That(p.Position, Is.EqualTo(144.0).Within(1e-9), "5 s of live to frame 26, the cut, then 1 s of dead time");
        }

        // ------------------------------------------------------------------ believable-match R11: a fade at every cut

        private static readonly CutSummary LongCut = new CutSummary(26, 140, PossessionSide.Home, CutZone.Middle, BallActionKind.Foul);

        /// <summary>Live [0,26), cut [26,140) with a summary, dead time [140,180), cut [180,190) without one, live [190,217).</summary>
        private static BroadcastTimeline TwoCuts() => new BroadcastTimeline(Fpm, 217, 100,
            new[]
            {
                new BroadcastSegment(0, 26, PlaybackRate.Live),
                new BroadcastSegment(26, 140, PlaybackRate.Cut),
                new BroadcastSegment(140, 180, PlaybackRate.DeadTime),
                new BroadcastSegment(180, 190, PlaybackRate.Cut),
                new BroadcastSegment(190, 217, PlaybackRate.Live)
            },
            new[] { LongCut });

        private static List<CutTransition> PlayThrough(BroadcastPlayback p, double step)
        {
            var seen = new List<CutTransition>();
            p.CutCrossed += seen.Add;
            while (!p.Finished)
                p.Advance(step);
            return seen;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void EveryCutBoundary_EmitsOneTransition_WithItsSummaryAndTheFadeAtThatSpeed(int speed)
        {
            var p = new BroadcastPlayback(TwoCuts());
            p.SetSpeed(speed);

            List<CutTransition> seen = PlayThrough(p, Step);

            Assert.That(seen.Select(x => (x.FromFrame, x.ToFrame)), Is.EqualTo(new[] { (26, 140), (180, 190) }));
            Assert.That(seen[0].Summary, Is.EqualTo(LongCut), "the cut's one-line summary is the caption");
            Assert.That(seen[1].Summary, Is.Null, "a cut the director filed no summary for fades without a caption");
            Assert.That(seen.Select(x => x.Seconds), Is.All.EqualTo(BroadcastPlayback.FadeSeconds / speed).Within(1e-12));
            Assert.That(BroadcastPlayback.FadeSeconds, Is.GreaterThanOrEqualTo(0.3));
        }

        [TestCase(1)]
        [TestCase(4)]
        public void EveryCutBoundary_EmitsATransition_OnARealMatch(int speed)
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            List<BroadcastSegment> cuts = t.Segments.Where(s => s.Rate == PlaybackRate.Cut).ToList();
            Assert.That(cuts, Is.Not.Empty);

            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);
            List<CutTransition> seen = PlayThrough(p, Step);

            Assert.That(seen.Select(x => (x.FromFrame, x.ToFrame)), Is.EqualTo(cuts.Select(c => (c.StartFrame, c.EndFrame))));
            foreach (CutTransition x in seen)
            {
                CutSummary[] filed = t.Summaries.Where(c => c.StartFrame == x.FromFrame).ToArray();
                Assert.That(x.Summary, Is.EqualTo(filed.Length == 1 ? filed[0] : (CutSummary?)null), $"cut [{x.FromFrame},{x.ToFrame})");
            }

            Assert.That(seen.Count(x => x.Summary.HasValue), Is.EqualTo(t.Summaries.Count), "every summary is captioned once");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void TheFade_LastsPointThreeSecondsOfWallAtOneX_ScaledBySpeed(int speed)
        {
            const double fine = 1.0 / 2400.0;
            var p = new BroadcastPlayback(HandBuilt());
            p.SetSpeed(speed);

            double covered = 0.0;
            while (!p.Finished)
            {
                p.Advance(fine);
                if (p.Curtain > 0.0) covered += fine;
            }

            Assert.That(covered, Is.EqualTo(BroadcastPlayback.FadeSeconds / speed).Within(2 * fine));
        }

        [Test]
        public void TheCurtain_IsShutAcrossTheJump_AndOpenAwayFromAnyCut()
        {
            var p = new BroadcastPlayback(HandBuilt());

            Assert.That(p.Curtain, Is.EqualTo(0.0), "kickoff with no cut behind it");
            p.Seek(13);
            Assert.That(p.Curtain, Is.EqualTo(0.0), "mid-stretch, seconds from any cut");
            p.Seek(60);
            Assert.That(p.Curtain, Is.EqualTo(1.0), "a cut frame is never shown");
            p.Seek(140);
            Assert.That(p.Curtain, Is.EqualTo(1.0), "the first frame after the jump is fully covered");

            p.Seek(0);
            p.Advance(10.0 - 0.075);
            Assert.That(p.Curtain, Is.EqualTo(0.5).Within(1e-6), "half-way down, 0.075 s before the cut");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void NoShownFrame_JumpsAcrossACutUncovered_OnARealMatch(int speed)
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            var p = new BroadcastPlayback(t);
            p.SetSpeed(speed);
            int crossed = 0;
            p.CutCrossed += _ => crossed++;

            // Each side of a jump is within one pump of the boundary, so at most one pump's worth off black.
            double floor = 1.0 - Step * speed / (BroadcastPlayback.FadeSeconds / 2) - 1e-9;
            double before = p.Curtain;
            int pumps = 0;
            while (!p.Finished)
            {
                int was = crossed;
                p.Advance(Step);
                pumps++;
                if (crossed == was || p.Finished) { before = p.Curtain; continue; }

                Assert.That(p.Curtain, Is.GreaterThanOrEqualTo(floor), $"pump {pumps}: frame {p.Frame} after the jump");
                if (pumps > 1)
                    Assert.That(before, Is.GreaterThanOrEqualTo(floor), $"pump {pumps}: the frame before the jump");
                before = p.Curtain;
            }
        }

        [Test]
        public void FollowingASharedPosition_CrossesTheSameCuts_AsPlayingIt()
        {
            BroadcastTimeline t = new BroadcastDirector().Build(Match);
            var played = new BroadcastPlayback(t);
            var follower = new BroadcastPlayback(t);
            var expected = new List<CutTransition>();
            var seen = new List<CutTransition>();
            played.CutCrossed += expected.Add;
            follower.CutCrossed += seen.Add;

            while (!played.Finished)
            {
                played.Advance(0.25);
                follower.Follow(played.Position);
                Assert.That(follower.Position, Is.EqualTo(played.Position));
                Assert.That(follower.Curtain, Is.EqualTo(played.Curtain));
            }

            Assert.That(seen, Is.EqualTo(expected));

            int before = seen.Count;
            follower.Follow(t.FramesPerMinute * 10);
            Assert.That(seen, Has.Count.EqualTo(before), "a clock stepping back replays no transition");
            Assert.That(follower.Position, Is.EqualTo(t.FramesPerMinute * 10));
        }
    }
}
