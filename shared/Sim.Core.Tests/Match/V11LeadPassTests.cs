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

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R4, issue #180: a pass can be aimed at a point ahead of a running receiver (a
    /// lead pass) or behind the defensive line (a through ball); the receiver runs onto it (a
    /// through ball at its aim, a lead pass where he meets it on his way), and an opponent nearer
    /// the arrival point gets there first. The aim is <see cref="V11PassTarget"/>'s,
    /// priced by <see cref="V11ActionValuation"/>; the run-on is the simulator's own: the pass is
    /// struck by the simulator (private, so through reflection) on a simulator set up for a real
    /// match, every man but the scene's heaped in a far corner, and each tick the scene's men move by
    /// their brain, the ball steps and control is resolved, as the match does. Home attacks toward
    /// x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11LeadPassTests
    {
        private const int UnitsPerDm = 16;
        private const int TopSpeed = 75;   // dm/s
        private const int Mid = Pitch.CenterY;
        private const int Seeds = 64;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261101));
        }

        // ---------------------------------------------------------------- the aim

        [Test]
        public void ALeadPass_IsAimedWhereARunningMansRunTakesHim_AndAStandingMansIsAheadOfHimTowardGoal()
        {
            V11Scene s = Scene(400, Mid);
            var running = new PitchActor(600, 200, 60, 0, TopSpeed);
            var standing = new PitchActor(600, 200, 0, 0, TopSpeed);

            Assert.That(V11PassTarget.Aim(s, running, false, V11PassShape.Lead, Cfg, out int rx, out int ry), Is.True);
            Assert.That(rx - running.XDm, Is.GreaterThanOrEqualTo(30), "at least 3 m ahead of him: into space");
            Assert.That(rx - running.XDm, Is.LessThanOrEqualTo(Cfg.PassRunLeadMaxDm), "no further than his run takes him");
            Assert.That(ry, Is.EqualTo(running.YDm), "along his run");

            Assert.That(V11PassTarget.Aim(s, standing, false, V11PassShape.Lead, Cfg, out int sx, out int sy), Is.True);
            Assert.That((sx, sy), Is.EqualTo((standing.XDm + Cfg.V11PassLeadDm, standing.YDm)), "a standing man: into his path toward goal");

            Assert.That(V11PassTarget.Aim(s, running, false, V11PassShape.Feet, Cfg, out int fx, out int fy), Is.True);
            Assert.That((fx, fy), Is.EqualTo((running.XDm, running.YDm)), "a ball to feet is to his feet");
            Assert.That(V11PassTarget.Aim(s, running, true, V11PassShape.Lead, Cfg, out _, out _), Is.False, "never ahead of his own keeper");
        }

        [Test]
        public void ALeadPass_ToAManRunningIntoSpace_IsThePriceOfChoice()
        {
            V11Scene s = Scene(400, Mid);
            s.AddMate(new PitchActor(600, 200, 60, 0, TopSpeed), keeper: false);

            new V11ActionValuation(Cfg).PassValue(s, out V11Choice best);

            Assert.That(best.Shape, Is.EqualTo(V11PassShape.Lead));
            Assert.That(best.XDm - 600, Is.GreaterThanOrEqualTo(30), "played ahead of him, not to where he was");
        }

        [Test]
        public void AThroughBall_IsAimedBehindTheLine_ForAnOnsideManOnItsShoulder_AndIsChosen()
        {
            V11Scene s = BackLine(passerX: 650);
            int striker = s.AddMate(new PitchActor(790, Mid, 0, 0, TopSpeed), keeper: false);

            Assert.That(V11PassTarget.Aim(s, s.Mates[striker], false, V11PassShape.Through, Cfg, out int tx, out _), Is.True);
            Assert.That(tx, Is.GreaterThan(s.OffsideLineXDm), "behind the line");

            new V11ActionValuation(Cfg).PassValue(s, out V11Choice best);
            Assert.That(best.Shape, Is.EqualTo(V11PassShape.Through));
            Assert.That(best.XDm, Is.GreaterThan(s.OffsideLineXDm));
            Assert.That(best.SafetyPermille, Is.GreaterThan(500), "the gap is wide enough and he is first to it");
        }

        [Test]
        public void NoThroughBall_ForAManFarBehindTheLine_OrWithTheBallAlreadyOnIt()
        {
            V11Scene s = BackLine(passerX: 650);
            var deep = new PitchActor(800 - Cfg.ThroughBallRunInDm - 20, Mid, 0, 0, TopSpeed);
            Assert.That(V11PassTarget.Aim(s, deep, false, V11PassShape.Through, Cfg, out _, out _), Is.False, "too far off the line to get in behind");

            V11Scene onIt = BackLine(passerX: 790);
            var shoulder = new PitchActor(790, Mid + 100, 0, 0, TopSpeed);
            Assert.That(V11PassTarget.Aim(onIt, shoulder, false, V11PassShape.Through, Cfg, out _, out _), Is.False, "the ball is level with the line");
        }

        // ---------------------------------------------------------------- the run-on, in the simulator

        [Test]
        public void ALeadPass_ArrivesAheadOfTheReceiver_WhoRunsOntoIt()
        {
            int won = 0, ahead = 0;
            var scene = NewScene();
            var receiver = new PitchActor(600, 260, 60, 0, TopSpeed);
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                scene.Reset(seed);
                scene.Place(scene.Passer, 400, Mid);
                scene.Place(scene.Receiver, 600, 260, vxDm: 60);
                V11PassTarget.Aim(scene.Read(400, Mid), receiver, false, V11PassShape.Lead, Cfg, out int tx, out int ty);
                Assert.That(tx - receiver.XDm, Is.GreaterThanOrEqualTo(30), "aimed at least 3 m ahead of him");
                scene.Pass(tx, ty, runOn: false);

                BallEvent? first = scene.RunToFirstTouch(scene.Receiver);
                if (first == null || first.Value.Side != 0 || first.Value.Slot != scene.Receiver.Slot) continue;
                won++;
                if (first.Value.XDm - receiver.XDm >= 10) ahead++;
            }

            TestContext.Out.WriteLine($"[lead-pass] receiver first {won}/{Seeds}, met >= 1 m ahead of where he was {ahead}/{Seeds}");
            Assert.That(won, Is.GreaterThanOrEqualTo(Seeds * 9 / 10), "with nobody near, he runs onto it");
            Assert.That(ahead, Is.EqualTo(won), "and meets it ahead of where he was when it was played, not back at the passer");
        }

        [Test]
        public void AThroughBall_BeatsTheLine_AndTheRunnerTakesItBehindIt()
        {
            int behind = 0, offside = 0;
            var scene = NewScene();
            const int line = 800;
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                scene.Reset(seed);
                scene.Place(scene.Passer, 650, Mid + 80);
                scene.Place(scene.Receiver, line - 10, Mid - 40, vxDm: 30);
                for (int i = 0; i < scene.Line.Length; i++)
                    scene.Place(scene.Line[i], line, LineY[i]);
                scene.PlaceKeeper(Pitch.LengthDm - 5, Mid);

                V11Scene read = scene.Read(650, Mid + 80);
                Assert.That(V11PassTarget.Aim(read, new PitchActor(line - 10, Mid - 40, 30, 0, TopSpeed), false,
                    V11PassShape.Through, Cfg, out int tx, out int ty), Is.True);
                scene.Pass(tx, ty, runOn: true);
                if (scene.FlaggedOffside(scene.Receiver)) offside++;

                BallEvent? first = scene.RunToFirstTouch(scene.Receiver, scene.Line);
                if (first != null && first.Value.Side == 0 && first.Value.Slot == scene.Receiver.Slot && first.Value.XDm > line) behind++;
            }

            TestContext.Out.WriteLine($"[through-ball] runner first behind the line {behind}/{Seeds}, flagged {offside}");
            Assert.That(offside, Is.Zero, "he was onside when it was played");
            Assert.That(behind, Is.GreaterThanOrEqualTo(Seeds * 3 / 4), "the ball beats the line and he gets to it first");
        }

        [Test]
        public void AnOpponentCloserToTheArrivalPoint_WinsTheBall_AndOneFurtherOff_DoesNot()
        {
            (int defender, int receiver) closer = Race(defenderOffDm: 30, receiverOffDm: 60);
            (int defender, int receiver) further = Race(defenderOffDm: 60, receiverOffDm: 30);

            TestContext.Out.WriteLine(
                $"[interceptor] defender 3 m off: he {closer.defender}/{Seeds}, receiver {closer.receiver}; " +
                $"defender 6 m off: he {further.defender}/{Seeds}, receiver {further.receiver}");
            Assert.That(closer.defender, Is.GreaterThanOrEqualTo(Seeds * 3 / 4), "the man nearer where it arrives reaches it first");
            Assert.That(further.receiver, Is.GreaterThanOrEqualTo(Seeds * 3 / 4), "and the receiver does when he is the nearer");
        }

        private (int defender, int receiver) Race(int defenderOffDm, int receiverOffDm)
        {
            int defender = 0, receiver = 0;
            var scene = NewScene();
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                scene.Reset(seed);
                scene.Place(scene.Passer, 500, Mid);
                scene.Place(scene.Receiver, 700, Mid + receiverOffDm);
                scene.Place(scene.Line[1], 700, Mid - defenderOffDm);
                scene.Pass(700, Mid, runOn: true);

                BallEvent? first = scene.RunToFirstTouch(scene.Receiver, scene.Line[1]);
                if (first == null) continue;
                if (first.Value.Side == 1 && first.Value.Slot == scene.Line[1].Slot) defender++;
                if (first.Value.Side == 0 && first.Value.Slot == scene.Receiver.Slot) receiver++;
            }

            return (defender, receiver);
        }

        // ---------------------------------------------------------------- scenes

        private static readonly int[] LineY = { 60, 200, 480, 620 };

        private static V11Scene Scene(int ballX, int ballY)
        {
            var s = new V11Scene { BallXDm = ballX, BallYDm = ballY, Passing = 70, Technique = 70, MaxPassDm = 600 };
            s.AddFoe(new PitchActor(Pitch.LengthDm - 5, Mid, 0, 0, TopSpeed), keeper: true);
            return s;
        }

        /// <summary>A back four flat at x = 800 with a 28 m gap between the centre-backs, the keeper on his line.</summary>
        private static V11Scene BackLine(int passerX)
        {
            V11Scene s = Scene(passerX, Mid);
            foreach (int y in LineY) s.AddFoe(new PitchActor(800, y, 0, 0, TopSpeed), keeper: false);
            s.OffsideLineXDm = 800;
            return s;
        }

        /// <summary>A scene on a simulator set up for a match, as its kickoff finds it.</summary>
        private static PassScene NewScene()
        {
            var sim = new MatchSimulator(Cfg);
            Lineup home = LineupSelector.BestEleven(_league.Clubs[3]), away = LineupSelector.BestEleven(_league.Clubs[6]);
            var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
            typeof(MatchSimulator).GetField("_rng", Any)!.SetValue(sim, new Pcg32(180));
            typeof(MatchSimulator).GetMethod("Setup", Any)!.Invoke(sim, new object?[] { home, away, report, null, 90 * Cfg.TicksPerMinute });
            return new PassScene(sim, home, away);
        }

        /// <summary>A man of the scene: his side and slot.</summary>
        private readonly struct Man
        {
            public Man(int side, int slot)
            {
                Side = side;
                Slot = slot;
            }

            public int Side { get; }
            public int Slot { get; }
        }

        /// <summary>
        /// A pass struck by a home central midfielder to the home striker, away centre-backs and
        /// full-backs for the line; every other man heaped in the home corner, standing.
        /// </summary>
        private sealed class PassScene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private readonly object _brain;
            private readonly BallEventLog _log = new BallEventLog();
            private readonly int _n;
            private int _tick;

            public PassScene(MatchSimulator sim, Lineup home, Lineup away)
            {
                _sim = sim;
                _ball = Get(sim, "_ball");
                _brain = Get(sim, "_brain");
                _n = Field<int[]>("_px").Length / 2;
                Passer = new Man(0, Slot(home, PositionRole.CentralMidfielder, PositionRole.DefensiveMidfielder, PositionRole.AttackingMidfielder));
                Receiver = new Man(0, Slot(home, PositionRole.Striker, PositionRole.Winger, PositionRole.AttackingMidfielder));
                var line = new List<Man>();
                for (int i = 0; i < away.Slots.Count && line.Count < 4; i++)
                    if (away.Slots[i].Role == PositionRole.CentreBack || away.Slots[i].Role == PositionRole.FullBack)
                        line.Add(new Man(1, i));
                Line = line.ToArray();
                for (int i = 0; i < away.Slots.Count; i++)
                    if (away.Slots[i].Role == PositionRole.Goalkeeper) Keeper = new Man(1, i);
            }

            public Man Passer { get; }
            public Man Receiver { get; }
            public Man[] Line { get; }
            public Man Keeper { get; }

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

            public void Place(Man m, int xDm, int yDm, int vxDm = 0)
            {
                int k = m.Side * _n + m.Slot;
                Field<int[]>("_px")[k] = xDm * UnitsPerDm;
                Field<int[]>("_py")[k] = yDm * UnitsPerDm;
                Field<int[]>("_vx")[k] = vxDm * UnitsPerDm / Cfg.TicksPerSecond;
                Field<int[]>("_vy")[k] = 0;
            }

            public void PlaceKeeper(int xDm, int yDm) => Place(Keeper, xDm, yDm);

            /// <summary>The scene as the passer at (x, y) reads it: the away men on the pitch, and the line.</summary>
            public V11Scene Read(int xDm, int yDm)
            {
                var s = new V11Scene { BallXDm = xDm, BallYDm = yDm, OffsideLineXDm = 800, MaxPassDm = 600 };
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py");
                foreach (Man m in Line.Append(Keeper))
                {
                    int k = m.Side * _n + m.Slot;
                    s.AddFoe(new PitchActor(px[k] / UnitsPerDm, py[k] / UnitsPerDm, 0, 0, TopSpeed), m.Slot == Keeper.Slot);
                }

                return s;
            }

            /// <summary>
            /// The passer strikes it to (x, y) with the weight the brain gives a pass there; into
            /// space (<paramref name="runOn"/>), as the brain plays a lead pass or a through ball.
            /// </summary>
            public void Pass(int xDm, int yDm, bool runOn)
            {
                int k = Passer.Side * _n + Passer.Slot;
                int bx = Field<int[]>("_px")[k], by = Field<int[]>("_py")[k];
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, bx);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, by);
                Set(_ball, "Vx", 0);
                Set(_ball, "Vy", 0);
                Set(_ball, "_z", 0);
                Set(_ball, "_vz", 0);
                Set(_ball, "OwnerSide", Passer.Side);
                Set(_ball, "OwnerSlot", Passer.Slot);
                Set(_ball, "LastTouchSide", Passer.Side);
                Set(_ball, "LastTouchSlot", Passer.Slot);
                foreach (string f in new[] { "_ballAgoX", "_ballTwoAgoX" }) Set(_sim, f, bx);
                foreach (string f in new[] { "_ballAgoY", "_ballTwoAgoY" }) Set(_sim, f, by);

                int tx = xDm * UnitsPerDm, ty = yDm * UnitsPerDm;
                int distance = (int)Math.Round(Math.Sqrt((double)(tx - bx) * (tx - bx) + (double)(ty - by) * (ty - by)));
                object[] force = { distance, Get(_sim, "_arrivalStepU"), Get(_sim, "_maxPassForce"), 0 };
                int f0 = (int)_ball.GetType().GetMethod("ForceToArrive")!.Invoke(_ball, force)!;

                Type choiceType = typeof(MatchSimulator).GetNestedType("PassChoice", BindingFlags.NonPublic)!;
                object choice = Activator.CreateInstance(choiceType)!;
                choiceType.GetField("Slot")!.SetValue(choice, Receiver.Slot);
                choiceType.GetField("X")!.SetValue(choice, tx);
                choiceType.GetField("Y")!.SetValue(choice, ty);
                choiceType.GetField("Force")!.SetValue(choice, f0);
                choiceType.GetField("Completion")!.SetValue(choice, 1000);
                choiceType.GetField("RunOn")!.SetValue(choice, runOn);
                typeof(MatchSimulator).GetMethod("PlayPass", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_sim, new[] { _tick, Passer.Side, Passer.Slot, choice, 0 });
                ClearLog();
            }

            public bool FlaggedOffside(Man m)
            {
                object offside = Get(_sim, "_offside");
                return (bool)offside.GetType().GetMethod("IsFlagged")!.Invoke(offside, new object[] { m.Side * _n + m.Slot })!;
            }

            /// <summary>Ticks the scene until somebody but the passer touches the ball; null if nobody does in 8 s.</summary>
            public BallEvent? RunToFirstTouch(params Man[] movers) => RunToFirstTouch(movers.AsEnumerable());

            public BallEvent? RunToFirstTouch(Man first, Man[] more) => RunToFirstTouch(new[] { first }.Concat(more));

            private BallEvent? RunToFirstTouch(IEnumerable<Man> movers)
            {
                Man[] men = movers.ToArray();
                MethodInfo update = _brain.GetType().GetMethod("UpdateTeams")!;
                MethodInfo move = _brain.GetType().GetMethod("Move")!;
                MethodInfo advance = _ball.GetType().GetMethod("Advance")!;
                MethodInfo resolve = typeof(MatchSimulator).GetMethod("ResolveControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (int i = 0; i < 8 * Cfg.TicksPerSecond; i++)
                {
                    _tick++;
                    Set(_ball, "EventTick", _tick);
                    update.Invoke(_brain, new object[] { _tick });
                    foreach (Man m in men) move.Invoke(_brain, new object[] { _tick, m.Side, m.Slot });
                    Set(_sim, "_ballTwoAgoX", Get(_sim, "_ballAgoX"));
                    Set(_sim, "_ballTwoAgoY", Get(_sim, "_ballAgoY"));
                    Set(_sim, "_ballAgoX", Get(_ball, "X"));
                    Set(_sim, "_ballAgoY", Get(_ball, "Y"));
                    advance.Invoke(_ball, null);
                    resolve.Invoke(_sim, new object[] { _tick });
                    BallEvent touch = _log.Records.FirstOrDefault(e => e.Kind == BallEventKind.Touch || e.Kind == BallEventKind.Deflection);
                    if (touch.Kind != BallEventKind.None) return touch;
                    if ((bool)Get(_ball, "Dead")) return null;
                }

                return null;
            }

            private void ClearLog() => ((List<BallEvent>)Get(_log, "_events")).Clear();

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
