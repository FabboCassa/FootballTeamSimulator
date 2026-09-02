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
    /// The measuring instrument of phase 0 (docs/engine/MATCH_ENGINE_PLAN.md).
    ///
    /// A harness reading is only worth having if the reading itself is right, and "the block is
    /// 51 m deep" is exactly the kind of claim that is easy to get wrong by a factor of ten and
    /// impossible to notice. So every figure is pinned here against a stream built by hand, with
    /// the expected value worked out on paper rather than recorded from a run — a test that just
    /// remembers what the code did would happily bless the same mistake forever.
    ///
    /// The last few tests run the REAL engine and check the invariants that must hold whatever the
    /// engine does: a stream comes back, it is the length it says it is, and the picture and the
    /// result agree on the score.
    /// </summary>
    [TestFixture]
    public class MatchAnalyzerTests
    {
        private const int Players = 11;
        private const int Keeper = 0;

        // ------------------------------------------------------------------ building a stream

        /// <summary>An empty stream: everybody on the centre spot, nobody on the ball.</summary>
        private static PositionStream NewStream(int ticks, int ticksPerMinute = 12)
        {
            var s = new PositionStream
            {
                TicksPerMinute = ticksPerMinute,
                PlayerCount = Players,
                LastTick = ticks - 1,
                BallXY = new int[ticks * 2],
                HomeXY = new int[ticks * Players * 2],
                AwayXY = new int[ticks * Players * 2],
                Owner = new int[ticks],
                HomePlayerIds = new int[Players],
                AwayPlayerIds = new int[Players],
                HomeShirts = new int[Players],
                AwayShirts = new int[Players]
            };

            // Keepers on their lines, everybody else on the centre spot, so a test that does not
            // care about shape still gets a well-formed match.
            for (int t = 0; t < ticks; t++)
            {
                Put(s, true, t, Keeper, 40, Pitch.CenterY);
                Put(s, false, t, Keeper, Pitch.LengthDm - 40, Pitch.CenterY);
                for (int i = 1; i < Players; i++)
                {
                    Put(s, true, t, i, Pitch.CenterX - 100, Pitch.CenterY);
                    Put(s, false, t, i, Pitch.CenterX + 100, Pitch.CenterY);
                }

                s.BallXY[t * 2] = Pitch.CenterX;
                s.BallXY[t * 2 + 1] = Pitch.CenterY;
            }

            return s;
        }

        private static void Put(PositionStream s, bool home, int tick, int slot, int x, int y)
        {
            int[] side = home ? s.HomeXY : s.AwayXY;
            side[(tick * Players + slot) * 2] = x;
            side[(tick * Players + slot) * 2 + 1] = y;
        }

        private static void Hold(PositionStream s, int tick, bool home, int slot) =>
            s.Owner[tick] = s.OwnerCode(home, slot);

        private static void Ball(PositionStream s, int tick, int x, int y)
        {
            s.BallXY[tick * 2] = x;
            s.BallXY[tick * 2 + 1] = y;
        }

        private static MatchReport Report(PositionStream s, int homeGoals = 0, int awayGoals = 0) =>
            new MatchReport { HomeGoals = homeGoals, AwayGoals = awayGoals, Positions = s };

        /// <summary>The ten outfielders of a shape whose numbers are worked out in the test below.</summary>
        private static readonly int[][] KnownShape =
        {
            new[] { 200, 100 }, new[] { 200, 200 }, new[] { 200, 400 }, new[] { 200, 550 },
            new[] { 350, 200 }, new[] { 350, 340 }, new[] { 350, 480 },
            new[] { 600, 150 }, new[] { 600, 340 }, new[] { 600, 520 }
        };

        private static void LayOutKnownShape(PositionStream s, int tick, bool home)
        {
            for (int i = 0; i < KnownShape.Length; i++)
            {
                int x = KnownShape[i][0], y = KnownShape[i][1];
                if (!home) { x = Pitch.LengthDm - x; y = Pitch.WidthDm - y; }
                Put(s, home, tick, i + 1, x, y);
            }

            Put(s, home, tick, Keeper, home ? 40 : Pitch.LengthDm - 40, Pitch.CenterY);
        }

        // ------------------------------------------------------------------ the basics

        [Test]
        public void Analyze_ReturnsNull_WhenThereIsNoStream()
        {
            Assert.That(MatchAnalyzer.Analyze(new MatchReport()), Is.Null);
        }

        [Test]
        public void Keeper_IsTheManWhoStaysNearestHisOwnGoal()
        {
            PositionStream s = NewStream(4);
            for (int t = 0; t < 4; t++)
            {
                // Slot 7 keeps goal for the home side, slot 3 for the away side.
                Put(s, true, t, Keeper, Pitch.CenterX, Pitch.CenterY);
                Put(s, true, t, 7, 45, Pitch.CenterY);
                Put(s, false, t, Keeper, Pitch.CenterX, Pitch.CenterY);
                Put(s, false, t, 3, Pitch.LengthDm - 45, Pitch.CenterY);
            }

            var analyzer = new MatchAnalyzer();
            analyzer.Measure(Report(s));

            Assert.That(analyzer.KeeperSlot(home: true), Is.EqualTo(7));
            Assert.That(analyzer.KeeperSlot(home: false), Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ passing

        [Test]
        public void Pass_IsCompleted_WhenTheNextManOnTheBallIsATeamMate()
        {
            PositionStream s = NewStream(20);
            s.Actions.Add(new BallAction(2, BallActionKind.Pass, true, 5, 6));
            Hold(s, 6, true, 6);

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PassesAttempted, Is.EqualTo(1));
            Assert.That(m.Home.PassesCompleted, Is.EqualTo(1));
            Assert.That(m.Home.PassAccuracyPercent, Is.EqualTo(100.0));
        }

        [Test]
        public void Pass_IsIncomplete_WhenAnOpponentGetsThereFirst()
        {
            PositionStream s = NewStream(20);
            s.Actions.Add(new BallAction(2, BallActionKind.Pass, true, 5, 6));
            Hold(s, 5, false, 4);      // cut out
            Hold(s, 9, true, 6);       // won back later: too late to count

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PassesAttempted, Is.EqualTo(1));
            Assert.That(m.Home.PassesCompleted, Is.EqualTo(0));
        }

        [Test]
        public void Pass_IsIncomplete_WhenTheBallLeavesThePitchFirst()
        {
            PositionStream s = NewStream(20);
            s.Actions.Add(new BallAction(2, BallActionKind.Pass, true, 5, 6));
            s.Actions.Add(new BallAction(4, BallActionKind.ThrowIn, false, 3, -1));
            Hold(s, 8, true, 6);       // the taker's team-mate collects, but the pass had gone out

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PassesCompleted, Is.EqualTo(0));
            Assert.That(m.Away.ThrowIns, Is.EqualTo(1));
        }

        [Test]
        public void Pass_IsIncomplete_WhenNobodyEverTouchesItAgain()
        {
            PositionStream s = NewStream(20);
            s.Actions.Add(new BallAction(2, BallActionKind.Pass, true, 5, 6));

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PassesCompleted, Is.EqualTo(0));
        }

        [Test]
        public void LongBallsAndCrosses_CountAsPassesAndAreAlsoCountedSeparately()
        {
            PositionStream s = NewStream(30);
            s.Actions.Add(new BallAction(1, BallActionKind.LongBall, true, 2, 9));
            Hold(s, 4, true, 9);
            s.Actions.Add(new BallAction(10, BallActionKind.Cross, true, 7, 9));
            Hold(s, 13, false, 4);

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PassesAttempted, Is.EqualTo(2));
            Assert.That(m.Home.PassesCompleted, Is.EqualTo(1));
            Assert.That(m.Home.LongBalls, Is.EqualTo(1));
            Assert.That(m.Home.Crosses, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ shooting

        [Test]
        public void Shot_IsOnTarget_WhenItBecomesAGoalOrASave()
        {
            PositionStream s = NewStream(40);
            s.Actions.Add(new BallAction(2, BallActionKind.Shot, true, 9, -1));
            s.Actions.Add(new BallAction(5, BallActionKind.Goal, true, 9, -1));
            s.Actions.Add(new BallAction(12, BallActionKind.Shot, false, 8, -1));
            s.Actions.Add(new BallAction(15, BallActionKind.Save, true, Keeper, -1));
            s.Actions.Add(new BallAction(22, BallActionKind.Shot, false, 10, -1));
            s.Actions.Add(new BallAction(25, BallActionKind.Miss, false, 10, -1));

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s, homeGoals: 1))!;
            Assert.That(m.Home.Shots, Is.EqualTo(1));
            Assert.That(m.Home.ShotsOnTarget, Is.EqualTo(1));
            Assert.That(m.Away.Shots, Is.EqualTo(2));
            Assert.That(m.Away.ShotsOnTarget, Is.EqualTo(1));
            Assert.That(m.StreamGoals, Is.EqualTo(1));
            Assert.That(m.GoalsAgree, Is.True);
        }

        [Test]
        public void GoalsDisagree_WhenTheStreamAndTheReportTellDifferentStories()
        {
            PositionStream s = NewStream(10);
            s.Actions.Add(new BallAction(2, BallActionKind.Goal, true, 9, -1));

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s, homeGoals: 2))!;
            Assert.That(m.GoalsAgree, Is.False);
        }

        // ------------------------------------------------------------------ possession and territory

        [Test]
        public void Possession_LooseBall_AndThirds_AreCountedOffTheStream()
        {
            PositionStream s = NewStream(10);
            for (int t = 0; t < 4; t++) Hold(s, t, true, 5);      // home holds 4 ticks
            for (int t = 4; t < 6; t++) Hold(s, t, false, 5);     // away holds 2
                                                                 // 4 ticks loose

            Ball(s, 0, 100, Pitch.CenterY);                       // home third  (< 350)
            Ball(s, 1, 100, Pitch.CenterY);
            Ball(s, 2, 500, Pitch.CenterY);                       // middle third
            for (int t = 3; t < 10; t++) Ball(s, t, 900, Pitch.CenterY);   // away third

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.PossessionTicks, Is.EqualTo(4));
            Assert.That(m.Away.PossessionTicks, Is.EqualTo(2));
            Assert.That(m.LooseTicks, Is.EqualTo(4));
            Assert.That(m.HomePossessionPercent, Is.EqualTo(100.0 * 4 / 6).Within(0.001));
            Assert.That(m.LoosePercent, Is.EqualTo(40.0).Within(0.001));
            Assert.That(m.HomeThirdTicks, Is.EqualTo(2));
            Assert.That(m.MiddleThirdTicks, Is.EqualTo(1));
            Assert.That(m.AwayThirdTicks, Is.EqualTo(7));
        }

        [Test]
        public void ABallHeldOnALineIsCounted_BecauseTheLawsCallThatOutOfPlay()
        {
            PositionStream s = NewStream(6);
            for (int t = 0; t < 6; t++) Hold(s, t, true, 5);
            Ball(s, 0, 400, 0);                    // on the touchline
            Ball(s, 1, 400, Pitch.WidthDm);        // on the other one
            Ball(s, 2, Pitch.LengthDm, 300);       // on the goal line
            Ball(s, 3, 400, 300);                  // safely inside
            Ball(s, 4, 400, 5);                    // inside, if only just
            Ball(s, 5, 0, 300);

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.BallOnLineWhileHeldTicks, Is.EqualTo(4));
        }

        [Test]
        public void ALooseBallOnALineIsNotCounted_BecauseNobodyIsHoldingIt()
        {
            PositionStream s = NewStream(3);
            Ball(s, 0, 400, 0);
            Ball(s, 1, 400, 0);
            Ball(s, 2, 400, 0);

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.BallOnLineWhileHeldTicks, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ ground covered

        [Test]
        public void GroundCovered_IsSummedPerPlayerAndReportedInKilometres()
        {
            PositionStream s = NewStream(3);
            // Slot 4 walks 300 dm then 400 dm at right angles: 70 m in all.
            Put(s, true, 0, 4, 100, 100);
            Put(s, true, 1, 4, 400, 100);
            Put(s, true, 2, 4, 400, 500);

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            // Nobody else moves, so the whole team's total is that one player's.
            Assert.That(m.Home.TeamDistanceKm, Is.EqualTo(0.070).Within(1e-9));
            Assert.That(m.Home.MaxPlayerDistanceKm, Is.EqualTo(0.070).Within(1e-9));
            Assert.That(m.Home.MinPlayerDistanceKm, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(m.Home.DistancePerPlayerKm(Players), Is.EqualTo(0.070 / Players).Within(1e-9));
        }

        // ------------------------------------------------------------------ shape

        [Test]
        public void Shape_IsMeasuredOnTheTenOutfielders_InMetres()
        {
            PositionStream s = NewStream(2);
            LayOutKnownShape(s, 1, home: true);
            for (int i = 0; i < Players; i++) Put(s, false, 1, i, 1000, 40);   // out of the way
            Hold(s, 1, true, 5);                                               // home in possession

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            ShapeMetrics shape = m.Home.Attacking;

            Assert.That(shape.Samples, Is.EqualTo(1));
            Assert.That(shape.WidthM, Is.EqualTo(45.0).Within(0.001));       // y 100..550
            Assert.That(shape.DepthM, Is.EqualTo(40.0).Within(0.001));       // x 200..600
            Assert.That(shape.BackLineSpreadM, Is.EqualTo(0.0).Within(0.001));   // four men on x=200
            Assert.That(shape.LargestLineGapM, Is.EqualTo(25.0).Within(0.001));  // 350 -> 600
            Assert.That(shape.NearestTeammateM, Is.EqualTo(14.7).Within(0.001));
            // mean x = (200*4 + 350*3 + 600*3) / 10 = 365 dm; mean y = 3280 / 10 = 328 dm
            Assert.That(shape.CentroidXM, Is.EqualTo(36.5).Within(0.001));
            Assert.That(shape.CentroidYM, Is.EqualTo(32.8).Within(0.001));

            // The side that does NOT have the ball is the one defending.
            Assert.That(m.Home.Defending.Samples, Is.EqualTo(0));
            Assert.That(m.Away.Defending.Samples, Is.EqualTo(1));
        }

        [Test]
        public void BackLineSpread_IsTheSpreadOfTheFourDeepestMen()
        {
            PositionStream s = NewStream(2);
            for (int i = 0; i < Players; i++) Put(s, false, 1, i, 1000, 40);
            Put(s, true, 1, Keeper, 40, Pitch.CenterY);

            // A back four that is not a line: 150, 200, 260, 300. Spread 150 dm = 15 m.
            int[] backs = { 150, 200, 260, 300 };
            for (int i = 0; i < 4; i++) Put(s, true, 1, i + 1, backs[i], 100 + i * 150);
            for (int i = 5; i <= 10; i++) Put(s, true, 1, i, 500 + i * 10, 200 + i * 20);

            Hold(s, 1, true, 5);
            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.Attacking.BackLineSpreadM, Is.EqualTo(15.0).Within(0.001));
        }

        [Test]
        public void OpponentProximity_CountsTheMenWithAnOpponentInsideThreeMetres()
        {
            PositionStream s = NewStream(2);
            LayOutKnownShape(s, 1, home: true);
            for (int i = 0; i < Players; i++) Put(s, false, 1, i, 1000, 40);

            // Three away men stand on three of our outfielders: 25 dm, 30 dm (the edge, which
            // counts) and 31 dm (which does not).
            Put(s, false, 1, 1, KnownShape[0][0] + 25, KnownShape[0][1]);
            Put(s, false, 1, 2, KnownShape[1][0], KnownShape[1][1] + 30);
            Put(s, false, 1, 3, KnownShape[2][0] + 31, KnownShape[2][1]);

            Hold(s, 1, true, 5);
            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.Attacking.WithinThreeMetresOfOpponentPercent, Is.EqualTo(20.0).Within(0.001));
        }

        [Test]
        public void BothSidesAreMeasuredFromTheirOwnGoal_SoMirroredShapesReadTheSame()
        {
            PositionStream s = NewStream(2);
            LayOutKnownShape(s, 1, home: true);
            LayOutKnownShape(s, 1, home: false);   // the same shape, rotated 180 degrees
            Hold(s, 1, true, 5);                   // home attacking, away defending

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            ShapeMetrics h = m.Home.Attacking, a = m.Away.Defending;

            Assert.That(a.WidthM, Is.EqualTo(h.WidthM).Within(0.001));
            Assert.That(a.DepthM, Is.EqualTo(h.DepthM).Within(0.001));
            Assert.That(a.BackLineSpreadM, Is.EqualTo(h.BackLineSpreadM).Within(0.001));
            Assert.That(a.LargestLineGapM, Is.EqualTo(h.LargestLineGapM).Within(0.001));
            Assert.That(a.NearestTeammateM, Is.EqualTo(h.NearestTeammateM).Within(0.001));
            Assert.That(a.CentroidXM, Is.EqualTo(h.CentroidXM).Within(0.001));
            Assert.That(a.CentroidYM, Is.EqualTo(h.CentroidYM).Within(0.001));
        }

        [Test]
        public void ShapeIsSampledOnlyWhileSomebodyHoldsTheBall()
        {
            PositionStream s = NewStream(5);
            for (int t = 0; t < 5; t++) LayOutKnownShape(s, t, home: true);
            Hold(s, 1, true, 5);
            Hold(s, 2, false, 5);
            // ticks 0, 3 and 4 are loose

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.Attacking.Samples, Is.EqualTo(1));
            Assert.That(m.Home.Defending.Samples, Is.EqualTo(1));
            Assert.That(m.LooseTicks, Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ restarts and duels

        [Test]
        public void RestartsAndDuels_AreCreditedToTheSideThatWonThem()
        {
            PositionStream s = NewStream(30);
            s.Actions.Add(new BallAction(1, BallActionKind.ThrowIn, true, 3, -1));
            s.Actions.Add(new BallAction(4, BallActionKind.Corner, true, 7, -1));
            s.Actions.Add(new BallAction(8, BallActionKind.GoalKick, false, Keeper, -1));
            s.Actions.Add(new BallAction(11, BallActionKind.Tackle, false, 4, -1));
            s.Actions.Add(new BallAction(14, BallActionKind.Interception, true, 6, -1));
            s.Actions.Add(new BallAction(17, BallActionKind.Clearance, false, 2, -1));
            s.Actions.Add(new BallAction(20, BallActionKind.Dribble, true, 10, -1));

            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;
            Assert.That(m.Home.ThrowIns, Is.EqualTo(1));
            Assert.That(m.Home.Corners, Is.EqualTo(1));
            Assert.That(m.Away.GoalKicks, Is.EqualTo(1));
            Assert.That(m.Away.TacklesWon, Is.EqualTo(1));
            Assert.That(m.Home.Interceptions, Is.EqualTo(1));
            Assert.That(m.Away.Clearances, Is.EqualTo(1));
            Assert.That(m.Home.Dribbles, Is.EqualTo(1));
            Assert.That(m.TotalThrowIns, Is.EqualTo(1));
            Assert.That(m.TotalCorners, Is.EqualTo(1));
        }

        [Test]
        public void OffsidesAndFouls_ReadZero_BecauseTheEngineHasNeitherYet()
        {
            PositionStream s = NewStream(5);
            MatchMetrics m = MatchAnalyzer.Analyze(Report(s))!;

            // Not a placeholder: this is the phase-0 reading that phase 5 has to move.
            Assert.That(m.TotalOffsides, Is.EqualTo(0));
            Assert.That(m.TotalFouls, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ against the real engine

        [Test]
        public void ARealMatch_IsMeasurable_AndItsPictureAgreesWithItsResult()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260902));
            var engine = new MatchEngine(new BalanceConfig());

            var analyzer = new MatchAnalyzer();
            for (ulong seed = 1; seed <= 8; seed++)
            {
                MatchReport report = engine.Simulate(
                    LineupSelector.BestEleven(league.Clubs[3]),
                    LineupSelector.BestEleven(league.Clubs[11]),
                    new Pcg32(seed));

                MatchMetrics? m = analyzer.Measure(report);
                Assert.That(m, Is.Not.Null);
                Assert.That(m!.GoalsAgree, Is.True,
                    $"seed {seed}: {m.StreamGoals} goals in the stream, {m.ReportGoals} in the report");
                Assert.That(m.Ticks, Is.EqualTo(report.Positions!.TickCount));
                Assert.That(m.Home.PossessionTicks + m.Away.PossessionTicks + m.LooseTicks,
                    Is.EqualTo(m.Ticks));
                Assert.That(m.HomeThirdTicks + m.MiddleThirdTicks + m.AwayThirdTicks,
                    Is.EqualTo(m.Ticks));
            }
        }

        [Test]
        public void ARealMatch_PutsAKeeperInEachGoal()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260902));
            Lineup home = LineupSelector.BestEleven(league.Clubs[0]);
            Lineup away = LineupSelector.BestEleven(league.Clubs[1]);
            MatchReport report = new MatchEngine().Simulate(home, away, new Pcg32(77));

            var analyzer = new MatchAnalyzer();
            analyzer.Measure(report);

            Assert.That(home.Slots[analyzer.KeeperSlot(home: true)].Role,
                Is.EqualTo(PositionRole.Goalkeeper));
            Assert.That(away.Slots[analyzer.KeeperSlot(home: false)].Role,
                Is.EqualTo(PositionRole.Goalkeeper));
        }

        [Test]
        public void Measuring_TheSameMatchTwice_GivesTheSameNumbers()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260902));
            MatchReport report = new MatchEngine().Simulate(
                LineupSelector.BestEleven(league.Clubs[5]),
                LineupSelector.BestEleven(league.Clubs[6]),
                new Pcg32(4242));

            var analyzer = new MatchAnalyzer();
            MatchMetrics a = analyzer.Measure(report)!;
            MatchMetrics b = analyzer.Measure(report)!;

            // Reusing an analyzer must not carry state from one match into the next.
            Assert.That(b.Home.PassesAttempted, Is.EqualTo(a.Home.PassesAttempted));
            Assert.That(b.Home.TeamDistanceKm, Is.EqualTo(a.Home.TeamDistanceKm).Within(1e-9));
            Assert.That(b.Home.Defending.WidthM, Is.EqualTo(a.Home.Defending.WidthM).Within(1e-9));
            Assert.That(b.LooseTicks, Is.EqualTo(a.LooseTicks));
        }
    }
}
