using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Match.Movement.Models;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Issue #206 (free-ball spec R7/R8): the central zone near goal is defended. A pass into it is
    /// met by the defender nearest its arrival point, and the centre-backs hold the zone rather than
    /// follow a man out of it. Each scene runs on a simulator set up for a real match (private, so
    /// through reflection, as in <see cref="V11LeadPassTests"/>): every man but the scene's heaped
    /// in the home corner, the scene's defenders moved by their brain each tick, the ball stepped
    /// and control resolved as the match does. Home attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11ZoneGuardTests
    {
        private const int UnitsPerDm = 16;
        private const int Mid = Pitch.CenterY;
        private const int Seeds = 64;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // The two centre-backs in place in front of their goal, 8.5 m off the line.
        private const int BackX = 965;
        private static readonly int[] BackY = { Mid - 55, Mid + 55 };

        // The away midfield in front of them, between the ball and the zone.
        private const int MidfieldX = 820;
        private static readonly int[] MidfieldY = { Mid - 90, Mid + 90, Mid };

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261105));
        }

        [Test]
        public void AReceiverArrivingInTheCentralZone_IsWithin3mOfADefender_ByArrival()
        {
            int pressed = 0;
            var scene = new ZoneScene();
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                scene.Reset(seed);
                // Arrival points 12-16 m from the goal centre, across the width of the zone; the
                // receiver arrives from 7 m short of his, onto a pass played into space.
                int ax = 900 + (int)(seed % 4) * 10, ay = Mid - 60 + (int)(seed % 5) * 30;
                scene.Place(scene.Passer, 770, Mid + 30);
                scene.Place(scene.Receiver, ax - 70, ay);
                for (int i = 0; i < 2; i++) scene.Place(scene.CentreBacks[i], BackX, BackY[i]);
                for (int i = 0; i < scene.Midfield.Length; i++) scene.Place(scene.Midfield[i], MidfieldX, MidfieldY[i]);
                scene.Place(scene.Keeper, Pitch.LengthDm - 10, Mid);
                // In place: 3 s with the ball at the passer's feet and the receiver standing, the
                // centre-backs and the midfield taking their places in the block by their brain.
                scene.Hold(scene.Passer);
                scene.Settle(3 * Cfg.TicksPerSecond, scene.Receiver, scene.Defenders);
                scene.Pass(ax, ay);

                int gap = scene.GapAtFirstTouch(scene.Receiver, scene.Defenders);
                if (gap <= 30) pressed++;
            }

            TestContext.Out.WriteLine($"[zone-guard] receiver within 3 m of a defender on arrival {pressed}/{Seeds}");
            Assert.That(pressed, Is.GreaterThanOrEqualTo(Seeds * 9 / 10), "a defender meets him as the ball does");
        }

        [Test]
        public void ACentreBack_DoesNotLeaveTheZone_ToFollowAForwardDroppingDeep_WhileTheBallIsWide()
        {
            int farthest = 0;
            var scene = new ZoneScene();
            for (ulong seed = 0; seed < 8; seed++)
            {
                scene.Reset(seed);
                scene.Place(scene.Passer, 880, Mid + 260);
                scene.Hold(scene.Passer);
                for (int i = 0; i < 2; i++) scene.Place(scene.CentreBacks[i], BackX, BackY[i]);
                // The man nearest the wide ball is a midfielder, who goes to it: the centre-backs' job is the zone.
                for (int i = 0; i < scene.Midfield.Length; i++) scene.Place(scene.Midfield[i], 850, Mid + 120 + 50 * i);
                scene.Place(scene.Keeper, Pitch.LengthDm - 10, Mid);

                // The striker drops from between the centre-backs to 25 m out, at a jog, over 4 s.
                farthest = Math.Max(farthest, scene.FollowDrop(scene.Receiver, from: 960, to: 800, Mid, scene.CentreBacks, scene.Defenders));
            }

            TestContext.Out.WriteLine(
                $"[zone-guard] farthest centre-back from the goal centre {farthest} dm (zone {Cfg.V11ZoneGuardRangeDm}, striker to 25 m out)");
            Assert.That(farthest, Is.LessThanOrEqualTo(Cfg.V11ZoneGuardRangeDm), "the centre-backs stay inside the zone they guard");
        }

        [Test]
        public void ACarrierNearingTheCentralZone_IsClosedTighterThanOneFarOut()
        {
            // The man on the ball held centrally at 30 m and at 15 m from the goal centre: the
            // goal-side presser jockeys off him far out but stops giving ground near the zone.
            var scene = new ZoneScene();
            int far = 0, near = 0;
            for (ulong seed = 0; seed < 8; seed++)
            {
                far += scene.PressGap(Pitch.LengthDm - 300, seed);
                near += scene.PressGap(Pitch.LengthDm - 150, seed);
            }

            far /= 8;
            near /= 8;
            TestContext.Out.WriteLine($"[zone-guard] presser off the carrier: {far} dm at 30 m, {near} dm at 15 m");
            Assert.That(near, Is.LessThanOrEqualTo(30), "near the zone the carrier is pressed (a defender within 3 m)");
            Assert.That(near, Is.LessThan(far), "and closer than he is far out");
        }

        [Test]
        public void AManGoalSideOfTheCarrier_CapsHisRun_OnlyInFrontOfGoal()
        {
            var scene = new ZoneScene();
            scene.Reset(1);
            int carrier = scene.Passer, man = scene.CentreBacks[0];
            int goal = Pitch.LengthDm, near = Cfg.V11CarrierBlockedDm - 5;

            int Top(int x, int y, int foeX, int foeY)
            {
                scene.Place(carrier, x, y);
                scene.Place(man, foeX, foeY);
                scene.Hold(carrier);
                return scene.CarrierTop(carrier, 1000);
            }

            int capped = 1000 * Cfg.V11CarrierBlockedSpeedPercent / 100;
            Assert.That(Top(goal - 250, Mid, goal - 250 + near, Mid), Is.EqualTo(capped), "goal-side, 25 m out: his run stops");
            Assert.That(Top(goal - 250, Mid, goal - 250 - near, Mid), Is.EqualTo(1000), "behind him: he runs on");
            Assert.That(Top(goal - 250, Mid, goal - 250, Mid + near), Is.EqualTo(1000), "beside him: he runs on");
            Assert.That(Top(goal - 250, Mid, goal - 250 + Cfg.V11CarrierBlockedDm + 20, Mid), Is.EqualTo(1000), "out of reach: he runs on");
            Assert.That(Top(goal - Cfg.V11CarrierBlockedRangeDm - 50, Mid, goal - Cfg.V11CarrierBlockedRangeDm - 50 + near, Mid), Is.EqualTo(1000),
                "far from goal it is the press's business, not the zone's");

            Assert.That(scene.CarrierTop(man, 1000), Is.EqualTo(1000), "a man without the ball is not a carrier");
        }

        [Test]
        public void AManForcingHimInside_DoesNotStopHisRun_AManForcingHimOutsideDoes()
        {
            int Top(PressDirection direction)
            {
                var scene = new ZoneScene(direction);
                scene.Reset(1);
                int carrier = scene.Passer, man = scene.CentreBacks[0];
                int x = Pitch.LengthDm - 250;
                scene.Place(carrier, x, Mid);
                scene.Place(man, x + Cfg.V11CarrierBlockedDm - 5, Mid);
                scene.Hold(carrier);
                return scene.CarrierTop(carrier, 1000);
            }

            int capped = 1000 * Cfg.V11CarrierBlockedSpeedPercent / 100;
            Assert.That(Top(PressDirection.None), Is.EqualTo(capped), "no press direction: his run stops");
            Assert.That(Top(PressDirection.ForceOutside), Is.EqualTo(capped), "forced outside: his run stops");
            Assert.That(Top(PressDirection.ForceInside), Is.EqualTo(1000), "forced inside: the side lets him come");
        }

        [Test]
        public void ABlockedCarrier_ReleasesDuringHisHold_AndAFreeOneRunsOn()
        {
            var scene = new ZoneScene();
            int released = 0, freeReleased = 0;
            for (ulong seed = 0; seed < 8; seed++)
            {
                if (scene.ReleasesDuringHold(seed, Cfg.V11CarrierBlockedDm - 15)) released++;
                if (scene.ReleasesDuringHold(seed, -(Cfg.V11CarrierBlockedDm - 15))) freeReleased++;
            }

            TestContext.Out.WriteLine($"[zone-guard] released in the hold: blocked {released}/8, man behind him {freeReleased}/8");
            Assert.That(released, Is.EqualTo(8), "a man goal-side of him: he plays the free man");
            Assert.That(freeReleased, Is.Zero, "nobody in front of him: the hold runs");
        }

        [Test]
        public void ABlockedCarrier_HasNoCarryNorClearance_ButStillPassOrShot()
        {
            var valuation = new V11ActionValuation(Cfg);
            var scene = new V11Scene
            {
                BallXDm = Pitch.LengthDm - 250,
                BallYDm = Mid,
                Shooting = 60,
                Technique = 60,
                Passing = 60,
                CarryTouchDm = 100,
                CanClear = true,
                ClearXDm = Pitch.LengthDm - 60,
                ClearYDm = 60
            };
            scene.AddFoe(new PitchActor(Pitch.LengthDm - 5, Mid, 0, 0, 75), keeper: true);
            scene.AddMate(new PitchActor(Pitch.LengthDm - 300, Mid + 200, 0, 0, 75), keeper: false);

            Assert.That(valuation.CarryValue(scene, out _, out _), Is.GreaterThan(V11ActionValuation.NoOption), "free, he can carry");
            Assert.That(valuation.ClearValue(scene), Is.GreaterThan(V11ActionValuation.NoOption), "and hoof it");
            Assert.That(valuation.Choose(scene, holding: true).Kind, Is.EqualTo(V11ActionKind.Hold), "and sits out his hold");

            scene.GoalSideBlocked = true;
            Assert.That(valuation.CarryValue(scene, out _, out _), Is.EqualTo(V11ActionValuation.NoOption));
            Assert.That(valuation.ClearValue(scene), Is.EqualTo(V11ActionValuation.NoOption));
            V11Choice held = valuation.Choose(scene, holding: true);
            Assert.That(held.Kind, Is.EqualTo(V11ActionKind.Pass), "blocked, the free man gets it during the hold");
            Assert.That(held.Release, Is.False, "and that is not R9's pressed release");
            Assert.That(valuation.Choose(scene, holding: false).Kind, Is.AnyOf(V11ActionKind.Pass, V11ActionKind.Shot));
        }

        [Test]
        public void ADefendersLooseFirstTouch_IsFiled_SoTheFeedSaysWhoPutTheBallOut()
        {
            var scene = new ZoneScene();
            int loose = 0;
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                scene.Reset(seed);
                int defender = scene.CentreBacks[0];
                scene.Place(scene.Passer, 600, Mid);
                scene.Place(defender, 700, Mid);
                if (!scene.StrikeAtHisFeet(defender)) continue;

                loose++;
                Assert.That(scene.FiledFor(defender), Is.True, "seed " + seed + ": his touch is the feed's last word on the ball");
            }

            TestContext.Out.WriteLine($"[zone-guard] loose first touches filed {loose}/{Seeds}");
            Assert.That(loose, Is.GreaterThan(0), "sanity: a ball that fast comes off some of his touches loose");
        }

        /// <summary>
        /// A home central midfielder and the home striker against the away centre-backs, central
        /// midfielders and keeper; every other man heaped in the home corner, standing.
        /// </summary>
        private sealed class ZoneScene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private readonly object _brain;
            private readonly BallEventLog _log = new BallEventLog();
            private readonly int _n;
            private int _tick;

            public ZoneScene(PressDirection awayDirection = PressDirection.None)
            {
                _sim = new MatchSimulator(Cfg);
                Lineup home = LineupSelector.BestEleven(_league.Clubs[2]), away = LineupSelector.BestEleven(_league.Clubs[5]);
                var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
                Set(_sim, "_rng", new Pcg32(206));
                typeof(MatchSimulator).GetMethod("Setup", Any)!.Invoke(_sim, new object?[] { home, away, report, awayDirection == PressDirection.None ? null : AwayPressing(awayDirection), 90 * Cfg.TicksPerMinute });
                _ball = Get(_sim, "_ball");
                _brain = Get(_sim, "_brain");
                _n = Field<int[]>("_px").Length / 2;
                Passer = 0 * _n + Slot(home, PositionRole.CentralMidfielder, PositionRole.DefensiveMidfielder, PositionRole.AttackingMidfielder);
                Receiver = 0 * _n + Slot(home, PositionRole.Striker, PositionRole.Winger, PositionRole.AttackingMidfielder);
                CentreBacks = Enumerable.Range(0, away.Slots.Count).Where(i => away.Slots[i].Role == PositionRole.CentreBack)
                    .Take(2).Select(i => _n + i).ToArray();
                Assert.That(CentreBacks, Has.Length.EqualTo(2), "two centre-backs");
                Keeper = _n + Slot(away, PositionRole.Goalkeeper);
                Midfield = Enumerable.Range(0, away.Slots.Count)
                    .Where(i => away.Slots[i].Role == PositionRole.CentralMidfielder || away.Slots[i].Role == PositionRole.DefensiveMidfielder)
                    .Take(MidfieldY.Length).Select(i => _n + i).ToArray();
                Defenders = CentreBacks.Concat(Midfield).ToArray();
            }

            private static MatchTactics AwayPressing(PressDirection direction)
            {
                var instructions = new TacticInstructions(
                    Mentality.Balanced, PassingDirectness.Mixed, Tempo.Normal, Width.Normal,
                    BuildUp.Mixed, FinalThirdApproach.Mixed, ShotSelection.Normal, DribbleFrequency.Normal,
                    OnBallLost.Normal, OnBallWon.Normal, LineOfEngagement.Mid, DefensiveLine.Normal,
                    Pressing.Medium, MarkingScheme.Mixed, direction);
                return new MatchTactics(
                    TacticContext.Neutral(new BalanceConfig().Tactics.FamiliarityMax),
                    new TacticContext(new Tactic(Formation.F433, instructions), new BalanceConfig().Tactics.FamiliarityMax));
            }

            // Men as k = side * n + slot.
            public int Passer { get; }
            public int Receiver { get; }
            public int[] CentreBacks { get; }
            public int Keeper { get; }
            public int[] Midfield { get; }
            public int[] Defenders { get; }

            public void Reset(ulong seed)
            {
                Set(_sim, "_rng", new Pcg32(seed));
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py"), vx = Field<int[]>("_vx"), vy = Field<int[]>("_vy");
                bool[] sentOff = Field<bool[]>("_sentOff");
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = 50 * UnitsPerDm;
                    py[i] = 50 * UnitsPerDm;
                    vx[i] = 0;
                    vy[i] = 0;
                    sentOff[i] = false;
                }

                int[] receiver = Field<int[]>("_receiver");
                receiver[0] = -1;
                receiver[1] = -1;
                Set(_sim, "_releasedBy", -1);
                Set(_sim, "_releasedUntil", -1);
                Set(_sim, "_closeIn", -1);
                object ctx = Get(_sim, "_ctx");
                ctx.GetType().GetField("ShotLive")!.SetValue(ctx, false);
                object offside = Get(_sim, "_offside");
                offside.GetType().GetMethod("ClearFlags")!.Invoke(offside, null);
                Set(_ball, "Dead", false);
                Set(_ball, "EventSink", _log);
                ((PositionStream)Get(_sim, "_stream")).Actions.Clear();
                _tick = 1;
            }

            public void Place(int k, int xDm, int yDm)
            {
                Field<int[]>("_px")[k] = xDm * UnitsPerDm;
                Field<int[]>("_py")[k] = yDm * UnitsPerDm;
                Field<int[]>("_vx")[k] = 0;
                Field<int[]>("_vy")[k] = 0;
            }

            /// <summary>The man has the ball at his feet.</summary>
            public void Hold(int k)
            {
                PutBallAt(k);
                Set(_ball, "OwnerSide", k / _n);
                Set(_ball, "OwnerSlot", k % _n);
            }

            /// <summary>
            /// The passer strikes a ground pass to (x, y) with the weight the brain gives a pass there,
            /// into space: the receiver runs onto it at its aim, as the brain plays a lead pass.
            /// </summary>
            public void Pass(int xDm, int yDm)
            {
                PutBallAt(Passer);
                int bx = Field<int[]>("_px")[Passer], by = Field<int[]>("_py")[Passer];
                Set(_ball, "OwnerSide", 0);
                Set(_ball, "OwnerSlot", Passer % _n);
                int tx = xDm * UnitsPerDm, ty = yDm * UnitsPerDm;
                int distance = (int)Math.Round(Math.Sqrt((double)(tx - bx) * (tx - bx) + (double)(ty - by) * (ty - by)));
                object[] force = { distance, Get(_sim, "_arrivalStepU"), Get(_sim, "_maxPassForce"), 0 };
                int f0 = (int)_ball.GetType().GetMethod("ForceToArrive")!.Invoke(_ball, force)!;

                Type choiceType = typeof(MatchSimulator).GetNestedType("PassChoice", BindingFlags.NonPublic)!;
                object choice = Activator.CreateInstance(choiceType)!;
                choiceType.GetField("Slot")!.SetValue(choice, Receiver % _n);
                choiceType.GetField("X")!.SetValue(choice, tx);
                choiceType.GetField("Y")!.SetValue(choice, ty);
                choiceType.GetField("Force")!.SetValue(choice, f0);
                choiceType.GetField("Completion")!.SetValue(choice, 1000);
                choiceType.GetField("RunOn")!.SetValue(choice, true);
                typeof(MatchSimulator).GetMethod("PlayPass", Any)!.Invoke(_sim, new[] { _tick, 0, Passer % _n, choice, 0 });
                ((List<BallEvent>)Get(_log, "_events")).Clear();
            }

            /// <summary>
            /// The passer strikes it along the ground at full force at this opponent, who cannot help
            /// meeting it. True when his first touch left it loose (it is free and off him).
            /// </summary>
            public bool StrikeAtHisFeet(int defender)
            {
                PutBallAt(Passer);
                object[] kick = { 0, Passer % _n, 1, 0, Get(_sim, "_maxPassForce") };
                _ball.GetType().GetMethod("Kick")!.Invoke(_ball, kick);
                ((List<BallEvent>)Get(_log, "_events")).Clear();
                for (int i = 0; i < Cfg.TicksPerSecond; i++)
                {
                    Step(Array.Empty<int>());
                    if (_log.Records.Any(e => e.Kind == BallEventKind.Touch && e.Side == defender / _n && e.Slot == defender % _n))
                        return (bool)_ball.GetType().GetProperty("Free")!.GetValue(_ball)!;
                }

                return false;
            }

            /// <summary>The simulator's speed cap for this man (see MatchSimulator.CarrierTop), from a top speed.</summary>
            public int CarrierTop(int k, int top) =>
                (int)typeof(MatchSimulator).GetMethod("CarrierTop", Any)!.Invoke(_sim, new object[] { k, top })!;

            /// <summary>
            /// The man on the ball 25 m out with a long hold still to run, a free team-mate wide of him
            /// and a centre-back standing <paramref name="offsetDm"/> goal-side of him (behind him when
            /// negative): whether, by his brain and his feet held still, he plays it inside 1.5 s.
            /// </summary>
            public bool ReleasesDuringHold(ulong seed, int offsetDm)
            {
                Reset(seed);
                int x = Pitch.LengthDm - 250;
                Place(Passer, x, Mid);
                Place(Receiver, x - 40, Mid + 200);
                Place(CentreBacks[0], x + offsetDm, Mid);
                Place(Keeper, Pitch.LengthDm - 10, Mid);
                Hold(Passer);
                Field<int[]>("_hold")[Passer] = 10 * Cfg.TicksPerSecond;
                MethodInfo update = _brain.GetType().GetMethod("UpdateTeams")!;
                MethodInfo act = _brain.GetType().GetMethod("Act")!;
                List<BallAction> actions = ((PositionStream)Get(_sim, "_stream")).Actions;
                for (int i = 0; i < 3 * Cfg.TicksPerSecond / 2; i++)
                {
                    _tick++;
                    Set(_ball, "EventTick", _tick);
                    update.Invoke(_brain, new object[] { _tick });
                    act.Invoke(_brain, new object[] { _tick, Passer / _n, Passer % _n });
                    if (actions.Any(a => a.Kind == BallActionKind.Pass)) return true;
                }

                return false;
            }

            /// <summary>Whether the last action in the feed is this man's.</summary>
            public bool FiledFor(int k)
            {
                List<BallAction> actions = ((PositionStream)Get(_sim, "_stream")).Actions;
                return actions.Count > 0 && actions[actions.Count - 1].Home == (k / _n == 0) && actions[actions.Count - 1].Slot == k % _n;
            }

            /// <summary>
            /// Ticks the scene until somebody but the passer touches the ball and returns the gap (dm)
            /// from the receiver to the nearest defender then; 0 when a defender is first to it, and
            /// int.MaxValue if nobody touches it in 8 s. The receiver and the defenders move by their brain.
            /// </summary>
            public int GapAtFirstTouch(int receiver, int[] defenders)
            {
                int[] movers = defenders.Append(receiver).ToArray();
                for (int i = 0; i < 8 * Cfg.TicksPerSecond; i++)
                {
                    Step(movers);
                    BallEvent touch = _log.Records.FirstOrDefault(e => e.Kind == BallEventKind.Touch || e.Kind == BallEventKind.Deflection);
                    if (touch.Kind == BallEventKind.None)
                    {
                        if ((bool)Get(_ball, "Dead")) return int.MaxValue;
                        continue;
                    }

                    if (touch.Side == 1) return 0;
                    int[] px = Field<int[]>("_px"), py = Field<int[]>("_py");
                    return defenders.Min(d => (int)Math.Round(Math.Sqrt(
                        Math.Pow((px[d] - px[receiver]) / (double)UnitsPerDm, 2) + Math.Pow((py[d] - py[receiver]) / (double)UnitsPerDm, 2))));
                }

                return int.MaxValue;
            }

            /// <summary>
            /// The passer on the ball at (x, centre) for 3 s with the away centre-backs and midfield
            /// moving by their brain; the gap (dm) from him to the nearest of them at the end.
            /// </summary>
            public int PressGap(int xDm, ulong seed)
            {
                Reset(seed);
                Place(Passer, xDm, Mid);
                for (int i = 0; i < 2; i++) Place(CentreBacks[i], BackX, BackY[i]);
                for (int i = 0; i < Midfield.Length; i++) Place(Midfield[i], MidfieldX, MidfieldY[i]);
                Place(Keeper, Pitch.LengthDm - 10, Mid);
                Hold(Passer);
                Settle(3 * Cfg.TicksPerSecond, Passer, Defenders);
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py");
                return Defenders.Min(d => (int)Math.Round(Math.Sqrt(
                    Math.Pow((px[d] - px[Passer]) / (double)UnitsPerDm, 2) + Math.Pow((py[d] - py[Passer]) / (double)UnitsPerDm, 2))));
            }

            /// <summary>
            /// Ticks the scene with the ball held still at the passer's feet and <paramref name="still"/>
            /// kept where he stands; the defenders move by their brain.
            /// </summary>
            public void Settle(int ticks, int still, int[] defenders)
            {
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py");
                int x = px[still] / UnitsPerDm, y = py[still] / UnitsPerDm;
                for (int i = 0; i < ticks; i++)
                {
                    Place(still, x, y);
                    Step(defenders);
                    PutBallAt(Passer);
                }

                Place(still, x, y);
            }

            /// <summary>
            /// The forward is walked from x = <paramref name="from"/> to <paramref name="to"/> over 4 s
            /// (scripted, not by his brain) with the ball held still by the passer; the movers move by
            /// their brain. Returns the farthest (dm) any of the defenders got from the centre of his goal.
            /// </summary>
            public int FollowDrop(int forward, int from, int to, int y, int[] defenders, int[] movers)
            {
                int ticks = 4 * Cfg.TicksPerSecond, farthest = 0;
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py");
                for (int i = 0; i <= ticks; i++)
                {
                    Place(forward, from + (to - from) * i / ticks, y);
                    Step(movers);
                    PutBallAt(Passer);
                    foreach (int d in defenders)
                    {
                        double gx = Pitch.LengthDm - px[d] / (double)UnitsPerDm, gy = py[d] / (double)UnitsPerDm - Mid;
                        farthest = Math.Max(farthest, (int)Math.Round(Math.Sqrt(gx * gx + gy * gy)));
                    }
                }

                return farthest;
            }

            private void Step(int[] movers)
            {
                MethodInfo update = _brain.GetType().GetMethod("UpdateTeams")!;
                MethodInfo move = _brain.GetType().GetMethod("Move")!;
                MethodInfo advance = _ball.GetType().GetMethod("Advance")!;
                MethodInfo resolve = typeof(MatchSimulator).GetMethod("ResolveControl", Any)!;
                _tick++;
                Set(_ball, "EventTick", _tick);
                update.Invoke(_brain, new object[] { _tick });
                foreach (int m in movers) move.Invoke(_brain, new object[] { _tick, m / _n, m % _n });
                Set(_sim, "_ballTwoAgoX", Get(_sim, "_ballAgoX"));
                Set(_sim, "_ballTwoAgoY", Get(_sim, "_ballAgoY"));
                Set(_sim, "_ballAgoX", Get(_ball, "X"));
                Set(_sim, "_ballAgoY", Get(_ball, "Y"));
                advance.Invoke(_ball, null);
                resolve.Invoke(_sim, new object[] { _tick });
            }

            private void PutBallAt(int k)
            {
                int bx = Field<int[]>("_px")[k], by = Field<int[]>("_py")[k];
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, bx);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, by);
                Set(_ball, "Vx", 0);
                Set(_ball, "Vy", 0);
                Set(_ball, "_z", 0);
                Set(_ball, "_vz", 0);
                Set(_ball, "LastTouchSide", k / _n);
                Set(_ball, "LastTouchSlot", k % _n);
                foreach (string f in new[] { "_ballAgoX", "_ballTwoAgoX" }) Set(_sim, f, bx);
                foreach (string f in new[] { "_ballAgoY", "_ballTwoAgoY" }) Set(_sim, f, by);
            }

            private static int Slot(Lineup lineup, params PositionRole[] roles)
            {
                foreach (PositionRole role in roles)
                    for (int i = 0; i < lineup.Slots.Count; i++)
                        if (lineup.Slots[i].Role == role) return i;
                throw new InvalidOperationException("no man in these roles");
            }

            private T Field<T>(string name) => (T)Get(_sim, name);

            private static object Get(object o, string name) => o.GetType().GetField(name, Any)!.GetValue(o)!;

            private static void Set(object o, string name, object? value) => o.GetType().GetField(name, Any)!.SetValue(o, value);
        }
    }
}
