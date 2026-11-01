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
    /// Free-ball spec R4 (issue #181): long balls and switches are lofted; where a high ball drops a
    /// man of each side goes up for it, the better in the air (Strength, Positioning, Technique)
    /// winning more often; and the ball that comes off the header is free, the second ball, for
    /// the others to contest. The scenes drive the simulator's own ball and control resolution
    /// (private, so through reflection) on a simulator a real match has set up, with every man the
    /// scene does not use heaped in a far corner.
    /// </summary>
    [TestFixture]
    public class AerialDuelTests
    {
        private const int UnitsPerDm = 16;
        private const int Seeds = 64;
        private const int HomeSlot = 5, AwaySlot = 5, AwayMate = 6;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void ALoftedLongBall_RisesWellAboveTheHeads_PeaksMidFlight_AndDropsThroughHeadHeightAsItLands()
        {
            var scene = new Scene(PlayedSimulator());
            List<(int X, int Z)> flight = scene.LoftedFlight(fromDm: 300, toDm: 640);

            int peak = flight.Max(f => f.Z);
            int peakAt = flight.FindIndex(f => f.Z == peak);
            int landed = flight.FindIndex(f => f.Z == 0);
            int header = flight.FindLastIndex(f => f.Z >= Cfg.AerialDuelFromDm && f.Z <= Cfg.TouchHeightDm);
            TestContext.Out.WriteLine($"[loft] peak {peak} dm at tick {peakAt}, header height at tick {header} x {flight[header].X} dm, landed tick {landed} x {flight[landed].X} dm");

            Assert.That(peak, Is.GreaterThanOrEqualTo(40), "a thirty-four-metre ball goes well over the heads");
            Assert.That(landed, Is.GreaterThan(0), "and comes down");
            Assert.That(peakAt, Is.InRange(landed / 3, 2 * landed / 3), "its peak is in the middle third of its flight");
            Assert.That(flight.Take(landed).Count(f => f.Z <= Cfg.TouchHeightDm), Is.LessThanOrEqualTo(4),
                "out of everybody's reach but at its two ends");
            Assert.That(header, Is.GreaterThan(peakAt), "it drops through head height after the peak");
            Assert.That(flight[landed].X - flight[header].X, Is.InRange(0, Cfg.ControlRadiusDm),
                "in the last metre before it lands: where it drops is where it is contested");
            Assert.That(landed, Is.LessThanOrEqualTo(3 * Cfg.TicksPerSecond), "it hangs three seconds at most");
            Assert.That(flight[landed].X, Is.InRange(600, 640), "and lands within four metres short of its target");
            Assert.That(flight[landed + 1].X, Is.GreaterThan(flight[landed].X), "then runs on toward it");
        }

        [Test]
        public void ASwitchOfPlay_IsLofted_AShortPassIsNot()
        {
            var scene = new Scene(PlayedSimulator());

            int switchPeak = scene.PassPeak(fromX: 500, fromY: 60, toX: 520, toY: 340);   // 28 m across, under a long ball
            int shortPeak = scene.PassPeak(fromX: 500, fromY: 340, toX: 650, toY: 380);   // 15.5 m forward

            TestContext.Out.WriteLine($"[switch] peak {switchPeak} dm; short pass {shortPeak} dm");
            Assert.That(switchPeak, Is.GreaterThan(Cfg.TouchHeightDm), "a switch goes over the heads");
            Assert.That(shortPeak, Is.Zero, "a short pass stays on the grass");
        }

        [Test]
        public void AnAerialDuel_FavoursTheStrongerHeader_Over64Seeds()
        {
            var scene = new Scene(PlayedSimulator());
            int homeStrong = scene.HomeWins(homeAerial: 90, awayAerial: 30, Seeds);
            int homeWeak = scene.HomeWins(homeAerial: 30, awayAerial: 90, Seeds);

            TestContext.Out.WriteLine($"[aerial] home wins {homeStrong} of {Seeds} when stronger, {homeWeak} when weaker");
            Assert.That(homeStrong, Is.GreaterThanOrEqualTo(Seeds * 3 / 4), "the stronger header wins most duels");
            Assert.That(homeWeak, Is.LessThanOrEqualTo(Seeds / 4), "wherever he stands");
            Assert.That(homeWeak, Is.GreaterThan(0), "but not every one");
        }

        [Test]
        public void AfterTheDuel_TheSecondBallIsFree_TheDuellistsSitItOut_AndTheLosersSideContestsIt()
        {
            var scene = new Scene(PlayedSimulator());
            int contested = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                BallEvent duel = scene.Duel(homeAerial: 60, awayAerial: 60, seed);
                Assert.That(duel.AerialDuel, Is.True, "the header is the duel's event");
                Assert.That(scene.Free, Is.True, "nobody controls the ball off a header");
                Assert.That(scene.Receivers, Is.EqualTo((-1, -1)), "it is nobody's pass");
                Assert.That(scene.BallSpeed, Is.GreaterThan(0), "it runs on");

                // The loser's side: the away team-mate, where it next comes down to a header's height.
                int loserSide = 1 - duel.Side;
                BallEvent next = scene.NextTouch(loserSide);
                Assert.That((next.Side, next.Slot), Is.Not.EqualTo((0, HomeSlot)).And.Not.EqualTo((1, AwaySlot)),
                    "neither duellist takes the second ball straight back");
                if (next.Side == loserSide) contested++;
            }

            TestContext.Out.WriteLine($"[second ball] the loser's side took it {contested} of {Seeds}");
            Assert.That(contested, Is.EqualTo(Seeds), "a man of the loser's side where it drops gets to it");
        }

        private MatchSimulator PlayedSimulator()
        {
            var sim = new MatchSimulator(Cfg);
            Lineup home = LineupSelector.BestEleven(_league.Clubs[9]), away = LineupSelector.BestEleven(_league.Clubs[10]);
            var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
            sim.Generate(home, away, report, new Pcg32(7), null);
            return sim;
        }

        /// <summary>
        /// The home man at (500 dm, 340 dm) and the away man 0.6 m from him, a long ball the home side
        /// played dropping onto them along the x axis; everybody else heaped in a far corner.
        /// </summary>
        private sealed class Scene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private readonly MethodInfo _advance, _resolve;
            private readonly int _n;
            private BallEventLog _log = new BallEventLog();
            private int _tick;

            public Scene(MatchSimulator sim)
            {
                _sim = sim;
                _ball = Get(_sim, "_ball");
                _n = ((bool[])Get(_sim, "_keeper")).Length / 2;
                _advance = _ball.GetType().GetMethod("Advance")!;
                _resolve = typeof(MatchSimulator).GetMethod("ResolveControl", Any)!;
            }

            public bool Free => (int)Get(_ball, "OwnerSide") < 0;
            public (int, int) Receivers => (((int[])Get(_sim, "_receiver"))[0], ((int[])Get(_sim, "_receiver"))[1]);
            public int BallSpeed => (int)_ball.GetType().GetProperty("Speed")!.GetValue(_ball)!;
            private int Z => (int)_ball.GetType().GetProperty("Z")!.GetValue(_ball)!;

            /// <summary>A long ball struck from x = <paramref name="fromDm"/> to arrive at <paramref name="toDm"/>: (x, height) each tick, dm.</summary>
            public List<(int X, int Z)> LoftedFlight(int fromDm, int toDm)
            {
                Reset();
                PlaceBall(fromDm, 340, 0, 0, 0);
                int distance = (toDm - fromDm) * UnitsPerDm;
                int force = ForceToArrive(distance);
                Call("Kick", 0, HomeSlot, 1, 0, force);
                Call("Loft", distance, 0);

                var flight = new List<(int, int)>();
                for (int t = 0; t < 80; t++)
                {
                    _advance.Invoke(_ball, null);
                    flight.Add(((int)Get(_ball, "X") / UnitsPerDm, Z));
                }

                return flight;
            }

            /// <summary>The highest the ball gets on a pass the simulator plays from one point to another (dm).</summary>
            public int PassPeak(int fromX, int fromY, int toX, int toY)
            {
                Reset();
                int[] px = (int[])Get(_sim, "_px"), py = (int[])Get(_sim, "_py");
                px[HomeSlot] = fromX * UnitsPerDm;
                py[HomeSlot] = fromY * UnitsPerDm;
                int mate = HomeSlot + 1;
                px[mate] = toX * UnitsPerDm;
                py[mate] = toY * UnitsPerDm;
                PlaceBall(fromX, fromY, 0, 0, 0);
                Set(_ball, "OwnerSide", 0);
                Set(_ball, "OwnerSlot", HomeSlot);

                Set(_sim, "_rng", new Pcg32(11));
                int distance = U(System.Math.Sqrt((double)(toX - fromX) * (toX - fromX) + (double)(toY - fromY) * (toY - fromY)));
                System.Type choiceType = typeof(MatchSimulator).GetNestedType("PassChoice", BindingFlags.NonPublic)!;
                object choice = System.Activator.CreateInstance(choiceType)!;
                choiceType.GetField("Slot")!.SetValue(choice, mate);
                choiceType.GetField("X")!.SetValue(choice, toX * UnitsPerDm);
                choiceType.GetField("Y")!.SetValue(choice, toY * UnitsPerDm);
                choiceType.GetField("Force")!.SetValue(choice, ForceToArrive(distance));
                choiceType.GetField("Completion")!.SetValue(choice, 900);
                typeof(MatchSimulator).GetMethod("PlayPass", Any)!.Invoke(_sim, new[] { (object)0, 0, HomeSlot, choice, 0 });

                int peak = 0;
                for (int t = 0; t < 60; t++)
                {
                    _advance.Invoke(_ball, null);
                    if (Z > peak) peak = Z;
                }

                return peak;
            }

            /// <summary>Home wins of one duel per seed, the two men rated as given in the air.</summary>
            public int HomeWins(int homeAerial, int awayAerial, int seeds)
            {
                int wins = 0;
                for (int seed = 0; seed < seeds; seed++)
                    if (Duel(homeAerial, awayAerial, seed).Side == 0) wins++;
                return wins;
            }

            /// <summary>One duel: the touch the header made, which is the winner's with both men within reach.</summary>
            public BallEvent Duel(int homeAerial, int awayAerial, int seed)
            {
                Reset();
                Man(0, HomeSlot, 500, 340, homeAerial);
                Man(1, AwaySlot, 500, 346, awayAerial);
                Set(_sim, "_rng", new Pcg32(9000 + (ulong)seed));

                // Coming down at 1 m a tick from 2.2 m, 8 m/s along x: 2.1 m after this tick's step.
                PlaceBall(495, 340, 80 * UnitsPerDm / Cfg.TicksPerSecond, 22_000, -800);
                NewLog();
                Step();
                List<BallEvent> touches = Touches();
                Assert.That(touches, Has.Count.EqualTo(1), "one header settles the duel");
                return touches[0];
            }

            /// <summary>
            /// The away team-mate stands where the ball next comes down to a header's height (or stops);
            /// the ball runs until somebody touches it. The first touch after the duel.
            /// </summary>
            public BallEvent NextTouch(int loserSide)
            {
                int ahead = 1;
                MethodInfo zAfter = _ball.GetType().GetMethod("ZAfter")!;
                while (ahead < 60 && (int)zAfter.Invoke(_ball, new object[] { ahead })! > Cfg.TouchHeightDm) ahead++;
                object[] at = { ahead, 0, 0 };
                _ball.GetType().GetMethod("Future")!.Invoke(_ball, at);
                Man(loserSide, loserSide == 1 ? AwayMate : HomeSlot + 1, (int)at[1] / UnitsPerDm, (int)at[2] / UnitsPerDm, 50);

                NewLog();
                for (int t = 0; t < 80 && Touches().Count == 0; t++) Step();
                Assert.That(Touches(), Is.Not.Empty, "somebody gets to the second ball");
                return Touches()[0];
            }

            private List<BallEvent> Touches() => _log.Records.Where(e => e.Kind != BallEventKind.Bounce).ToList();

            private void Man(int side, int slot, int xDm, int yDm, int aerial)
            {
                int k = side * _n + slot;
                ((int[])Get(_sim, "_px"))[k] = xDm * UnitsPerDm;
                ((int[])Get(_sim, "_py"))[k] = yDm * UnitsPerDm;
                foreach (string skill in new[] { "_skStrength", "_skPositioning", "_skTechnique" })
                    ((int[])Get(_sim, skill))[k] = aerial;
            }

            private void Step()
            {
                Set(_sim, "_ballTwoAgoX", Get(_sim, "_ballAgoX"));
                Set(_sim, "_ballTwoAgoY", Get(_sim, "_ballAgoY"));
                Set(_sim, "_ballAgoX", Get(_ball, "X"));
                Set(_sim, "_ballAgoY", Get(_ball, "Y"));
                Set(_ball, "EventTick", _tick);
                _advance.Invoke(_ball, null);
                _resolve.Invoke(_sim, new object[] { _tick });
                _tick++;
            }

            private void NewLog()
            {
                _log = new BallEventLog();
                Set(_ball, "EventSink", _log);
            }

            private int ForceToArrive(int distance)
            {
                object[] args = { distance, Cfg.PassArrivalSpeedDmPerSecond * UnitsPerDm / Cfg.TicksPerSecond, int.MaxValue, 0 };
                return (int)_ball.GetType().GetMethod("ForceToArrive")!.Invoke(_ball, args)!;
            }

            private void Call(string method, params object[] args) => _ball.GetType().GetMethod(method)!.Invoke(_ball, args);

            private void Reset()
            {
                _tick += 100;
                int[] px = (int[])Get(_sim, "_px"), py = (int[])Get(_sim, "_py");
                int[] vx = (int[])Get(_sim, "_vx"), vy = (int[])Get(_sim, "_vy");
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = 50 * UnitsPerDm;
                    py[i] = 50 * UnitsPerDm;
                    vx[i] = 0;
                    vy[i] = 0;
                }

                System.Array.Clear((bool[])Get(_sim, "_sentOff"), 0, px.Length);
                int[] receiver = (int[])Get(_sim, "_receiver");
                receiver[0] = -1;
                receiver[1] = -1;
                Set(_sim, "_releasedBy", -1);
                Set(_sim, "_releasedUntil", -1);
                Set(_sim, "_tackleLock", int.MaxValue);
                object ctx = Get(_sim, "_ctx");
                ctx.GetType().GetField("ShotLive")!.SetValue(ctx, false);
                object offside = Get(_sim, "_offside");
                offside.GetType().GetMethod("ClearFlags")!.Invoke(offside, null);
            }

            /// <summary>A free ball the home side last played, height and climb in milli-dm and milli-dm a tick.</summary>
            private void PlaceBall(int xDm, int yDm, int vx, int zMilliDm, int vzMilliDm)
            {
                int x = xDm * UnitsPerDm, y = yDm * UnitsPerDm;
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, x);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, y);
                Set(_sim, "_ballAgoX", x);
                Set(_sim, "_ballAgoY", y);
                Set(_ball, "Vx", vx);
                Set(_ball, "Vy", 0);
                Set(_ball, "_z", zMilliDm);
                Set(_ball, "_vz", vzMilliDm);
                Set(_ball, "Dead", false);
                Set(_ball, "JustStruck", false);
                Set(_ball, "OwnerSide", -1);
                Set(_ball, "OwnerSlot", -1);
                Set(_ball, "LastTouchSide", 0);
                Set(_ball, "LastTouchSlot", 1);
            }

            private static int U(double dm) => (int)System.Math.Round(dm * UnitsPerDm);

            private static object Get(object o, string name) => o.GetType().GetField(name, Any)!.GetValue(o)!;

            private static void Set(object o, string name, object? value) => o.GetType().GetField(name, Any)!.SetValue(o, value);
        }
    }
}
