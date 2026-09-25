namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11SetPieces
        {
            /// <summary>Close enough to his set-piece spot to count as on it.</summary>
            private const int PlacedDm = 20;

            /// <summary>How far short of the offside line a man waiting for a crossed free kick stands.</summary>
            private const int LineMarginDm = 10;

            /// <summary>
            /// Decided once per dead ball, at the first look after the whistle: whether a free
            /// kick is a set piece and which of its two options it is, and who takes which role.
            /// Draw-free, so the order of the match's draws is only ever moved by the kick itself.
            /// </summary>
            private void Plan()
            {
                if (_planAt == _ctx.DeadAt && _planKind == _ctx.DeadKind && _planSide == _ctx.DeadSide) return;

                _planAt = _ctx.DeadAt;
                _planKind = _ctx.DeadKind;
                _planSide = _ctx.DeadSide;
                _placedAt = -1;
                _freeKick = FreeKickOption.None;
                _hasRoles = false;

                int side = _ctx.DeadSide;
                if (side < 0) return;
                _lowY = _ball.Y < U.CenterYU;

                if (_ctx.DeadKind == BallActionKind.FreeKick)
                {
                    int t = side * _n + _ctx.DeadTaker;
                    int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
                    int distance = U.Dm(U.Distance(_ball.X, _ball.Y, goalX, U.CenterYU));
                    int offCentre = U.Dm(_ball.Y > U.CenterYU ? _ball.Y - U.CenterYU : U.CenterYU - _ball.Y);
                    _freeKick = SetPiecePlan.ChooseFreeKick(
                        distance, offCentre, _sim._skShooting[t], _sim._skTechnique[t], _cfg, out _, out _);
                }

                if (_ctx.DeadKind == BallActionKind.Corner || _freeKick == FreeKickOption.Cross) AssignRoles(side);
            }

            private void AssignRoles(int side)
            {
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    _eligible[i] = !_sim._keeper[k] && !_sim._sentOff[k] && i != _ctx.DeadTaker;
                    _skillA[i] = BallSkill.Mix(_sim._skStrength[k], 7, _sim._skTechnique[k], 3);
                    _skillB[i] = BallSkill.Mix(_sim._skShooting[k], 7, _sim._skTechnique[k], 3);
                }

                CornerRoles.Assign(_skillA, _skillB, _eligible, _roles);
                _hasRoles = true;
            }

            /// <summary>
            /// Where a role stands, in units. A corner has no offside; for a free kick every role
            /// stands level with the line, where a real side lines up to attack the ball.
            /// </summary>
            private void RoleSpot(int side, CornerRole role, out int x, out int y)
            {
                CornerRoles.Spot(role, side == 0, _lowY, _cfg, out int xDm, out int yDm);
                x = U.Units(xDm);
                y = U.Units(yDm);
                if (_ctx.DeadKind != BallActionKind.FreeKick) return;

                int dir = MovementGeometry.Direction(side == 0);
                int deepest = _sim._offside.OffsideLineDepth(side) - U.Units(LineMarginDm);
                if (dir * x > deepest) x = dir * deepest;
            }

            /// <summary>
            /// Everybody the set piece needs is where it needs him — the wall on its mark and the
            /// attacking roles on their spots — and has been for a moment; or the wait is over.
            /// </summary>
            private bool Ready(int tick, int side)
            {
                if (tick >= _ctx.DeadAt + _cfg.SetPieceWaitTicks) return true;

                if (!InPlace(side))
                {
                    _placedAt = -1;
                    return false;
                }

                if (_placedAt < 0) _placedAt = tick;
                return tick >= _placedAt + _cfg.SetPieceSettleTicks;
            }

            private bool InPlace(int side)
            {
                int placed = U.Units(PlacedDm);
                int retreat = U.Units(_cfg.FreeKickRetreatDm);
                int defending = 1 - side;
                for (int i = 0; i < _n; i++)
                {
                    int dk = defending * _n + i;
                    if (_sim._freeKickWall.IsInWall(dk))
                    {
                        int away = U.Distance(_px[dk], _py[dk], _ball.X, _ball.Y);
                        if (away < retreat - placed / 2 || away > retreat + placed) return false;
                    }

                    if (!_hasRoles || _roles[i] == CornerRole.None) continue;
                    int k = side * _n + i;
                    RoleSpot(side, _roles[i], out int x, out int y);
                    if (U.Distance(_px[k], _py[k], x, y) > placed) return false;
                }

                return true;
            }

            /// <summary>The near-post or the far-post man, as often as the balance says.</summary>
            private int PostTarget()
            {
                CornerRole want = _ctx.Rng.NextInt(0, 1000) < _cfg.CornerNearPostPermille
                    ? CornerRole.NearPost
                    : CornerRole.FarPost;
                int fallback = -1;
                for (int i = 0; i < _n; i++)
                {
                    if (_roles[i] == want) return i;
                    if (_roles[i] == CornerRole.NearPost || _roles[i] == CornerRole.FarPost) fallback = i;
                }

                return fallback;
            }

            /// <summary>
            /// Swung in at the man, and off by as much as a long ball from this taker is. Offside
            /// is judged on a free kick and never on a corner (Law 11).
            /// </summary>
            private void Deliver(int tick, int side, int slot, int target)
            {
                int k = side * _n + slot;
                int aimX, aimY;
                if (target >= 0)
                {
                    aimX = _px[side * _n + target];
                    aimY = _py[side * _n + target];
                }
                else
                {
                    aimX = U.Units(MovementGeometry.AttackedGoalX(side == 0))
                           - MovementGeometry.Direction(side == 0) * U.Units(_cfg.PenaltySpotDm);
                    aimY = U.CenterYU;
                }

                int dx = aimX - _ball.X, dy = aimY - _ball.Y;
                int distance = U.Length(dx, dy);
                int error = BallSkill.PassErrorPermille(_sim._skPassing[k], _sim._skTechnique[k], 0, true, _cfg);
                int sideways = (int)((long)distance * BallSkill.Spread(_ctx.Rng, error) / 1000);
                if (distance > 0 && sideways != 0)
                {
                    aimX += (int)((long)(-dy) * sideways / distance);
                    aimY += (int)((long)dx * sideways / distance);
                }

                aimX = U.ClampX(aimX);
                aimY = U.ClampY(aimY);
                int flight = U.Distance(_ball.X, _ball.Y, aimX, aimY);
                _ball.Kick(side, slot, aimX - _ball.X, aimY - _ball.Y,
                    _ball.ForceForTicks(flight, _cfg.TicksOfMs(1200), _sim._maxPassForce));
                _sim.Release(tick, side, slot);

                _sim._offside.ClearFlags();
                if (_ctx.DeadKind == BallActionKind.FreeKick) _sim._offside.FlagOffside(side, slot);

                _sim._receiver[side] = target;
                _sim._receiveX[side] = aimX;
                _sim._receiveY[side] = aimY;
                _ctx.Sheet.Record(tick, BallActionKind.Cross, side == 0, slot, target);
            }

            /// <summary>
            /// A goal kick or a throw-in, played at once: short to the nearest man or long to the
            /// furthest man forward, as the build-up instruction says. Neither can be offside.
            /// </summary>
            private void PlayRestart(int tick, int side, int slot)
            {
                BallActionKind kind = _ctx.DeadKind;
                _sim.BeginRestart(tick, side, slot);

                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    _xs[i] = _px[k];
                    _ys[i] = _py[k];
                    _eligible[i] = !_sim._keeper[k] && !_sim._sentOff[k] && i != slot;
                }

                bool longBall = SetPiecePlan.LongRestart(kind, _sim._tempo[side], _cfg);
                int reach = kind == BallActionKind.ThrowIn ? U.Units(_cfg.ThrowInMaxDm) : _sim._maxPassRange;
                int target = SetPiecePlan.PickRestartTarget(
                    longBall, _ball.X, _ball.Y, MovementGeometry.Direction(side == 0),
                    _xs, _ys, _eligible, U.Units(_cfg.RestartMinPassDm), reach);
                if (target < 0) return;

                int tk = side * _n + target;
                int distance = U.Distance(_ball.X, _ball.Y, _px[tk], _py[tk]);
                var pass = new PassChoice
                {
                    Slot = target,
                    X = _px[tk],
                    Y = _py[tk],
                    Force = _ball.ForceToArrive(distance, _sim._arrivalStepU, _sim._maxPassForce, out _)
                };
                _sim.PlayPass(tick, side, slot, pass, _sim.PressurePermille(side, slot));
                _sim._offside.ClearFlags();
            }
        }
    }
}
