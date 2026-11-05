using System;
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
    /// Free-ball spec R6, issue #184: in possession the striker holds the second-last defender's
    /// line (no more than 0.5 m beyond it) while the ball is not playable to him, starts a run in
    /// behind when the team-mate on the ball has a lane to the space behind, and a run he cannot
    /// pull out of in time is still flagged. Played on a simulator set up for a real match (private,
    /// so through reflection): a home team-mate holds the ball, the away back four stands flat at
    /// x = <see cref="Line"/> with the keeper on his line, every other man is heaped in the home
    /// corner, and each tick the striker moves by his brain. Home attacks toward x = Pitch.LengthDm.
    /// </summary>
    [TestFixture]
    public class V11StrikerLineTests
    {
        private const int UnitsPerDm = 16;
        private const int Mid = Pitch.CenterY;
        private const int Line = 700;
        private const int HoldDm = 5;   // R6: on the line, no more than 0.5 m beyond it
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly int[] LineY = { 60, 200, 480, 620 };
        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20261184));
        }

        [Test]
        public void AStrikerBeyondTheLine_WithNoPassOn_IsBackOnsideWithinTwoSeconds_AndStaysOnTheLine()
        {
            var scene = new Scene(_league);
            int[] beyond = { 30, 60 };
            int[] across = { Mid - 120, Mid, Mid + 120 };
            int[] standing = { 0, 10 };   // standing, or walking on toward goal (dm/s)
            foreach (int off in beyond)
            foreach (int y in across)
            foreach (int vx in standing)
            {
                // The ball 35 m behind the line: too far for a pass in behind, so nothing is on.
                scene.Reset();
                scene.Place(scene.Carrier, Line - 350, Mid);
                scene.Place(scene.Striker, Line + off, y, vx);
                scene.GiveBall(scene.Carrier);

                int backAt = -1, worst = int.MinValue;
                for (int t = 1; t <= 5 * Cfg.TicksPerSecond; t++)
                {
                    scene.Tick(scene.Striker);
                    int past = scene.X(scene.Striker) - Line;
                    if (backAt < 0 && past <= HoldDm) backAt = t;
                    if (backAt >= 0 && past > worst) worst = past;
                }

                string at = $"{off / 10.0} m beyond at y {y / 10.0}, moving {vx / 10.0} m/s";
                Assert.That(backAt, Is.InRange(1, 2 * Cfg.TicksPerSecond), $"{at}: back onside within 2 s");
                Assert.That(worst, Is.LessThanOrEqualTo(HoldDm), $"{at}: and he holds the line, not beyond it, while nothing is on");
                Assert.That(scene.Runs, Is.Zero, $"{at}: no run in behind without a pass to make it for");
            }
        }

        [Test]
        public void ARunInBehind_StartsWhenTheLaneOpens_AndNotBefore()
        {
            var scene = new Scene(_league);
            scene.Reset();
            scene.Place(scene.Carrier, 500, Mid);
            scene.Place(scene.Striker, Line - 20, Mid - 40);
            scene.Place(scene.Blocker, 620, Mid - 10);
            scene.GiveBall(scene.Carrier);

            for (int t = 0; t < 2 * Cfg.TicksPerSecond; t++) scene.Tick(scene.Striker);
            Assert.That(scene.Runs, Is.Zero, "the lane to the space behind is shut: he holds");
            Assert.That(scene.X(scene.Striker) - Line, Is.LessThanOrEqualTo(HoldDm), "on the line");

            scene.Place(scene.Blocker, 620, Mid + 260);
            int startedAt = -1, before = scene.X(scene.Striker);
            for (int t = 1; t <= Cfg.TicksPerSecond && startedAt < 0; t++)
            {
                scene.Tick(scene.Striker);
                if (scene.Runs > 0) startedAt = t;
            }

            Assert.That(startedAt, Is.InRange(1, 3), "the lane opens: he goes at once");
            for (int t = 0; t < Cfg.TicksPerSecond; t++) scene.Tick(scene.Striker);
            Assert.That(scene.X(scene.Striker), Is.GreaterThan(before), "toward the space behind");
            Assert.That(scene.Vx(scene.Striker), Is.GreaterThan(0), "running at it");
        }

        [Test]
        public void ARunThatCannotBeCorrected_IsStillFlagged()
        {
            var scene = new Scene(_league);
            scene.Reset();
            scene.Place(scene.Carrier, 500, Mid);
            scene.Place(scene.Striker, Line - 20, Mid - 40);
            scene.GiveBall(scene.Carrier);

            int t = 0;
            for (; t < 3 * Cfg.TicksPerSecond && scene.X(scene.Striker) - Line <= Cfg.OffsideMarginDm; t++)
                scene.Tick(scene.Striker);

            Assert.That(scene.Runs, Is.EqualTo(1), "he made the run");
            Assert.That(scene.X(scene.Striker) - Line, Is.GreaterThan(Cfg.OffsideMarginDm),
                "at a sprint he cannot stop on the line when no ball comes");
            scene.Pass(scene.Striker);
            Assert.That(scene.FlaggedOffside(scene.Striker), Is.True, "a ball played to him then is offside");
        }

        // ---------------------------------------------------------------- the scene

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

        private sealed class Scene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private readonly object _brain;
            private readonly Man[] _line;
            private readonly Man _keeper;
            private readonly int _n;
            private int _tick;
            private int _runsAtReset;

            public Scene(League league)
            {
                _sim = new MatchSimulator(Cfg);
                Lineup home = LineupSelector.BestEleven(league.Clubs[3]), away = LineupSelector.BestEleven(league.Clubs[6]);
                var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
                Set(_sim, "_rng", new Pcg32(184));
                typeof(MatchSimulator).GetMethod("Setup", Any)!.Invoke(_sim, new object?[] { home, away, report, null, 90 * Cfg.TicksPerMinute });
                _ball = Get(_sim, "_ball");
                _brain = Get(_sim, "_brain");
                _n = Field<int[]>("_px").Length / 2;

                Carrier = new Man(0, Slot(home, PositionRole.CentralMidfielder, PositionRole.DefensiveMidfielder));
                Striker = new Man(0, Slot(home, PositionRole.Striker));
                Blocker = new Man(1, Slot(away, PositionRole.DefensiveMidfielder, PositionRole.CentralMidfielder));
                var line = new System.Collections.Generic.List<Man>();
                for (int i = 0; i < away.Slots.Count && line.Count < 4; i++)
                    if (away.Slots[i].Role == PositionRole.CentreBack || away.Slots[i].Role == PositionRole.FullBack)
                        line.Add(new Man(1, i));
                _line = line.ToArray();
                _keeper = new Man(1, Slot(away, PositionRole.Goalkeeper));
            }

            public Man Carrier { get; }
            public Man Striker { get; }
            public Man Blocker { get; }

            /// <summary>Runs in behind the home side started since the last <see cref="Reset"/>.</summary>
            public int Runs => _sim.RunsInBehind(0) - _runsAtReset;

            public void Reset()
            {
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py"), vx = Field<int[]>("_vx"), vy = Field<int[]>("_vy");
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = 50 * UnitsPerDm;
                    py[i] = 50 * UnitsPerDm;
                    vx[i] = 0;
                    vy[i] = 0;
                }

                for (int i = 0; i < _line.Length; i++) Place(_line[i], Line, LineY[i]);
                Place(_keeper, Pitch.LengthDm - 5, Mid);
                int[] receiver = Field<int[]>("_receiver");
                receiver[0] = -1;
                receiver[1] = -1;
                object offside = Get(_sim, "_offside");
                offside.GetType().GetMethod("ClearFlags")!.Invoke(offside, null);
                _runsAtReset = _sim.RunsInBehind(0);
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

            public int X(Man m) => Field<int[]>("_px")[m.Side * _n + m.Slot] / UnitsPerDm;

            public int Vx(Man m) => Field<int[]>("_vx")[m.Side * _n + m.Slot];

            /// <summary>The ball at the feet of <paramref name="m"/>, live.</summary>
            public void GiveBall(Man m)
            {
                int k = m.Side * _n + m.Slot;
                int bx = Field<int[]>("_px")[k], by = Field<int[]>("_py")[k];
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, bx);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, by);
                foreach (string f in new[] { "_ballAgoX", "_ballTwoAgoX" }) Set(_sim, f, bx);
                foreach (string f in new[] { "_ballAgoY", "_ballTwoAgoY" }) Set(_sim, f, by);
                Set(_ball, "_z", 0);
                Set(_ball, "_vz", 0);
                _ball.GetType().GetMethod("Take")!.Invoke(_ball, new object[] { m.Side, m.Slot });
            }

            /// <summary>One tick: the team brains read the pitch, then <paramref name="m"/> moves by his.</summary>
            public void Tick(Man m)
            {
                _tick++;
                _brain.GetType().GetMethod("UpdateTeams")!.Invoke(_brain, new object[] { _tick });
                _brain.GetType().GetMethod("Move")!.Invoke(_brain, new object[] { _tick, m.Side, m.Slot });
            }

            /// <summary>The man on the ball plays it to <paramref name="m"/>'s feet, as the simulator plays a pass.</summary>
            public void Pass(Man m)
            {
                int k = m.Side * _n + m.Slot;
                Type choiceType = typeof(MatchSimulator).GetNestedType("PassChoice", BindingFlags.NonPublic)!;
                object choice = Activator.CreateInstance(choiceType)!;
                choiceType.GetField("Slot")!.SetValue(choice, m.Slot);
                choiceType.GetField("X")!.SetValue(choice, Field<int[]>("_px")[k]);
                choiceType.GetField("Y")!.SetValue(choice, Field<int[]>("_py")[k]);
                choiceType.GetField("Force")!.SetValue(choice, Get(_sim, "_maxPassForce"));
                choiceType.GetField("Completion")!.SetValue(choice, 1000);
                typeof(MatchSimulator).GetMethod("PlayPass", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_sim, new[] { _tick, Carrier.Side, Carrier.Slot, choice, 0 });
            }

            public bool FlaggedOffside(Man m)
            {
                object offside = Get(_sim, "_offside");
                return (bool)offside.GetType().GetMethod("IsFlagged")!.Invoke(offside, new object[] { m.Side * _n + m.Slot })!;
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
