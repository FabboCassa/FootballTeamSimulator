using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Match.Movement;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Sim.Core.Tests.Match
{
    /// <summary>
    /// A wide carrier scripted on a simulator set up for a real match, 4-3-3 against 4-3-3 (private,
    /// so through reflection), for the free-ball spec R5 tests (issue #183). Every man starts heaped
    /// in the home corner; a test places the ones it reads. Home attacks toward x = Pitch.LengthDm,
    /// and the home carrier is the winger whose flank is the low-y touchline.
    /// </summary>
    internal sealed class CrossScene
    {
        public const int UnitsPerDm = 16;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static readonly MatchBalance Cfg = new BalanceConfig().Match;

        private readonly MatchSimulator _sim;
        private readonly object _ball;
        private readonly object _brain;
        private readonly MovementLog _log = new();
        private readonly int _n;
        private int _tick = 1;

        public CrossScene(League league)
        {
            _sim = new MatchSimulator(Cfg);
            Home = LineupSelector.BestEleven(league.Clubs[3], Formation.F433);
            Away = LineupSelector.BestEleven(league.Clubs[6], Formation.F433);
            var report = new MatchReport { HomeClubId = Home.ClubId, AwayClubId = Away.ClubId };
            Set(_sim, "_rng", new Pcg32(183));
            typeof(MatchSimulator).GetMethod("Setup", Any)!.Invoke(_sim, new object?[] { Home, Away, report, null, 90 * Cfg.TicksPerMinute });
            _ball = Get(_sim, "_ball");
            _brain = Get(_sim, "_brain");
            _n = Field<int[]>("_px").Length / 2;
            for (int slot = 0; slot < _n; slot++)
            {
                _log.Watch(0, slot);
                _log.Watch(1, slot);
            }
            _sim.MovementSink = _log;
            _sim.DecisionSink = Decisions;

            int[] wingers = Slots(Home, PositionRole.Winger);
            int[] baseY = Field<int[]>("_baseY");
            Carrier = baseY[wingers[0]] < baseY[wingers[1]] ? wingers[0] : wingers[1];
            FarWinger = Carrier == wingers[0] ? wingers[1] : wingers[0];
            Striker = Slots(Home, PositionRole.Striker)[0];
            Midfielders = Slots(Home, PositionRole.CentralMidfielder);
            BackLine = Slots(Away, PositionRole.CentreBack).Concat(Slots(Away, PositionRole.FullBack)).ToArray();
            Keeper = Slots(Away, PositionRole.Goalkeeper)[0];
        }

        /// <summary>What the men on the ball chose, as the explained replay hears it.</summary>
        public DecisionLog Decisions { get; } = new();

        public Lineup Home { get; }
        public Lineup Away { get; }

        /// <summary>Home slots.</summary>
        public int Carrier { get; }
        public int FarWinger { get; }
        public int Striker { get; }
        public int[] Midfielders { get; }

        /// <summary>Away slots.</summary>
        public int[] BackLine { get; }
        public int Keeper { get; }

        /// <summary>Everybody back in the home corner, at rest, the away keeper on his line, nothing in flight.</summary>
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

            Place(1, Keeper, Pitch.LengthDm - 5, Pitch.CenterY);
            int[] receiver = Field<int[]>("_receiver");
            receiver[0] = -1;
            receiver[1] = -1;
            object offside = Get(_sim, "_offside");
            offside.GetType().GetMethod("ClearFlags")!.Invoke(offside, null);
        }

        public void Place(int side, int slot, int xDm, int yDm)
        {
            int k = side * _n + slot;
            Field<int[]>("_px")[k] = xDm * UnitsPerDm;
            Field<int[]>("_py")[k] = yDm * UnitsPerDm;
            Field<int[]>("_vx")[k] = 0;
            Field<int[]>("_vy")[k] = 0;
        }

        public int X(int side, int slot) => Field<int[]>("_px")[side * _n + slot] / UnitsPerDm;
        public int Y(int side, int slot) => Field<int[]>("_py")[side * _n + slot] / UnitsPerDm;

        /// <summary>The ball at the feet of home man <paramref name="slot"/>, live and on the grass.</summary>
        public void GiveBall(int slot)
        {
            int bx = Field<int[]>("_px")[slot], by = Field<int[]>("_py")[slot];
            foreach (string f in new[] { "X", "PrevX" }) Set(_ball, f, bx);
            foreach (string f in new[] { "Y", "PrevY" }) Set(_ball, f, by);
            foreach (string f in new[] { "_ballAgoX", "_ballTwoAgoX" }) Set(_sim, f, bx);
            foreach (string f in new[] { "_ballAgoY", "_ballTwoAgoY" }) Set(_sim, f, by);
            Set(_ball, "_z", 0);
            Set(_ball, "_vz", 0);
            _ball.GetType().GetMethod("Take")!.Invoke(_ball, new object[] { 0, slot });
        }

        /// <summary>One tick of the team brains, then the home men in <paramref name="movers"/> move by theirs, traced.</summary>
        public void Tick(params int[] movers)
        {
            _tick++;
            _brain.GetType().GetMethod("UpdateTeams")!.Invoke(_brain, new object[] { _tick });
            MethodInfo traced = typeof(MatchSimulator).GetMethod("MoveTraced", Any)!;
            foreach (int slot in movers) traced.Invoke(_sim, new object[] { _tick, 0, slot });
        }

        /// <summary>The last traced move of home man <paramref name="slot"/>.</summary>
        public MovementRecord Last(int slot) => _log.Records.Last(r => r.Side == 0 && r.Slot == slot);

        /// <summary>One tick of the team brains, then the away men in <paramref name="movers"/> move by theirs, traced.</summary>
        public void TickAway(params int[] movers)
        {
            _tick++;
            _brain.GetType().GetMethod("UpdateTeams")!.Invoke(_brain, new object[] { _tick });
            MethodInfo traced = typeof(MatchSimulator).GetMethod("MoveTraced", Any)!;
            foreach (int slot in movers) traced.Invoke(_sim, new object[] { _tick, 1, slot });
        }

        /// <summary>The last traced move of away man <paramref name="slot"/>.</summary>
        public MovementRecord LastAway(int slot) => _log.Records.Last(r => r.Side == 1 && r.Slot == slot);

        /// <summary>The away man closing the receiver of a pass into the central zone (issue #206), -1 for none.</summary>
        public int AwayZoneCloser => ((int[])Get(_brain, "_zoneCloser"))[1];

        /// <summary>Where the last home pass is going (dm).</summary>
        public int ReceiveX => Field<int[]>("_receiveX")[0] / UnitsPerDm;
        public int ReceiveY => Field<int[]>("_receiveY")[0] / UnitsPerDm;

        /// <summary>
        /// The home man on the ball decides, his hold run out and settled on it, as the brain decides
        /// in a match: the team brains read the pitch first.
        /// </summary>
        public void Act(int slot)
        {
            _tick++;
            Field<int[]>("_hold")[slot] = 0;
            _brain.GetType().GetMethod("UpdateTeams")!.Invoke(_brain, new object[] { _tick });
            Set(_brain, "_possessor", slot);
            Set(_brain, "_possessedSince", _tick - 100);
            _brain.GetType().GetMethod("Act")!.Invoke(_brain, new object[] { _tick, 0, slot });
        }

        public bool BallOwned => (int)Get(_ball, "OwnerSide") >= 0;
        public int BallX => (int)Get(_ball, "X") / UnitsPerDm;
        public int BallY => (int)Get(_ball, "Y") / UnitsPerDm;
        public int BallZ => (int)_ball.GetType().GetProperty("Z")!.GetValue(_ball)!;

        /// <summary>Who the ball was last played to (home receiver slot), -1 for nobody.</summary>
        public int Receiver => Field<int[]>("_receiver")[0];

        /// <summary>The home side delivered a set piece into the box just now.</summary>
        public void SetPieceDelivered() => Field<int[]>("_setPieceCrossTick")[0] = _tick;

        /// <summary>The ball's flight on its own for one tick (nobody may touch it).</summary>
        public void StepBall() => _ball.GetType().GetMethod("Advance")!.Invoke(_ball, null);

        private static int[] Slots(Lineup lineup, PositionRole role)
        {
            var slots = new List<int>();
            for (int i = 0; i < lineup.Slots.Count; i++)
                if (lineup.Slots[i].Role == role) slots.Add(i);
            if (slots.Count == 0) throw new InvalidOperationException($"no {role} in the lineup");
            return slots.ToArray();
        }

        private T Field<T>(string name) => (T)Get(_sim, name);

        private static object Get(object o, string name)
        {
            FieldInfo? f = o.GetType().GetField(name, Any);
            if (f != null) return f.GetValue(o)!;
            return o.GetType().GetProperty(name, Any)!.GetValue(o)!;
        }

        private static void Set(object o, string name, object? value)
        {
            FieldInfo? f = o.GetType().GetField(name, Any);
            if (f != null) f.SetValue(o, value);
            else o.GetType().GetProperty(name, Any)!.SetValue(o, value);
        }
    }
}
