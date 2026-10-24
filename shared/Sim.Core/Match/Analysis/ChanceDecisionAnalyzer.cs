using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The chance-decision table of the believable-match spec (R2), counted off a finished match's
    /// position stream: what a man holding the ball near the goal he attacks does next. A measuring
    /// instrument like <see cref="ShotPassAnalyzer"/>: it reads the report and touches nothing.
    ///
    /// Definitions:
    ///   * Chance zone: the carrier stands within <see cref="ZoneRangeDm"/> of the centre of the
    ///     goal he attacks and within <see cref="ZoneHalfWidthDm"/> of the pitch's long axis. Each
    ///     side attacks the same goal all match: the engine does not swap ends at half time.
    ///   * Possession: an unbroken spell of one carrier; loose frames while he runs with the ball do
    ///     not break it. Counted once, in the bucket of its FIRST frame in the zone: free when no
    ///     outfield opponent is within <see cref="FreeRadiusDm"/> of him, near within
    ///     <see cref="NearRangeDm"/> of the goal centre. A set-piece taker's first spell is a set
    ///     piece (a penalty from the spot), not a chance, and is left out.
    ///   * Action: his own next ball action from that frame. Shot; Pass, LongBall or Cross read as
    ///     back when the ball ends more than <see cref="BackMarginDm"/> farther from the goal
    ///     centre than he stood, forward/square otherwise; Dribble is a carry; Clearance a clear.
    ///     Anything else first (an opponent's touch or hold, a foul, any stoppage) is lost. A
    ///     possession still open at full time made no decision and is not counted.
    ///   * Where a pass ends: the ball on the first frame anybody holds it after the kick, or on
    ///     the frame before the next action filed (a touch or a stoppage), whichever comes first.
    /// </summary>
    public sealed class ChanceDecisionAnalyzer
    {
        public const int ZoneRangeDm = 160;
        public const int ZoneHalfWidthDm = 100;
        public const int NearRangeDm = 120;
        public const int FreeRadiusDm = 30;
        public const int BackMarginDm = 30;

        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();

        private PositionStream _s = null!;
        private ChanceDecisionMetrics _m = null!;

        // The spell being followed, and the chance it opened when it reached the zone.
        private int _carrier;
        private bool _countable;
        private bool _pending;
        private bool _free;
        private bool _near;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static ChanceDecisionMetrics? Analyze(MatchReport report) => new ChanceDecisionAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public ChanceDecisionMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            int ticks = s.TickCount;
            _s = s;
            _m = new ChanceDecisionMetrics();
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: false);
            FindSentOff(s);
            Walk(ticks);
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
            _pending = false;

            for (int t = 0; t < ticks; t++)
            {
                // Actions filed on a frame come before its owner: a kick is filed on the first frame after it.
                for (; next < actions.Count && actions[next].Tick <= t; next++)
                {
                    BallAction a = actions[next];
                    if (IsRestart(a.Kind)) afterRestart = true;
                    if (_carrier == PositionStream.NoOwner) continue;

                    if (a.Slot >= 0 && _s.OwnerCode(a.Home, a.Slot) == _carrier)
                    {
                        if (a.Kind == BallActionKind.Dribble) Decide(ChanceAction.Carry);
                        else if (IsKick(a.Kind)) Decide(Classify(a, next));
                        // His own touch filed late (the recovery that won him the ball) leaves the spell going.
                        if (!IsKick(a.Kind) && !IsStoppage(a.Kind)) continue;
                    }

                    Decide(ChanceAction.Lost);
                    ended = _carrier;
                    endedAt = a.Tick;
                    _carrier = PositionStream.NoOwner;
                }

                int code = _s.Owner[t];
                // A man is sometimes still shown holding the ball on the frame his spell ended (his own strike).
                if (code == PositionStream.NoOwner || (code == ended && t <= endedAt)) continue;

                if (code != _carrier)
                {
                    Decide(ChanceAction.Lost);
                    _carrier = code;
                    _countable = !afterRestart;
                    afterRestart = false;
                }

                if (_countable) OpenIfInZone(t, code);
            }
        }

        private void OpenIfInZone(int t, int code)
        {
            _s.TryOwner(code, out bool home, out int slot);
            int[] xy = home ? _s.HomeXY : _s.AwayXY;
            int x = _s.PlayerX(xy, t, slot), y = _s.PlayerY(xy, t, slot);
            if (Math.Abs(y - Pitch.CenterY) > ZoneHalfWidthDm) return;
            long toGoal = DistanceSq(x, y, MovementGeometry.AttackedGoalX(home), Pitch.CenterY);
            if (toGoal > (long)ZoneRangeDm * ZoneRangeDm) return;

            _countable = false;
            _pending = true;
            _near = toGoal <= (long)NearRangeDm * NearRangeDm;
            _free = IsFree(t, home, x, y);
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

        /// <summary>Counts the open chance, if any, as <paramref name="action"/>; a spell decides once.</summary>
        private void Decide(ChanceAction action)
        {
            if (!_pending) return;
            _pending = false;
            _m.Add(_free, _near, action);
        }

        private ChanceAction Classify(BallAction kick, int index)
        {
            if (kick.Kind == BallActionKind.Shot) return ChanceAction.Shot;
            if (kick.Kind == BallActionKind.Clearance) return ChanceAction.Clear;

            int goalX = MovementGeometry.AttackedGoalX(kick.Home);
            int kf = kick.Tick > 0 ? kick.Tick - 1 : 0;
            int[] xy = kick.Home ? _s.HomeXY : _s.AwayXY;
            double from = Math.Sqrt(DistanceSq(_s.PlayerX(xy, kf, kick.Slot), _s.PlayerY(xy, kf, kick.Slot), goalX, Pitch.CenterY));

            int end = PassEnd(kick.Tick, index);
            double to = Math.Sqrt(DistanceSq(_s.BallXY[end * 2], _s.BallXY[end * 2 + 1], goalX, Pitch.CenterY));
            return to - from > BackMarginDm ? ChanceAction.PassBack : ChanceAction.PassForward;
        }

        /// <summary>The frame whose ball is where the pass at <paramref name="index"/> ended.</summary>
        private int PassEnd(int kickTick, int index)
        {
            List<BallAction> actions = _s.Actions;
            int filed = index + 1 < actions.Count ? actions[index + 1].Tick : int.MaxValue;
            int last = _s.TickCount - 1;
            for (int f = kickTick + 1; f <= last; f++)
            {
                if (f >= filed) return Math.Max(kickTick, f - 1);
                if (_s.Owner[f] != PositionStream.NoOwner) return f;
            }

            return Math.Min(kickTick, last);
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
            || k == BallActionKind.HalfTime || k == BallActionKind.Kickoff || IsRestart(k);

        // A kick-off is never taken in the chance zone, so it needs no exclusion.
        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick || k == BallActionKind.Corner
            || k == BallActionKind.FreeKick || k == BallActionKind.Penalty;
    }
}
