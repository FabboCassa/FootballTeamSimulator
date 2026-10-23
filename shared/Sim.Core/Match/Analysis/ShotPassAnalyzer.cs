using System;
using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The shot, keeper and passing readings of the real-match spec (R2, R7, R9, R10), counted off
    /// a finished match's position stream. A measuring instrument like <see cref="MatchAnalyzer"/>:
    /// it reads the report, draws from no RNG and touches nothing, so it cannot move a result.
    ///
    /// Definitions are those of docs/research/football-reference.md §1.5 (and
    /// <see cref="RealismReference"/>). The stream keeps less than a data provider does, so a few
    /// readings are DERIVED, each by a fixed rule:
    ///   * A touch is a frame a player holds the ball, or a ball action naming him (a kick, a
    ///     save, a block, a deflection, a duel won, an interception). A kick is read where the ball
    ///     was on the frame before it is filed (the stream files it on the first frame after it).
    ///   * Attempted pass: Pass, LongBall, Cross, and a Clearance by a man who held the ball on one
    ///     of the two frames before it. A Clearance by a man who never held it is the engine's
    ///     deflection: a touch, not a pass.
    ///   * Completed: the next touch is by a teammate (not the passer), before any stoppage.
    ///   * Stoppage: a goal, a foul, an offside, half time, or a restart whistled (throw-in, goal
    ///     kick, corner, free kick, penalty, kick-off). A restart's taker starts the next sequence.
    ///   * Ball in play: every frame but those from a stoppage to the next touch.
    ///   * Long ball: what the engine files as LongBall (struck at a target more than 30 m away).
    ///   * Cross: the aim point is not in the stream, so it is read as where the pass ENDED — the
    ///     ball at the next touch, or its last in-play position before a stoppage.
    ///   * PPDA actions: the stream records tackles WON (Recovery) and not tackles lost, so the
    ///     denominator is duels won, interceptions and fouls.
    ///   * Headed shot: the stream has no ball height. A shot is read as headed when the action
    ///     before it is a teammate's Cross and the striker struck within
    ///     <see cref="HeaderWindowMs"/> of first holding the ball, without another action between.
    ///   * High turnover: an open-play sequence that starts in play (not a restart, not a rebound)
    ///     within 40 m of the centre of the opponent's goal.
    ///   * Rebound: a sequence the shooting side starts after its own shot with no stoppage
    ///     between and no opponent holding the ball (a parry, a block or a deflection may touch it).
    ///     The striker still shown holding the ball on his strike's own frame is the strike itself.
    ///   * R9 "excl. rebounds/high turnovers": those shots leave both the 0-1 count and the
    ///     open-play shots it is a share of.
    ///   * R7 keeper reach: the strike point is the ball on the last frame the striker held it
    ///     (looking back <see cref="StrikeLookbackFrames"/> frames from the strike's own, else the
    ///     strike's frame). The shot's path runs from there through the ball on the first later
    ///     frame it is seen free before the save frame, to the keeper's goal line (the crossing
    ///     point). The stream puts a caught ball on the keeper's feet, so the ball at contact is
    ///     read as the point of that path nearest the keeper on the save frame. Reached: within
    ///     <see cref="KeeperReachDm"/> of either. A save with no free frame between strike and
    ///     save (saved at once) has no readable path and is left out of the R7 reading.
    ///   * R10 open-goal shortcut: a shot struck while the R4 lane is open on the strike frame
    ///     (<see cref="OpenGoalLane"/>, the brain's own geometry, as <see cref="RealismAnalyzer"/>).
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed partial class ShotPassAnalyzer
    {
        private const int MsPerMinute = 60_000;
        private const int DmPerM = 10;

        /// <summary>The longest a header's striker may hold the ball before the strike.</summary>
        public const int HeaderWindowMs = 1_000;

        /// <summary>R7: how near the keeper must be to the crossing point or the ball's path.</summary>
        public const int KeeperReachDm = 15;

        /// <summary>How far back from the strike's frame the striker's last held frame is looked for.</summary>
        public const int StrikeLookbackFrames = 2;

        /// <summary>A high turnover starts this near the centre of the opponent's goal.</summary>
        public const int HighTurnoverRangeDm = 400;

        private readonly MatchBalance _cfg;
        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();

        /// <param name="cfg">Whose R4 geometry to read (the brain's V11OpenGoal* values); the shipped one when null.</param>
        public ShotPassAnalyzer(MatchBalance? cfg = null)
        {
            _cfg = cfg ?? new MatchBalance();
        }

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static ShotPassMetrics? Analyze(MatchReport report) => new ShotPassAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public ShotPassMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            int ticks = s.TickCount;
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: false);
            FindSentOff(s);

            var m = new ShotPassMetrics();
            CountShots(s, m);
            Walk(s, ticks, m);
            return m;
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

        // ------------------------------------------------------------------ shots

        private void CountShots(PositionStream s, ShotPassMetrics m)
        {
            List<BallAction> actions = s.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction shot = actions[i];
                if (shot.Kind != BallActionKind.Shot) continue;

                int bx = BallX(s, shot.Tick), by = BallY(s, shot.Tick);
                bool inside = InAttackedBox(shot.Home, bx, by);
                m.Shots++;
                if (inside) m.ShotsInsideBox++;
                if (IsHeader(s, actions, i)) m.HeadedShots++;
                if (LaneIsOpen(s, shot.Tick, shot.Home, bx, by)) m.OpenGoalShortcutShots++;

                int o = Outcome(actions, i);
                if (o < 0) continue;
                switch (actions[o].Kind)
                {
                    case BallActionKind.Goal:
                        m.ShotsOnTarget++;
                        m.ShotGoals++;
                        if (inside) m.ShotGoalsInsideBox++;
                        break;
                    case BallActionKind.Save:
                        m.ShotsOnTarget++;
                        m.Saves++;
                        if (inside) m.SavesInsideBox++;
                        MeasureKeeperReach(s, shot, actions[o], m);
                        break;
                    case BallActionKind.Block:
                        m.ShotsBlocked++;
                        break;
                }
            }
        }

        /// <summary>The action that settles the strike at <paramref name="index"/>, or -1 when play stops or another strike comes first.</summary>
        private static int Outcome(List<BallAction> actions, int index)
        {
            bool home = actions[index].Home;
            for (int j = index + 1; j < actions.Count; j++)
            {
                BallActionKind kind = actions[j].Kind;
                if (kind == BallActionKind.Goal) return actions[j].Home == home ? j : -1;
                if (kind == BallActionKind.Save || kind == BallActionKind.Miss || kind == BallActionKind.Block) return j;
                if (kind == BallActionKind.Shot || IsStoppage(kind)) return -1;
            }

            return -1;
        }

        private static bool IsHeader(PositionStream s, List<BallAction> actions, int index)
        {
            if (index == 0) return false;
            BallAction shot = actions[index], cross = actions[index - 1];
            if (cross.Kind != BallActionKind.Cross || cross.Home != shot.Home || cross.Slot == shot.Slot) return false;

            int code = s.OwnerCode(shot.Home, shot.Slot);
            int first = shot.Tick;
            for (int t = cross.Tick + 1; t <= shot.Tick && t < s.Owner.Length; t++)
            {
                if (s.Owner[t] != code) continue;
                first = t;
                break;
            }

            return (long)(shot.Tick - first) * MsPerMinute <= (long)HeaderWindowMs * TicksPerMinute(s);
        }

        // ------------------------------------------------------------------ R7: keeper reach

        private static void MeasureKeeperReach(PositionStream s, BallAction shot, BallAction save, ShotPassMetrics m)
        {
            int sf = StrikeFrame(s, shot), fv = save.Tick;
            int x0 = BallX(s, sf), y0 = BallY(s, sf);

            // The first frame the ball is seen free after the strike and BEFORE the save frame: on
            // the save frame a caught ball is on the keeper's feet and a parried one already off them.
            int flight = None;
            for (int t = sf + 1; t < fv; t++)
            {
                if (s.Owner[t] != PositionStream.NoOwner || (BallX(s, t) == x0 && BallY(s, t) == y0)) continue;
                flight = t;
                break;
            }

            if (flight == None) return;
            double dx = BallX(s, flight) - x0, dy = BallY(s, flight) - y0;
            double lineX = MovementGeometry.OwnGoalX(save.Home);
            if (dx == 0 || (lineX - x0) * dx <= 0) return;

            double cx = lineX, cy = y0 + dy * (lineX - x0) / dx;
            int[] side = save.Home ? s.HomeXY : s.AwayXY;
            double kx = s.PlayerX(side, fv, save.Slot), ky = s.PlayerY(side, fv, save.Slot);

            double toCrossing = Distance(kx, ky, cx, cy);
            double toPath = DistanceToSegment(kx, ky, x0, y0, cx, cy);

            m.SavesMeasured++;
            m.KeeperToCrossingSumM += toCrossing / DmPerM;
            if (Math.Min(toCrossing, toPath) <= KeeperReachDm) m.SavesReached++;
        }

        /// <summary>
        /// The last frame, at most <see cref="StrikeLookbackFrames"/> before the strike's own, on
        /// which the striker held the ball: the ball is at his boot there. Else the strike's frame.
        /// </summary>
        private static int StrikeFrame(PositionStream s, BallAction shot)
        {
            int code = s.OwnerCode(shot.Home, shot.Slot);
            for (int t = shot.Tick; t >= 0 && t >= shot.Tick - StrikeLookbackFrames; t--)
                if (s.Owner[t] == code) return t;
            return shot.Tick;
        }

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

        // ------------------------------------------------------------------ R10: open-goal shortcut

        private bool LaneIsOpen(PositionStream s, int t, bool home, int bx, int by)
        {
            int goalX = MovementGeometry.AttackedGoalX(home);
            if (!OpenGoalLane.InRange(bx, by, goalX, _cfg.V11OpenGoalRangeDm, _cfg.V11OpenGoalMinMouthSinePermille)) return false;

            int n = s.PlayerCount;
            int foe = home ? 1 : 0;
            int[] xy = home ? s.AwayXY : s.HomeXY;
            for (int slot = 0; slot < n; slot++)
            {
                if (slot == _keeper[foe] || t >= _sentOffFrom[foe * n + slot]) continue;
                if (OpenGoalLane.Closes(s.PlayerX(xy, t, slot), s.PlayerY(xy, t, slot), bx, by, goalX,
                        _cfg.V11OpenGoalLaneMarginDm, _cfg.V11OpenGoalFreeRadiusDm))
                    return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ geometry

        private static int TicksPerMinute(PositionStream s) => s.TicksPerMinute > 0 ? s.TicksPerMinute : 1;

        private static int BallX(PositionStream s, int t) => s.BallXY[t * 2];

        private static int BallY(PositionStream s, int t) => s.BallXY[t * 2 + 1];

        /// <summary>Inside the penalty area the side attacks; its lines count as inside.</summary>
        private static bool InAttackedBox(bool home, int x, int y)
        {
            bool deep = home ? x >= Pitch.LengthDm - MovementGeometry.BoxDepthDm : x <= MovementGeometry.BoxDepthDm;
            int across = Math.Abs(y - Pitch.CenterY);
            return deep && across <= MovementGeometry.BoxHalfWidthDm;
        }

        private static bool IsStoppage(BallActionKind k) =>
            k == BallActionKind.Goal || k == BallActionKind.Foul || k == BallActionKind.Offside
            || k == BallActionKind.HalfTime || IsRestart(k);

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick || k == BallActionKind.Corner
            || k == BallActionKind.FreeKick || k == BallActionKind.Penalty || k == BallActionKind.Kickoff;
    }
}
