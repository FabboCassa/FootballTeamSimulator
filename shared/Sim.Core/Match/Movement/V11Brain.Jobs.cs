using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11Brain
        {
            private readonly V11RoleJobs _roleJobs;

            // R7's staggered re-reads, per man: the ball and the supporting run as he last saw them
            // (units), and the last tick his place was read, so a man back from a job reads afresh.
            private int[] _seenBallX = System.Array.Empty<int>();
            private int[] _seenBallY = System.Array.Empty<int>();
            private int[] _seenSupportX = System.Array.Empty<int>();
            private int[] _seenSupportY = System.Array.Empty<int>();
            private int[] _readTick = System.Array.Empty<int>();

            private void BeginReReads()
            {
                int total = _sim._n * SideCount;
                _seenBallX = new int[total];
                _seenBallY = new int[total];
                _seenSupportX = new int[total];
                _seenSupportY = new int[total];
                _readTick = new int[total];
                for (int k = 0; k < total; k++) _readTick[k] = int.MinValue;
            }

            /// <summary>
            /// He looks up on his own clock (<see cref="V11ReRead"/>): the ball and the supporting run
            /// are re-read when his tick comes round, or when he comes back from a job; in between he
            /// goes on what he saw. The eleven re-targeting on the tick the ball moved is what kept
            /// the team's directions in lock-step.
            /// </summary>
            private void ReRead(int tick, int side, int k)
            {
                bool fresh = _readTick[k] != tick - 1;
                _readTick[k] = tick;
                if (!fresh && !V11ReRead.Due(k, tick, V11ReRead.PeriodTicks(_sim._skPositioning[k], _sim._cfg))) return;

                _seenBallX[k] = _sim._ball.X;
                _seenBallY[k] = _sim._ball.Y;
                _base.SupportSpot(side, out _seenSupportX[k], out _seenSupportY[k]);
                _sim.TraceReread(k);
            }

            /// <summary>
            /// R7's role jobs for a man with no other job while his side is without the ball: the
            /// near-side full-back steps to a wide man on the ball, the holding midfielder screens the
            /// lane to the most advanced opponent, the far-side full-back tucks in. Targets in units.
            /// </summary>
            private bool RoleJob(int side, V11Slot man, int k, int spotX, int spotY,
                out int x, out int y, out bool sprint, out MovementJob kind)
            {
                x = spotX;
                y = spotY;
                sprint = false;
                kind = MovementJob.Spot;
                MatchBall ball = _sim._ball;
                if (ball.Dead) return false;

                bool home = side == 0;
                int sx = U.Dm(spotX), sy = U.Dm(spotY);
                int bx = U.Dm(_seenBallX[k]), by = U.Dm(_seenBallY[k]);
                int tx, ty;

                if (ball.OwnerSide == 1 - side && _roleJobs.Step(man, home, sx, sy, bx, by, out tx, out ty))
                {
                    sprint = true;
                    kind = MovementJob.Step;
                }
                else if (ScreenTarget(side, man, sx, sy, bx, by, out tx, out ty))
                {
                    kind = MovementJob.Screen;
                }
                else if (_roleJobs.Tuck(man, home, sy, by, out ty))
                {
                    tx = sx;
                    kind = MovementJob.Tuck;
                }
                else return false;

                x = U.Units(tx);
                y = U.Units(ty);
                return true;
            }

            /// <summary>
            /// R7's held jobs, read once a tick. An overlap ends when its spell does or the side loses
            /// the ball. With a cross or cut-back on, the central midfielder nearest the penalty spot
            /// goes, one a side, and keeps going for a spell after it stops being on: the cross itself
            /// is what ends it being on, and it is what he is running for.
            /// </summary>
            private void UpdateHeldJobs(int tick, int side)
            {
                MatchBall ball = _sim._ball;
                int n = _sim._n;
                if (!_phases.InPossession(side) || ball.Dead
                    || (_arriver[side] >= 0 && (tick >= _arriveUntil[side] || _sim._sentOff[side * n + _arriver[side]])))
                    _arriver[side] = -1;
                if (_overlapper[side] >= 0 && (!_phases.InPossession(side) || ball.Dead || tick >= _overlapUntil[side]
                                               || _sim._sentOff[side * n + _overlapper[side]]))
                    _overlapper[side] = -1;

                bool home = side == 0;
                if (!_phases.InPossession(side) || ball.Dead || ball.OwnerSide != side
                    || !_roleJobs.CrossOn(home, U.Dm(_sim._px[side * n + ball.OwnerSlot]), U.Dm(_sim._py[side * n + ball.OwnerSlot]),
                        _sim._tactics[side].Instructions.FinalThirdApproach))
                    return;

                _arriveUntil[side] = tick + _sim._cfg.V11BoxArrivalTicks;
                if (_arriver[side] == ball.OwnerSlot) _arriver[side] = -1;
                if (_arriver[side] >= 0) return;

                _roleJobs.BoxSpot(home, out int sx, out int sy);
                long best = long.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    int k = side * n + j;
                    if (j == ball.OwnerSlot || _sim._sentOff[k] || !_roleJobs.ArrivesInTheBox(SlotOf(side, j))) continue;
                    long d = U.DistanceSq(_sim._px[k], _sim._py[k], U.Units(sx), U.Units(sy));
                    if (d < best)
                    {
                        best = d;
                        _arriver[side] = j;
                    }
                }
            }

            private bool ScreenTarget(int side, V11Slot man, int sx, int sy, int bx, int by, out int x, out int y)
            {
                x = sx;
                y = sy;
                int front = V11RoleJobs.MostAdvanced(side == 0, _foeX[side], _foeCount[side]);
                return front >= 0
                    && _roleJobs.Screen(man, side == 0, sx, sy, bx, by, _foeX[side][front], _foeY[side][front], out x, out y);
            }

            /// <summary>
            /// R7's curved run: the waypoint round the first opponent within V11CurveTriggerDm of his
            /// straight line, or (-1, -1) when the line is clear.
            /// </summary>
            private void CurveVia(int side, int k, int tx, int ty, out int viaX, out int viaY)
            {
                viaX = -1;
                viaY = -1;
                MatchBalance cfg = _sim._cfg;
                if (!RunCurve.Bend(U.Dm(_sim._px[k]), U.Dm(_sim._py[k]), U.Dm(tx), U.Dm(ty),
                        _foeX[side], _foeY[side], _foeCount[side], cfg.V11CurveTriggerDm, cfg.V11CurveClearanceDm,
                        out int vx, out int vy))
                    return;

                viaX = U.ClampX(U.Units(vx));
                viaY = _sim._ctx.Inside(U.Units(vy));
            }
        }
    }
}
