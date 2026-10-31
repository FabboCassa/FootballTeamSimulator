using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Analysis;
using Sim.Core.Match.Movement;
using Sim.Core.Random;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// Free-ball spec R3 (issue #178): a free ball moves only by its physics, and a man changes it
    /// only through a touch within reach — his feet within ControlRadiusDm of its path, the ball no
    /// higher than a header. The scene tests drive the simulator's own ball and control resolution
    /// (private, so through reflection) on a simulator a real match has set up, with every other
    /// man moved far out of the ball's way; the match tests read the reach the engine stamps on
    /// every touch it makes.
    /// </summary>
    [TestFixture]
    public class FreeBallReachTests
    {
        private const int UnitsPerDm = 16;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MatchBalance Cfg = new BalanceConfig().Match;
        private static League _league = null!;

        [OneTimeSetUp]
        public void GenerateWorld()
        {
            _league = new LeagueGenerator().Generate(new Pcg32(20260611));
        }

        [Test]
        public void ABallPassing1_5mFromAMan_WithoutATouch_KeepsItsLine()
        {
            var scene = new BallScene(PlayedSimulator(), manYDm: 340 + 15);
            scene.Roll(fromXDm: 300, yDm: 340, speedDmPerSecond: 120, heightDm: 0, ticks: 20);

            Assert.That(scene.Touches, Is.Empty, "nobody touched it");
            Assert.That(scene.Free, Is.True, "nobody holds it");
            Assert.That(scene.Ys.Distinct(), Is.EquivalentTo(new[] { 340 * UnitsPerDm }), "it kept its line");
            Assert.That(scene.MaxSpeedGain, Is.LessThanOrEqualTo(0), "and it only slowed down");
            Assert.That(scene.X, Is.GreaterThan(scene.ManX + 50 * UnitsPerDm), "it ran on past him");
        }

        [Test]
        public void ABallPassingWithinHisReach_IsTouched_WhereItMetHim()
        {
            var scene = new BallScene(PlayedSimulator(), manYDm: 340 + 8);
            scene.Roll(fromXDm: 300, yDm: 340, speedDmPerSecond: 120, heightDm: 0, ticks: 20);

            Assert.That(scene.Touches, Is.Not.Empty, "a ball 0.8 m from his feet is his");
            BallEvent touch = scene.Touches[0];
            Assert.That((touch.Side, touch.Slot), Is.EqualTo((0, scene.Slot)));
            Assert.That(touch.FromFeetMm, Is.LessThanOrEqualTo(Cfg.ControlRadiusDm * 100), "met within reach");
            int fromManDm = (int)System.Math.Round(System.Math.Sqrt(
                System.Math.Pow(touch.XDm - scene.ManX / UnitsPerDm, 2) + System.Math.Pow(touch.YDm - (340 + 8), 2)));
            Assert.That(fromManDm, Is.LessThanOrEqualTo(Cfg.ControlRadiusDm),
                "where it met him on its path, not where the tick's flight ended");
        }

        [Test]
        public void ABallAboveHeaderHeight_PassesOverHim()
        {
            var scene = new BallScene(PlayedSimulator(), manYDm: 340 + 3);
            scene.Roll(fromXDm: 380, yDm: 340, speedDmPerSecond: 120, heightDm: 40, ticks: 3);

            Assert.That(scene.Touches, Is.Empty, "nobody heads a ball four metres up");
            Assert.That(scene.Free, Is.True);
        }

        [Test]
        public void ARealMatch_TouchesTheBallOnlyWithinReach()
        {
            int touches = 0, beyond = 0;
            for (int i = 0; i < 4; i++)
            {
                var log = new BallEventLog();
                new MatchEngine(new BalanceConfig()) { BallEventSink = log }.Simulate(
                    LineupSelector.BestEleven(_league.Clubs[2 * i]), LineupSelector.BestEleven(_league.Clubs[2 * i + 1]),
                    new Pcg32(9100 + (ulong)i));
                touches += log.Records.Count(e => e.Kind == BallEventKind.Touch || e.Kind == BallEventKind.Deflection);
                beyond += TouchReachAnalyzer.BeyondReach(log.Records, Cfg);
            }

            Assert.That(touches, Is.GreaterThan(4000), "the sample holds the touches of four matches");
            Assert.That(beyond, Is.Zero, "no touch is made from beyond reach");
        }

        [Test]
        public void TheReachAnalyzer_CountsTouchesBeyondTheFeet_AboveTheHead_AndBeyondTheKeepersHands()
        {
            int feet = Cfg.ControlRadiusDm * 100, hands = (Cfg.ControlRadiusDm + Cfg.KeeperSaveReachDm) * 100;
            var events = new List<BallEvent>
            {
                Touch(BallEventKind.Touch, feet, 0, keeper: false),             // at the end of his reach: in
                Touch(BallEventKind.Touch, feet + 1, 0, keeper: false),         // a millimetre past it: out
                Touch(BallEventKind.Deflection, feet + 400, 0, keeper: false),  // a deflection is a touch: out
                Touch(BallEventKind.Touch, 0, Cfg.TouchHeightDm, keeper: false),     // a header: in
                Touch(BallEventKind.Touch, 0, Cfg.TouchHeightDm + 1, keeper: false), // above it: out
                Touch(BallEventKind.Touch, hands, Cfg.TouchHeightDm + Cfg.KeeperArmHeightDm, keeper: true), // a keeper's hands, up high: in
                Touch(BallEventKind.Touch, hands + 1, 0, keeper: true),         // past them: out
                Touch(BallEventKind.Touch, 0, Cfg.TouchHeightDm + Cfg.KeeperArmHeightDm + 1, keeper: true), // above them: out
                new BallEvent { Kind = BallEventKind.Bounce, Side = -1, ZDm = 99 } // a bounce is nobody's touch
            };

            Assert.That(TouchReachAnalyzer.BeyondReach(events, Cfg), Is.EqualTo(5));
        }

        private static BallEvent Touch(BallEventKind kind, int fromFeetMm, int zDm, bool keeper) =>
            new BallEvent { Kind = kind, Side = 0, Slot = 4, FromFeetMm = fromFeetMm, ZDm = zDm, Keeper = keeper };

        private MatchSimulator PlayedSimulator()
        {
            var sim = new MatchSimulator(Cfg);
            Lineup home = LineupSelector.BestEleven(_league.Clubs[9]), away = LineupSelector.BestEleven(_league.Clubs[10]);
            var report = new MatchReport { HomeClubId = home.ClubId, AwayClubId = away.ClubId };
            sim.Generate(home, away, report, new Pcg32(7), null);
            return sim;
        }

        /// <summary>
        /// A free ball rolled past one home outfielder at (400 dm, <c>manYDm</c>), everybody else in a
        /// heap in a far corner; each tick the ball steps and control is resolved, as the match does.
        /// </summary>
        private sealed class BallScene
        {
            private readonly MatchSimulator _sim;
            private readonly object _ball;
            private readonly BallEventLog _log = new BallEventLog();

            public BallScene(MatchSimulator sim, int manYDm)
            {
                _sim = sim;
                _ball = Field<object>("_ball");
                int[] px = Field<int[]>("_px"), py = Field<int[]>("_py"), vx = Field<int[]>("_vx"), vy = Field<int[]>("_vy");
                bool[] keeper = Field<bool[]>("_keeper"), sentOff = Field<bool[]>("_sentOff");

                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = 50 * UnitsPerDm;
                    py[i] = 50 * UnitsPerDm;
                    vx[i] = 0;
                    vy[i] = 0;
                }

                while (keeper[Slot] || sentOff[Slot]) Slot++;
                px[Slot] = 400 * UnitsPerDm;
                py[Slot] = manYDm * UnitsPerDm;
                ManX = px[Slot];

                // In open play, a ball his own side last played and nobody was meant to have.
                int[] receiver = Field<int[]>("_receiver");
                receiver[0] = -1;
                receiver[1] = -1;
                Set(_sim, "_releasedBy", -1);
                Set(_sim, "_releasedUntil", -1);
                object ctx = Field<object>("_ctx");
                ctx.GetType().GetField("ShotLive")!.SetValue(ctx, false);
                object offside = Field<object>("_offside");
                offside.GetType().GetMethod("ClearFlags")!.Invoke(offside, null);
                Set(_ball, "Dead", false);
                Set(_ball, "OwnerSide", -1);
                Set(_ball, "OwnerSlot", -1);
                Set(_ball, "LastTouchSide", 0);
                Set(_ball, "LastTouchSlot", Slot == 1 ? 2 : 1);
                Set(_ball, "EventSink", _log);
            }

            public int Slot { get; }
            public int ManX { get; }
            public List<int> Ys { get; } = new List<int>();
            public int MaxSpeedGain { get; private set; } = int.MinValue;
            public List<BallEvent> Touches => _log.Records.Where(e => e.Kind != BallEventKind.Bounce).ToList();
            public bool Free => (int)Get(_ball, "OwnerSide") < 0;
            public int X => (int)Get(_ball, "X");

            public void Roll(int fromXDm, int yDm, int speedDmPerSecond, int heightDm, int ticks)
            {
                int x = fromXDm * UnitsPerDm, y = yDm * UnitsPerDm;
                foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, x);
                foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, y);
                Set(_ball, "Vx", speedDmPerSecond * UnitsPerDm / Cfg.TicksPerSecond);
                Set(_ball, "Vy", 0);
                Set(_ball, "_z", heightDm * 1000);
                Set(_ball, "_vz", 0);

                MethodInfo advance = _ball.GetType().GetMethod("Advance")!;
                MethodInfo resolve = typeof(MatchSimulator).GetMethod("ResolveControl", Private)!;
                for (int tick = 0; tick < ticks && Free; tick++)
                {
                    // The two positions a gather is judged against, as the match's tick keeps them.
                    Set(_sim, "_ballTwoAgoX", Get(_sim, "_ballAgoX"));
                    Set(_sim, "_ballTwoAgoY", Get(_sim, "_ballAgoY"));
                    Set(_sim, "_ballAgoX", Get(_ball, "X"));
                    Set(_sim, "_ballAgoY", Get(_ball, "Y"));

                    int before = Speed();
                    advance.Invoke(_ball, null);
                    resolve.Invoke(_sim, new object[] { tick });
                    if (!Free) break;
                    Ys.Add((int)Get(_ball, "Y"));
                    if (Speed() - before > MaxSpeedGain) MaxSpeedGain = Speed() - before;
                }
            }

            private int Speed() => System.Math.Abs((int)Get(_ball, "Vx")) + System.Math.Abs((int)Get(_ball, "Vy"));

            private T Field<T>(string name) => (T)Get(_sim, name);

            private static object Get(object o, string name) =>
                o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(o)!;

            private static void Set(object o, string name, object? value) =>
                o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(o, value);
        }
    }
}
