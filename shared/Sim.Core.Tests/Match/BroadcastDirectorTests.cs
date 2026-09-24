using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
    /// The broadcast director (spec watchable-match-engine R12-R14): a report goes in, a playback
    /// timeline of 1x / 2x / cut segments comes out. The acceptance runs over a corpus of real
    /// engine matches; a hand-built stream pins the classification and the budget arithmetic
    /// against values worked out on paper.
    /// </summary>
    [TestFixture]
    public class BroadcastDirectorTests
    {
        private const int ReportCount = 200;

        private const long MinuteMs = 60_000;
        private const long MatchBandLowMs = 9 * MinuteMs;
        private const long MatchBandHighMs = 11 * MinuteMs;
        private const long HalfBandLowMs = 270_000;
        private const long HalfBandHighMs = 330_000;

        private static List<MatchReport>? _reports;

        private static IReadOnlyList<MatchReport> Reports => _reports ??= Generate(ReportCount);

        private static readonly BallActionKind[] KeyKinds =
        {
            BallActionKind.Shot, BallActionKind.Goal, BallActionKind.YellowCard,
            BallActionKind.RedCard, BallActionKind.Penalty
        };

        // ------------------------------------------------------------------ corpus

        private static List<MatchReport> Generate(int count)
        {
            var watch = Stopwatch.StartNew();
            League league = new LeagueGenerator().Generate(new Pcg32(20260924));
            int clubs = league.Clubs.Count;

            var plans = new MatchPlan[count];
            for (int i = 0; i < count; i++)
            {
                Club homeClub = league.Clubs[i % clubs];
                Club awayClub = league.Clubs[(i * 7 + 3) % clubs];
                if (awayClub == homeClub) awayClub = league.Clubs[(i + 1) % clubs];

                Lineup home = LineupSelector.BestEleven(homeClub);
                Lineup away = LineupSelector.BestEleven(awayClub);
                plans[i] = new MatchPlan(new MatchInput(home, away));

                // Every third match changes a man, so the corpus has substitution frames to show.
                if (i % 3 == 0)
                    plans[i] = plans[i].WithChange(50 + i % 35, new MatchInput(WithOneSubstitution(homeClub, home), away));
            }

            // Each match is independent (own engine, own seed, stored by index), so running them in
            // parallel changes nothing but the wall time: 200 matches take ~155 s on one core.
            var reports = new MatchReport[count];
            Parallel.For(0, count, i =>
                reports[i] = new MatchEngine(new BalanceConfig()).Simulate(plans[i], new Pcg32((ulong)(1000 + i))));

            TestContext.Progress.WriteLine($"BroadcastDirector corpus: {count} matches in {watch.Elapsed.TotalSeconds:F1} s");
            return reports.ToList();
        }

        private static Lineup WithOneSubstitution(Club club, Lineup eleven)
        {
            var ids = new HashSet<int>(eleven.Slots.Select(s => s.Player.Id));
            Player bench = club.Squad.Players.First(p => !ids.Contains(p.Id));

            var result = new Lineup { ClubId = eleven.ClubId };
            bool swapped = false;
            foreach (LineupSlot slot in eleven.Slots)
            {
                bool swap = !swapped && slot.Role != PositionRole.Goalkeeper;
                result.Slots.Add(new LineupSlot { Role = slot.Role, Player = swap ? bench : slot.Player });
                swapped |= swap;
            }

            return result;
        }

        // ------------------------------------------------------------------ acceptance over the corpus

        [Test]
        public void EveryReport_PlaysTenMinutesAtOneX_FiveEachHalf()
        {
            var director = new BroadcastDirector();
            foreach (MatchReport report in Reports)
            {
                BroadcastTimeline t = director.Build(report);
                long total = t.TotalPlaybackMilliseconds;
                long first = t.PlaybackMilliseconds(0, t.SecondHalfStartFrame);
                long second = t.PlaybackMilliseconds(t.SecondHalfStartFrame, t.FrameCount);

                Assert.That(total, Is.InRange(MatchBandLowMs, MatchBandHighMs), $"{Describe(report)} total");
                Assert.That(first, Is.InRange(HalfBandLowMs, HalfBandHighMs), $"{Describe(report)} first half");
                Assert.That(second, Is.InRange(HalfBandLowMs, HalfBandHighMs), $"{Describe(report)} second half");
                Assert.That(first + second, Is.EqualTo(total));
            }
        }

        [Test]
        public void EveryGoalShotCardPenaltyAndSubstitution_IsShownAtRealTime()
        {
            var director = new BroadcastDirector();
            int substitutions = 0, keyActions = 0, goals = 0;
            foreach (MatchReport report in Reports)
            {
                BroadcastTimeline t = director.Build(report);
                PositionStream s = report.Positions!;

                foreach (BallAction a in s.Actions.Where(a => KeyKinds.Contains(a.Kind)))
                {
                    keyActions++;
                    if (a.Kind == BallActionKind.Goal) goals++;
                    Assert.That(t.RateAt(a.Tick), Is.EqualTo(PlaybackRate.RealTime),
                        $"{Describe(report)}: {a.Kind} at frame {a.Tick}");
                }

                foreach (SlotChange c in s.Changes)
                {
                    substitutions++;
                    Assert.That(t.RateAt(c.Frame), Is.EqualTo(PlaybackRate.RealTime),
                        $"{Describe(report)}: substitution at frame {c.Frame}");
                }
            }

            Assert.That(substitutions, Is.GreaterThan(0), "the corpus must exercise substitutions");
            Assert.That(goals, Is.GreaterThan(0), "the corpus must exercise goals");
            Assert.That(keyActions, Is.GreaterThan(goals));
        }

        [Test]
        public void Segments_TileTheMatch_AndNoneIsSlowerThanRealTime()
        {
            var director = new BroadcastDirector();
            foreach (MatchReport report in Reports)
            {
                BroadcastTimeline t = director.Build(report);
                Assert.That(t.FrameCount, Is.EqualTo(report.Positions!.TickCount));
                Assert.That(t.Segments, Is.Not.Empty);

                int expectedStart = 0;
                PlaybackRate? previous = null;
                foreach (BroadcastSegment seg in t.Segments)
                {
                    Assert.That(seg.StartFrame, Is.EqualTo(expectedStart), Describe(report));
                    Assert.That(seg.EndFrame, Is.GreaterThan(seg.StartFrame), Describe(report));
                    Assert.That(seg.Rate, Is.Not.EqualTo(previous), "adjacent segments are merged");
                    Assert.That(seg.Rate, Is.AnyOf(PlaybackRate.Cut, PlaybackRate.RealTime, PlaybackRate.Double));
                    if (seg.Rate != PlaybackRate.Cut)
                        Assert.That((int)seg.Rate, Is.GreaterThanOrEqualTo(1), "nothing plays slower than real time");

                    expectedStart = seg.EndFrame;
                    previous = seg.Rate;
                }

                Assert.That(expectedStart, Is.EqualTo(t.FrameCount));
            }
        }

        [Test]
        public void EveryCutSpanOfAMinuteOrMore_CarriesExactlyOneSummary()
        {
            var director = new BroadcastDirector();
            int longCuts = 0;
            foreach (MatchReport report in Reports)
            {
                BroadcastTimeline t = director.Build(report);
                int minute = t.FramesPerMinute;

                List<BroadcastSegment> cuts = t.Segments
                    .Where(s => s.Rate == PlaybackRate.Cut && s.EndFrame - s.StartFrame >= minute)
                    .ToList();
                longCuts += cuts.Count;

                Assert.That(t.Summaries.Count, Is.EqualTo(cuts.Count), Describe(report));
                foreach (BroadcastSegment cut in cuts)
                {
                    int matching = t.Summaries.Count(x => x.StartFrame == cut.StartFrame && x.EndFrame == cut.EndFrame);
                    Assert.That(matching, Is.EqualTo(1), $"{Describe(report)}: cut [{cut.StartFrame},{cut.EndFrame})");
                }
            }

            Assert.That(longCuts, Is.GreaterThan(0), "a ten-minute broadcast of ninety must cut whole minutes");
        }

        [Test]
        public void TheSameReport_GivesAnIdenticalTimeline()
        {
            var engine = new MatchEngine(new BalanceConfig());
            League league = new LeagueGenerator().Generate(new Pcg32(20260924));
            MatchReport Play() => engine.Simulate(
                LineupSelector.BestEleven(league.Clubs[2]),
                LineupSelector.BestEleven(league.Clubs[9]),
                new Pcg32(4040));

            MatchReport report = Play();
            BroadcastTimeline a = new BroadcastDirector().Build(report);
            BroadcastTimeline b = new BroadcastDirector().Build(report);
            BroadcastTimeline c = new BroadcastDirector().Build(Play());

            foreach (BroadcastTimeline other in new[] { b, c })
            {
                Assert.That(other.Segments, Is.EqualTo(a.Segments));
                Assert.That(other.Summaries, Is.EqualTo(a.Summaries));
                Assert.That(other.SecondHalfStartFrame, Is.EqualTo(a.SecondHalfStartFrame));
            }
        }

        // ------------------------------------------------------------------ a hand-built stream

        private const int Fpm = 120;
        private const int Frames = 1200; // ten minutes, one "half" (there is no HalfTime whistle)
        private const int ShotFrame = 1190;

        /// <summary>
        /// Home kicks off (dead 0-3), keeps the ball in its own third (X 200) until frame 1139,
        /// is in the final third (X 900) from 1140 and shoots at 1190 (X 950).
        /// </summary>
        private static MatchReport HandBuilt()
        {
            var s = new PositionStream
            {
                TicksPerMinute = Fpm,
                PlayerCount = 11,
                LastTick = Frames - 1,
                BallXY = new int[Frames * 2],
                Owner = new int[Frames]
            };

            for (int f = 0; f < Frames; f++)
            {
                int x = f < 4 ? Pitch.CenterX : f < 1140 ? 200 : f < ShotFrame ? 900 : 950;
                s.BallXY[f * 2] = x;
                s.BallXY[f * 2 + 1] = Pitch.CenterY;
                s.Owner[f] = f >= 4 && f < ShotFrame ? s.OwnerCode(true, 5) : PositionStream.NoOwner;
            }

            s.Actions.Add(new BallAction(0, BallActionKind.Kickoff, true, 9, -1));
            s.Actions.Add(new BallAction(4, BallActionKind.Pass, true, 9, 5));
            s.Actions.Add(new BallAction(ShotFrame, BallActionKind.Shot, true, 5, -1));
            return new MatchReport { Positions = s };
        }

        [Test]
        public void HandBuiltStream_GrowsTheLeadInBackFromTheShot_UntilTheBudgetIsSpent()
        {
            // Budget 30 s = 120 quarter-second units. Shot + 3 s = frames 1190-1196 at 1x (14 units);
            // the final-third run-up 1140-1189 at 1x (100); then six own-half frames at 2x (6) = 120.
            BroadcastTimeline t = new BroadcastDirector(new BroadcastSettings { TargetSecondsPerHalf = 30 })
                .Build(HandBuilt());

            Assert.That(t.Segments, Is.EqualTo(new[]
            {
                new BroadcastSegment(0, 1134, PlaybackRate.Cut),
                new BroadcastSegment(1134, 1140, PlaybackRate.Double),
                new BroadcastSegment(1140, 1197, PlaybackRate.RealTime),
                new BroadcastSegment(1197, Frames, PlaybackRate.Cut)
            }));
            Assert.That(t.TotalPlaybackMilliseconds, Is.EqualTo(30_000));

            Assert.That(t.Summaries, Is.EqualTo(new[]
            {
                new CutSummary(0, 1134, PossessionSide.Home, CutZone.Defensive, BallActionKind.Kickoff)
            }), "the three-frame cut after the shot is under a minute and carries no summary");
        }

        [Test]
        public void AKeyEvent_IsNeverCut_EvenWhenItAloneOverrunsTheBudget()
        {
            BroadcastTimeline t = new BroadcastDirector(new BroadcastSettings { TargetSecondsPerHalf = 1 })
                .Build(HandBuilt());

            Assert.That(t.Segments, Is.EqualTo(new[]
            {
                new BroadcastSegment(0, ShotFrame, PlaybackRate.Cut),
                new BroadcastSegment(ShotFrame, 1197, PlaybackRate.RealTime),
                new BroadcastSegment(1197, Frames, PlaybackRate.Cut)
            }));
        }

        [Test]
        public void AReportWithoutAStream_HasAnEmptyTimeline()
        {
            BroadcastTimeline t = new BroadcastDirector().Build(new MatchReport());

            Assert.That(t.Segments, Is.Empty);
            Assert.That(t.Summaries, Is.Empty);
            Assert.That(t.TotalPlaybackMilliseconds, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ one classification rule per stream

        // A budget no stream here can spend: every frame the rules allow is bought, so the rate
        // shows the class directly (Hot = 1x, Warm = 2x, Dead = cut).
        private static readonly BroadcastSettings Unlimited = new BroadcastSettings { TargetSecondsPerHalf = 10_000 };

        private const int Slot = 5;

        private static PositionStream EmptyStream(int frames) => new PositionStream
        {
            TicksPerMinute = Fpm,
            PlayerCount = 11,
            LastTick = frames - 1,
            BallXY = new int[frames * 2],
            Owner = new int[frames]
        };

        /// <summary>Frames [from, to): the ball at X <paramref name="x"/>, held by home, away, or nobody (null).</summary>
        private static void Hold(PositionStream s, int from, int to, int x, bool? home)
        {
            for (int f = from; f < to; f++)
            {
                s.BallXY[f * 2] = x;
                s.BallXY[f * 2 + 1] = Pitch.CenterY;
                s.Owner[f] = home.HasValue ? s.OwnerCode(home.Value, Slot) : PositionStream.NoOwner;
            }
        }

        /// <summary>Kick-off whistle at 0 (dead 0-3), first touch at 4 by the side kicking off.</summary>
        private static PositionStream KickedOff(int frames, bool home)
        {
            PositionStream s = EmptyStream(frames);
            Hold(s, 0, 4, Pitch.CenterX, null);
            s.Actions.Add(new BallAction(0, BallActionKind.Kickoff, home, 9, -1));
            s.Actions.Add(new BallAction(4, BallActionKind.Pass, home, 9, Slot));
            return s;
        }

        private static BroadcastTimeline Direct(PositionStream s, BroadcastSettings settings) =>
            new BroadcastDirector(settings).Build(new MatchReport { Positions = s });

        private static void AssertRate(BroadcastTimeline t, int from, int to, PlaybackRate rate, string what)
        {
            for (int f = from; f < to; f++)
                Assert.That(t.RateAt(f), Is.EqualTo(rate), $"{what}: frame {f}");
        }

        [Test]
        public void ACounter_WonInTheOwnHalfAndInTheFinalThirdWithinTheWindow_PlaysAtRealTime()
        {
            // Away keeps a warm spell (X 400 = 650 dm up for away). Home wins it at 100 in its own
            // half (X 300) and is in the final third at 110 (5 s, inside the 12 s window): a counter.
            // At 150 home wins it again, but only reaches the final third at 190 (20 s): build-up.
            PositionStream s = KickedOff(300, home: false);
            Hold(s, 4, 100, 400, false);
            Hold(s, 100, 110, 300, true);
            Hold(s, 110, 120, 800, true);
            Hold(s, 120, 150, 400, false);
            Hold(s, 150, 190, 300, true);
            Hold(s, 190, 200, 800, true);
            Hold(s, 200, 300, 400, false);
            s.Actions.Add(new BallAction(100, BallActionKind.Interception, true, Slot, -1));
            s.Actions.Add(new BallAction(120, BallActionKind.Interception, false, Slot, -1));
            s.Actions.Add(new BallAction(150, BallActionKind.Interception, true, Slot, -1));
            s.Actions.Add(new BallAction(200, BallActionKind.Interception, false, Slot, -1));

            BroadcastTimeline t = Direct(s, Unlimited);

            AssertRate(t, 100, 120, PlaybackRate.RealTime, "the counter, own half included");
            AssertRate(t, 150, 190, PlaybackRate.Double, "a slow build-up out of the own half");
            AssertRate(t, 4, 100, PlaybackRate.Double, "other open play");
        }

        /// <summary>
        /// Home plays at X 600, has a free kick taken from <paramref name="kickX"/> (dead 100-109) and
        /// the touch at 110 already has the ball at <paramref name="playedToX"/>, where home keeps it.
        /// </summary>
        private static PositionStream HomeFreeKick(int kickX, int playedToX)
        {
            PositionStream s = KickedOff(200, home: true);
            Hold(s, 4, 100, 600, true);
            Hold(s, 100, 110, kickX, null);
            Hold(s, 110, 200, playedToX, true);
            s.Actions.Add(new BallAction(100, BallActionKind.FreeKick, true, Slot, -1));
            s.Actions.Add(new BallAction(110, BallActionKind.Pass, true, Slot, 7));
            return s;
        }

        [Test]
        public void ASetPieceInTheAttackingHalf_PlaysAtRealTime()
        {
            // X 650 is past halfway but short of the final third: only the set-piece rule makes it 1x.
            // It is played back to X 450, so the spot is read where the ball waits, not where it goes.
            BroadcastTimeline t = Direct(HomeFreeKick(650, 450), Unlimited);

            AssertRate(t, 100, 110, PlaybackRate.RealTime, "the free kick in the attacking half");
            AssertRate(t, 110, 200, PlaybackRate.Double, "open play after it");
        }

        [Test]
        public void ADeadBallOutsideTheAttackingHalf_IsCut_EvenWithBudgetToSpare()
        {
            // Played forward to X 650: the spot, not the landing, decides.
            BroadcastTimeline t = Direct(HomeFreeKick(300, 650), Unlimited);

            AssertRate(t, 0, 4, PlaybackRate.Cut, "the kick-off wait");
            AssertRate(t, 100, 110, PlaybackRate.Cut, "the free kick in the own half");
            AssertRate(t, 4, 100, PlaybackRate.Double, "open play before");
            AssertRate(t, 110, 200, PlaybackRate.Double, "open play after");
        }

        [Test]
        public void ASterileSpell_IsCut_WhileTheBudgetStillBuysOtherOpenPlay()
        {
            // Home plays a warm spell (X 600) for 4-199; away then keeps it in its own half
            // (X 900 = 150 dm up for away) for 200-399. The budget, 49 s = 196 half-frames, buys
            // exactly the 196 warm frames at 2x. Were the sterile spell ordinary open play it would
            // be bought first (it is later, so cheaper) and the warm spell would be the one cut.
            PositionStream s = KickedOff(400, home: true);
            Hold(s, 4, 200, 600, true);
            Hold(s, 200, 400, 900, false);
            s.Actions.Add(new BallAction(200, BallActionKind.Interception, false, Slot, -1));

            BroadcastTimeline t = Direct(s, new BroadcastSettings { TargetSecondsPerHalf = 49 });

            AssertRate(t, 4, 200, PlaybackRate.Double, "the warm spell");
            AssertRate(t, 200, 400, PlaybackRate.Cut, "the sterile spell");
        }

        [Test]
        public void AGoalPlaysForThreeSeconds_AndTheCelebrationBeyondIsCut()
        {
            // Home scores at 100; the ball sits in the net (X 1040) until away kicks off at 140.
            PositionStream s = KickedOff(200, home: true);
            Hold(s, 4, 100, 900, true);
            Hold(s, 100, 140, 1040, null);
            Hold(s, 140, 144, Pitch.CenterX, null);
            Hold(s, 144, 200, 400, false);
            s.Actions.Add(new BallAction(100, BallActionKind.Goal, true, Slot, -1));
            s.Actions.Add(new BallAction(140, BallActionKind.Kickoff, false, 9, -1));
            s.Actions.Add(new BallAction(144, BallActionKind.Pass, false, 9, Slot));

            BroadcastTimeline t = Direct(s, Unlimited);

            AssertRate(t, 100, 107, PlaybackRate.RealTime, "the goal and 3 s after");
            AssertRate(t, 107, 144, PlaybackRate.Cut, "the celebration and the kick-off wait");
            AssertRate(t, 144, 200, PlaybackRate.Double, "the restart");
        }

        [Test]
        public void APenalty_PlaysFromTheAwardToTheKick_EvenWhenTheBudgetIsStarved()
        {
            // Awarded at 100, kicked at 160 (30 s later, far past the 3 s aftermath). A 1 s budget
            // buys nothing, so only the key rules decide what plays.
            PositionStream s = KickedOff(200, home: true);
            Hold(s, 4, 100, 800, true);
            Hold(s, 100, 160, 940, null);
            Hold(s, 160, 200, 1000, null);
            s.Actions.Add(new BallAction(100, BallActionKind.Penalty, true, Slot, -1));
            s.Actions.Add(new BallAction(160, BallActionKind.Shot, true, Slot, -1));

            BroadcastTimeline t = Direct(s, new BroadcastSettings { TargetSecondsPerHalf = 1 });

            Assert.That(t.Segments, Is.EqualTo(new[]
            {
                new BroadcastSegment(0, 100, PlaybackRate.Cut),
                new BroadcastSegment(100, 167, PlaybackRate.RealTime),
                new BroadcastSegment(167, 200, PlaybackRate.Cut)
            }));
        }

        private static string Describe(MatchReport r) =>
            $"{r.HomeClubId}-{r.AwayClubId} {r.HomeGoals}:{r.AwayGoals}";
    }
}
