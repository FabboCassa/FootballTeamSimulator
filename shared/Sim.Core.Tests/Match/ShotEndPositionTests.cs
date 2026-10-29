using System;
using System.Collections.Generic;
using System.Linq;
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
    /// R5 of believable-match (issue #148), read off real matches: where a strike's ball ends.
    /// A wide shot crosses the goal line and stays beyond it at least 1.5 s before the restart is
    /// filed; a goal ends at least a metre behind the line between the posts and stays there until
    /// the kick-off; a save has no jump.
    /// </summary>
    [TestFixture]
    public class ShotEndPositionTests
    {
        private const int Matches = 6;
        private const ulong FirstSeed = 14_800;
        private const int BehindLineMs = 1500;

        /// <summary>Half the goal mouth, 7.32 m / 2 rounded to the decimetre (MovementGeometry.GoalHalfWidthDm).</summary>
        private const int GoalHalfWidthDm = 37;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static readonly List<PositionStream> Streams = new List<PositionStream>();

        [OneTimeSetUp]
        public void PlayOnce()
        {
            Streams.Clear();
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            for (int i = 0; i < Matches; i++)
            {
                MatchReport report = new MatchEngine().Simulate(
                    LineupSelector.BestEleven(league.Clubs[9]),
                    LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(FirstSeed + (ulong)i));
                Streams.Add(report.Positions!.Unpack());
            }
        }

        [Test]
        public void AWideShot_CrossesTheGoalLine_AndStaysBeyondIt_BeforeTheGoalKick()
        {
            int frames = (BehindLineMs * Cfg.FramesPerMinute + 59_999) / 60_000;
            int read = 0;
            foreach (PositionStream s in Streams)
            {
                List<BallAction> actions = s.Actions;
                for (int i = 0; i + 2 < actions.Count; i++)
                {
                    BallAction shot = actions[i], miss = actions[i + 1], restart = actions[i + 2];
                    if (shot.Kind != BallActionKind.Shot || miss.Kind != BallActionKind.Miss
                        || restart.Kind != BallActionKind.GoalKick) continue;
                    read++;

                    int lineX = shot.Home ? Pitch.LengthDm : 0;
                    Assert.That(restart.Tick - miss.Tick, Is.GreaterThanOrEqualTo(frames),
                        $"miss at frame {miss.Tick}: the goal kick follows {restart.Tick - miss.Tick} frames later");
                    for (int t = miss.Tick; t < restart.Tick; t++)
                        Assert.That(Depth(s, t, lineX), Is.GreaterThan(0),
                            $"miss at frame {miss.Tick}: the ball is not beyond the goal line on frame {t}");
                }
            }

            TestContext.Out.WriteLine($"[shot-end] wide shots over the goal line read: {read}");
            Assert.That(read, Is.GreaterThan(0), "the matches play at least one shot wide of the goal");
        }

        [Test]
        public void AGoal_EndsAMetreBehindTheLine_BetweenThePosts_UntilTheKickoff()
        {
            int read = 0;
            foreach (PositionStream s in Streams)
            {
                List<BallAction> actions = s.Actions;
                for (int i = 0; i < actions.Count; i++)
                {
                    BallAction goal = actions[i];
                    if (goal.Kind != BallActionKind.Goal) continue;
                    int kickoff = actions.Skip(i + 1).FirstOrDefault(a => a.Kind == BallActionKind.Kickoff).Tick;
                    if (kickoff <= goal.Tick) continue;   // the whistle went first
                    read++;

                    int lineX = goal.Home ? Pitch.LengthDm : 0;
                    for (int t = goal.Tick; t < kickoff; t++)
                    {
                        Assert.That(Depth(s, t, lineX), Is.GreaterThan(0), $"goal at frame {goal.Tick}: out of the net on frame {t}");
                        Assert.That(Math.Abs(s.BallXY[t * 2 + 1] - Pitch.CenterY), Is.LessThanOrEqualTo(GoalHalfWidthDm),
                            $"goal at frame {goal.Tick}: wide of the posts on frame {t}");
                    }

                    Assert.That(Depth(s, kickoff - 1, lineX), Is.GreaterThanOrEqualTo(BallContinuityAnalyzer.BehindLineDm),
                        $"goal at frame {goal.Tick}: the ball rests {Depth(s, kickoff - 1, lineX)} dm behind the line");
                }
            }

            TestContext.Out.WriteLine($"[shot-end] goals read: {read}");
            Assert.That(read, Is.GreaterThan(0), "the matches score at least one goal");
        }

        [Test]
        public void ASave_GoesToTheKeeper_ARebound_OrBehind_WithNoJump()
        {
            int read = 0;
            foreach (PositionStream s in Streams)
            {
                List<BallAction> actions = s.Actions;
                for (int i = 1; i < actions.Count; i++)
                {
                    BallAction save = actions[i];
                    if (save.Kind != BallActionKind.Save || actions[i - 1].Kind != BallActionKind.Shot) continue;
                    read++;

                    // From the strike to two frames after the save, up to any restart being filed.
                    int end = Math.Min(save.Tick + 2, s.TickCount - 1);
                    BallAction next = actions.Skip(i + 1).FirstOrDefault(a => IsRestart(a.Kind));
                    if (next.Tick > save.Tick) end = Math.Min(end, next.Tick - 1);
                    for (int t = actions[i - 1].Tick + 1; t <= end; t++)
                        Assert.That(Step(s, t), Is.LessThanOrEqualTo(BallContinuityAnalyzer.JumpDm),
                            $"save at frame {save.Tick}: the ball jumps {Step(s, t):F0} dm on frame {t}");
                }
            }

            TestContext.Out.WriteLine($"[shot-end] saves read: {read}");
            Assert.That(read, Is.GreaterThan(0), "the matches play at least one save");
        }

        [Test]
        public void TheContinuityHarness_ReadsEveryWideShotOverTheLine_AndEveryGoalInTheNet()
        {
            BallContinuityMetrics sum = Streams
                .Select(s => BallContinuityAnalyzer.Analyze(new MatchReport { Positions = s })!)
                .Aggregate((a, b) => a.Plus(b));

            TestContext.Out.WriteLine(
                $"[shot-end] wide shots crossing {sum.WideShotsCrossing}/{sum.WideShots}, goals in the net {sum.GoalsBehindLine}/{sum.Goals}");
            Assert.That(sum.WideShots, Is.GreaterThan(0));
            Assert.That(sum.WideShotsCrossing, Is.EqualTo(sum.WideShots), "every wide shot crosses the goal line");
            Assert.That(sum.GoalsBehindLine, Is.EqualTo(sum.Goals), "every goal ends a metre behind the line");
        }

        /// <summary>How far the ball is beyond the goal line at <paramref name="lineX"/> (negative: short of it).</summary>
        private static int Depth(PositionStream s, int t, int lineX) =>
            lineX == 0 ? -s.BallXY[t * 2] : s.BallXY[t * 2] - lineX;

        private static double Step(PositionStream s, int t)
        {
            double dx = s.BallXY[t * 2] - s.BallXY[(t - 1) * 2], dy = s.BallXY[t * 2 + 1] - s.BallXY[(t - 1) * 2 + 1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick || k == BallActionKind.Corner
            || k == BallActionKind.FreeKick || k == BallActionKind.Penalty || k == BallActionKind.Kickoff;
    }
}
