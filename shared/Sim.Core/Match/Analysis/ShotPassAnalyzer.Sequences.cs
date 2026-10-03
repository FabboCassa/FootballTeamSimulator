using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The passing half of <see cref="ShotPassAnalyzer"/>: one walk over the frames, the ball's
    /// actions on a frame read before its owner, that rebuilds touches, sequences and passes.
    /// </summary>
    public sealed partial class ShotPassAnalyzer
    {
        private const int None = -1;
        private const int TenPlusPasses = 10;
        private const double MinDirectSpeedSeconds = 1.0;

        private static readonly int PpdaZoneDm = (int)Math.Round(Pitch.LengthDm * RealismReference.PpdaZoneFraction);

        private struct Sequence
        {
            public int Side;          // 0 home, 1 away, None when no sequence is open
            public int RestartKind;   // the BallActionKind that started it, None in play
            public bool OpenPlay;
            public bool Rebound;
            public bool HighTurnover;
            public int FirstFrame, FirstX, LastFrame, LastX;
            public int Passes;
        }

        private struct PendingPass
        {
            public bool Active;
            public int Side, Slot, X, Y;
            public bool SetPieceDelivery;   // a corner kick or a throw-in: never a cross
        }

        private Sequence _seq;
        private PendingPass _pass;
        private int _pendingRestart, _reboundSide;

        // The last strike's frame and striker. The stream files an odd-tick strike on the frame
        // written BEFORE the kick, so its owner can still be the striker: that is the strike, not
        // a new touch, and must not open a sequence after the shot closed his.
        private int _strikeFrame, _strikerCode;
        private bool _dead;
        private ShotPassMetrics _m = new ShotPassMetrics();
        private PositionStream _s = new PositionStream();

        private void Walk(PositionStream s, int ticks, ShotPassMetrics m)
        {
            _s = s;
            _m = m;
            _seq = new Sequence { Side = None };
            _pass = default;
            _pendingRestart = None;
            _reboundSide = None;
            _strikeFrame = None;
            _strikerCode = PositionStream.NoOwner;
            _dead = false;

            List<BallAction> actions = s.Actions;
            int next = 0, deadFrames = 0;
            for (int t = 0; t < ticks; t++)
            {
                for (; next < actions.Count && actions[next].Tick <= t; next++) Handle(actions[next]);

                int code = s.Owner[t];
                if (code != PositionStream.NoOwner && !(t == _strikeFrame && code == _strikerCode))
                {
                    s.TryOwner(code, out bool home, out int slot);
                    int side = home ? 0 : 1;
                    if (_reboundSide != None && side != _reboundSide) _reboundSide = None;
                    Touch(side, slot, t, BallX(s, t), BallY(s, t));
                }

                if (_dead) deadFrames++;
            }

            if (_pass.Active) ResolvePass(false, BallX(s, ticks - 1), BallY(s, ticks - 1));
            CloseSequence();
            m.BallInPlayMinutes = (double)(ticks - deadFrames) / TicksPerMinute(s);
        }

        private void Handle(BallAction a)
        {
            int side = a.Home ? 0 : 1;
            int f = a.Tick;
            switch (a.Kind)
            {
                case BallActionKind.Pass:
                case BallActionKind.LongBall:
                case BallActionKind.Cross:
                    Kick(a);
                    break;
                case BallActionKind.Clearance:
                    if (HeldBefore(a)) Kick(a);
                    else Touch(side, a.Slot, f, BallX(_s, f), BallY(_s, f));
                    break;
                case BallActionKind.Shot:
                    Touch(side, a.Slot, f, BallX(_s, f), BallY(_s, f));
                    CountOpenPlayShot(side);
                    CloseSequence();
                    _reboundSide = side;
                    _strikeFrame = f;
                    _strikerCode = _s.OwnerCode(a.Home, a.Slot);
                    break;
                case BallActionKind.Save:
                case BallActionKind.Block:
                case BallActionKind.Dribble:
                    Touch(side, a.Slot, f, BallX(_s, f), BallY(_s, f));
                    break;
                case BallActionKind.Recovery:
                case BallActionKind.Interception:
                    CountDefensiveAction(side, f);
                    Touch(side, a.Slot, f, BallX(_s, f), BallY(_s, f));
                    break;
                case BallActionKind.Foul:
                    CountDefensiveAction(side, f);
                    Stop(f);
                    break;
                case BallActionKind.Offside:
                case BallActionKind.Goal:
                case BallActionKind.HalfTime:
                    Stop(f);
                    break;
                default:
                    if (!IsRestart(a.Kind)) break;
                    Stop(f);
                    _pendingRestart = (int)a.Kind;
                    break;
            }
        }

        /// <summary>A man of <paramref name="side"/> touches the ball at frame <paramref name="frame"/>.</summary>
        private void Touch(int side, int slot, int frame, int x, int y)
        {
            _dead = false;
            if (_pass.Active) ResolvePass(side == _pass.Side && slot != _pass.Slot, x, y);

            if (_seq.Side != side)
            {
                CloseSequence();
                StartSequence(side, frame, x, y);
            }
            else if (frame >= _seq.LastFrame)
            {
                _seq.LastFrame = frame;
                _seq.LastX = x;
            }
        }

        private void Kick(BallAction a)
        {
            int side = a.Home ? 0 : 1;
            int kf = a.Tick > 0 ? a.Tick - 1 : 0;
            int x = BallX(_s, kf), y = BallY(_s, kf);
            Touch(side, a.Slot, kf, x, y);

            _m.PassesAttempted++;
            if (a.Kind == BallActionKind.LongBall) _m.LongBalls++;

            bool delivery = _seq.Passes == 0
                && (_seq.RestartKind == (int)BallActionKind.Corner || _seq.RestartKind == (int)BallActionKind.ThrowIn);
            _seq.Passes++;
            if (_seq.OpenPlay && InOwnZone(side, x)) _m.PpdaPasses++;

            _pass = new PendingPass { Active = true, Side = side, Slot = a.Slot, X = x, Y = y, SetPieceDelivery = delivery };
        }

        /// <summary>A Clearance by a man who held the ball on one of the two frames before it was filed.</summary>
        private bool HeldBefore(BallAction a)
        {
            int code = _s.OwnerCode(a.Home, a.Slot);
            for (int t = a.Tick - 1; t >= 0 && t >= a.Tick - 2; t--)
                if (_s.Owner[t] == code) return true;
            return false;
        }

        private void Stop(int frame)
        {
            if (_pass.Active)
            {
                int last = frame > 0 ? frame - 1 : 0;
                ResolvePass(false, BallX(_s, last), BallY(_s, last));
            }

            CloseSequence();
            _reboundSide = None;
            _dead = true;
        }

        private void ResolvePass(bool completed, int endX, int endY)
        {
            if (completed) _m.PassesCompleted++;
            if (!_pass.SetPieceDelivery && IsCross(_pass.Side == 0, _pass.X, _pass.Y, endX, endY)) _m.Crosses++;
            _pass = default;
        }

        private void StartSequence(int side, int frame, int x, int y)
        {
            bool restart = _pendingRestart != None;
            bool rebound = _reboundSide == side;
            _seq = new Sequence
            {
                Side = side,
                RestartKind = _pendingRestart,
                OpenPlay = !restart
                           || _pendingRestart == (int)BallActionKind.ThrowIn
                           || _pendingRestart == (int)BallActionKind.GoalKick,
                Rebound = rebound,
                HighTurnover = !restart && !rebound && NearAttackedGoal(side == 0, x, y),
                FirstFrame = frame, FirstX = x, LastFrame = frame, LastX = x,
            };

            if (_seq.HighTurnover) _m.HighTurnovers++;
            _pendingRestart = None;
            if (rebound) _reboundSide = None;
        }

        private void CloseSequence()
        {
            if (_seq.Side == None) return;
            if (_seq.OpenPlay)
            {
                _m.OpenPlaySequences++;
                _m.OpenPlaySequencePasses += _seq.Passes;
                if (_seq.Passes >= TenPlusPasses) _m.TenPlusSequences++;

                double seconds = (_seq.LastFrame - _seq.FirstFrame) * 60.0 / TicksPerMinute(_s);
                if (seconds >= MinDirectSpeedSeconds)
                {
                    int progressDm = _seq.Side == 0 ? _seq.LastX - _seq.FirstX : _seq.FirstX - _seq.LastX;
                    _m.DirectSpeedSequences++;
                    _m.DirectSpeedSumMps += progressDm / (double)DmPerM / seconds;
                }
            }

            _seq = new Sequence { Side = None };
        }

        private void CountOpenPlayShot(int side)
        {
            if (_seq.Side != side || !_seq.OpenPlay) return;
            _m.OpenPlayShots++;
            if (_seq.Rebound) _m.OpenPlayShotsRebound++;
            if (_seq.HighTurnover) _m.OpenPlayShotsHighTurnover++;
            if (_seq.Passes <= 1)
            {
                _m.OpenPlayShotsZeroToOne++;
                if (_seq.Rebound || _seq.HighTurnover) _m.OpenPlayShotsZeroToOneReboundOrHighTurnover++;
            }
            else if (_seq.Passes == 2) _m.OpenPlayShotsTwo++;
            else _m.OpenPlayShotsThreePlus++;
        }

        /// <summary>PPDA's denominator: the pressing side's action in the passer's zone while he owns an open-play sequence.</summary>
        private void CountDefensiveAction(int side, int frame)
        {
            if (_seq.Side == None || _seq.Side == side || !_seq.OpenPlay) return;
            if (InOwnZone(_seq.Side, BallX(_s, frame))) _m.PpdaActions++;
        }

        // ------------------------------------------------------------------ zones

        /// <summary>The 60% of the length nearest the side's own goal (Understat's PPDA zone).</summary>
        private static bool InOwnZone(int side, int x) =>
            side == 0 ? x <= PpdaZoneDm : x >= Pitch.LengthDm - PpdaZoneDm;

        private static bool NearAttackedGoal(bool home, int x, int y)
        {
            long dx = x - MovementGeometry.AttackedGoalX(home), dy = y - Pitch.CenterY;
            return dx * dx + dy * dy <= (long)HighTurnoverRangeDm * HighTurnoverRangeDm;
        }

        /// <summary>From the last third, outside the width of the area, ending inside the area attacked.</summary>
        private static bool IsCross(bool home, int sx, int sy, int ex, int ey)
        {
            bool lastThird = home ? sx >= 2 * Pitch.LengthDm / 3 : sx <= Pitch.LengthDm / 3;
            bool wide = Math.Abs(sy - Pitch.CenterY) > MovementGeometry.BoxHalfWidthDm;
            return lastThird && wide && InAttackedBox(home, ex, ey);
        }
    }
}
