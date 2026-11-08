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
                bool on = _phases.InPossession(side) && !ball.Dead && ball.OwnerSide == side
                          && _roleJobs.CrossOn(home, U.Dm(_sim._px[side * n + ball.OwnerSlot]), U.Dm(_sim._py[side * n + ball.OwnerSlot]),
                              _sim._tactics[side].Instructions.FinalThirdApproach);
                // R5: the post runs last while the cross is on and while it is on its way.
                _crossOn[side] = on;
                if (!on && !_sim.CrossInFlight(side))
                {
                    _nearPost[side] = -1;
                    _farPost[side] = -1;
                }

                if (!on) return;

                _arriveUntil[side] = tick + _sim._cfg.V11BoxArrivalTicks;
                AssignPostRuns(side, ball.OwnerSlot);
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

            /// <summary>
            /// R5: with the cross on from the man on the ball, the striker nearest the near post attacks
            /// it and the wide man on the far side nearest the far post attacks that, each once a side.
            /// A man who is now on the ball gives his run up.
            /// </summary>
            private void AssignPostRuns(int side, int owner)
            {
                int n = _sim._n;
                bool home = side == 0;
                int fromY = U.Dm(_sim._py[side * n + owner]);
                if (_nearPost[side] == owner) _nearPost[side] = -1;
                if (_farPost[side] == owner) _farPost[side] = -1;
                bool sameFlank = (fromY < Pitch.CenterY) == (_crossFromY[side] < Pitch.CenterY);
                if (!sameFlank)
                {
                    _nearPost[side] = -1;
                    _farPost[side] = -1;
                }

                _crossFromY[side] = fromY;
                _roleJobs.NearPost(home, fromY, out int nx, out int ny);
                _roleJobs.FarPost(home, fromY, out int fx, out int fy);
                long nearBest = long.MaxValue, farBest = long.MaxValue;
                bool pickNear = _nearPost[side] < 0, pickFar = _farPost[side] < 0;
                for (int j = 0; j < n; j++)
                {
                    int k = side * n + j;
                    if (j == owner || _sim._sentOff[k] || j == _arriver[side]) continue;
                    V11Slot man = SlotOf(side, j);
                    if (pickNear && man.Role == Domain.PositionRole.Striker)
                    {
                        long d = U.DistanceSq(_sim._px[k], _sim._py[k], U.Units(nx), U.Units(ny));
                        if (d < nearBest)
                        {
                            nearBest = d;
                            _nearPost[side] = j;
                        }
                    }
                    else if (pickFar && _roleJobs.OnTheFarSide(man, home, fromY))
                    {
                        long d = U.DistanceSq(_sim._px[k], _sim._py[k], U.Units(fx), U.Units(fy));
                        if (d < farBest)
                        {
                            farBest = d;
                            _farPost[side] = j;
                        }
                    }
                }
            }

            /// <summary>
            /// R5, defending it: while the other side's cross is on or on its way, and for
            /// V11BoxMarkHoldMs after, the nearest outfield man of this side to each of its box
            /// runners picks him up, one runner each, and stays on him as he meets the ball. Not for
            /// V11BoxMarkAfterSetPieceMs after a set piece's delivery: that scramble is anybody's.
            /// </summary>
            private void UpdateBoxMarks(int tick, int side)
            {
                int n = _sim._n, attacking = 1 - side;
                for (int r = 0; r < BoxRunners; r++) _boxMarker[side * BoxRunners + r] = -1;
                if (!_phases.InPossession(attacking) || _sim._ball.Dead)
                {
                    _shotLiner[side] = -1;
                    return;
                }

                UpdateShotLine(tick, side);
                bool afterSetPiece = tick - _sim._setPieceCrossTick[attacking] < _sim._cfg.V11BoxMarkAfterSetPieceTicks;
                if ((_crossOn[attacking] && !afterSetPiece) || _sim.CrossInFlight(attacking))
                {
                    _boxRunner[attacking * BoxRunners] = _nearPost[attacking];
                    _boxRunner[attacking * BoxRunners + 1] = _farPost[attacking];
                    _boxRunner[attacking * BoxRunners + 2] = _arriver[attacking];
                    _boxMarkUntil[attacking] = tick + _sim._cfg.V11BoxMarkHoldTicks;
                }
                else if (tick >= _boxMarkUntil[attacking]) return;

                for (int r = 0; r < BoxRunners; r++)
                {
                    int runner = _boxRunner[attacking * BoxRunners + r];
                    if (runner < 0 || _sim._sentOff[attacking * n + runner]) continue;
                    int rk = attacking * n + runner;

                    long best = long.MaxValue;
                    for (int j = 0; j < n; j++)
                    {
                        int k = side * n + j;
                        if (_sim._keeper[k] || _sim._sentOff[k] || j == _shotLiner[side] || MarksABoxRunner(side, j)) continue;
                        long d = U.DistanceSq(_sim._px[k], _sim._py[k], _sim._px[rk], _sim._py[rk]);
                        if (d >= best) continue;
                        best = d;
                        _boxMarker[side * BoxRunners + r] = j;
                    }
                }
            }

            /// <summary>
            /// R5: the cross itself is contested. When the other side crosses in open play, the
            /// outfield man of this side nearest the shot line from where it is going steps into that
            /// line, V11CrossShotLineDm goal-side of the ball's landing point, and stays in the line
            /// from the ball until V11CrossShotLineHoldMs after it is met: a first-time finish is
            /// struck at him. Not the man closing the receiver (issue #206), whose job comes first.
            /// </summary>
            private void UpdateShotLine(int tick, int side)
            {
                int attacking = 1 - side;
                if (_sim.CrossInFlight(attacking))
                {
                    if (_shotLiner[side] < 0 || _shotLineCross[side] != _sim._crossTouch)
                    {
                        _shotLineCross[side] = _sim._crossTouch;
                        _shotLiner[side] = -1;
                        ShotLinePoint(side, out int px, out int py);
                        long best = long.MaxValue;
                        for (int j = 0; j < _sim._n; j++)
                        {
                            int k = side * _sim._n + j;
                            if (_sim._keeper[k] || _sim._sentOff[k] || j == _zoneCloser[side]) continue;
                            long d = U.DistanceSq(_sim._px[k], _sim._py[k], px, py);
                            if (d >= best) continue;
                            best = d;
                            _shotLiner[side] = j;
                        }
                    }

                    _shotLineUntil[side] = tick + _sim._cfg.V11CrossShotLineHoldTicks;
                }
                else if (tick >= _shotLineUntil[side]) _shotLiner[side] = -1;
            }

            /// <summary>R5: the spot in a cross's shot line (units): goal-side of where the ball is going, or of the ball once met.</summary>
            private void ShotLinePoint(int side, out int x, out int y)
            {
                int attacking = 1 - side;
                bool inFlight = _sim.CrossInFlight(attacking);
                int fromX = inFlight ? _sim._receiveX[attacking] : _sim._ball.X;
                int fromY = inFlight ? _sim._receiveY[attacking] : _sim._ball.Y;
                int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                U.Scaled(goalX - fromX, U.CenterYU - fromY, U.Units(_sim._cfg.V11CrossShotLineDm), out int dx, out int dy);
                x = U.ClampX(fromX + dx);
                y = _sim._ctx.Inside(fromY + dy);
            }

            /// <summary>R5: where this man stands in the shot line of a cross (units). False when he is not the man.</summary>
            private bool ShotLine(int side, int slot, out int x, out int y)
            {
                x = 0;
                y = 0;
                if (_shotLiner[side] != slot) return false;
                ShotLinePoint(side, out x, out y);
                return true;
            }

            private bool MarksABoxRunner(int side, int slot)
            {
                for (int r = 0; r < BoxRunners; r++)
                    if (_boxMarker[side * BoxRunners + r] == slot) return true;
                return false;
            }

            /// <summary>R5: where this man stands to mark a box runner (units): goal-side of him. False when he marks none.</summary>
            private bool BoxMark(int side, int slot, out int x, out int y)
            {
                x = 0;
                y = 0;
                int n = _sim._n, attacking = 1 - side;
                for (int r = 0; r < BoxRunners; r++)
                {
                    if (_boxMarker[side * BoxRunners + r] != slot) continue;
                    int runner = _boxRunner[attacking * BoxRunners + r];
                    int rk = attacking * n + runner;
                    int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                    U.Scaled(goalX - _sim._px[rk], U.CenterYU - _sim._py[rk], U.Units(_sim._cfg.V11BoxMarkGoalSideDm), out int dx, out int dy);
                    x = U.ClampX(_sim._px[rk] + dx);
                    y = _sim._ctx.Inside(_sim._py[rk] + dy);
                    return true;
                }

                return false;
            }

            /// <summary>R5: where a post runner of <paramref name="side"/> is going (units), false for a man with no post run.</summary>
            private bool PostRun(int side, int slot, out int x, out int y, out MovementJob job)
            {
                x = 0;
                y = 0;
                job = MovementJob.NearPost;
                bool home = side == 0;
                int tx, ty;
                if (_nearPost[side] == slot) _roleJobs.NearPost(home, _crossFromY[side], out tx, out ty);
                else if (_farPost[side] == slot)
                {
                    _roleJobs.FarPost(home, _crossFromY[side], out tx, out ty);
                    job = MovementJob.FarPost;
                }
                else return false;

                x = U.ClampX(U.Units(tx));
                y = _sim._ctx.Inside(U.Units(ty));
                return true;
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
