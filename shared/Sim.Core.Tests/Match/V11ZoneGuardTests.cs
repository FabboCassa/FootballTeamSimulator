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
using Sim.Core.Random;

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

            public ZoneScene()
            {
                _sim = new MatchSimulator(Cfg);
                Lineup home = LineupSelector.BestEleven(_league.Clubs[2]), away = LineupSelector.BestEleven(_league.Clubs[5]);
                var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
                Set(_sim, "_rng", new Pcg32(206));
                typeof(MatchSimulator).GetMethod("Setup", Any)!.Invoke(_sim, new object?[] { home, away, report, null, 90 * Cfg.TicksPerMinute });
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
