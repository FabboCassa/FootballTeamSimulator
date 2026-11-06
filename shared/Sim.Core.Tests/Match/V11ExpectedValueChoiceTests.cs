using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R7 / believable-match R3 (issue #182): shot, pass (to feet, lead, through),
    /// long ball, cross and carry priced in one unit, the expected goal value of the possession.
    /// The scripted scenes of R3's acceptance, each over 64 seeded layouts. Home attacks toward
    /// x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11ExpectedValueChoiceTests
    {
        private const int TopSpeed = 75;   // dm/s
        private const int GoalX = Pitch.LengthDm;
        private const int Mid = Pitch.CenterY;
        private const int Seeds = 64;
        private const int GoalHalfWidth = 37;   // MovementGeometry's, which is internal
        private const int BoxDepth = 165;
        private const int BoxHalfWidth = 201;

        private MatchBalance _cfg = null!;
        private V11ActionValuation _valuation = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _valuation = new V11ActionValuation(_cfg);
        }

        private static PitchActor At(int x, int y) => new PitchActor(x, y, 0, 0, TopSpeed);

        /// <summary>A carrier of average ball skill with the keeper set on his line.</summary>
        private static V11Scene Scene(int ballX, int ballY)
        {
            var s = new V11Scene
            {
                BallXDm = ballX, BallYDm = ballY, Shooting = 60, Technique = 60, Passing = 60, CarryTouchDm = 100
            };
            s.AddFoe(At(GoalX - 5, Mid), keeper: true);
            return s;
        }

        private static bool IsBallToAMate(V11ActionKind kind) => kind == V11ActionKind.Pass || kind == V11ActionKind.Cross;

        private static double Distance(int ax, int ay, int bx, int by) =>
            Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));

        /// <summary>How far (x, y) is from the triangle the ball at (bx, by) makes with the posts; 0 inside it.</summary>
        private static double FromShotLane(int x, int y, int bx, int by)
        {
            int half = GoalHalfWidth;
            double best = double.MaxValue;
            for (int py = Mid - half; py <= Mid + half; py += 4)
                best = Math.Min(best, FromSegment(x, y, bx, by, GoalX, py));
            return best;
        }

        private static double FromSegment(int x, int y, int ax, int ay, int bx, int by)
        {
            double dx = bx - ax, dy = by - ay;
            double t = ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            return Distance(x, y, (int)(ax + t * dx), (int)(ay + t * dy));
        }

        // ---------------------------------------------------------------- R3's scripted scenes

        /// <summary>
        /// Free at 12 m or closer, central, keeper on his line: nobody within 3 m of him and nobody in
        /// the lane to the posts; the team-mates are no better placed than he is. He shoots, every time.
        /// </summary>
        [Test]
        public void FreeWithin12mCentral_KeeperOnHisLine_ShootsEveryTime()
        {
            var misses = new List<string>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(9100 + seed));
                int ballX = GoalX - rng.NextInt(60, 121), ballY = Mid + rng.NextInt(-60, 61);
                if (Distance(ballX, ballY, GoalX, Mid) > 120) ballX = GoalX - 90;
                V11Scene s = Scene(ballX, ballY);
                double fromGoal = Distance(ballX, ballY, GoalX, Mid);

                int foes = rng.NextInt(3, 7);
                for (int placed = 0, tries = 0; placed < foes && tries < 500; tries++)
                {
                    int fx = GoalX - rng.NextInt(10, 260), fy = Mid + rng.NextInt(-200, 201);
                    if (Distance(fx, fy, ballX, ballY) < 30 || FromShotLane(fx, fy, ballX, ballY) < 20) continue;
                    s.AddFoe(At(fx, fy), keeper: false);
                    placed++;
                }

                int mates = rng.NextInt(1, 4);
                for (int placed = 0, tries = 0; placed < mates && tries < 500; tries++)
                {
                    int mx = GoalX - rng.NextInt(60, 300), my = Mid + rng.NextInt(-250, 251);
                    if (Distance(mx, my, GoalX, Mid) < fromGoal + 20) continue;
                    s.AddMate(At(mx, my), keeper: false);
                    placed++;
                }

                V11Choice choice = _valuation.Choose(s, holding: false);
                if (choice.Kind != V11ActionKind.Shot)
                    misses.Add($"seed {seed}: ({ballX},{ballY}) chose {choice.Kind} {choice.ValuePer10k} over a shot worth {_valuation.ShotValue(s)}");
            }

            Assert.That(misses, Is.Empty, string.Join("\n", misses));
        }

        /// <summary>
        /// A defender square in his lane to goal, a stride or two in front of him, and a team-mate
        /// unmarked at 6 m or closer, central: the ball goes to the team-mate.
        /// </summary>
        [Test]
        public void ADefenderSquareInTheLane_AndAMateUnmarkedWithin6mCentral_IsPassedTo()
        {
            var misses = new List<string>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(9300 + seed));
                int side = seed % 2 == 0 ? 1 : -1;
                int ballX = GoalX - rng.NextInt(120, 181), ballY = Mid - side * rng.NextInt(0, 41);
                V11Scene s = Scene(ballX, ballY);

                // Square in front of him, on his line to the goal centre.
                int gap = rng.NextInt(12, 21);
                double toGoal = Distance(ballX, ballY, GoalX, Mid);
                int dx = (int)((GoalX - ballX) * gap / toGoal), dy = (int)((Mid - ballY) * gap / toGoal);
                s.AddFoe(At(ballX + dx, ballY + dy), keeper: false);
                s.PressurePermille = Math.Max(0, 1000 - 1000 * gap / _cfg.PressureRadiusDm);
                s.CarryKeepPermille = 500;

                int mateX = GoalX - rng.NextInt(35, 61), mateY = Mid + side * rng.NextInt(0, 31);
                s.AddMate(At(mateX, mateY), keeper: false);
                s.AddMate(At(ballX - rng.NextInt(80, 150), ballY - side * rng.NextInt(50, 150)), keeper: false);

                // The rest of the back line, well away from the free man.
                int others = rng.NextInt(1, 4);
                for (int placed = 0, tries = 0; placed < others && tries < 500; tries++)
                {
                    int fx = GoalX - rng.NextInt(20, 200), fy = Mid + rng.NextInt(-200, 201);
                    if (Distance(fx, fy, mateX, mateY) < 60 || FromSegment(fx, fy, ballX, ballY, mateX, mateY) < 40) continue;
                    s.AddFoe(At(fx, fy), keeper: false);
                    placed++;
                }

                V11Choice choice = _valuation.Choose(s, holding: false);
                if (!IsBallToAMate(choice.Kind) || choice.Mate != 0)
                    misses.Add($"seed {seed}: ({ballX},{ballY}) chose {choice.Kind} to {choice.Mate} worth {choice.ValuePer10k}");
            }

            Assert.That(misses, Is.Empty, string.Join("\n", misses));
        }

        /// <summary>
        /// 25 m out, nobody in the lane to the posts, the box crowded (its men marked), a side of
        /// normal shot appetite: he does not have a go in at least 70% of the layouts.
        /// </summary>
        [Test]
        public void From25m_ClearLane_CrowdedBox_NormalAppetite_MostlyWorksTheBall()
        {
            int shots = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Pcg32((ulong)(9500 + seed));
                int ballX = GoalX - rng.NextInt(240, 261), ballY = Mid + rng.NextInt(-50, 51);
                V11Scene s = Scene(ballX, ballY);
                Assert.That(s.ShotAppetitePercent, Is.EqualTo(100), "normal appetite");

                int defenders = rng.NextInt(6, 9);
                for (int placed = 0, tries = 0; placed < defenders && tries < 1000; tries++)
                {
                    int fx = GoalX - rng.NextInt(20, BoxDepth);
                    int fy = Mid + rng.NextInt(-BoxHalfWidth, BoxHalfWidth + 1);
                    if (FromShotLane(fx, fy, ballX, ballY) < 30) continue;
                    s.AddFoe(At(fx, fy), keeper: false);
                    placed++;

                    // Every other defender has a man of ours, and is on him.
                    if (placed % 2 == 0) s.AddMate(At(fx - 8, fy + 8), keeper: false);
                }

                s.AddMate(At(ballX - rng.NextInt(60, 120), ballY + rng.NextInt(-150, 151)), keeper: false);
                if (_valuation.Choose(s, holding: false).Kind == V11ActionKind.Shot) shots++;
            }

            TestContext.Out.WriteLine($"[R3 25 m] shot in {shots}/{Seeds}");
            Assert.That(shots, Is.LessThanOrEqualTo(Seeds * 3 / 10));
        }

        // ---------------------------------------------------------------- the unit

        [Test]
        public void AShot_IsItsXg_LessTheBlocks_PlusWhatAMissLeaves()
        {
            V11Scene s = Scene(GoalX - 140, Mid);
            int xg = ExpectedGoals.Permille(s.BallXDm, s.BallYDm, true, 0, s.Shooting, s.Technique, _cfg.ActionModels) * 10;
            int expected = xg + (10_000 - xg) * _valuation.MissValue(s, s.BallXDm, s.BallYDm) / 10_000;
            Assert.That(_valuation.ShotValue(s), Is.EqualTo(expected), "no body in the way: the xG and the follow-up");

            V11Scene blocked = Scene(GoalX - 140, Mid);
            blocked.AddFoe(At(GoalX - 80, Mid), keeper: false);
            Assert.That(_valuation.ShotValue(blocked), Is.LessThan(expected * 3 / 4), "a man square on the line charges the strikes at the middle of the goal down");

            V11Scene shoulder = Scene(GoalX - 140, Mid);
            shoulder.AddFoe(At(GoalX - 150, Mid + 30), keeper: false);
            int pressure = 1000 - 1000 * 32 / _cfg.PressureRadiusDm;
            shoulder.PressurePermille = pressure;
            int pressed = ExpectedGoals.Permille(s.BallXDm, s.BallYDm, true, pressure, s.Shooting, s.Technique, _cfg.ActionModels) * 10;
            Assert.That(_valuation.ShotValue(shoulder), Is.EqualTo(pressed + (10_000 - pressed) * _valuation.MissValue(s, s.BallXDm, s.BallYDm) / 10_000),
                "a man at his shoulder, behind the ball, is the pressure the xG reads, never a block");
        }

        [Test]
        public void AMiss_PaysTheBallItLoses_AsAPassDoes()
        {
            V11Scene s = Scene(GoalX - 280, Mid);
            int threat = ExpectedThreat.ValuePer10k(s.BallXDm, s.BallYDm, true, _cfg.ActionModels);
            int theirs = ExpectedThreat.ValuePer10k(GoalX, Mid, false, _cfg.ActionModels);
            int lost = theirs * _cfg.V11LossWeightPercent / 100;
            int expected = _cfg.V11ShotMissCornerPer10k + threat * _cfg.V11ShotMissKeptPercent / 100
                           - lost * _cfg.V11ShotMissLostPercent / 100;
            Assert.That(_valuation.MissValue(s, s.BallXDm, s.BallYDm), Is.EqualTo(expected));
            Assert.That(expected, Is.LessThan(0), "from long range a miss costs the side more than it leaves");
        }

        [Test]
        public void AShotWorthLessThanThePossession_IsNotTaken_EvenWhenNothingElseIsOn()
        {
            // 28 m out with nobody to pass to and the way blocked: the shot beats the only other
            // option there is, but it ends a possession worth more than the shot.
            V11Scene s = Scene(GoalX - 280, Mid);
            s.AddFoe(At(GoalX - 262, Mid), keeper: false);
            s.GoalSideBlocked = true;
            int shot = _valuation.ShotValue(s);
            int threat = ExpectedThreat.ValuePer10k(s.BallXDm, s.BallYDm, true, _cfg.ActionModels);
            Assert.That(shot, Is.LessThan(threat));
            Assert.That(_valuation.Choose(s, holding: false).Kind, Is.Not.EqualTo(V11ActionKind.Shot));

            V11Scene near = Scene(GoalX - 90, Mid);
            Assert.That(_valuation.Choose(near, holding: false).Kind, Is.EqualTo(V11ActionKind.Shot),
                "9 m out the shot is worth more than the threat of the spot");
        }

        [Test]
        public void TheOldShotMultipliers_AreGoneFromTheConfig()
        {
            string[] removed =
            {
                "V11ShotFromDistancePercent", "V11ShotValuePercent", "V11ShotBlockerPercent", "V11ShotCloseBlockerPercent"
            };
            foreach (string name in removed)
                Assert.That(typeof(MatchBalance).GetProperty(name), Is.Null, name + " is no decision multiplier any more");
        }

        [Test]
        public void ABallToAManInFrontOfGoal_IsWorthHisShot_NotJustTheThreatOfHisSpot()
        {
            int Ball(int finishing)
            {
                // Nobody near him: the odds are the same, and 3 m out his shot can beat the threat of the spot.
                var s = new V11Scene { BallXDm = GoalX - 200, BallYDm = Mid + 100, Shooting = 60, Technique = 60, Passing = 60, CarryTouchDm = 100 };
                s.AddMate(At(GoalX - 30, Mid), keeper: false, shooting: finishing, technique: finishing);
                return _valuation.PassValue(s, out _);
            }

            Assert.That(Ball(90), Is.GreaterThan(Ball(20)), "the threat of the spot is the same: the better finisher's shot is dearer");
        }

        // ---------------------------------------------------------------- the option kinds

        [Test]
        public void TheOptionKinds_NameLeadAndThroughBalls_LongBalls_AndCrosses()
        {
            Assert.That(Kind(V11ActionKind.Pass, V11PassShape.Feet, false), Is.EqualTo(V11OptionKind.Pass));
            Assert.That(Kind(V11ActionKind.Pass, V11PassShape.Lead, false), Is.EqualTo(V11OptionKind.LeadPass));
            Assert.That(Kind(V11ActionKind.Pass, V11PassShape.Through, false), Is.EqualTo(V11OptionKind.ThroughBall));
            Assert.That(Kind(V11ActionKind.Pass, V11PassShape.Feet, true), Is.EqualTo(V11OptionKind.LongBall));
            Assert.That(Kind(V11ActionKind.Cross, V11PassShape.Feet, true), Is.EqualTo(V11OptionKind.Cross));
            Assert.That(Kind(V11ActionKind.Shot, V11PassShape.Feet, false), Is.EqualTo(V11OptionKind.Shot));
            Assert.That(Kind(V11ActionKind.Carry, V11PassShape.Feet, false), Is.EqualTo(V11OptionKind.Carry));

            static V11OptionKind Kind(V11ActionKind kind, V11PassShape shape, bool longBall) =>
                new V11Choice { Kind = kind, Shape = shape, LongBall = longBall }.Option;
        }

        [Test]
        public void ABallOverThirtyMetres_IsPricedAsALongBall()
        {
            V11Scene s = Scene(300, Mid);
            s.AddMate(At(300 + _cfg.LongBallFromDm + 80, Mid + 60), keeper: false);
            _valuation.PassValue(s, out V11Choice pass);

            Assert.That(pass.Mate, Is.Zero);
            Assert.That(pass.Option, Is.EqualTo(V11OptionKind.LongBall));
        }

        [Test]
        public void TheDecisionSink_RecordsTheNewOptionKinds()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20261024));
            var log = new DecisionLog();
            var engine = new MatchEngine(new BalanceConfig()) { DecisionSink = log };
            engine.Simulate(LineupSelector.BestEleven(league.Clubs[2]), LineupSelector.BestEleven(league.Clubs[7]), new Pcg32(4141));

            var chosen = new HashSet<V11OptionKind>(log.Records.Select(r => r.Chosen.Option));
            var offered = new HashSet<V11OptionKind>(log.Records.SelectMany(r =>
                Enumerable.Range(0, r.OptionCount).Select(i => r.Option(i).Option)));
            TestContext.Out.WriteLine($"chosen: {string.Join(", ", chosen)} | offered: {string.Join(", ", offered)}");

            foreach (V11OptionKind kind in new[] { V11OptionKind.Pass, V11OptionKind.LeadPass, V11OptionKind.LongBall, V11OptionKind.Shot, V11OptionKind.Carry })
                Assert.That(chosen, Does.Contain(kind), kind + " is chosen in a match");
            Assert.That(offered.Contains(V11OptionKind.ThroughBall) || chosen.Contains(V11OptionKind.ThroughBall), Is.True, "through balls are priced");
            Assert.That(chosen.Contains(V11OptionKind.Cross) || offered.Contains(V11OptionKind.Cross), Is.True, "crosses are priced");

            // The flat dump carries each option's kind at the end of its record.
            int[] flat = log.Flat();
            for (int r = 0; r < log.Records.Count; r++)
            {
                int tail = (r + 1) * DecisionLog.Stride - 4;
                Assert.That(flat[tail], Is.EqualTo((int)log.Records[r].Chosen.Option));
                for (int i = 0; i < log.Records[r].OptionCount; i++)
                    Assert.That(flat[tail + 1 + i], Is.EqualTo((int)log.Records[r].Option(i).Option));
            }
        }
    }
}
