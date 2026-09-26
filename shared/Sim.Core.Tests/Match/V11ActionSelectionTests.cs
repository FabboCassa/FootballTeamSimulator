using System;
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

        // ---------------------------------------------------------------- R4: the open goal

        [Test]
        public void OpenGoal_Inside20m_IsTakenOn_EvenWhileHolding_AndEvenWithAManBetterPlaced()
        {
            V11Scene s = Scene(GoalX - 160, Mid - 40);
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
            V11Scene s = Scene(GoalX - 100, Mid + 30);
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
            V11Scene blocked = Scene(GoalX - 160, Mid);
            blocked.AddFoe(At(GoalX - 80, Mid + 10), keeper: false);
            Assert.That(_valuation.IsOpenGoal(blocked), Is.False, "a defender on the line to goal closes it");

            V11Scene far = Scene(GoalX - 230, Mid);
            Assert.That(_valuation.IsOpenGoal(far), Is.False, "23 m out is not R4's open goal");

            V11Scene keeperOnly = Scene(GoalX - 160, Mid);
            Assert.That(_valuation.IsOpenGoal(keeperOnly), Is.True, "the keeper alone does not close it");
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
        [TestCase(40)]
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
            close.AddFoe(At(GoalX - 70, Mid + 5), keeper: false);    // in the triangle: no open goal
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
        /// The scripted rule, on the pitch: over a few whole V11 matches, measured off the position
        /// stream by the harness's own <see cref="RealismAnalyzer"/>, the open goals are shot at
        /// within 1.5 s. The 1,000-match reading is <see cref="V11ActionHarnessTests"/>.
        /// </summary>
        [Test]
        public void InAMatch_OpenGoalsAreShotWithinOneAndAHalfSeconds()
        {
            League league = new LeagueGenerator().Generate(new Pcg32(20260611));
            var cfg = new BalanceConfig();
            cfg.Match.Brain = MatchBrainVersion.V11;
            var engine = new MatchEngine(cfg, applyCondition: true, applyMatchFatigue: true,
                applyPositioning: true, generatePositions: true);

            int chances = 0, shots = 0;
            for (ulong seed = 3400; seed < 3404; seed++)
            {
                MatchReport r = engine.Simulate(LineupSelector.BestEleven(league.Clubs[9]),
                    LineupSelector.BestEleven(league.Clubs[10]), new Pcg32(seed));
                RealismMetrics m = RealismAnalyzer.Analyze(r)!;
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

        [Test]
        public void ShotAppetite_PricesTheShot()
        {
            V11Scene keen = Scene(GoalX - 220, Mid);
            keen.ShotAppetitePercent = 120;
            V11Scene shy = Scene(GoalX - 220, Mid);
            shy.ShotAppetitePercent = 80;

            Assert.That(_valuation.ShotValue(keen), Is.GreaterThan(_valuation.ShotValue(shy)));
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
