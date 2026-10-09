using Sim.Core.Domain;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V11SetPieces
        {
            /// <summary>Close enough to his set-piece spot to count as on it.</summary>
            private const int PlacedDm = 20;

            // A man easing into his offer spot, or kept off it by a team-mate, stops short of it;
            // this keeps the throw-in's wait at ThrowInOfferDm + slack, inside R11's 15 m.
            private const int OfferSlackDm = 25;

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
                _hasShape = false;
                System.Array.Clear(_placed, 0, _placed.Length);
                System.Array.Clear(_offering, 0, _offering.Length);

                int side = _ctx.DeadSide;
                if (side < 0) return;
                _lowY = _ball.Y < U.CenterYU;

                if (_ctx.DeadKind == BallActionKind.FreeKick)
                {
                    int t = side * _n + _ctx.DeadTaker;
                    int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
                    int distance = U.StreamDistanceDm(_ball.X, _ball.Y, goalX, U.CenterYU);
                    int offCentre = U.Dm(_ball.Y > U.CenterYU ? _ball.Y - U.CenterYU : U.CenterYU - _ball.Y);
                    _freeKick = SetPiecePlan.ChooseFreeKick(
                        distance, offCentre, _sim._skShooting[t], _sim._skTechnique[t], _cfg, out _, out _);
                }

                if (_ctx.DeadKind == BallActionKind.Corner || _freeKick == FreeKickOption.Cross) AssignRoles(side);
                if (_ctx.DeadKind == BallActionKind.GoalKick
                    && SetPiecePlan.Length(BallActionKind.GoalKick, _sim._tempo[side], _cfg) != RestartLength.Long)
                    ShapeGoalKick(side);
                if (_ctx.DeadKind == BallActionKind.ThrowIn) ShapeThrowIn(side);
            }

            /// <summary>A goal kick that may be played short is played out of a shape (R11).</summary>
            private void ShapeGoalKick(int side)
            {
                Lineup lineup = side == 0 ? _sim._home : _sim._away;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    _eligible[i] = !_sim._keeper[k] && !_sim._sentOff[k];
                    _roleOf[i] = lineup.Slots[i].Role;
                    _baseYOf[i] = _sim._baseY[k];
                }

                RestartShape.GoalKick(_roleOf, _baseYOf, _eligible, side == 0, _cfg, _placed, _spotX, _spotY);
                _hasShape = true;
            }

            /// <summary>The two men nearest a throw-in offer for it (R11), picked where they stand at the whistle.</summary>
            private void ShapeThrowIn(int side)
            {
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    _xs[i] = U.Dm(_px[k]);
                    _ys[i] = U.Dm(_py[k]);
                    _eligible[i] = !_sim._keeper[k] && !_sim._sentOff[k] && i != _ctx.DeadTaker;
                }

                _offers = RestartShape.ThrowIn(U.Dm(_ball.X), U.Dm(_ball.Y), _xs, _ys, _eligible, _offering);
                _hasShape = true;
            }

            /// <summary>
            /// Whether this man offers for the throw-in being taken: he goes to his place in the
            /// shape held within reach of the ball (<see cref="RestartShape.OfferSpot"/>).
            /// </summary>
            public bool Offers(int side, int slot) =>
                _ball.Dead && _hasShape && _ctx.DeadKind == BallActionKind.ThrowIn && _ctx.DeadSide == side
                && slot != _ctx.DeadTaker && _offering[slot];

            /// <summary>The restart's shape is up or the wait for it is over.</summary>
            private bool ShapeUp(int tick) =>
                !_hasShape || tick >= _ctx.DeadAt + _cfg.RestartShapeWaitTicks
                || (_ctx.DeadKind == BallActionKind.ThrowIn ? Offering() >= _offers : ShapeInPlace());

            /// <summary>
            /// Team-mates of a throw-in's taker within reach of the ball now, whoever they are: the
            /// reading R11 takes.
            /// </summary>
            private int Offering()
            {
                int reach = U.Units(_cfg.ThrowInOfferDm + OfferSlackDm), near = 0;
                for (int i = 0; i < _n; i++)
                {
                    int k = _ctx.DeadSide * _n + i;
                    if (i == _ctx.DeadTaker || _sim._keeper[k] || _sim._sentOff[k]) continue;
                    if (U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= reach) near++;
                }

                return near;
            }

            /// <summary>Every man of the restart's shape is on his spot.</summary>
            private bool ShapeInPlace()
            {
                int placed = U.Units(PlacedDm);
                for (int i = 0; i < _n; i++)
                {
                    if (!_placed[i] || i == _ctx.DeadTaker) continue;
                    int k = _ctx.DeadSide * _n + i;
                    if (U.Distance(_px[k], _py[k], U.Units(_spotX[i]), U.Units(_spotY[i])) > placed) return false;
                }

                return true;
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
            /// A goal kick or a throw-in: short to the nearest man or long to the furthest man
            /// forward, as the build-up instruction says. A goal kick out of the shape goes short to
            /// a split centre-back, or long when the wait for the shape ran out; one that is short
            /// unless pressed goes long when every man it could go to short has an opponent on him.
            /// Neither can be offside.
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

                RestartLength length = SetPiecePlan.Length(kind, _sim._tempo[side], _cfg);
                // A keeper whose back line is not split yet when the wait runs out does not play it short into them.
                if (kind == BallActionKind.GoalKick && _hasShape && !ShapeInPlace()) length = RestartLength.Long;
                int reach = kind == BallActionKind.ThrowIn ? U.Units(_cfg.ThrowInMaxDm) : _sim._maxPassRange;
                int target = length == RestartLength.Long
                    ? PickTarget(side, true, _eligible, reach)
                    : ShortTarget(side, length == RestartLength.ShortUnlessPressed, reach);
                if (target < 0 && length == RestartLength.ShortUnlessPressed) target = PickTarget(side, true, _eligible, reach);

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

            private int PickTarget(int side, bool longBall, bool[] eligible, int reach) =>
                SetPiecePlan.PickRestartTarget(
                    longBall, _ball.X, _ball.Y, MovementGeometry.Direction(side == 0),
                    _xs, _ys, eligible, U.Units(_cfg.RestartMinPassDm), reach);

            /// <summary>
            /// The nearest man a restart can be played short to: on a goal kick out of the shape, a
            /// centre-back on his box corner; when it is short only unless pressed, a man with no
            /// opponent on him. -1 when there is none.
            /// </summary>
            private int ShortTarget(int side, bool unlessPressed, int reach)
            {
                bool splitOnly = false;
                if (_ctx.DeadKind == BallActionKind.GoalKick && _hasShape)
                    for (int i = 0; i < _n; i++) splitOnly |= _eligible[i] && SplitCentreBack(i);

                if (unlessPressed) LoadFoes(side);
                int radius = U.Units(_cfg.GoalKickPressedDm);
                for (int i = 0; i < _n; i++)
                {
                    _shortOk[i] = _eligible[i] && (!splitOnly || SplitCentreBack(i))
                        && (!unlessPressed || !SetPiecePlan.Pressed(_xs[i], _ys[i], _foeX, _foeY, _foeOn, radius));
                }

                return PickTarget(side, false, _shortOk, reach);
            }

            private bool SplitCentreBack(int i) => _placed[i] && _roleOf[i] == PositionRole.CentreBack;

            private void LoadFoes(int side)
            {
                int foe = 1 - side;
                for (int i = 0; i < _n; i++)
                {
                    int k = foe * _n + i;
                    _foeX[i] = _px[k];
                    _foeY[i] = _py[k];
                    _foeOn[i] = !_sim._sentOff[k];
                }
            }
        }
    }
}
