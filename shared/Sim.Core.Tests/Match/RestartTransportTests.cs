using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R5 of believable-match (issue #147), read off real matches: the ball is never placed. A
    /// restart's ball travels from where play stopped to the spot the taker plays it from at no more
    /// than 8 m/s, a frame-to-frame step never exceeds 8 m, and a pass or a shot never flies faster
    /// in a frame than the R1 p99 of its kind plus 10%.
    /// </summary>
    [TestFixture]
    public class RestartTransportTests
    {
        /// <summary>Enough matches that "no jump, no placed restart" is a claim about the engine, not about a sample.</summary>
        private const int Matches = 40;
        private const ulong FirstSeed = 14_700;

        /// <summary>A transported ball moves at most this far per frame: 8 m/s for a frame of a fifth of a second.</summary>
        private static readonly double TransportStepDm =
            (double)BallContinuityAnalyzer.TransportCapMps * 10 * 60 / new BalanceConfig().Match.FramesPerMinute;

        /// <summary>Half the width of the goal, centre to post.</summary>
        private const int GoalHalfWidthDm = 37;

        /// <summary>A transport counts as one when the ball covers at least this much ground to its spot.</summary>
        private const int MovedDm = 20;

        private static readonly List<PositionStream> Streams = new List<PositionStream>();

        [OneTimeSetUp]
        public void PlayOnce()
        {
            Streams.Clear();
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            var streams = new PositionStream[Matches];
            Parallel.For(0, Matches, i =>
            {
                MatchReport report = new MatchEngine().Simulate(
                    LineupSelector.BestEleven(league.Clubs[(9 + i) % league.Clubs.Count]),
                    LineupSelector.BestEleven(league.Clubs[(10 + 3 * i) % league.Clubs.Count]), new Pcg32(FirstSeed + (ulong)i));
                streams[i] = report.Positions!.Unpack();
            });
            Streams.AddRange(streams);
        }

        [Test]
        public void AThrowIn_IsTransportedToItsSpot() => AssertTransported(BallActionKind.ThrowIn);

        [Test]
        public void AGoalKick_IsTransportedToItsSpot() => AssertTransported(BallActionKind.GoalKick);

        [Test]
        public void ACorner_IsTransportedToItsSpot() => AssertTransported(BallActionKind.Corner);

        [Test]
        public void AFreeKick_IsTransportedToItsSpot() => AssertTransported(BallActionKind.FreeKick);

        [Test]
        public void AKickoff_IsTransportedToTheCentreSpot() => AssertTransported(BallActionKind.Kickoff);

        [Test]
        public void TheContinuityHarness_ReadsNoBallJump_AndNoPlacedRestart()
        {
            BallContinuityMetrics sum = Measured();
            TestContext.Out.WriteLine(
                $"[transport] jumps {sum.Jumps} (at restarts {sum.JumpsAtRestarts}), placed restarts {sum.PlacedRestarts}/{sum.Restarts}");
            Assert.That(sum.Restarts, Is.GreaterThan(0));
            Assert.That(sum.PlacedRestarts, Is.EqualTo(0), "no restart's ball reaches its spot faster than 8 m/s");
            Assert.That(sum.Jumps, Is.EqualTo(0), "no ball step longer than 8 m in one frame");
        }

        [Test]
        public void PassAndShotFrames_StayUnderTheR1P99_PlusTenPercent()
        {
            BallContinuityMetrics sum = Measured();
            double passCap = RealismReference.Movement.PassSpeedP99.Max * 1.1;
            double shotCap = RealismReference.Movement.ShotSpeedP99.Max * 1.1;
            double pass = sum.SpeedPercentile(shot: false, 1.0), shot = sum.SpeedPercentile(shot: true, 1.0);
            TestContext.Out.WriteLine($"[transport] fastest pass frame {pass:F2} m/s (cap {passCap:F2}), shot {shot:F2} m/s (cap {shotCap:F2})");
            Assert.That(sum.SpeedFrames(shot: false), Is.GreaterThan(0));
            Assert.That(sum.SpeedFrames(shot: true), Is.GreaterThan(0));
            Assert.That(pass, Is.LessThanOrEqualTo(passCap), "pass ball speed per frame");
            Assert.That(shot, Is.LessThanOrEqualTo(shotCap), "shot ball speed per frame");
        }

        /// <summary>
        /// A shot wide of a post stays wide of it while it lies behind the line before its restart:
        /// running on across the line, one that crossed just wide went through the side netting and
        /// lay between the posts, a miss drawn as a goal.
        /// </summary>
        [Test]
        public void AWideShot_LiesWideOfThePosts_UntilItsRestart()
        {
            int read = 0;
            foreach (PositionStream s in Streams)
            {
                List<BallAction> actions = s.Actions;
                for (int i = 0; i < actions.Count; i++)
                {
                    BallAction miss = actions[i];
                    if (miss.Kind != BallActionKind.Miss) continue;
                    int lineX = miss.Home ? Pitch.LengthDm : 0;
                    int end = s.TickCount;
                    for (int j = i + 1; j < actions.Count; j++)
                    {
                        if (actions[j].Tick <= miss.Tick) continue;
                        end = Math.Min(end, actions[j].Tick);
                        break;
                    }

                    for (int t = miss.Tick; t < end; t++)
                    {
                        int x = s.BallXY[t * 2];
                        if ((miss.Home ? x - lineX : lineX - x) <= 0) continue;
                        read++;
                        Assert.That(Math.Abs(s.BallXY[t * 2 + 1] - Pitch.CenterY), Is.GreaterThan(GoalHalfWidthDm),
                            $"the miss filed at frame {miss.Tick} lies between the posts on frame {t}");
                    }
                }
            }

            Assert.That(read, Is.GreaterThan(0), "the matches put a shot wide");
        }

        /// <summary>
        /// Every restart of the kind: from the frame it is filed to the frame before the taker plays
        /// it (the spot, as <see cref="BallContinuityAnalyzer"/> reads it), no step is longer than a
        /// transport covers, and the ball arrives on the spot at transport speed. At least one of them
        /// has the ball travel to its spot from somewhere else.
        /// </summary>
        private static void AssertTransported(BallActionKind kind)
        {
            int read = 0, moved = 0;
            foreach (PositionStream s in Streams)
            {
                List<BallAction> actions = s.Actions;
                int lastTick = -1;
                for (int i = 0; i < actions.Count; i++)
                {
                    BallAction r = actions[i];
                    if (r.Kind != kind || r.Tick == lastTick || r.Tick <= 0 || r.Tick >= s.TickCount) continue;
                    lastTick = r.Tick;

                    int spot = SpotFrame(s, actions, i);
                    if (spot < 0) continue;
                    int arrival = spot;
                    while (arrival > 0 && Distance(s, arrival - 1, spot) <= BallContinuityAnalyzer.SpotToleranceDm) arrival--;
                    if (arrival == 0) continue;   // the opening kickoff: on the spot from the first frame
                    read++;

                    string at = $"{kind} filed at frame {r.Tick}, played from frame {spot}";
                    Assert.That(Step(s, r.Tick), Is.LessThanOrEqualTo(BallContinuityAnalyzer.JumpDm), $"{at}: the ball jumps on the whistle");
                    for (int t = r.Tick + 1; t <= spot; t++)
                        Assert.That(Step(s, t), Is.LessThanOrEqualTo(TransportStepDm), $"{at}: the ball moves {Step(s, t):F0} dm on frame {t}");
                    Assert.That(Step(s, arrival), Is.LessThanOrEqualTo(TransportStepDm), $"{at}: placed on frame {arrival}");
                    if (Distance(s, r.Tick - 1, spot) >= MovedDm) moved++;
                }
            }

            TestContext.Out.WriteLine($"[transport] {kind}: read {read}, carried at least {MovedDm / 10} m {moved}");
            Assert.That(read, Is.GreaterThan(0), $"the matches play at least one {kind}");
            Assert.That(moved, Is.GreaterThan(0), $"at least one {kind} ball travels to its spot");
        }

        /// <summary>
        /// The frame before the next action after the restart at <paramref name="index"/> (cards
        /// aside), as <see cref="BallContinuityAnalyzer"/> reads it; -1 when the half-time or
        /// full-time whistle comes first and its ball is still on the way.
        /// </summary>
        private static int SpotFrame(PositionStream s, List<BallAction> actions, int index)
        {
            BallAction r = actions[index];
            int spot = s.TickCount - 1;
            bool taken = false;
            for (int j = index + 1; j < actions.Count; j++)
            {
                BallAction next = actions[j];
                if (next.Tick <= r.Tick || next.Kind == BallActionKind.YellowCard || next.Kind == BallActionKind.RedCard) continue;
                spot = Math.Min(next.Tick - 1, s.TickCount - 1);
                taken = next.Kind != BallActionKind.HalfTime;
                break;
            }

            return !taken && Step(s, spot) > 0 ? -1 : spot;
        }

        private static BallContinuityMetrics Measured() => Streams
            .Select(s => BallContinuityAnalyzer.Analyze(new MatchReport { Positions = s })!)
            .Aggregate((a, b) => a.Plus(b));

        private static double Distance(PositionStream s, int a, int b)
        {
            double dx = s.BallXY[a * 2] - s.BallXY[b * 2], dy = s.BallXY[a * 2 + 1] - s.BallXY[b * 2 + 1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Step(PositionStream s, int t) => Distance(s, t - 1, t);
    }
}
