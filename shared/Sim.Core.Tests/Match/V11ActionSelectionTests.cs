using System;
using System.Threading.Tasks;
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
    /// R3-R5, the V11 brain's choice on the ball (watchable-match spec, task 7), on scripted
    /// positions. Pass, cross, carry, clear and shot are priced in one currency — ten-thousandths
    /// of a goal: xT for moving the ball, pitch control for the odds it comes off, xG for the
    /// shot — and the instructions weigh them. Home attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11ActionSelectionTests
    {
        private const int TopSpeed = 75;   // dm/s
        private const int GoalX = Pitch.LengthDm;
        private const int Mid = Pitch.CenterY;

        private MatchBalance _cfg = null!;
        private V11ActionValuation _valuation = null!;

        [SetUp]
        public void Fresh()
        {
            _cfg = new BalanceConfig().Match;
            _valuation = new V11ActionValuation(_cfg);
        }

        private static PitchActor At(int x, int y) => new PitchActor(x, y, 0, 0, TopSpeed);

        private static V11Scene Scene(int ballX, int ballY, TacticInstructions? instructions = null)
        {
            var s = new V11Scene
            {
                BallXDm = ballX,
                BallYDm = ballY,
                Shooting = 60,
                Technique = 60,
                Passing = 60,
                CarryTouchDm = 100,
                Instructions = instructions ?? TacticInstructions.Neutral
            };
            s.AddFoe(At(GoalX - 5, Mid), keeper: true);
            return s;
        }

        /// <summary>A scene with the keeper stranded 15 m out and wide of his goal (R10: beaten).</summary>
        private static V11Scene Stranded(int ballX, int ballY)
        {
            V11Scene s = Scene(ballX, ballY);
            s.Reset();
            s.AddFoe(At(GoalX - 150, Mid + 150), keeper: true);
            return s;
        }

        // ---------------------------------------------------------------- R4: the open goal

        [Test]
        public void OpenGoal_Inside20m_IsTakenOn_EvenWhileHolding_AndEvenWithAManBetterPlaced()
        {
            V11Scene s = Stranded(GoalX - 160, Mid - 40);
            s.AddFoe(At(GoalX - 250, Mid), keeper: false);        // beaten: behind the ball
            s.AddFoe(At(GoalX - 120, Mid + 200), keeper: false);  // wide of the posts' triangle
            s.AddMate(At(GoalX - 60, Mid + 10), keeper: false);   // free on the six-yard line

            Assert.That(_valuation.IsOpenGoal(s), Is.True);

            // From 16 m: one touch in at the goal the moment he sees it, through the hold...
            V11Choice first = _valuation.Choose(s, holding: true);
            Assert.That(first.Kind, Is.EqualTo(V11ActionKind.Drive), "an open goal is gone at, not passed up");
            Assert.That(Distance(first.XDm, first.YDm), Is.LessThan(Distance(s.BallXDm, s.BallYDm)), "toward goal");

            // ...and the shot on his next decision, whatever it is worth against the pass.
            s.BallXDm = first.XDm;
            s.BallYDm = first.YDm;
            s.OpenGoalForMs = 600;
            Assert.That(_valuation.Choose(s, holding: true).Kind, Is.EqualTo(V11ActionKind.Shot));
        }

        [Test]
        public void OpenGoal_CloseIn_IsShotOnSight()
        {
            V11Scene s = Stranded(GoalX - 100, Mid + 30);
            s.AddFoe(At(GoalX - 180, Mid), keeper: false);
            s.AddMate(At(GoalX - 50, Mid - 20), keeper: false);

            Assert.That(_valuation.Choose(s, holding: true).Kind, Is.EqualTo(V11ActionKind.Shot));
        }

        [Test]
        public void OpenGoal_WithTheKeeperBeatenToo_IsShotAtOnce()
        {
            V11Scene s = Scene(GoalX - 120, Mid);
            s.Reset();
            s.AddFoe(At(GoalX - 200, Mid + 60), keeper: true);    // stranded behind the play
            s.AddFoe(At(GoalX - 230, Mid), keeper: false);

            Assert.That(_valuation.Choose(s, holding: true).Kind, Is.EqualTo(V11ActionKind.Shot));
        }

        [Test]
        public void ADefenderInTheTriangle_OrBeyond20m_IsNoOpenGoal()
        {
            V11Scene blocked = Stranded(GoalX - 160, Mid);
            blocked.AddFoe(At(GoalX - 80, Mid + 10), keeper: false);
            Assert.That(_valuation.IsOpenGoal(blocked), Is.False, "a defender on the line to goal closes it");

            V11Scene far = Stranded(GoalX - 230, Mid);
            Assert.That(_valuation.IsOpenGoal(far), Is.False, "23 m out is not R4's open goal");

            V11Scene keeperOnly = Stranded(GoalX - 160, Mid);
            Assert.That(_valuation.IsOpenGoal(keeperOnly), Is.True, "a stranded keeper alone does not close it");
        }

        [Test]
        public void AnEmptySliverByTheByline_IsNoOpenGoal_TheMouthMustBeInView()
        {
            // 18 m from the goal centre, but two metres off the byline: the posts are almost in line.
            V11Scene byline = Stranded(GoalX - 20, Mid + 180);
            Assert.That(_valuation.IsOpenGoal(byline), Is.False);

            V11Scene inFront = Stranded(GoalX - 180, Mid + 20);
            Assert.That(_valuation.IsOpenGoal(inFront), Is.True, "the same distance with the goal in view is open");
        }

        [Test]
        public void ADefenderAStrideFromTheLane_OrOnTheBall_ClosesTheOpenGoal()
        {
            // From 16 m straight on, 6 m in front of goal the lane is (37 * 6 / 16) = 14 dm either side.
            int margin = _cfg.V11OpenGoalLaneMarginDm;
            V11Scene beside = Stranded(GoalX - 160, Mid);
            beside.AddFoe(At(GoalX - 60, Mid + 14 + margin / 2), keeper: false);
            Assert.That(_valuation.IsOpenGoal(beside), Is.False, "a man a stride from the line of the shot blocks it");

            V11Scene wide = Stranded(GoalX - 160, Mid);
            wide.AddFoe(At(GoalX - 60, Mid + 14 + margin * 2), keeper: false);
            Assert.That(_valuation.IsOpenGoal(wide), Is.True, "a man well wide of it does not");

            V11Scene onHim = Stranded(GoalX - 160, Mid);
            onHim.AddFoe(At(GoalX - 160 - _cfg.V11OpenGoalFreeRadiusDm * 3 / 4, Mid + 5), keeper: false);
            Assert.That(_valuation.IsOpenGoal(onHim), Is.False, "a man on the ball closes it");
        }

        [Test]
        public void AKeeperInTheWay_ClosesTheShortcut_OneStrandedOrOutOnTheBall_DoesNot()
        {
            V11Scene set = Scene(GoalX - 160, Mid);
            Assert.That(_valuation.IsOpenGoal(set), Is.False, "a keeper set between the posts is no open goal");

            V11Scene stranded = Scene(GoalX - 160, Mid);
            stranded.Reset();
            stranded.AddFoe(At(GoalX - 40, Mid + 37 + _cfg.V11OpenGoalKeeperMarginDm * 2), keeper: true);
            Assert.That(_valuation.IsOpenGoal(stranded), Is.True, "a keeper out by the post leaves the goal open");

            V11Scene midway = Scene(GoalX - 160, Mid);
            midway.Reset();
            midway.AddFoe(At(GoalX - 80, Mid + 10), keeper: true);
            Assert.That(_valuation.IsOpenGoal(midway), Is.False, "a keeper off his line but still in the way covers it");

            V11Scene onHim = Scene(GoalX - 160, Mid);
            onHim.Reset();
            onHim.AddFoe(At(GoalX - 160 + _cfg.V11OpenGoalKeeperOneOnOneDm - 5, Mid), keeper: true);
            Assert.That(_valuation.IsOpenGoal(onHim), Is.True, "a keeper come right out onto him is committed: round him or past him");
        }

        /// <summary>
        /// R10: from outside the box, with a defender in the lane to goal and a team-mate free in the
        /// box, the play is worked on — a ball to him or a run at the box — not a shot into the man.
        /// </summary>
        [Test]
        public void OutsideTheBox_ABlockerInTheLane_AndAManFreeInTheBox_IsPassedOrCarried()
        {
            int cases = 0, worked = 0;
            foreach (int depth in new[] { 170, 190, 210, 230 })
            foreach (int across in new[] { -80, 0, 80 })
            foreach (int mateY in new[] { Mid - 50, Mid + 60 })
            {
                int ballX = GoalX - depth, ballY = Mid + across;
                V11Scene s = Scene(ballX, ballY);
                s.AddFoe(At((ballX + GoalX) / 2, (ballY + Mid) / 2), keeper: false);   // square in the lane
                s.AddMate(At(GoalX - 80, mateY), keeper: false);                        // free at the penalty spot

                V11ActionKind kind = _valuation.Choose(s, holding: false).Kind;
                cases++;
                if (kind == V11ActionKind.Pass || kind == V11ActionKind.Cross || kind == V11ActionKind.Carry) worked++;
            }

            TestContext.Out.WriteLine($"[R10] worked {worked}/{cases}");
            Assert.That(worked, Is.GreaterThanOrEqualTo(cases * 7 / 10));
        }

        [Test]
        public void ABodyInTheWay_CutsTheShot_ButTheManOnHimIsOnlyPressure_UnlessHeIsSquareInTheLane()
        {
            int Shot(int? foeX, int foeY = Mid)
            {
                V11Scene s = Scene(GoalX - 160, Mid);
                if (foeX.HasValue) s.AddFoe(At(foeX.Value, foeY), keeper: false);
                return _valuation.ShotValue(s);
            }

            int clear = Shot(null);
            int onHim = _cfg.PressureRadiusDm / 2;
            Assert.That(Shot(GoalX - 60), Is.EqualTo(clear * _cfg.V11ShotBlockerPercent / 100), "a blocker 10 m ahead");
            Assert.That(Shot(GoalX - 160, Mid + _cfg.PressureRadiusDm - 8), Is.EqualTo(clear),
                "the man on him at his shoulder is the pressure the xG reads, not a second discount");
            Assert.That(Shot(GoalX - 160 + onHim), Is.EqualTo(clear * _cfg.V11ShotCloseBlockerPercent / 100),
                "R10: the man on him square in the line of it charges it down");
        }

        [Test]
        public void TheLossWeight_PricesTheBallThatCanBeLost()
        {
            int Risky(int lossWeight)
            {
                _cfg.V11LossWeightPercent = lossWeight;
                V11Scene s = Scene(300, Mid);
                s.AddMate(At(520, Mid), keeper: false);
                s.AddFoe(At(420, Mid + 40), keeper: false);
                return new V11ActionValuation(_cfg).PassValue(s, out _);
            }

            Assert.That(Risky(300), Is.LessThan(Risky(100)));
        }

        [Test]
        public void AKeeperOnTheBall_NeverShoots_AtTheOtherGoal()
        {
            V11Scene s = Scene(GoalX - 150, Mid);
            s.CarrierIsKeeper = true;

            Assert.That(_valuation.Choose(s, holding: false).Kind, Is.Not.EqualTo(V11ActionKind.Shot));
        }

        // ---------------------------------------------------------------- R4: the 1v1

        /// <summary>
        /// A scripted 1v1, played decision by decision: each touch in or round the keeper moves the
        /// ball where it was aimed and the keeper holds his ground. It must end in a shot, with a
        /// dribble round him on the way if he is on top of him — never in a pass back or a stall.
        /// </summary>
        [TestCase(30)]   // inside V11OpenGoalKeeperOneOnOneDm (35): further off, still in the lane, he is not beaten (R10)
        [TestCase(20)]
        [TestCase(10)]
        public void OneVersusOne_EndsInAShot_OrADribbleRoundTheKeeper(int keeperGapDm)
        {
            int ballX = GoalX - 150;
            V11Scene s = Scene(ballX, Mid);
            s.Reset();
            s.AddFoe(At(ballX + keeperGapDm, Mid), keeper: true);    // come off his line to meet him
            s.AddFoe(At(ballX - 60, Mid + 20), keeper: false);       // chasing, a stride behind
            s.AddMate(At(ballX - 150, Mid + 150), keeper: false);    // the only other option: back

            bool roundedHim = false;
            for (int decision = 0; decision < 3; decision++)
            {
                V11Choice choice = _valuation.Choose(s, holding: true);
                if (choice.Kind == V11ActionKind.Shot) return;

                Assert.That(choice.Kind, Is.EqualTo(V11ActionKind.Drive).Or.EqualTo(V11ActionKind.RoundKeeper),
                    "a 1v1 is gone at: a touch in, or round the keeper");
                if (choice.Kind == V11ActionKind.RoundKeeper)
                {
                    Assert.That(roundedHim, Is.False, "he goes round him once");
                    Assert.That(choice.XDm, Is.GreaterThan(ballX + keeperGapDm), "past the keeper");
                    Assert.That(Math.Abs(choice.YDm - Mid), Is.GreaterThanOrEqualTo(_cfg.V11RoundKeeperSideDm / 2),
                        "round him, not through him");
                    roundedHim = true;
                }

                s.BallXDm = choice.XDm;
                s.BallYDm = choice.YDm;
                s.OpenGoalForMs += 600;
            }

            Assert.Fail("the 1v1 was not finished with a shot");
        }

        [Test]
        public void OneVersusOne_TheKeeperRightOnHim_IsDribbledRound_AndOneFarOff_IsShotPast()
        {
            int ballX = GoalX - 110;
            V11Scene close = Scene(ballX, Mid);
            close.Reset();
            close.AddFoe(At(ballX + 12, Mid), keeper: true);
            Assert.That(_valuation.Choose(close, holding: false).Kind, Is.EqualTo(V11ActionKind.RoundKeeper));

            V11Scene far = Scene(ballX, Mid);
            Assert.That(_valuation.Choose(far, holding: false).Kind, Is.EqualTo(V11ActionKind.Shot));
        }

        private static double Distance(int x, int y) => Math.Sqrt((double)(x - GoalX) * (x - GoalX) + (double)(y - Mid) * (y - Mid));

        // ---------------------------------------------------------------- R3: one currency

        [Test]
        public void TheChoice_IsTheBestPricedOption_OutOfAllFive()
        {
            // Midfield, one free man ahead, one marked man ahead, one safe man square.
            V11Scene s = Scene(560, 300);
            s.AddFoe(At(700, 420), keeper: false);
            s.AddFoe(At(780, 470), keeper: false);
            s.AddFoe(At(600, 330), keeper: false);
            s.AddMate(At(760, 200), keeper: false);
            s.AddMate(At(740, 440), keeper: false);
            s.AddMate(At(520, 480), keeper: false);
            s.CarryKeepPermille = 600;                              // a man at his shoulder
            s.CanClear = true;
            s.ClearXDm = 950;
            s.ClearYDm = 60;

            int pass = _valuation.PassValue(s, out V11Choice bestPass);
            int carry = _valuation.CarryValue(s, out _, out _);
            int clear = _valuation.ClearValue(s);
            int shot = _valuation.ShotValue(s);

            V11Choice choice = _valuation.Choose(s, holding: false);
            int best = Math.Max(Math.Max(pass, carry), Math.Max(clear, shot));

            Assert.That(shot, Is.EqualTo(V11ActionValuation.NoOption), "no shot from 49 m");
            Assert.That(choice.ValuePer10k, Is.EqualTo(best), "he takes the dearest option");
            Assert.That(choice.Kind, Is.EqualTo(V11ActionKind.Pass));
            Assert.That(bestPass.Mate, Is.EqualTo(0), "the free man ahead, not the marked one or the square one");
        }

        [Test]
        public void AForwardPass_GainsThreat_AndAManInTheLane_CutsItsOdds()
        {
            V11Scene open = Scene(500, Mid);
            int free = open.AddMate(At(750, Mid), keeper: false);
            Assert.That(free, Is.Zero);
            int openValue = _valuation.PassValue(open, out V11Choice openPass);

            V11Scene cut = Scene(500, Mid);
            cut.AddMate(At(750, Mid), keeper: false);
            cut.AddFoe(At(620, Mid + 10), keeper: false);            // in the lane
            int cutValue = _valuation.PassValue(cut, out V11Choice cutPass);

            Assert.That(openPass.SafetyPermille, Is.GreaterThan(cutPass.SafetyPermille), "pitch control reads the lane");
            Assert.That(openValue, Is.GreaterThan(cutValue));
            Assert.That(openValue, Is.GreaterThan(ExpectedThreat.ValuePer10k(500, Mid, true, _cfg.ActionModels)),
                "a safe ball 25 m forward is worth more than keeping it where it is");
        }

        [Test]
        public void ACloseCentralChance_IsShot_AndThe30mOne_IsWorked()
        {
            V11Scene close = Scene(GoalX - 90, Mid);
            close.AddFoe(At(GoalX - 30, Mid + 5), keeper: false);    // in the triangle: no open goal, but not on him
            close.AddMate(At(GoalX - 250, Mid + 150), keeper: false);
            Assert.That(_valuation.IsOpenGoal(close), Is.False);
            Assert.That(_valuation.Choose(close, holding: false).Kind, Is.EqualTo(V11ActionKind.Shot));

            V11Scene far = Scene(GoalX - 290, Mid);
            far.AddFoe(At(GoalX - 200, Mid - 20), keeper: false);
            far.AddMate(At(GoalX - 180, Mid + 120), keeper: false);
            Assert.That(_valuation.Choose(far, holding: false).Kind, Is.Not.EqualTo(V11ActionKind.Shot));
        }

        [Test]
        public void FromTheWideChannel_ABallIntoTheBox_IsACross()
        {
            V11Scene s = Scene(GoalX - 180, 60);
            s.AddFoe(At(GoalX - 150, 50), keeper: false);            // shows him down the line
            s.AddFoe(At(GoalX - 40, Mid + 120), keeper: false);
            s.AddMate(At(GoalX - 100, Mid + 30), keeper: false);     // at the far post
            s.AddMate(At(GoalX - 320, 120), keeper: false);          // the ball back
            s.CarryKeepPermille = 500;

            V11Choice choice = _valuation.Choose(s, holding: false);

            Assert.That(choice.Kind, Is.EqualTo(V11ActionKind.Cross));
            Assert.That(choice.Mate, Is.Zero);
        }

        [Test]
        public void ClosedDownOnHisOwnBox_HeClearsIt()
        {
            V11Scene s = Scene(110, 200);
            s.AddFoe(At(130, 210), keeper: false);
            s.AddFoe(At(150, 160), keeper: false);
            s.AddFoe(At(170, 300), keeper: false);
            s.AddMate(At(250, 150), keeper: false);                  // marked
            s.AddFoe(At(260, 160), keeper: false);
            s.CarryKeepPermille = 300;
            s.PressurePermille = 900;
            s.CanClear = true;
            s.ClearXDm = 500;
            s.ClearYDm = 60;

            Assert.That(_valuation.Choose(s, holding: false).Kind, Is.EqualTo(V11ActionKind.Clear));
        }

        [Test]
        public void WhileHolding_WithNoOpenGoal_HeKeepsIt()
        {
            V11Scene s = Scene(500, Mid);
            s.AddMate(At(750, Mid), keeper: false);

            Assert.That(_valuation.Choose(s, holding: true).Kind, Is.EqualTo(V11ActionKind.Hold));
        }

        // ---------------------------------------------------------------- in a match

        /// <summary>
        /// The scripted rule, on the pitch: over 480 whole V11 matches, measured off the position
        /// stream by the harness's own <see cref="RealismAnalyzer"/>, the open goals are shot at
        /// within 1.5 s. The 1,000-match reading is <see cref="V11ActionHarnessTests"/>.
        /// </summary>
        [Test]
        public void InAMatch_OpenGoalsAreShotWithinOneAndAHalfSeconds()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            var cfg = new BalanceConfig();
            // Four matches (~24 chances) flipped with every tuning change; since R10 (#81) a genuinely
            // open goal is rarer (~0.4 a match), and this gives a couple of hundred again.
            const int Matches = 480;
            var metrics = new RealismMetrics[Matches];

            Parallel.For(0, Matches, i =>
            {
                var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                    applyPositioning: true, generatePositions: true);
                MatchReport r = engine.Simulate(LineupSelector.BestEleven(league.Clubs[9]),
                    LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(3400UL + (ulong)i));
                metrics[i] = RealismAnalyzer.Analyze(r)!;
            });

            int chances = 0, shots = 0;
            foreach (RealismMetrics m in metrics)
            {
                chances += m.OpenGoalChances;
                shots += m.OpenGoalShots;
            }

            TestContext.Out.WriteLine($"[V11 open goal] {shots}/{chances} shot within 1.5 s");
            Assert.That(chances, Is.GreaterThan(0));
            Assert.That((double)shots / chances, Is.GreaterThanOrEqualTo(0.90));
        }

        // ---------------------------------------------------------------- instructions

        private static TacticInstructions With(Mentality m, Tempo t, Width w = Width.Normal) =>
            new TacticInstructions(m, Pressing.Medium, t, w);

        /// <summary>
        /// R10: the shortcut is for a genuinely open goal within 20 m, whatever the side's shot
        /// appetite — a keen side's strike from further out is a priced shot, not a goal walked into.
        /// </summary>
        [Test]
        public void AKeenSide_GetsNoShortcutBeyond20m_NorPastASetKeeper()
        {
            V11Scene keenFar = Stranded(GoalX - 230, Mid);
            keenFar.ShotAppetitePercent = 125;
            Assert.That(_valuation.IsOpenGoal(keenFar), Is.False, "23 m is beyond the shortcut, keeper beaten or not");

            V11Scene keenNear = Scene(GoalX - 160, Mid);
            keenNear.ShotAppetitePercent = 125;
            Assert.That(_valuation.IsOpenGoal(keenNear), Is.False, "inside 20 m the set keeper closes it for anybody");

            V11Scene keenOpen = Stranded(GoalX - 160, Mid);
            keenOpen.ShotAppetitePercent = 125;
            Assert.That(_valuation.IsOpenGoal(keenOpen), Is.True, "16 m with the keeper stranded is open");
        }

        /// <summary>
        /// R10's shot selection from distance: from outside the box a clear sight is credited
        /// V11ShotFromDistancePercent of its value; a man in the lane takes that away as well as
        /// cutting the shot, so the ball goes to the man free in the box instead.
        /// </summary>
        [Test]
        public void FromOutsideTheBox_AClearSightIsWorthTheShot_AManInTheLaneIsNot()
        {
            int Shot(int ballX, bool blocker)
            {
                V11Scene s = Scene(ballX, Mid);
                if (blocker) s.AddFoe(At((ballX + GoalX) / 2, Mid), keeper: false);
                return _valuation.ShotValue(s);
            }

            int Plain(int ballX)
            {
                V11Scene s = Scene(ballX, Mid);
                int xg = ExpectedGoals.Permille(s.BallXDm, s.BallYDm, s.AttacksHighX, s.PressurePermille,
                    s.Shooting, s.Technique, _cfg.ActionModels);
                return xg * 10 * _cfg.V11ShotValuePercent / 100;
            }

            int far = Plain(GoalX - 200);
            Assert.That(Shot(GoalX - 200, blocker: false), Is.EqualTo(far * _cfg.V11ShotFromDistancePercent / 100),
                "a clear sight from 20 m");
            Assert.That(Shot(GoalX - 200, blocker: true), Is.EqualTo(far * _cfg.V11ShotBlockerPercent / 100),
                "a man in the lane: no credit for distance, and the block's cut");
            Assert.That(Shot(GoalX - 120, blocker: false), Is.EqualTo(Plain(GoalX - 120)), "inside the box the xG alone");
        }

        [Test]
        public void ShotAppetite_PricesTheShot()
        {
            V11Scene keen = Scene(GoalX - 220, Mid);
            keen.ShotAppetitePercent = 120;
            V11Scene shy = Scene(GoalX - 220, Mid);
            shy.ShotAppetitePercent = 80;

            Assert.That(_valuation.ShotValue(keen), Is.GreaterThan(_valuation.ShotValue(shy)));
        }

        /// <summary>
        /// Issue #66's long sight, priced since R10 (#81): a keen side credits a clear sight from
        /// outside the box with V11ShotAppetiteRangePercent of its appetite above 100 on top of the
        /// appetite itself; inside the box, and for a patient side, the appetite alone.
        /// </summary>
        [Test]
        public void AKeenSide_CreditsAClearSightFromDistance_BeyondItsAppetite()
        {
            double Ratio(int ballX, int appetite)
            {
                V11Scene s = Scene(ballX, Mid);
                s.ShotAppetitePercent = appetite;
                return (double)_valuation.ShotValue(s) / _valuation.ShotValue(Scene(ballX, Mid));
            }

            double reach = (100 + 25 * _cfg.V11ShotAppetiteRangePercent / 100) / 100.0;
            Assert.That(Ratio(GoalX - 230, 125), Is.EqualTo(1.25 * reach).Within(0.01), "23 m out, a clear sight");
            Assert.That(Ratio(GoalX - 120, 125), Is.EqualTo(1.25).Within(0.01), "inside the box: the appetite alone");
            Assert.That(Ratio(GoalX - 230, 80), Is.EqualTo(0.80).Within(0.01), "a patient side: the appetite alone");
        }

        [Test]
        public void Tempo_PricesTheGain_AndDirectness_TheLongBall()
        {
            int Forward(Tempo tempo, int mateX)
            {
                V11Scene s = Scene(400, Mid, With(Mentality.Balanced, tempo));
                s.AddMate(At(mateX, Mid), keeper: false);
                return _valuation.PassValue(s, out _);
            }

            Assert.That(Forward(Tempo.Fast, 560), Is.GreaterThan(Forward(Tempo.Normal, 560)));
            Assert.That(Forward(Tempo.Normal, 560), Is.GreaterThan(Forward(Tempo.Slow, 560)));

            // Past LongBallFromDm the fast side's extra appetite is larger than the gain weight alone.
            double shortRatio = (double)Forward(Tempo.Fast, 560) / Forward(Tempo.Normal, 560);
            double longRatio = (double)Forward(Tempo.Fast, 760) / Forward(Tempo.Normal, 760);
            Assert.That(longRatio, Is.GreaterThan(shortRatio));
        }

        [Test]
        public void Risk_PricesTheBallThatCanBeLost()
        {
            int Risky(Mentality mentality)
            {
                V11Scene s = Scene(300, Mid, With(mentality, Tempo.Normal));
                s.AddMate(At(520, Mid), keeper: false);
                s.AddFoe(At(420, Mid + 40), keeper: false);
                return _valuation.PassValue(s, out _);
            }

            Assert.That(Risky(Mentality.Attacking), Is.GreaterThan(Risky(Mentality.Balanced)));
            Assert.That(Risky(Mentality.Balanced), Is.GreaterThan(Risky(Mentality.Defensive)));
        }

        [Test]
        public void AShout_WeighsTheRisk_AndTheGain()
        {
            int Risky(int riskPercent, int gainPercent)
            {
                V11Scene s = Scene(300, Mid);
                s.AddMate(At(520, Mid), keeper: false);
                s.AddFoe(At(420, Mid + 40), keeper: false);
                s.RiskPercent = riskPercent;
                s.GainPercent = gainPercent;
                return _valuation.PassValue(s, out _);
            }

            Assert.That(Risky(150, 100), Is.LessThan(Risky(100, 100)), "a ball that can be lost is dearer");
            Assert.That(Risky(100, 75), Is.LessThan(Risky(100, 100)), "the threat it gains is worth less");
        }

        [Test]
        public void AtTheEndOfHisRun_HeDoesNotStandOnTheBall()
        {
            // The carry in the last stretch goes to a point 11 m out in front of goal; from that
            // point a carry would take him nowhere, and choosing it again and again stood a man on
            // the ball there for the rest of the half.
            V11Scene s = Scene(GoalX - _cfg.CarryGoalStandOffDm, Mid);
            s.AddFoe(At(GoalX - 55, Mid - 25), keeper: false);
            s.AddFoe(At(GoalX - 50, Mid + 25), keeper: false);

            Assert.That(_valuation.CarryValue(s, out _, out _), Is.EqualTo(V11ActionValuation.NoOption));
            Assert.That(_valuation.Choose(s, holding: false).Kind, Is.Not.EqualTo(V11ActionKind.Carry));
        }

        [Test]
        public void Width_PricesTheCross()
        {
            int Cross(Width width)
            {
                V11Scene s = Scene(GoalX - 180, 60, With(Mentality.Balanced, Tempo.Normal, width));
                s.AddMate(At(GoalX - 100, Mid + 30), keeper: false);
                _valuation.PassValue(s, out V11Choice c);
                Assert.That(c.Kind, Is.EqualTo(V11ActionKind.Cross));
                return c.ValuePer10k;
            }

            Assert.That(Cross(Width.Wide), Is.GreaterThan(Cross(Width.Normal)));
            Assert.That(Cross(Width.Normal), Is.GreaterThan(Cross(Width.Narrow)));
        }

        [Test]
        public void Instructions_CanTurnTheChoice()
        {
            // A risky ball forward against a safe one square: the defensive, slow side keeps it,
            // the attacking, fast side goes forward.
            V11Scene Make(TacticInstructions i)
            {
                V11Scene s = Scene(450, Mid, i);
                s.AddMate(At(700, Mid - 30), keeper: false);
                s.AddFoe(At(600, Mid + 60), keeper: false);          // could get across to the lane
                s.AddMate(At(430, Mid + 180), keeper: false);
                s.CarryKeepPermille = 500;
                // A passer at the trust mark, so his own skill adds no directness (R3) and the
                // instructions are the only thing that differs.
                s.Passing = _cfg.V11ShortGameTrustSkill;
                s.Technique = _cfg.V11ShortGameTrustSkill;
                return s;
            }

            V11Choice bold = _valuation.Choose(Make(With(Mentality.Attacking, Tempo.Fast)), holding: false);
            V11Choice careful = _valuation.Choose(Make(With(Mentality.Defensive, Tempo.Slow)), holding: false);

            Assert.That(bold.Kind, Is.EqualTo(V11ActionKind.Pass));
            Assert.That(bold.Mate, Is.EqualTo(0), "forward");
            Assert.That(careful.Kind, Is.EqualTo(V11ActionKind.Pass));
            Assert.That(careful.Mate, Is.EqualTo(1), "square");
        }
    }
}
