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
    /// Free-ball spec R3 (issue #179): the first touch leaves the ball with a control error that grows
    /// with the ball's speed and shrinks with Technique, so a poor touch leaves it loose; and a man
    /// running with the ball keeps it 0.5-2 m ahead of him through touches instead of glued to his
    /// feet. The scenes drive the simulator's own control resolution (private, so through reflection)
    /// on a simulator a real match has set up, with every other man moved far out of the way.
    /// </summary>
    [TestFixture]
    public class FirstTouchDribbleTests
    {
        private const int UnitsPerDm = 16;
        private const int Seeds = 128;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void ALowTechniqueReceiver_OfAFastPass_LosesControlInMoreSeeds_ThanAHighTechniqueOne()
        {
            var scene = new Scene(PlayedSimulator());
            int low = scene.LostTouches(technique: 15, speedDmPerSecond: 160, Seeds);
            int high = scene.LostTouches(technique: 95, speedDmPerSecond: 160, Seeds);

            TestContext.Out.WriteLine($"[first touch] lost {low} of {Seeds} at Technique 15, {high} at 95");
            Assert.That(low, Is.GreaterThan(high), $"lost {low} of {Seeds} at Technique 15, {high} at 95");
            Assert.That(low, Is.GreaterThan(0), "a poor touch can leave the ball loose");
        }

        [Test]
        public void TheFirstTouchError_GrowsWithTheBallsSpeed()
        {
            var scene = new Scene(PlayedSimulator());
            int fast = scene.LostTouches(technique: 50, speedDmPerSecond: 180, Seeds);
            int slow = scene.LostTouches(technique: 50, speedDmPerSecond: 50, Seeds);

            TestContext.Out.WriteLine($"[first touch] lost {fast} of {Seeds} at 18 m/s, {slow} at 5 m/s");
            Assert.That(fast, Is.GreaterThan(slow), $"lost {fast} of {Seeds} at 18 m/s, {slow} at 5 m/s");
        }

        [Test]
        public void AManRunningWithTheBall_KeepsIt0_5To2mAhead_ThroughTouches()
        {
            var scene = new Scene(PlayedSimulator());
            scene.Dribble(speedDmPerSecond: 50, ticks: 80);

            TestContext.Out.WriteLine($"[dribble] {scene.Touches.Count} touches, ahead {scene.Gaps.Min()}-{scene.Gaps.Max()} dm");
            Assert.That(scene.Touches.Count, Is.GreaterThanOrEqualTo(3), "he plays it ahead with touch after touch");
            Assert.That(scene.Touches.All(t => t.Side == 0 && t.Slot == scene.Slot), Is.True, "every touch is his");
            Assert.That(scene.Touches.All(t => t.FromFeetMm <= Cfg.ControlRadiusDm * 100), Is.True,
                "each touch is made within his reach");
            Assert.That(scene.Owned, Is.True, "he still has it");
            Assert.That(scene.Gaps, Is.Not.Empty);
            Assert.That(scene.Gaps.Min(), Is.GreaterThanOrEqualTo(5), "never glued to his feet: at least 0.5 m ahead");
            Assert.That(scene.Gaps.Max(), Is.LessThanOrEqualTo(20), "never more than 2 m ahead");
            Assert.That(scene.AheadEveryTick, Is.True, "ahead of him, the way he runs");
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
        /// One home outfielder at (400 dm, 340 dm), everybody else in a heap in a far corner; each
        /// tick the ball steps and control is resolved, as the match does.
        /// </summary>
        private sealed class Scene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private BallEventLog _log = new BallEventLog();
            private readonly MethodInfo _advance, _resolve;

            public Scene(MatchSimulator sim)
            {
                _sim = sim;
                _ball = Get(_sim, "_ball");
                bool[] keeper = (bool[])Get(_sim, "_keeper"), sentOff = (bool[])Get(_sim, "_sentOff");
                while (keeper[Slot] || sentOff[Slot]) Slot++;
                _advance = _ball.GetType().GetMethod("Advance")!;
                _resolve = typeof(MatchSimulator).GetMethod("ResolveControl", Any)!;
            }

            public int Slot { get; }
            public List<int> Gaps { get; } = new List<int>();
            public bool AheadEveryTick { get; private set; } = true;
            public List<BallEvent> Touches => _log.Records.Where(e => e.Kind != BallEventKind.Bounce).ToList();
            public bool Owned => (int)Get(_ball, "OwnerSide") == 0 && (int)Get(_ball, "OwnerSlot") == Slot;
            private bool Free => (int)Get(_ball, "OwnerSide") < 0;
            private int K => Slot;

            /// <summary>
            /// A pass at <paramref name="speedDmPerSecond"/> meant for him, from a team-mate 10 m away,
            /// once per seed: how many times his first touch left the ball loose.
            /// </summary>
            public int LostTouches(int technique, int speedDmPerSecond, int seeds)
            {
                int lost = 0;
                for (int seed = 0; seed < seeds; seed++)
                {
                    Reset(400, 340, technique);
                    Set(_sim, "_rng", new Pcg32(5000 + (ulong)seed));
                    int[] receiver = (int[])Get(_sim, "_receiver");
                    receiver[0] = Slot;
                    PlaceBall(300, 340, speedDmPerSecond);

                    NewLog();
                    for (int tick = 0; tick < 60 && Touches.Count == 0; tick++) Step(tick);

                    Assert.That(Touches, Is.Not.Empty, "the pass reached him");
                    Assert.That((Touches[0].Side, Touches[0].Slot), Is.EqualTo((0, Slot)), "and he touched it");
                    if (Free) lost++;
                }

                return lost;
            }

            /// <summary>
            /// He is running with the ball straight at the goal he attacks: it rolls 0.8 m in front of
            /// him at his own pace.
            /// </summary>
            public void Dribble(int speedDmPerSecond, int ticks)
            {
                Reset(300, 340, technique: 60);
                PlaceBall(308, 340, speedDmPerSecond);
                Set(_ball, "OwnerSide", 0);
                Set(_ball, "OwnerSlot", Slot);
                Set(_ball, "LastTouchSlot", Slot);
                NewLog();

                int[] px = (int[])Get(_sim, "_px"), vx = (int[])Get(_sim, "_vx");
                int step = speedDmPerSecond * UnitsPerDm / Cfg.TicksPerSecond;
                for (int tick = 0; tick < ticks && Owned; tick++)
                {
                    px[K] += step;
                    vx[K] = step;
                    Step(tick);

                    int dx = (int)Get(_ball, "X") - px[K];
                    int dy = (int)Get(_ball, "Y") - ((int[])Get(_sim, "_py"))[K];
                    Gaps.Add((int)System.Math.Round(System.Math.Sqrt((double)dx * dx + (double)dy * dy) / UnitsPerDm));
                    if (dx <= 0) AheadEveryTick = false;
                }
            }

            private void NewLog()
            {
                _log = new BallEventLog();
                Set(_ball, "EventSink", _log);
            }

            private void Step(int tick)
            {
                // The two positions a gather is judged against, as the match's tick keeps them.
                Set(_sim, "_ballTwoAgoX", Get(_sim, "_ballAgoX"));
                Set(_sim, "_ballTwoAgoY", Get(_sim, "_ballAgoY"));
                Set(_sim, "_ballAgoX", Get(_ball, "X"));
                Set(_sim, "_ballAgoY", Get(_ball, "Y"));
                _advance.Invoke(_ball, null);
                _resolve.Invoke(_sim, new object[] { tick });
            }

            private void Reset(int xDm, int yDm, int technique)
            {
                int[] px = (int[])Get(_sim, "_px"), py = (int[])Get(_sim, "_py");
                int[] vx = (int[])Get(_sim, "_vx"), vy = (int[])Get(_sim, "_vy");
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = 50 * UnitsPerDm;
                    py[i] = 50 * UnitsPerDm;
                    vx[i] = 0;
                    vy[i] = 0;
                }

                px[K] = xDm * UnitsPerDm;
                py[K] = yDm * UnitsPerDm;
                ((int[])Get(_sim, "_skTechnique"))[K] = technique;

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

            /// <summary>A ball his team-mate last played, rolling along the x axis.</summary>
            private void PlaceBall(int xDm, int yDm, int speedDmPerSecond)
            {
                int x = xDm * UnitsPerDm, y = yDm * UnitsPerDm;
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, x);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, y);
                Set(_sim, "_ballAgoX", x);
                Set(_sim, "_ballAgoY", y);
                Set(_ball, "Vx", speedDmPerSecond * UnitsPerDm / Cfg.TicksPerSecond);
                Set(_ball, "Vy", 0);
                Set(_ball, "_z", 0);
                Set(_ball, "_vz", 0);
                Set(_ball, "Dead", false);
                Set(_ball, "JustStruck", false);
                Set(_ball, "OwnerSide", -1);
                Set(_ball, "OwnerSlot", -1);
                Set(_ball, "LastTouchSide", 0);
                Set(_ball, "LastTouchSlot", Slot == 1 ? 2 : 1);
            }

            private static object Get(object o, string name) => o.GetType().GetField(name, Any)!.GetValue(o)!;

            private static void Set(object o, string name, object? value) => o.GetType().GetField(name, Any)!.SetValue(o, value);
        }
    }
}
