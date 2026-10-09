using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The shape, movement, pitch-bounds, keeper and restart readings of docs/specs/real-match-and-playing-styles.md
    /// (R4, R5, R6, R8, R11), counted off a finished match's position stream. A measuring instrument like
    /// <see cref="RealismAnalyzer"/>: it reads the report, draws nothing from any RNG and touches
    /// nothing, so it can never move a result. One pass over the frames, allocation-free per frame.
    ///
    /// Definitions ("convention" marks a choice the spec leaves open):
    ///   * Open play (convention): the ball is in play. It goes dead at a restart's whistle, a goal
    ///     or half-time, and comes back on the first frame, after the restart's whistle, that
    ///     somebody holds it — the taker playing it.
    ///   * Phase: the side that last held the ball, carried through loose frames (as
    ///     <see cref="OffTargetMeter"/> reads it); open-play frames before anybody held it have none.
    ///   * Outfielders: everybody but the inferred keeper and men already sent off.
    ///   * R4 block length: highest minus deepest outfielder along the pitch; width: widest minus
    ///     narrowest across it; both averaged over open-play frames. Out of possession "own half"
    ///     (convention) means the ball is in the defending side's half; in possession "opposition
    ///     half" means the ball is in the attacking side's opposition half. A touchline zone is
    ///     manned by an outfielder within 8 m of that touchline; the reading is the share of
    ///     attacking samples with both manned.
    ///   * R4 off-target seconds: <see cref="OffTargetMeter"/>, the median over the outfielders.
    ///   * R8 stand-still: an outfielder in an open-play frame whose step from the previous frame is
    ///     under 0.2 m/s. Positions are whole decimetres, so at 5 frames a second that means he did
    ///     not move at all.
    ///   * R8 distance per outfield player: doc §1.5 — each side's outfield distance over every
    ///     frame, substitutes included, ÷ 10.
    ///   * R5 off pitch: see <see cref="OffPitchMeter"/>.
    ///   * R11 goal kicks: short when the action that puts one in play is a pass by its side, long
    ///     when it is a long ball (over MatchBalance.LongBallFromDm).
    ///   * R11 throw-ins: one taken by its taker is offered when, on the frame before he throws it,
    ///     at least two outfielders of his side other than him are within 15 m of the ball.
    ///   * R6 keeper depth: in an open-play frame the keeper's distance from his goal line must be
    ///     ≤ 6 m with the ball within 35 m of his goal centre, ≤ 18 m otherwise. Claiming the ball
    ///     is exempt (convention): he holds it, or it is loose and nobody on the pitch is nearer it.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class ShapeMovementAnalyzer
    {
        private const int Unknown = -1;
        private const int TouchlineZoneDm = 80;
        private const int NearGoalDm = 350;
        private const int KeeperNearDepthDm = 60;
        private const int KeeperFarDepthDm = 180;
        private const int ThrowInOfferDm = 150;
        private const int ThrowInOffers = 2;

        // speed (m/s) = step (dm) × frames per minute ÷ 600, so speed < 0.2 ⇔ step × fpm < 120.
        private const long StandStillStepTimesFpm = 120;

        private readonly OffTargetMeter _offTarget = new OffTargetMeter();
        private readonly OffPitchMeter _offPitch = new OffPitchMeter();
        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();
        private int _n;

        // The dead-ball clock, advanced frame by frame.
        private int _nextAction;
        private bool _dead;
        private bool _awaitingTaker;
        private int _restartTick;
        private int _restartTaker;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static ShapeMovementMetrics? Analyze(MatchReport report) => new ShapeMovementAnalyzer().Measure(report);

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public ShapeMovementMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;

            int ticks = s.TickCount;
            _n = s.PlayerCount;
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: false);
            FindSentOff(s);

            var m = new ShapeMovementMetrics();
            WalkFrames(s, ticks, m);
            Restarts(s, m);
            m.MedianOffTargetSeconds = _offTarget.Measure(s, ticks, _keeper, _sentOffFrom);
            return m;
        }

        private void FindSentOff(PositionStream s)
        {
            if (_sentOffFrom.Length != 2 * _n) _sentOffFrom = new int[2 * _n];
            for (int i = 0; i < _sentOffFrom.Length; i++) _sentOffFrom[i] = int.MaxValue;

            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= _n) continue;
                int k = (a.Home ? 0 : _n) + a.Slot;
                if (a.Tick < _sentOffFrom[k]) _sentOffFrom[k] = a.Tick;
            }
        }

        private bool Outfielder(int side, int slot, int t) => slot != _keeper[side] && t < _sentOffFrom[side * _n + slot];

        private void WalkFrames(PositionStream s, int ticks, ShapeMovementMetrics m)
        {
            int fpm = s.TicksPerMinute > 0 ? s.TicksPerMinute : 1;
            _offPitch.Begin(_n, fpm);
            _nextAction = 0;
            _dead = _awaitingTaker = false;
            _restartTick = Unknown;
            _restartTaker = PositionStream.NoOwner;
            int phase = Unknown;

            for (int t = 0; t < ticks; t++)
            {
                bool open = AdvanceClock(s, t, out int takerCode);
                if (s.TryOwner(s.Owner[t], out bool home, out int _)) phase = home ? 0 : 1;

                if (t > 0) Move(s, t, open, fpm, m);
                _offPitch.Frame(s, t, takerCode, _keeper, _sentOffFrom);
                if (!open) continue;

                m.OpenPlayFrames++;
                Keepers(s, t, m);
                if (phase != Unknown) Shape(s, t, phase, m);
            }

            _offPitch.End(ticks);
            m.OffPitchFrames = _offPitch.Frames;
        }

        /// <summary>Whether frame t is open play, and the throw-in or corner taker it belongs to, if any.</summary>
        private bool AdvanceClock(PositionStream s, int t, out int takerCode)
        {
            List<BallAction> actions = s.Actions;
            for (; _nextAction < actions.Count && actions[_nextAction].Tick <= t; _nextAction++)
            {
                BallAction a = actions[_nextAction];
                if (IsRestart(a.Kind))
                {
                    _dead = _awaitingTaker = true;
                    _restartTick = a.Tick;
                    bool takerExempt = (a.Kind == BallActionKind.ThrowIn || a.Kind == BallActionKind.Corner)
                        && a.Slot >= 0 && a.Slot < _n;
                    _restartTaker = takerExempt ? s.OwnerCode(a.Home, a.Slot) : PositionStream.NoOwner;
                }
                else if (a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.HalfTime)
                {
                    _dead = true;
                    _awaitingTaker = false;
                    _restartTaker = PositionStream.NoOwner;
                }
            }

            bool takenNow = _dead && _awaitingTaker && t > _restartTick && s.Owner[t] != PositionStream.NoOwner;
            if (takenNow) _dead = _awaitingTaker = false;

            takerCode = _dead || takenNow ? _restartTaker : PositionStream.NoOwner;
            return !_dead;
        }

        // ------------------------------------------------------------------ R8: movement

        private void Move(PositionStream s, int t, bool open, int fpm, ShapeMovementMetrics m)
        {
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                double distance = 0;
                for (int k = 0; k < _n; k++)
                {
                    if (!Outfielder(side, k, t)) continue;
                    long dx = s.PlayerX(xy, t, k) - s.PlayerX(xy, t - 1, k);
                    long dy = s.PlayerY(xy, t, k) - s.PlayerY(xy, t - 1, k);
                    long step2 = dx * dx + dy * dy;
                    distance += Math.Sqrt(step2);

                    if (!open) continue;
                    m.OutfieldOpenPlayFrames++;
                    if (step2 * fpm * fpm < StandStillStepTimesFpm * StandStillStepTimesFpm) m.StandStillFrames++;
                }

                if (side == 0) m.HomeOutfieldDistanceDm += distance;
                else m.AwayOutfieldDistanceDm += distance;
            }
        }

        // ------------------------------------------------------------------ R4: team shape

        private void Shape(PositionStream s, int t, int attacking, ShapeMovementMetrics m)
        {
            int defending = 1 - attacking;
            int bx = s.BallXY[t * 2];
            bool inDefendersHalf = defending == 0 ? bx < Pitch.CenterX : bx >= Pitch.CenterX;

            if (Extents(s, t, defending, out int length, out int width, out int _, out int _))
            {
                m.DefendingSamples++;
                m.DefendingLengthDm += length;
                m.DefendingWidthDm += width;
                if (inDefendersHalf)
                {
                    m.OwnHalfDefendingSamples++;
                    m.OwnHalfLengthDm += length;
                }
            }

            if (inDefendersHalf && Extents(s, t, attacking, out int _, out width, out int minY, out int maxY))
            {
                m.AttackingSamples++;
                m.AttackingWidthDm += width;
                if (minY <= TouchlineZoneDm && maxY >= Pitch.WidthDm - TouchlineZoneDm) m.BothTouchlinesSamples++;
            }
        }

        /// <summary>A side's outfield extents in one frame; false with fewer than two outfielders.</summary>
        private bool Extents(PositionStream s, int t, int side, out int length, out int width, out int minY, out int maxY)
        {
            int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
            int minX = int.MaxValue, maxX = int.MinValue, count = 0;
            minY = int.MaxValue;
            maxY = int.MinValue;
            for (int k = 0; k < _n; k++)
            {
                if (!Outfielder(side, k, t)) continue;
                int x = s.PlayerX(xy, t, k), y = s.PlayerY(xy, t, k);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                count++;
            }

            length = count < 2 ? 0 : maxX - minX;
            width = count < 2 ? 0 : maxY - minY;
            return count >= 2;
        }

        // ------------------------------------------------------------------ R11: restart shapes

        private void Restarts(PositionStream s, ShapeMovementMetrics m)
        {
            List<BallAction> a = s.Actions;
            for (int i = 0; i + 1 < a.Count; i++)
            {
                BallAction restart = a[i], kick = a[i + 1];
                if (kick.Home != restart.Home) continue;

                if (restart.Kind == BallActionKind.GoalKick)
                {
                    if (kick.Kind == BallActionKind.Pass) m.ShortGoalKicks++;
                    else if (kick.Kind == BallActionKind.LongBall) m.LongGoalKicks++;
                }
                else if (restart.Kind == BallActionKind.ThrowIn && kick.Slot == restart.Slot && IsPlayed(kick.Kind))
                {
                    m.ThrowIns++;
                    int before = Math.Max(restart.Tick, kick.Tick - 1);
                    if (Offers(s, restart.Home, restart.Slot, before) >= ThrowInOffers) m.OfferedThrowIns++;
                }
            }
        }

        /// <summary>Outfielders of the taker's side, him aside, within 15 m of the ball in frame t.</summary>
        private int Offers(PositionStream s, bool home, int taker, int t)
        {
            int side = home ? 0 : 1, near = 0;
            int bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1];
            for (int k = 0; k < _n; k++)
            {
                if (k == taker || !Outfielder(side, k, t)) continue;
                if (Distance2(s, side, k, t, bx, by) <= (long)ThrowInOfferDm * ThrowInOfferDm) near++;
            }

            return near;
        }

        private static bool IsPlayed(BallActionKind k) =>
            k == BallActionKind.Pass || k == BallActionKind.LongBall || k == BallActionKind.Cross;

        // ------------------------------------------------------------------ R6: keeper depth

        private void Keepers(PositionStream s, int t, ShapeMovementMetrics m)
        {
            int bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1];
            for (int side = 0; side < 2; side++)
            {
                int k = _keeper[side];
                if (t >= _sentOffFrom[side * _n + k]) continue;

                bool home = side == 0;
                int[] xy = home ? s.HomeXY : s.AwayXY;
                int x = s.PlayerX(xy, t, k);
                int depth = home ? x : Pitch.LengthDm - x;
                long gx = bx - MovementGeometry.OwnGoalX(home), gy = by - Pitch.CenterY;
                int limit = gx * gx + gy * gy <= (long)NearGoalDm * NearGoalDm ? KeeperNearDepthDm : KeeperFarDepthDm;

                m.KeeperOpenPlayFrames++;
                if (depth > limit && !Claiming(s, t, side, k, bx, by)) m.KeeperDepthBreakFrames++;
            }
        }

        private bool Claiming(PositionStream s, int t, int side, int keeper, int bx, int by)
        {
            int owner = s.Owner[t];
            if (owner != PositionStream.NoOwner) return owner == s.OwnerCode(side == 0, keeper);

            long his = Distance2(s, side, keeper, t, bx, by);
            for (int other = 0; other < 2; other++)
            for (int k = 0; k < _n; k++)
            {
                if ((other == side && k == keeper) || t >= _sentOffFrom[other * _n + k]) continue;
                if (Distance2(s, other, k, t, bx, by) < his) return false;
            }

            return true;
        }

        private static long Distance2(PositionStream s, int side, int slot, int t, int bx, int by)
        {
            int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
            long dx = s.PlayerX(xy, t, slot) - bx, dy = s.PlayerY(xy, t, slot) - by;
            return dx * dx + dy * dy;
        }

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.Kickoff || k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick
            || k == BallActionKind.FreeKick || k == BallActionKind.Corner || k == BallActionKind.Penalty;
    }
}
