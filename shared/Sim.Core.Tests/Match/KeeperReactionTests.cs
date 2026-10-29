using System;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Match.Movement;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// R6 of believable-match (issue #149): from shot release the keeper holds still for a reaction
    /// time of 150-300 ms read off his Goalkeeping, then goes for the predicted crossing point, at
    /// dive speed for the last 2 m. The scripted shots are hit by hand at a home keeper (goal line
    /// x = 0) standing where <see cref="KeeperPositioning"/> puts him, and flown in a straight line
    /// at the engine's shot speed, in the simulator's sixteenths of a decimetre.
    /// </summary>
    [TestFixture]
    public class KeeperReactionTests
    {
        private const int Scale = 16;
        private const int AveragePace = 50;
        private const int ReachDm = 15;

        /// <summary>Half the goal mouth, 7.32 m / 2 rounded to the decimetre (MovementGeometry.GoalHalfWidthDm).</summary>
        private const int GoalHalfWidthDm = 37;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;

        private static int TickMs => 1000 / Cfg.TicksPerSecond;

        [Test]
        public void ReactionTime_RunsFrom300msAtWorst_To150msAtBest_NeverSlowerForABetterKeeper()
        {
            Assert.That(KeeperReaction.ReactionMs(Cfg, 1), Is.EqualTo(300));
            Assert.That(KeeperReaction.ReactionMs(Cfg, 100), Is.EqualTo(150));
            for (int gk = 1; gk <= 100; gk++)
            {
                int ms = KeeperReaction.ReactionMs(Cfg, gk);
                Assert.That(ms, Is.InRange(150, 300), $"Goalkeeping {gk}");
                if (gk > 1) Assert.That(ms, Is.LessThanOrEqualTo(KeeperReaction.ReactionMs(Cfg, gk - 1)), $"Goalkeeping {gk}");
            }
        }

        [TestCase(0, 150, 0)]
        [TestCase(100, 150, 50)]
        [TestCase(200, 150, 100)]
        [TestCase(200, 300, 0)]
        [TestCase(300, 300, 100)]
        public void MovingTime_IsThePartOfTheTickAfterTheReaction(int sinceReleaseMs, int reactionMs, int expected)
        {
            Assert.That(KeeperReaction.MovingMs(sinceReleaseMs, 100, reactionMs), Is.EqualTo(expected));
        }

        [Test]
        public void CrossingPoint_IsWhereTheBallMeetsTheGoalLine_AndNoneWhenItIsNotGoingThere()
        {
            Assert.That(KeeperReaction.CrossingPoint(200, 340, -40, 6, 0, out int y), Is.True);
            Assert.That(y, Is.EqualTo(370));
            Assert.That(KeeperReaction.CrossingPoint(200, 340, 40, 6, 0, out _), Is.False, "going away from the line");
            Assert.That(KeeperReaction.CrossingPoint(200, 340, 0, 6, 0, out _), Is.False, "running along it");
        }

        [Test]
        public void Step_RunsUntil2mOut_ThenDives()
        {
            const int run = 70, dive = 90, diveFrom = 20;

            int x = 0, y = 0;
            KeeperReaction.Step(ref x, ref y, 0, 50, 100, run, dive, diveFrom);
            Assert.That((x, y), Is.EqualTo((0, 7)), "5 m out: a tenth of a second at his running speed");

            x = 0; y = 0;
            KeeperReaction.Step(ref x, ref y, 0, 15, 100, run, dive, diveFrom);
            Assert.That((x, y), Is.EqualTo((0, 9)), "inside 2 m: a tenth of a second at dive speed");

            x = 0; y = 0;
            KeeperReaction.Step(ref x, ref y, 0, 24, 100, run, dive, diveFrom);
            Assert.That(y, Is.InRange(7, 8), "runs the 0.4 m to the dive and dives the rest of the tick");

            x = 0; y = 0;
            KeeperReaction.Step(ref x, ref y, 0, 5, 100, run, dive, diveFrom);
            Assert.That((x, y), Is.EqualTo((0, 5)), "stops on the spot");

            x = 0; y = 0;
            KeeperReaction.Step(ref x, ref y, 0, 50, 0, run, dive, diveFrom);
            Assert.That((x, y), Is.EqualTo((0, 0)), "not moving yet");
        }

        /// <summary>
        /// A strike at each corner: left and right of the goal, from the edge of the box and from
        /// the penalty spot. He holds still through his reaction time, then every tick takes him
        /// nearer the crossing point and to its side of the goal; from the edge of the box an
        /// average or better keeper is within 1.5 m of the ball's path as it reaches him.
        /// </summary>
        [Test]
        public void AScriptedShotToEachCorner_TheKeeperReachesTowardIt(
            [Values(-1, 1)] int corner, [Values(180, 110)] int fromDm, [Values(1, 50, 100)] int goalkeeping)
        {
            int ballX = fromDm * Scale, ballY = Pitch.CenterY * Scale;
            int aimY = (Pitch.CenterY + corner * (GoalHalfWidthDm - 4)) * Scale;
            int speed = Cfg.MaxShootSpeedDmPerSecond * Scale / Cfg.TicksPerSecond;
            int length = Length(ballX, aimY - ballY);
            int vx = -ballX * speed / length, vy = (aimY - ballY) * speed / length;

            KeeperPositioning.Target(Cfg, true, fromDm, Pitch.CenterY, out int startXDm, out int startYDm);
            int kx = startXDm * Scale, ky = startYDm * Scale, startX = kx, startY = ky;
            int run = (Cfg.PlayerTopSpeedDmPerSecond + Cfg.PlayerTopSpeedPaceDmPerSecond * AveragePace / 100) * Scale;
            int dive = Cfg.KeeperDiveDmPerSecond * Scale;
            int reaction = KeeperReaction.ReactionMs(Cfg, goalkeeping);

            Assert.That(KeeperReaction.CrossingPoint(ballX, ballY, vx, vy, 0, out int crossY), Is.True);
            int mouth = (GoalHalfWidthDm + 10) * Scale;
            int toCrossing = Length(kx, ky - crossY);
            double atHisDepth = double.MaxValue;
            for (int tick = 0; ballX > 0; tick++)
            {
                int sinceRelease = tick * TickMs;
                KeeperReaction.Target(kx, ky, ballX, ballY, 0, crossY, Pitch.CenterY * Scale - mouth, Pitch.CenterY * Scale + mouth,
                    out int tx, out int ty);
                KeeperReaction.Step(ref kx, ref ky, tx, ty, KeeperReaction.MovingMs(sinceRelease, TickMs, reaction),
                    run, dive, Cfg.KeeperDiveFromDm * Scale);

                if (sinceRelease + TickMs <= reaction)
                {
                    Assert.That((kx, ky), Is.EqualTo((startX, startY)), $"tick {tick}: moved inside his {reaction} ms reaction");
                }
                else
                {
                    int now = Length(kx, ky - crossY);
                    Assert.That(now, Is.LessThanOrEqualTo(toCrossing), $"tick {tick}: away from the crossing point");
                    toCrossing = now;
                }

                int nextX = ballX + vx, nextY = ballY + vy;
                if (ballX > kx && nextX <= kx)
                    atHisDepth = PathDistanceDm(kx, ky, fromDm * Scale, Pitch.CenterY * Scale, crossY);
                ballX = nextX;
                ballY = nextY;
            }

            TestContext.Out.WriteLine(
                $"[keeper] corner {corner} from {fromDm / 10} m, Goalkeeping {goalkeeping}: reaction {reaction} ms, "
                + $"moved {Length(kx - startX, ky - startY) / (double)Scale / 10:F2} m, off the path at his depth {atHisDepth / 10:F2} m");
            Assert.That(Length(kx, ky - crossY), Is.LessThan(Length(startX, startY - crossY)), "nearer the crossing point");
            Assert.That(Math.Sign(ky - startY), Is.EqualTo(corner), "across to the corner the shot is going to");
            if (fromDm == 180 && goalkeeping >= 50)
                Assert.That(atHisDepth, Is.LessThanOrEqualTo(ReachDm), "within 1.5 m of the ball as it reaches him");
        }

        [Test]
        public void ABetterKeeper_EndsNearerTheCrossingPoint()
        {
            Assert.That(EndGapDm(100), Is.LessThan(EndGapDm(1)));
        }

        private static int EndGapDm(int goalkeeping)
        {
            int fromDm = 180;
            int ballX = fromDm * Scale, ballY = Pitch.CenterY * Scale;
            int crossY = (Pitch.CenterY + GoalHalfWidthDm - 4) * Scale;
            int speed = Cfg.MaxShootSpeedDmPerSecond * Scale / Cfg.TicksPerSecond;
            int length = Length(ballX, crossY - ballY);
            int vx = -ballX * speed / length, vy = (crossY - ballY) * speed / length;
            KeeperPositioning.Target(Cfg, true, fromDm, Pitch.CenterY, out int kxDm, out int kyDm);
            int kx = kxDm * Scale, ky = kyDm * Scale;
            int run = (Cfg.PlayerTopSpeedDmPerSecond + Cfg.PlayerTopSpeedPaceDmPerSecond * AveragePace / 100) * Scale;
            int reaction = KeeperReaction.ReactionMs(Cfg, goalkeeping);
            for (int tick = 0; ballX > 0; tick++)
            {
                KeeperReaction.Target(kx, ky, ballX, ballY, 0, crossY, 0, Pitch.WidthDm * Scale, out int tx, out int ty);
                KeeperReaction.Step(ref kx, ref ky, tx, ty, KeeperReaction.MovingMs(tick * TickMs, TickMs, reaction),
                    run, Cfg.KeeperDiveDmPerSecond * Scale, Cfg.KeeperDiveFromDm * Scale);
                ballX += vx;
                ballY += vy;
            }

            return Length(kx, ky - crossY) / Scale;
        }

        /// <summary>From (<paramref name="x"/>, <paramref name="y"/>) to the path from the strike to the crossing point, in decimetres.</summary>
        private static double PathDistanceDm(int x, int y, int fromX, int fromY, int crossY)
        {
            double dx = -fromX, dy = crossY - fromY;
            double t = Math.Clamp(((x - fromX) * dx + (y - fromY) * dy) / (dx * dx + dy * dy), 0, 1);
            double px = fromX + t * dx - x, py = fromY + t * dy - y;
            return Math.Sqrt(px * px + py * py) / Scale;
        }

        private static int Length(int dx, int dy) => (int)Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
