using System;
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
    /// The shared clock of an online live match (spec watchable-match-engine R16, issue #45): every
    /// client maps the shared kickoff instant plus the director timeline to the moment on screen, so
    /// two viewers who opened the match at different times show the same minute.
    /// </summary>
    [TestFixture]
    public class LiveBroadcastClockTests
    {
        private const int Fpm = 120;
        private const double RealFramesPerSecond = Fpm / 60.0;

        private static readonly DateTime Kickoff = new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc);

        /// <summary>1x [0,20) = 10 s, cut [20,140), 2x [140,180) = 10 s, 1x [180,200) = 9.5 s to frame 199.</summary>
        private static BroadcastTimeline HandBuilt() => new BroadcastTimeline(Fpm, 200, 100,
            new[]
            {
                new BroadcastSegment(0, 20, PlaybackRate.RealTime),
                new BroadcastSegment(20, 140, PlaybackRate.Cut),
                new BroadcastSegment(140, 180, PlaybackRate.Double),
                new BroadcastSegment(180, 200, PlaybackRate.RealTime)
            },
            Array.Empty<CutSummary>());

        private static MatchReport? _match;

        private static MatchReport Match => _match ??= Simulate();

        private static MatchReport Simulate()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260925));
            Lineup home = LineupSelector.BestEleven(league.Clubs[2]);
            Lineup away = LineupSelector.BestEleven(league.Clubs[3]);
            return new MatchEngine(new BalanceConfig()).Simulate(
                new MatchPlan(new MatchInput(home, away)), new Pcg32(4501));
        }

        private static DateTime At(double secondsAfterKickoff) => Kickoff.AddTicks((long)(secondsAfterKickoff * TimeSpan.TicksPerSecond));

        /// <summary>
        /// One viewer: its own director timeline built from its own copy of the report, opened at
        /// <paramref name="openedAt"/> and then read at every pump of its own frame rate.
        /// </summary>
        private sealed class SimulatedClient
        {
            private readonly LiveBroadcastClock _clock;
            private readonly double _pumpSeconds;
            private double _now;

            public SimulatedClient(MatchReport report, double openedAt, double pumpSeconds)
            {
                _clock = new LiveBroadcastClock(new BroadcastDirector().Build(report), Kickoff);
                _pumpSeconds = pumpSeconds;
                _now = openedAt;
                _clock.PositionAt(At(_now));
            }

            public double RealFramesPerSecond => _clock.Timeline.FramesPerMinute / 60.0;

            /// <summary>Pumps up to <paramref name="instant"/> and returns (minute, position) there.</summary>
            public (int Minute, double Position) PumpTo(double instant)
            {
                while (_now + _pumpSeconds < instant)
                {
                    _now += _pumpSeconds;
                    _clock.PositionAt(At(_now));
                }

                _now = instant;
                double position = _clock.PositionAt(At(_now));
                return (_clock.MinuteAt(At(_now)), position);
            }
        }

        [Test]
        public void TwoClientsOpenedAtDifferentTimes_ShowTheSameMinuteWithinOneSecond()
        {
            MatchReport report = Match;
            BroadcastTimeline timeline = new BroadcastDirector().Build(report);
            Assert.That(timeline.FrameCount, Is.GreaterThan(0), "the report must carry a stream");
            double totalSeconds = timeline.TotalPlaybackMilliseconds / 1000.0;

            // A opens right after kickoff at 60 fps; B arrives 2'17" later on a 30 fps device.
            var early = new SimulatedClient(report, openedAt: 1.0, pumpSeconds: 1.0 / 60.0);
            var late = new SimulatedClient(report, openedAt: 137.3, pumpSeconds: 1.0 / 30.0);

            int checkedInstants = 0;
            int minutesSeen = 0;
            int lastMinute = -1;
            for (double t = 140.0; t < totalSeconds + 10.0; t += 1.37)
            {
                (int Minute, double Position) a = early.PumpTo(t);
                (int Minute, double Position) b = late.PumpTo(t);

                Assert.That(b.Minute, Is.EqualTo(a.Minute), $"minute differs at {t:F2} s after kickoff");
                double apartSeconds = Math.Abs(a.Position - b.Position) / early.RealFramesPerSecond;
                Assert.That(apartSeconds, Is.LessThanOrEqualTo(1.0), $"views {apartSeconds:F3} s apart at {t:F2} s");

                if (a.Minute != lastMinute) minutesSeen++;
                lastMinute = a.Minute;
                checkedInstants++;
            }

            Assert.That(checkedInstants, Is.GreaterThan(300), "the comparison must span the match");
            Assert.That(minutesSeen, Is.GreaterThan(15), "the shown minute must move along the match");
            Assert.That(lastMinute, Is.EqualTo(BroadcastPlayback.FullTimeMinute), "both reach full time");
        }

        [Test]
        public void ShownMoment_FollowsTheDirectorTimeline_NotAFlatPace()
        {
            var clock = new LiveBroadcastClock(HandBuilt(), Kickoff);

            Assert.Multiple(() =>
            {
                Assert.That(clock.PositionAt(At(5.0)), Is.EqualTo(5.0 * RealFramesPerSecond).Within(1e-6), "1x");
                // 10 s of 1x reach the cut, which costs no wall time: the next frame shown is 140.
                Assert.That(clock.PositionAt(At(10.0)), Is.EqualTo(140.0).Within(1e-6), "cut jumped");
                Assert.That(clock.PositionAt(At(15.0)), Is.EqualTo(160.0).Within(1e-6), "2x");
                Assert.That(clock.MinuteAt(At(15.0)), Is.EqualTo(1));
                Assert.That(clock.FinishedAt(At(29.4)), Is.False, "29.5 s of playback to full time");
                Assert.That(clock.FinishedAt(At(29.6)), Is.True);
                Assert.That(clock.MinuteAt(At(29.6)), Is.EqualTo(BroadcastPlayback.FullTimeMinute));
            });
        }

        [Test]
        public void BeforeKickoff_ShowsTheStart()
        {
            var clock = new LiveBroadcastClock(HandBuilt(), Kickoff);

            Assert.That(clock.PositionAt(At(-30.0)), Is.EqualTo(0.0));
            Assert.That(clock.MinuteAt(At(-30.0)), Is.EqualTo(0));
        }

        [Test]
        public void ReadingAnEarlierInstant_GivesThatInstantsMoment()
        {
            // A device clock that steps back must not leave the view stuck at the later moment.
            var clock = new LiveBroadcastClock(HandBuilt(), Kickoff);
            clock.PositionAt(At(20.0));

            Assert.That(clock.PositionAt(At(4.0)), Is.EqualTo(4.0 * RealFramesPerSecond).Within(1e-6));
        }

        [Test]
        public void SecondsToReach_IsTheWallTimeAfterKickoffAtWhichThatMinuteIsShown()
        {
            // The dev fast-forward moves the kickoff back by exactly this much, so it must land on the minute.
            var clock = new LiveBroadcastClock(HandBuilt(), Kickoff);

            Assert.Multiple(() =>
            {
                Assert.That(clock.SecondsToReach(0), Is.EqualTo(0.0));
                // Frame 120 sits inside the cut, which is reached after the 10 s of 1x and jumped for free.
                Assert.That(clock.SecondsToReach(1), Is.EqualTo(10.0).Within(2e-3));
                Assert.That(clock.SecondsToReach(90), Is.EqualTo(29.5).Within(2e-3), "full time: the last frame");
                Assert.That(clock.MinuteAt(At(clock.SecondsToReach(1))), Is.EqualTo(1));
                Assert.That(clock.MinuteAt(At(clock.SecondsToReach(1) - 0.5)), Is.EqualTo(0));
                Assert.That(clock.MinuteAt(At(clock.SecondsToReach(90))), Is.EqualTo(BroadcastPlayback.FullTimeMinute));
            });
        }

        [Test]
        public void SecondsToReach_OnARealMatch_LandsOnEveryMinute()
        {
            BroadcastTimeline timeline = new BroadcastDirector().Build(Match);
            var clock = new LiveBroadcastClock(timeline, Kickoff);

            double previous = 0.0;
            for (int minute = 1; minute <= BroadcastPlayback.FullTimeMinute; minute++)
            {
                double seconds = clock.SecondsToReach(minute);
                Assert.That(seconds, Is.GreaterThanOrEqualTo(previous), $"minute {minute} is not reached before {minute - 1}");
                Assert.That(clock.MinuteAt(At(seconds)), Is.GreaterThanOrEqualTo(minute), $"minute {minute} shown at its instant");
                previous = seconds;
            }

            Assert.That(previous, Is.EqualTo(timeline.PlaybackMilliseconds(0, timeline.FrameCount - 1) / 1000.0).Within(0.01),
                "full time is reached when playback arrives at the last frame");
        }

        [Test]
        public void EmptyTimeline_StaysAtTheStart()
        {
            var clock = new LiveBroadcastClock(BroadcastTimeline.Empty, Kickoff);

            Assert.That(clock.PositionAt(At(60.0)), Is.EqualTo(0.0));
            Assert.That(clock.MinuteAt(At(60.0)), Is.EqualTo(0));
            Assert.That(clock.SecondsToReach(45), Is.EqualTo(0.0));
        }
    }
}
