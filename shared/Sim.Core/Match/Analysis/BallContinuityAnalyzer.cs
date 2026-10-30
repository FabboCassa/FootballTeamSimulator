using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The ball-continuity, shot-flight and keeper readings of the believable-match spec (R2, read
    /// against R1, R5 and R6), counted off a finished match's position stream. A measuring
    /// instrument like <see cref="ShotPassAnalyzer"/>: it reads the report and touches nothing.
    ///
    /// Definitions (a kick is filed on the first frame after it, so the ball is at the boot on the
    /// frame before its action; each side attacks the same goal all match):
    ///   * Jump: a frame-to-frame ball step longer than <see cref="JumpDm"/>. The stream has no
    ///     director cuts, so every one counts; those within <see cref="RestartNearFrames"/> of a
    ///     restart being filed (throw-in, goal kick, corner, free kick, penalty, kick-off) are also
    ///     counted apart.
    ///   * Restart spot: the ball on the frame before the next action after the restart is filed
    ///     (the taker's kick). One the half-time or full-time whistle beats is read on the frame
    ///     before that whistle, or the last, if its ball lies still there; still on its way, it
    ///     is not read. The ball arrives there on the
    ///     first frame of the run, back from the spot frame, it stays within
    ///     <see cref="SpotToleranceDm"/> of it. Placed: that arrival step is faster than
    ///     <see cref="TransportCapMps"/>, so no player carried or rolled it there. A restart whose
    ///     ball sat on the spot from the first frame (the opening kick-off) is not read.
    ///   * Ball speed: Pass, LongBall and Cross are passes, Shot is a shot. Each frame step from
    ///     the kick until the frame before anybody holds the ball or the next action is filed,
    ///     whichever is first (the snap to the receiver is the end, not the flight).
    ///   * Release: the last frame, at most <see cref="StrikeLookbackFrames"/> before the shot's
    ///     own, the striker held the ball; else the frame before the shot is filed.
    ///   * Wide shot: a shot settled by a Miss. It crosses when the ball is beyond the goal line it
    ///     attacks on any frame from release to the frame before the next restart is filed; else
    ///     the gap is the nearest it came to the line.
    ///   * Goal behind the line: from the Goal's frame to the frame before the next kick-off, the
    ///     ball is at least <see cref="BehindLineDm"/> beyond the line, between the posts.
    ///   * Keeper displacement: the defending keeper's move from release to the save's frame, or
    ///     on a goal to the first frame the ball reaches the line.
    ///   * Keeper near (R6): on a save, the shot's path runs from the ball at release through its
    ///     last free frame before the save, to the keeper's goal line. Near: the keeper on the save
    ///     frame within <see cref="KeeperNearDm"/> of the crossing point or of that path (the ball
    ///     at contact; the stream snaps a caught ball to his feet, so the ball's own frame cannot
    ///     say). A save with no free frame between release and save has no readable path.
    /// </summary>
    public sealed class BallContinuityAnalyzer
    {
        public const int JumpDm = 80;
        public const int RestartNearFrames = 1;
        public const int TransportCapMps = 8;
        public const int SpotToleranceDm = 5;
        public const int BehindLineDm = 10;
        public const int KeeperNearDm = 15;
        public const int StrikeLookbackFrames = 2;

        private const int DmPerM = 10;
        private const int None = -1;

        private PositionStream _s = null!;
        private BallContinuityMetrics _m = null!;
        private int _ticks;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static BallContinuityMetrics? Analyze(MatchReport report) => new BallContinuityAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public BallContinuityMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            _s = s;
            _ticks = s.TickCount;
            _m = new BallContinuityMetrics();
            CountJumps();
            CountRestarts();
            CountKicks();
            CountGoals();
            return _m;
        }

        // ------------------------------------------------------------------ jumps and restarts

        private void CountJumps()
        {
            var nearRestart = new bool[_ticks];
            foreach (BallAction a in _s.Actions)
            {
                if (!IsRestart(a.Kind)) continue;
                for (int t = a.Tick - RestartNearFrames; t <= a.Tick + RestartNearFrames; t++)
                    if (t >= 0 && t < _ticks) nearRestart[t] = true;
            }

            for (int t = 1; t < _ticks; t++)
            {
                if (Step(t) <= JumpDm) continue;
                _m.Jumps++;
                if (nearRestart[t]) _m.JumpsAtRestarts++;
            }
        }

        private void CountRestarts()
        {
            List<BallAction> actions = _s.Actions;
            double capDm = (double)TransportCapMps * DmPerM * 60 / TicksPerMinute();
            int lastTick = None;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction r = actions[i];
                if (!IsRestart(r.Kind) || r.Tick == lastTick || r.Tick < 0 || r.Tick >= _ticks) continue;
                lastTick = r.Tick;

                int spot = _ticks - 1;
                bool taken = false;
                for (int j = i + 1; j < actions.Count; j++)
                {
                    BallAction next = actions[j];
                    if (next.Tick <= r.Tick || next.Kind == BallActionKind.YellowCard || next.Kind == BallActionKind.RedCard) continue;
                    spot = Math.Min(next.Tick - 1, _ticks - 1);
                    taken = next.Kind != BallActionKind.HalfTime;
                    break;
                }

                if (!taken && Step(spot) > 0) continue;
                int sx = X(spot), sy = Y(spot);
                int arrival = spot;
                while (arrival > 0 && Distance(X(arrival - 1), Y(arrival - 1), sx, sy) <= SpotToleranceDm) arrival--;
                if (arrival == 0) continue;

                _m.Restarts++;
                if (Step(arrival) > capDm) _m.PlacedRestarts++;
            }
        }

        // ------------------------------------------------------------------ kicks: speeds, shot flight, keepers

        private void CountKicks()
        {
            List<BallAction> actions = _s.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                bool shot = a.Kind == BallActionKind.Shot;
                if (!shot && a.Kind != BallActionKind.Pass && a.Kind != BallActionKind.LongBall && a.Kind != BallActionKind.Cross)
                    continue;
                if (a.Tick <= 0 || a.Tick >= _ticks) continue;

                ReadSpeeds(a, i + 1 < actions.Count ? actions[i + 1].Tick : _ticks, shot);
                if (!shot) continue;

                int o = Outcome(actions, i);
                if (o == None) continue;
                BallAction outcome = actions[o];
                int release = Release(a);
                switch (outcome.Kind)
                {
                    case BallActionKind.Miss:
                        ReadWideShot(a, release, NextRestartTick(actions, o));
                        break;
                    case BallActionKind.Save:
                        ReadSave(release, outcome);
                        break;
                    case BallActionKind.Goal:
                        ReadGoalKeeper(a, release, outcome.Tick);
                        break;
                }
            }
        }

        private void ReadSpeeds(BallAction kick, int nextActionTick, bool shot)
        {
            int start = _s.Owner[kick.Tick] == _s.OwnerCode(kick.Home, kick.Slot) ? kick.Tick + 1 : kick.Tick;
            int end = Math.Min(nextActionTick, _ticks);
            double mpsPerDm = TicksPerMinute() / 600.0;
            for (int t = start; t < end && _s.Owner[t] == PositionStream.NoOwner; t++)
                _m.AddSpeed(shot, Step(t) * mpsPerDm);
        }

        private void ReadWideShot(BallAction shot, int release, int restartTick)
        {
            int lineX = MovementGeometry.AttackedGoalX(shot.Home);
            int last = Math.Min(restartTick, _ticks) - 1;
            int deepest = int.MinValue;
            for (int t = release; t <= last; t++) deepest = Math.Max(deepest, Depth(t, lineX));
            if (deepest == int.MinValue) return;

            _m.WideShots++;
            if (deepest > 0) _m.WideShotsCrossing++;
            else _m.WideShotShortSumM += -deepest / (double)DmPerM;
        }

        private void ReadSave(int release, BallAction save)
        {
            int contact = save.Tick;
            if (contact <= release || contact >= _ticks) return;
            int[] side = save.Home ? _s.HomeXY : _s.AwayXY;
            int kx = _s.PlayerX(side, contact, save.Slot), ky = _s.PlayerY(side, contact, save.Slot);
            _m.SaveDisplacements++;
            _m.SaveDisplacementSumM += Distance(_s.PlayerX(side, release, save.Slot), _s.PlayerY(side, release, save.Slot), kx, ky) / DmPerM;

            int x0 = X(release), y0 = Y(release);
            int free = None;
            for (int t = contact - 1; t > release; t--)
            {
                if (_s.Owner[t] != PositionStream.NoOwner || (X(t) == x0 && Y(t) == y0)) continue;
                free = t;
                break;
            }

            if (free == None) return;
            double dx = X(free) - x0, dy = Y(free) - y0;
            double lineX = MovementGeometry.OwnGoalX(save.Home);
            if (dx == 0 || (lineX - x0) * dx <= 0) return;

            double cy = y0 + dy * (lineX - x0) / dx;
            double toCrossing = Distance(kx, ky, lineX, cy);
            double toPath = DistanceToSegment(kx, ky, x0, y0, lineX, cy);
            _m.SavesRead++;
            if (Math.Min(toCrossing, toPath) <= KeeperNearDm) _m.SavesNear++;
        }

        private void ReadGoalKeeper(BallAction shot, int release, int goalTick)
        {
            int lineX = MovementGeometry.AttackedGoalX(shot.Home);
            int last = Math.Min(goalTick, _ticks - 1);
            int atLine = last;
            for (int t = release + 1; t <= last; t++)
            {
                if (Depth(t, lineX) < 0) continue;
                atLine = t;
                break;
            }

            if (atLine <= release) return;
            int keeper = MatchAnalyzer.InferKeeperSlot(_s, _ticks, home: !shot.Home);
            int[] side = shot.Home ? _s.AwayXY : _s.HomeXY;
            _m.GoalDisplacements++;
            _m.GoalDisplacementSumM += Distance(_s.PlayerX(side, release, keeper), _s.PlayerY(side, release, keeper),
                _s.PlayerX(side, atLine, keeper), _s.PlayerY(side, atLine, keeper)) / DmPerM;
        }

        // ------------------------------------------------------------------ goals

        private void CountGoals()
        {
            List<BallAction> actions = _s.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction g = actions[i];
                if (g.Kind != BallActionKind.Goal || g.Tick < 0 || g.Tick >= _ticks) continue;

                int lineX = X(g.Tick) < Pitch.CenterX ? 0 : Pitch.LengthDm;
                int last = _ticks - 1;
                for (int j = i + 1; j < actions.Count; j++)
                {
                    if (actions[j].Kind != BallActionKind.Kickoff && actions[j].Kind != BallActionKind.HalfTime) continue;
                    last = Math.Min(last, Math.Max(g.Tick, actions[j].Tick - 1));
                    break;
                }

                _m.Goals++;
                for (int t = g.Tick; t <= last; t++)
                {
                    if (Depth(t, lineX) < BehindLineDm || Math.Abs(Y(t) - Pitch.CenterY) > MovementGeometry.GoalHalfWidthDm) continue;
                    _m.GoalsBehindLine++;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The action that settles the shot at <paramref name="index"/>, or None when play stops or another strike comes first.</summary>
        private static int Outcome(List<BallAction> actions, int index)
        {
            bool home = actions[index].Home;
            for (int j = index + 1; j < actions.Count; j++)
            {
                BallActionKind kind = actions[j].Kind;
                if (kind == BallActionKind.Goal) return actions[j].Home == home ? j : None;
                if (kind == BallActionKind.Save || kind == BallActionKind.Miss || kind == BallActionKind.Block) return j;
                if (kind == BallActionKind.Shot || kind == BallActionKind.Foul || kind == BallActionKind.Offside
                    || kind == BallActionKind.HalfTime || IsRestart(kind))
                    return None;
            }

            return None;
        }

        private int NextRestartTick(List<BallAction> actions, int from)
        {
            for (int j = from + 1; j < actions.Count; j++)
                if (IsRestart(actions[j].Kind)) return actions[j].Tick;
            return _ticks;
        }

        private int Release(BallAction shot)
        {
            int code = _s.OwnerCode(shot.Home, shot.Slot);
            for (int t = shot.Tick; t >= 0 && t >= shot.Tick - StrikeLookbackFrames; t--)
                if (_s.Owner[t] == code) return t;
            return shot.Tick - 1;
        }

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick || k == BallActionKind.Corner
            || k == BallActionKind.FreeKick || k == BallActionKind.Penalty || k == BallActionKind.Kickoff;

        /// <summary>How far the ball is beyond the goal line at <paramref name="lineX"/> (negative: short of it).</summary>
        private int Depth(int t, int lineX) => lineX == 0 ? -X(t) : X(t) - lineX;

        private int TicksPerMinute() => _s.TicksPerMinute > 0 ? _s.TicksPerMinute : 1;

        private int X(int t) => _s.BallXY[t * 2];

        private int Y(int t) => _s.BallXY[t * 2 + 1];

        private double Step(int t) => Distance(X(t - 1), Y(t - 1), X(t), Y(t));

        private static double Distance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double length = vx * vx + vy * vy;
            double t = length == 0 ? 0 : ((px - ax) * vx + (py - ay) * vy) / length;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return Distance(px, py, ax + t * vx, ay + t * vy);
        }
    }
}
