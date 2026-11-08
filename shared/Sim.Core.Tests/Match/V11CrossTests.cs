using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R5, issue #183: from the wide channel of the final third a cross is lofted
    /// (peak at least 2 m) or driven low / cut back, whichever the expected-value choice of R7 prices
    /// dearer, and a headed attempt comes from a cross. Home attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11CrossTests
    {
        private const int TopSpeed = 75;   // dm/s
        private const int GoalX = Pitch.LengthDm;
        private const int Mid = Pitch.CenterY;
        private const int Seeds = 32;
        private const int HighPeakDm = GeometricCrossAnalyzer.HighPeakDm;

        private MatchBalance _cfg = null!;
        private V11ActionValuation _valuation = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _valuation = new V11ActionValuation(_cfg);
        }

        private static PitchActor At(int x, int y) => new PitchActor(x, y, 0, 0, TopSpeed);

        /// <summary>A wide carrier of average ball skill, a full-back goal-side of him, the keeper on his line.</summary>
        private static V11Scene WideCarrier(int ballX, int ballY)
        {
            var s = new V11Scene
            {
                BallXDm = ballX, BallYDm = ballY, Shooting = 60, Technique = 60, Passing = 60, CarryTouchDm = 100,
                GoalSideBlocked = true
            };
            s.AddFoe(At(GoalX - 5, Mid), keeper: true);
            return s;
        }

        private static (int X, int Y) OnTheLane(int ax, int ay, int bx, int by, int permille) =>
            (ax + (bx - ax) * permille / 1000, ay + (by - ay) * permille / 1000);

        /// <summary>
        /// Up the touchline, a striker free in the box and a defender standing in the ground lane to
        /// him: the ball goes over the defender, a high cross.
        /// </summary>
        [Test]
        public void AWideCarrier_WithADefenderInTheGroundLane_CrossesHigh()
        {
            var misses = new List<string>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(18300 + seed));
                int ballX = GoalX - rng.NextInt(120, 181), ballY = rng.NextInt(60, 131);
                V11Scene s = WideCarrier(ballX, ballY);

                int mateX = GoalX - rng.NextInt(60, 111), mateY = Mid + rng.NextInt(-40, 41);
                s.AddMate(At(mateX, mateY), keeper: false, shooting: 60, technique: 60);
                var lane = OnTheLane(ballX, ballY, mateX, mateY, rng.NextInt(400, 601));
                s.AddFoe(At(lane.X, lane.Y), keeper: false);

                V11Choice choice = _valuation.Choose(s, holding: false);
                if (choice.Kind != V11ActionKind.Cross || choice.Mate != 0 || !choice.HighCross)
                    misses.Add($"seed {seed}: ({ballX},{ballY}) chose {choice.Kind} high={choice.HighCross} to {choice.Mate} worth {choice.ValuePer10k}");
            }

            Assert.That(misses, Is.Empty, string.Join("\n", misses));
        }

        /// <summary>
        /// From the byline, a team-mate free around the penalty spot, the ground lane back to him
        /// clear of defenders (on the six-yard line at the far post): the ball is cut back low, to
        /// his foot, in at least 90% of the layouts. In the rest, struck from within 3 m of the goal
        /// line, the keeper can reach the end of the ground lane and the lofted ball is dearer.
        /// </summary>
        [Test]
        public void AByLineCarrier_WithAClearLaneBackToTheSpot_CutsItBackLow()
        {
            var misses = new List<string>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(18400 + seed));
                V11Scene s = CutBack(rng, out _, out _, out _, out _);

                V11Choice choice = _valuation.Choose(s, holding: false);
                if (choice.Kind != V11ActionKind.Cross || choice.Mate != 0 || choice.HighCross)
                    misses.Add($"seed {seed}: ({s.BallXDm},{s.BallYDm}) chose {choice.Kind} high={choice.HighCross} to {choice.Mate} worth {choice.ValuePer10k}");
            }

            TestContext.Out.WriteLine(string.Join("\n", misses));
            Assert.That((Seeds - misses.Count) * 100 / Seeds, Is.GreaterThanOrEqualTo(90), string.Join("\n", misses));
        }

        /// <summary>
        /// The same cut-back with a defender stepped into its ground lane: the price, not a rule,
        /// picks the height, so the ball now goes in the air.
        /// </summary>
        [Test]
        public void TheSameDelivery_GoesHigh_OnceADefenderStepsIntoItsGroundLane()
        {
            var flipped = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(18400 + seed));
                V11Scene s = CutBack(rng, out int ballX, out int ballY, out int mateX, out int mateY);
                var lane = OnTheLane(ballX, ballY, mateX, mateY, 500);
                s.AddFoe(At(lane.X, lane.Y), keeper: false);

                V11Choice choice = _valuation.Choose(s, holding: false);
                if (choice.Kind == V11ActionKind.Cross && choice.HighCross) flipped++;
            }

            Assert.That(flipped * 100 / Seeds, Is.GreaterThanOrEqualTo(90), $"{flipped}/{Seeds} went high");
        }

        private static V11Scene CutBack(Pcg32 rng, out int ballX, out int ballY, out int mateX, out int mateY)
        {
            ballX = GoalX - rng.NextInt(25, 61);
            ballY = rng.NextInt(130, 166);
            V11Scene s = WideCarrier(ballX, ballY);
            mateX = GoalX - rng.NextInt(95, 121);
            mateY = Mid + rng.NextInt(-30, 31);
            s.AddMate(At(mateX, mateY), keeper: false, shooting: 60, technique: 60);
            s.AddFoe(At(GoalX - 10, Mid + 60), keeper: false);
            s.AddFoe(At(GoalX - 10, Mid + 120), keeper: false);
            return s;
        }

        // ---------------------------------------------------------------- the scripted wide carrier

        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261183));
        }

        /// <summary>
        /// A winger on the ball, the striker free in the box. Up the touchline with the full-back in
        /// the ground lane he lofts it, and it peaks above 2 m on its way; by the byline with the lane
        /// back to the striker on the penalty spot open, he cuts it back along the grass. Either way
        /// it is played to the striker, by the brain, as in a match.
        /// </summary>
        [Test]
        public void AScriptedWideCarrier_LoftsItOverAManInTheLane_AndCutsItBackLowThroughAnOpenOne()
        {
            var scene = new CrossScene(_league);

            Deliver(scene, 900, 60, 975, Mid - 20, fullBackInTheLane: true, out int peak, out int receiver);
            Assert.That(receiver, Is.EqualTo(scene.Striker), "crossed to the striker");
            Assert.That(peak, Is.GreaterThanOrEqualTo(HighPeakDm), "a high cross peaks at 2 m or more");

            Deliver(scene, 1000, 120, 940, Mid - 10, fullBackInTheLane: false, out peak, out receiver);
            Assert.That(receiver, Is.EqualTo(scene.Striker), "cut back to the striker");
            Assert.That(peak, Is.LessThan(HighPeakDm), "a low cross stays down");
            Assert.That(peak, Is.Zero, "driven along the grass");
        }

        /// <summary>
        /// The cross is contested (R5): as the high cross to the striker goes in, one defender, not the
        /// man closing the striker (issue #206), steps into the shot line, V11CrossShotLineDm goal-side
        /// of where the ball is going, and is nearer that spot a second later.
        /// </summary>
        [Test]
        public void AsTheCrossGoesIn_ADefenderStepsIntoTheShotLineFromWhereItLands()
        {
            var scene = new CrossScene(_league);
            Deliver(scene, 900, 60, 975, Mid - 20, fullBackInTheLane: true, out _, out int receiver, flight: false);
            Assert.That(receiver, Is.EqualTo(scene.Striker), "crossed to the striker");

            int landX = scene.ReceiveX, landY = scene.ReceiveY;
            int toGoalX = GoalX - landX, toGoalY = Mid - landY;
            double length = Math.Sqrt((double)toGoalX * toGoalX + (double)toGoalY * toGoalY);
            int lineX = landX + (int)Math.Round(toGoalX * CrossScene.Cfg.V11CrossShotLineDm / length);
            int lineY = landY + (int)Math.Round(toGoalY * CrossScene.Cfg.V11CrossShotLineDm / length);

            int[] outfield = Enumerable.Range(0, scene.Away.Slots.Count).Where(s => s != scene.Keeper).ToArray();
            scene.TickAway(outfield);
            List<MovementRecord> liners = outfield.Select(scene.LastAway).Where(r => r.Job == MovementJob.ShotLine).ToList();
            Assert.That(liners.Count, Is.EqualTo(1), "one man steps into the line");
            MovementRecord liner = liners[0];
            Assert.That(liner.Slot, Is.Not.EqualTo(scene.AwayZoneCloser), "not the man closing the striker");
            Assert.That(liner.TargetXDm, Is.EqualTo(lineX).Within(2), "goal-side of the landing point");
            Assert.That(liner.TargetYDm, Is.EqualTo(lineY).Within(2));

            long before = Gap(scene.X(1, liner.Slot), scene.Y(1, liner.Slot), lineX, lineY);
            for (int t = 0; t < CrossScene.Cfg.TicksPerSecond; t++)
            {
                scene.StepBall();
                scene.TickAway(outfield);
                Assert.That(scene.LastAway(liner.Slot).Job, Is.EqualTo(MovementJob.ShotLine), $"tick {t}: he holds the job");
            }

            Assert.That(Gap(scene.X(1, liner.Slot), scene.Y(1, liner.Slot), lineX, lineY), Is.LessThan(before), "he gets into the line");
        }

        /// <summary>
        /// The scramble after a set piece is anybody's (R5, owner decision #183): with the cross on
        /// from a winger in open play the defenders pick up the box runs, but within
        /// V11BoxMarkAfterSetPieceMs of a set piece's delivery, from the recycled ball, nobody does.
        /// </summary>
        [Test]
        public void AfterASetPiece_TheRecycledCrossIsNotBoxMarked_InOpenPlayItIs()
        {
            Assert.That(BoxMarksWithTheCrossOn(afterSetPiece: false), Is.GreaterThan(0), "open play: the box runs are picked up");
            Assert.That(BoxMarksWithTheCrossOn(afterSetPiece: true), Is.Zero, "after a set piece: nobody marks the box runs");
        }

        private static int BoxMarksWithTheCrossOn(bool afterSetPiece)
        {
            var scene = new CrossScene(_league);
            scene.Reset();
            scene.Place(0, scene.Carrier, 900, 60);
            scene.Place(0, scene.Striker, 975, Mid - 20);
            int[] line = scene.BackLine;
            scene.Place(1, line[0], 1030, Mid + 60);
            scene.Place(1, line[1], 1030, Mid + 120);
            scene.Place(1, line[2], 1000, Mid + 220);
            scene.Place(1, line[3], 1000, Mid + 300);
            scene.GiveBall(scene.Carrier);
            if (afterSetPiece) scene.SetPieceDelivered();

            int[] outfield = Enumerable.Range(0, scene.Away.Slots.Count).Where(s => s != scene.Keeper).ToArray();
            int marks = 0;
            for (int t = 0; t < 5; t++)
            {
                scene.TickAway(outfield);
                marks += outfield.Count(s => scene.LastAway(s).Job == MovementJob.BoxMark);
            }

            TestContext.Out.WriteLine($"[box marks] after a set piece {afterSetPiece}: {marks} man-ticks");
            return marks;
        }

        private static long Gap(int x, int y, int tx, int ty) => (long)(x - tx) * (x - tx) + (long)(y - ty) * (y - ty);

        private static void Deliver(CrossScene scene, int ballX, int ballY, int strikerX, int strikerY, bool fullBackInTheLane,
            out int peak, out int receiver, bool flight = true)
        {
            scene.Reset();
            scene.Place(0, scene.Carrier, ballX, ballY);
            scene.Place(0, scene.Striker, strikerX, strikerY);
            int[] line = scene.BackLine;
            scene.Place(1, line[0], 1030, Mid + 60);
            scene.Place(1, line[1], 1030, Mid + 120);
            scene.Place(1, line[2], 1000, Mid + 220);
            if (fullBackInTheLane) scene.Place(1, line[3], (ballX + strikerX) / 2, (ballY + strikerY) / 2);
            else scene.Place(1, line[3], 1000, Mid + 300);
            scene.GiveBall(scene.Carrier);

            scene.Act(scene.Carrier);
            DecisionOption chosen = scene.Decisions.Records[scene.Decisions.Records.Count - 1].Chosen;
            TestContext.Out.WriteLine($"[cross] chose {chosen.Kind} to slot {chosen.MateSlot} at ({chosen.XDm},{chosen.YDm})");
            Assert.That(scene.BallOwned, Is.False, "he plays it");
            receiver = scene.Receiver;

            peak = scene.BallZ;
            if (!flight) return;
            for (int tick = 0; tick < 4 * CrossScene.Cfg.TicksPerSecond; tick++)
            {
                scene.StepBall();
                if (scene.BallZ > peak) peak = scene.BallZ;
                int dx = scene.BallX - strikerX, dy = scene.BallY - strikerY;
                if (dx * dx + dy * dy <= 15 * 15) break;
            }

            TestContext.Out.WriteLine($"[cross] full-back in the lane {fullBackInTheLane}: peak {peak} dm");
        }

        // ---------------------------------------------------------------- headed attempts, in a match

        /// <summary>
        /// Every strike at goal made with the ball above the knee (a header) follows a team-mate's
        /// cross (a lofted cross, a corner or a crossed free kick), with nothing played between: over a
        /// handful of matches, headers happen, and none comes from anything else.
        /// </summary>
        [Test]
        public void InAMatch_HeadedAttempts_ComeFromCrosses()
        {
            int headed = 0, fromCross = 0, shots = 0;
            var stray = new List<string>();
            for (int m = 0; m < 6; m++)
            {
                var events = new BallEventLog();
                var engine = new MatchEngine { BallEventSink = events };
                MatchReport report = engine.Simulate(
                    LineupSelector.BestEleven(_league.Clubs[2 * m]),
                    LineupSelector.BestEleven(_league.Clubs[2 * m + 1]), new Pcg32((ulong)(1830 + m)));

                HeadedAttemptMetrics? h = HeadedAttemptAnalyzer.Analyze(report, events.Records, CrossScene.Cfg.StreamTicksPerFrame);
                Assert.That(h, Is.Not.Null);
                shots += h!.Shots;
                headed += h.Headed;
                fromCross += h.HeadedFromCross;
                stray.AddRange(h.Stray.Select(f => $"match {m}: header at frame {f} not off a cross"));
            }

            TestContext.Out.WriteLine($"[headers] {headed} headed of {shots} shots over 6 matches, {fromCross} off a cross");
            Assert.That(headed, Is.GreaterThan(0), "headers happen");
            Assert.That(stray, Is.Empty, string.Join("\n", stray));
            Assert.That(fromCross, Is.EqualTo(headed));
        }
    }
}
