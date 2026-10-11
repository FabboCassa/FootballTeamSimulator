using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R6 of docs/specs/real-match-and-playing-styles.md: in open play the keeper stands on the line
    /// between the ball and his goal centre, ≤ 6 m off his line with the ball within 35 m of goal and
    /// ≤ 18 m otherwise; on a goal kick he takes it from within 6 m of his line.
    /// </summary>
    [TestFixture]
    public class KeeperPositioningTests
    {
        private const int NearGoalDm = 350;
        private const int NearDepthDm = 60;
        private const int FarDepthDm = 180;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;

        // Five ball positions, written for the home keeper (his goal line at x = 0); the away keeper
        // gets the same five mirrored through the centre spot.
        private static readonly (int X, int Y)[] Balls =
        {
            (Pitch.CenterX, Pitch.CenterY),               // centre spot
            (200, Pitch.CenterY),                         // 20 m out, central
            (250, Pitch.CenterY + 190),                   // 31 m out, wide of the box
            (30, Pitch.CenterY - 200),                    // by the byline, at the box corner
            (900, 60),                                    // deep in the other half, by a touchline
        };

        [Test]
        public void Target_LiesOnTheBallGoalLine_WithinTheDepthRule(
            [Values(true, false)] bool home, [Range(0, 4)] int ball)
        {
            int bx = home ? Balls[ball].X : Pitch.LengthDm - Balls[ball].X;
            int by = home ? Balls[ball].Y : Pitch.WidthDm - Balls[ball].Y;
            int gx = home ? 0 : Pitch.LengthDm, gy = Pitch.CenterY;

            KeeperPositioning.Target(Cfg, home, bx, by, out int x, out int y);

            long ux = bx - gx, uy = by - gy, vx = x - gx, vy = y - gy;
            double ballDistance = Math.Sqrt(ux * ux + uy * uy);
            double offLine = Math.Abs(ux * vy - uy * vx) / ballDistance;
            // Positions are whole decimetres, so each coordinate rounds by under one.
            Assert.That(offLine, Is.LessThan(1.5), "on the ball-goal-centre line");
            Assert.That(ux * vx + uy * vy, Is.GreaterThanOrEqualTo(0), "on the ball's side of the goal centre");
            Assert.That(Math.Sqrt(vx * vx + vy * vy), Is.LessThanOrEqualTo(ballDistance), "between the goal and the ball");

            int depth = Math.Abs(x - gx);
            int limit = ballDistance <= NearGoalDm ? NearDepthDm : FarDepthDm;
            Assert.That(depth, Is.LessThanOrEqualTo(limit), $"depth off the line for ball ({bx},{by})");
            Assert.That(depth, Is.GreaterThan(0), "off his line, not on it");
        }

        [Test]
        public void Target_StepsOffHisLine_AsTheBallGoesAway()
        {
            KeeperPositioning.Target(Cfg, true, 200, Pitch.CenterY, out int near, out int _);
            KeeperPositioning.Target(Cfg, true, Pitch.CenterX, Pitch.CenterY, out int far, out int _);
            Assert.That(far, Is.GreaterThan(near), "a sweeper-keeper: further up with the ball in the other half");
        }

        // ------------------------------------------------------------------ played matches

        private const int Matches = 8;
        private const ulong FirstSeed = 75_000;

        private static MatchReport[] _reports = null!;

        [OneTimeSetUp]
        public void PlayMatches()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            Lineup a = LineupSelector.BestEleven(league.Clubs[9]), b = LineupSelector.BestEleven(league.Clubs[10]);
            var cfg = new BalanceConfig();

            _reports = new MatchReport[Matches];
            Parallel.For(0, Matches, i =>
            {
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                _reports[i] = i % 2 == 0
                    ? engine.Simulate(a, b, new Pcg32(FirstSeed + (ulong)i))
                    : engine.Simulate(b, a, new Pcg32(FirstSeed + (ulong)i));
            });
        }

        [Test]
        public void PlayedMatches_KeepersBreakTheDepthRule_InAtMostOnePercentOfOpenPlay()
        {
            var analyzer = new ShapeMovementAnalyzer();
            long frames = 0, breaks = 0;
            foreach (MatchReport r in _reports)
            {
                ShapeMovementMetrics m = analyzer.Measure(r)!;
                frames += m.KeeperOpenPlayFrames;
                breaks += m.KeeperDepthBreakFrames;
            }

            double percent = 100.0 * breaks / frames;
            TestContext.Out.WriteLine($"[keeper-depth] {breaks}/{frames} open-play keeper frames break R6 ({percent:F2}%)");
            Assert.That(frames, Is.GreaterThan(0));
            Assert.That(percent, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void GoalKicks_AreTakenByTheKeeper_WithinSixMetresOfHisLine()
        {
            int goalKicks = 0;
            foreach (MatchReport r in _reports)
            {
                PositionStream s = r.Positions!;
                for (int i = 0; i < s.Actions.Count; i++)
                {
                    BallAction restart = s.Actions[i];
                    if (restart.Kind != BallActionKind.GoalKick) continue;
                    goalKicks++;

                    int keeper = KeeperSlot(r, restart.Home);
                    Assert.That(restart.Slot, Is.EqualTo(keeper), "the keeper takes the goal kick");

                    // The next action is the kick that puts it back in play; the frame before it is
                    // him standing over the ball (on the kick's own frame he has already struck it).
                    if (i + 1 >= s.Actions.Count || s.Actions[i + 1].Kind == BallActionKind.HalfTime) continue;
                    BallAction kick = s.Actions[i + 1];
                    Assert.That(kick.Home == restart.Home && kick.Slot == keeper, Is.True, "he kicks it himself");
                    int[] xy = restart.Home ? s.HomeXY : s.AwayXY;
                    int x = s.PlayerX(xy, kick.Tick - 1, keeper);
                    int depth = restart.Home ? x : Pitch.LengthDm - x;
                    Assert.That(depth, Is.LessThanOrEqualTo(NearDepthDm), "he kicks it from within 6 m of his line");
                }
            }

            Assert.That(goalKicks, Is.GreaterThan(0));
        }

        private static int KeeperSlot(MatchReport r, bool home)
        {
            PositionStream s = r.Positions!;
            int[] xy = home ? s.HomeXY : s.AwayXY;
            // The kickoff frame: the keeper is the man nearest his own goal line.
            int best = 0, bestDepth = int.MaxValue;
            for (int k = 0; k < s.PlayerCount; k++)
            {
                int x = s.PlayerX(xy, 0, k);
                int depth = home ? x : Pitch.LengthDm - x;
                if (depth < bestDepth) { bestDepth = depth; best = k; }
            }

            return best;
        }
    }
}
