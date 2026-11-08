using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The central zone near goal (issue #206, free-ball spec R7/R8), counted off a finished match's
    /// position stream per side: on-ball spells that reach the zone, how many of them no defender is
    /// within 3 m of, and on-ball spells that touch the opponent's penalty area. The same spells as
    /// doc §11.3's StatsBomb ones, so each reading has an event band. A measuring instrument: it
    /// reads the report and touches nothing.
    ///
    /// Definitions:
    ///   * Spell: an unbroken run of one carrier on the ball; loose frames while he runs with it do
    ///     not break it. A kick filed for a man who was not holding it (a first-time shot, pass or
    ///     header) is a spell of its own, where he stood the frame before the kick.
    ///   * Central zone: within <see cref="ZoneRangeDm"/> of the centre of the goal the side attacks
    ///     (inside the width of the penalty area by construction). A spell is counted once, on its
    ///     first frame in the zone, and is unpressed when no outfield opponent is within
    ///     <see cref="FreeRadiusDm"/> of him on that frame.
    ///   * Penalty-area spell: any of its frames inside the opponent's penalty area.
    ///   * A set-piece taker's first spell (the restart itself, a penalty included) is left out.
    ///     Each side attacks the same goal all match: the engine does not swap ends.
    ///   * Source (issue #210): how the ball reached the zone and the box, and what came of the
    ///     spell (shots, goals, location xG), see the Sources part.
    /// </summary>
    public sealed partial class CentralZoneAnalyzer
    {
        public const int ZoneRangeDm = 160;
        public const int FreeRadiusDm = 30;

        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();

        private PositionStream _s = null!;
        private CentralZoneMetrics _m = null!;

        private int _carrier;
        private bool _countable, _box, _zone, _free;
        private bool _boxFree;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static CentralZoneMetrics? Analyze(MatchReport report) => new CentralZoneAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public CentralZoneMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            _s = s;
            _m = new CentralZoneMetrics();
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, s.TickCount, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, s.TickCount, home: false);
            FindSentOff(s);
            ResetSources();
            Walk(s.TickCount);
            return _m;
        }

        private void FindSentOff(PositionStream s)
        {
            int n = s.PlayerCount;
            if (_sentOffFrom.Length != 2 * n) _sentOffFrom = new int[2 * n];
            for (int i = 0; i < _sentOffFrom.Length; i++) _sentOffFrom[i] = int.MaxValue;

            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= n) continue;
                int k = (a.Home ? 0 : n) + a.Slot;
                if (a.Tick < _sentOffFrom[k]) _sentOffFrom[k] = a.Tick;
            }
        }

        private void Walk(int ticks)
        {
            List<BallAction> actions = _s.Actions;
            int next = 0;
            int ended = PositionStream.NoOwner, endedAt = -1;
            bool afterRestart = false;
            _carrier = PositionStream.NoOwner;

            for (int t = 0; t < ticks; t++)
            {
                // Actions filed on a frame come before its owner: a kick is filed on the first frame after it.
                for (; next < actions.Count && actions[next].Tick <= t; next++)
                {
                    BallAction a = actions[next];
                    if (IsRestart(a.Kind))
                    {
                        Close();
                        afterRestart = true;
                        _restartPending = true;
                        continue;
                    }

                    bool own = _carrier != PositionStream.NoOwner && a.Slot >= 0 && _s.OwnerCode(a.Home, a.Slot) == _carrier;
                    if (IsKick(a.Kind) && !own && a.Slot >= 0)
                    {
                        // A first-time kick: a spell of one frame, where he stood before it.
                        Close();
                        int code = _s.OwnerCode(a.Home, a.Slot);
                        int before = a.Tick > 0 ? a.Tick - 1 : 0;
                        Open(code, before, ref afterRestart);
                        Visit(before, code);
                        EndWith(a);
                        Close();
                        ended = code;
                        endedAt = a.Tick;
                        Heard(a);
                        continue;
                    }

                    if (_carrier == PositionStream.NoOwner)
                    {
                        Heard(a);
                        continue;
                    }

                    // His own touch filed late (the recovery that won him the ball) leaves the spell going.
                    if (own && !IsKick(a.Kind) && !IsStoppage(a.Kind))
                    {
                        Heard(a);
                        continue;
                    }

                    ended = _carrier;
                    endedAt = a.Tick;
                    if (own) EndWith(a);
                    Close();
                    Heard(a);
                }

                int owner = _s.Owner[t];
                // A man is sometimes still shown holding the ball on the frame his spell ended (his own strike).
                if (owner == PositionStream.NoOwner || (owner == ended && t <= endedAt)) continue;

                if (owner != _carrier)
                {
                    Close();
                    Open(owner, t, ref afterRestart);
                }

                Visit(t, owner);
                _lastHolder = owner;
                _lastHeldAt = t;
            }

            Close();
        }

        private void Open(int code, int frame, ref bool afterRestart)
        {
            _lastFrame = frame;
            _carrier = code;
            _countable = !afterRestart;
            afterRestart = false;
            _box = _zone = _free = _boxFree = false;
            _visited = false;
            if (_countable) _source = Classify(code);
        }

        private void Visit(int t, int code)
        {
            if (!_countable) return;
            _s.TryOwner(code, out bool home, out int slot);
            int[] xy = home ? _s.HomeXY : _s.AwayXY;
            int x = _s.PlayerX(xy, t, slot), y = _s.PlayerY(xy, t, slot);
            int goalX = MovementGeometry.AttackedGoalX(home);

            int depth = goalX > x ? goalX - x : x - goalX;
            int off = y > Pitch.CenterY ? y - Pitch.CenterY : Pitch.CenterY - y;
            bool first = !_visited;
            _visited = true;
            if (first) _startDm = (int)Math.Sqrt(DistanceSq(x, y, goalX, Pitch.CenterY));
            if (!_box && depth <= MovementGeometry.BoxDepthDm && off <= MovementGeometry.BoxHalfWidthDm)
            {
                _box = true;
                _boxFree = IsFree(t, home, x, y);
                _boxSource = first ? _source : ChanceSource.Carry;
            }

            if (_zone || DistanceSq(x, y, goalX, Pitch.CenterY) > (long)ZoneRangeDm * ZoneRangeDm) return;
            _zone = true;
            _free = IsFree(t, home, x, y);
            _zoneSource = first ? _source : ChanceSource.Carry;
        }

        private void Close()
        {
            if (_carrier != PositionStream.NoOwner && _countable)
            {
                _s.TryOwner(_carrier, out bool home, out _);
                _m.Add(home, _box, _zone, _free);
                AddSources(home);
            }

            _carrier = PositionStream.NoOwner;
            _countable = false;
            _shot = false;
        }

        private bool IsFree(int t, bool home, int x, int y)
        {
            int n = _s.PlayerCount;
            int foe = home ? 1 : 0;
            int[] xy = home ? _s.AwayXY : _s.HomeXY;
            for (int slot = 0; slot < n; slot++)
            {
                if (slot == _keeper[foe] || t >= _sentOffFrom[foe * n + slot]) continue;
                if (DistanceSq(_s.PlayerX(xy, t, slot), _s.PlayerY(xy, t, slot), x, y) <= (long)FreeRadiusDm * FreeRadiusDm)
                    return false;
            }

            return true;
        }

        private static long DistanceSq(int ax, int ay, int bx, int by)
        {
            long dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }

        private static bool IsKick(BallActionKind k) =>
            k == BallActionKind.Shot || k == BallActionKind.Pass || k == BallActionKind.LongBall
            || k == BallActionKind.Cross || k == BallActionKind.Clearance;

        private static bool IsStoppage(BallActionKind k) =>
            k == BallActionKind.Goal || k == BallActionKind.Foul || k == BallActionKind.Offside
            || k == BallActionKind.HalfTime || k == BallActionKind.Kickoff;

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick || k == BallActionKind.Corner
            || k == BallActionKind.FreeKick || k == BallActionKind.Penalty;
    }
}
