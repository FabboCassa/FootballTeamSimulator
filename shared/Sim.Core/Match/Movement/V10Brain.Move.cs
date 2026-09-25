namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            public void Move(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                bool home = side == 0;
                bool sprint = false;
                int tx, ty;

                // Sent off (engine phase 5). He WALKS off — to the touchline by the halfway line, at
                // the pace of a man who has just been sent off, because a body that jumps forty metres
                // in a tenth of a second is the one thing the stream's own contract forbids — and takes
                // no further part: every loop that reads the pitch skips him, so his side genuinely
                // plays the rest of the match with ten men. The duties are shared out among ten, the
                // offside line is drawn off ten, and there are ten men to pass to.
                if (_sentOff[k])
                {
                    // Aimed just OUTSIDE the touchline so the pitch's own clamp puts him exactly on it:
                    // steering at the line itself leaves him a couple of metres short, inside the field
                    // of play, which is precisely where he may not be.
                    _sim.Steer(k, U.CenterXU, home ? -U.Units(40) : U.WidthU + U.Units(40), sprint: false);
                    return;
                }

                // Filled by the press test below whenever this side is defending; the compiler
                // cannot see that the branch which reads them is the same branch that sets them.
                int pressHomeX = 0, pressHomeY = 0;

                if (_ball.Dead && _ctx.DeadSide == side && _ctx.DeadTaker == slot)
                {
                    // He goes to the ball — and at a kickoff he stands just BEHIND it, in his own half,
                    // because that is where the man taking a kickoff stands (Law 8) and standing on the
                    // centre spot itself puts him a stride into the other half. Placed, not steered:
                    // the deadband would leave him three metres off the ball, which at a kickoff is
                    // three metres inside the other side's half.
                    _sim.WalkTo(
                        k,
                        _ctx.DeadKind == BallActionKind.Kickoff
                            ? _ball.X - MovementGeometry.Direction(home) * U.Units(_cfg.KickoffStandOffDm)
                            : _ball.X,
                        _ball.Y);
                    return;
                }
                else if (_ball.Dead && _ctx.DeadKind == BallActionKind.Kickoff)
                {
                    // A kickoff is taken with both sides in their own half (Law 8), and it is a
                    // PLACEMENT like the wall: nobody makes a supporting run into the other half while
                    // the referee waits, and a man caught over the line WALKS back onto his mark rather
                    // than steering at it — steering leaves him a metre or two the wrong side of
                    // halfway, because inside fifteen metres the approach paces him down to a crawl.
                    HomeSpot(side, slot, out tx, out ty);
                    _sim.WalkTo(k, tx, ty);
                    return;
                }
                else if (_freeKickWall.RetreatSpot(side, slot, out int retreatX, out int retreatY))
                {
                    // Ten yards, and the wall (Laws 13 and 14) — WALKED TO, not steered at.
                    //
                    // This is the one place in the model where a man is going to a MARK rather than to
                    // the football, and the steering is wrong for it in both of its gears, which is
                    // what the replay dump showed: sprinting, he carries his momentum straight past the
                    // spot and spends the stoppage orbiting it (measured eight metres beyond); walking,
                    // the approach slowdown paces him down to a fifth of a jog inside fifteen metres and
                    // the arrival deadband stops him three metres short — so he never makes the nine
                    // fifteen and the wall stands at five metres, which is against the law he is
                    // standing there to obey. A referee walks a wall onto its mark and it stays there.
                    _sim.WalkTo(k, retreatX, retreatY);
                    return;
                }
                else if (_ball.OwnerSide == side && _ball.OwnerSlot == slot)
                {
                    // On the ball he goes FORWARD, drifting off the touchline toward the middle,
                    // and once he is inside the last quarter he stops running at the byline and
                    // cuts in at the goal instead. Sending him back to his position is how a
                    // defender ends up carrying the ball into his own corner; sending him straight
                    // ahead for ever is how he ends up standing on the goal line holding it.
                    _sim.CarryTarget(side, k, U.Units(160), out tx, out ty);
                    sprint = true;
                }
                else if (_keeper[k])
                {
                    KeeperSpot(side, out tx, out ty);
                }
                else if (_ball.Free && !_ball.Dead && _receiver[side] == slot)
                {
                    // He runs to MEET it, not to the spot it was aimed at (engine phase 4). Steering
                    // to a fixed point leaves him standing two or three metres off the line while
                    // the ball rolls past — and two or three metres is the whole of a footballer's
                    // control radius, which is why a pass into twelve metres of clear space was
                    // still lost a quarter of the time.
                    InterceptSpot(k, out tx, out ty);
                    sprint = true;
                }
                else if (_ball.Free && !_ball.Dead && _chaser[side] == slot)
                {
                    InterceptSpot(k, out tx, out ty);
                    sprint = true;
                }
                else if (!_attacking[side] && Pressing(tick, side, slot, out pressHomeX, out pressHomeY))
                {
                    PressSpot(side, k, out tx, out ty);
                    sprint = true;
                }
                else if (_attacking[side] && AlreadySupporting(side, slot))
                {
                    tx = _supportX[side];
                    ty = _supportY[side];

                    // A supporting run is a BURST to get there, not a ninety-minute sprint. Flat out
                    // while the ground is still to be covered, and a jog once he is in the space he
                    // ran into — which is the difference between a side that runs 12.1 km a man with
                    // a busiest of 19.5 and one that runs 11 with a busiest of 15.
                    sprint = U.DistanceSq(_px[k], _py[k], tx, ty) > (long)_approachU * _approachU;
                }
                else if (!_attacking[side] && _duty[k] == DutyCover)
                {
                    CoverSpot(side, out tx, out ty);
                }
                else if (!_attacking[side] && _duty[k] == DutyMark && _mark[k] >= 0)
                {
                    MarkSpot(side, k, out tx, out ty);
                }
                else if (!_attacking[side])
                {
                    // Already worked out by the press test above; recomputing it is pure waste.
                    tx = pressHomeX;
                    ty = pressHomeY;
                }
                else
                {
                    HomeSpot(side, slot, out tx, out ty);
                }

                // The recovery run (engine phase 3). A man who has been caught up the pitch does
                // not jog home while the ball goes the other way, and the difference is not cosmetic:
                // at a jog the block takes a dozen seconds to re-form, which is a dozen seconds in
                // which the side defending is a side still strung out from attacking.
                tx = U.ClampX(tx);
                ty = U.ClampY(ty);
                if (!sprint && !_attacking[side] && !_keeper[k]
                    && U.DistanceSq(_px[k], _py[k], tx, ty) > (long)_recoveryU * _recoveryU)
                    sprint = true;

                _sim.Steer(k, tx, ty, sprint);

                // And at a kickoff the halfway line is a WALL: the referee holds the whistle until both
                // sides are in their own half (Law 8), so nobody may move INTO the other one — the
                // step he just took across it is undone. Written as undoing his own step rather than as
                // a clamp on purpose: clamping would drag a man caught thirty metres upfield back onto
                // the line in a single tick, which is the one thing the stream's contract forbids (see
                // PositionStreamTests.NobodyTeleports). A man in the wrong half walks back under his
                // own steam — the branch above sends him home at a sprint — and this only stops him
                // going further. The ball sits on the line, so the taker can still reach it from his
                // own side of it.
                if (_ball.Dead && _ctx.DeadKind == BallActionKind.Kickoff)
                {
                    int dir = MovementGeometry.Direction(home);
                    int over = dir * (_px[k] - U.CenterXU);
                    int stepped = dir * (_px[k] - _stepFromX[k]);
                    if (over > 0 && stepped > 0) _px[k] -= dir * (over < stepped ? over : stepped);
                }

                // The man ON THE BALL keeps it on the pitch — unless he is actually running over the
                // line, which is a throw-in and is left alone. Wherever the game has dragged him
                // incidentally (a support run to the touchline, a loose ball chased into the corner)
                // his feet, and so the ball at them, end up a stride inside the line rather than on
                // it; a step that ended OUTSIDE is untouched, so the referee still reads it and gives
                // the throw-in. The inset is deliberately shorter than one stride at top speed, so a
                // man who means to take it out still can (engine phase 5).
                if (_ball.OwnerSide == side && _ball.OwnerSlot == slot && !_ball.Dead
                    && _stepToX[k] >= 0 && _stepToX[k] <= U.LengthU
                    && _stepToY[k] >= 0 && _stepToY[k] <= U.WidthU)
                {
                    _px[k] = MovementGeometry.Clamp(_px[k], _ctx.InsetU, U.LengthU - _ctx.InsetU);
                    _py[k] = _ctx.Inside(_py[k]);
                }
            }

            private void KeeperSpot(int side, out int x, out int y)
            {
                bool home = side == 0;
                int goalX = U.Units(MovementGeometry.OwnGoalX(home));
                int dx = _ball.X - goalX;
                int distance = dx < 0 ? -dx : dx;

                int far = U.LengthU / 2;
                int reach = distance < far ? distance : far;
                int depth = U.Units(_cfg.KeeperDepthDm) + U.Units(_cfg.KeeperRushDm) * reach / far;

                x = home ? depth : U.LengthU - depth;
                y = U.CenterYU + (_ball.Y - U.CenterYU) * _cfg.KeeperLateralPercent / 100;
                y = MovementGeometry.Clamp(y, U.CenterYU - U.Units(110), U.CenterYU + U.Units(110));

                // In his own box and nearest to it, he comes and claims it.
                if (_ball.Free && !_ball.Dead
                    && MovementGeometry.InOwnBox(home, U.Dm(_ball.X), U.Dm(_ball.Y))
                    && _sim.NearestToBall(side, includeKeeper: true) == _ctx.KeeperOf(side))
                {
                    x = _ball.X;
                    y = _ball.Y;
                }
            }

            private void InterceptSpot(int k, out int x, out int y)
            {
                // Run to where the ball will be, not to where it is. The roll is stepped forward once
                // rather than replayed from scratch for every horizon, which at 10 Hz matters.
                int bx = _ball.X, by = _ball.Y, vx = _ball.Vx, vy = _ball.Vy;
                int horizon = _cfg.InterceptLookaheadTicks;

                for (int ahead = 1; ahead <= horizon; ahead++)
                {
                    _ball.StepPrediction(ref bx, ref by, ref vx, ref vy);
                    int reach = U.Distance(_px[k], _py[k], U.ClampX(bx), U.ClampY(by));
                    if (reach <= (long)_maxSpeed[k] * ahead)
                    {
                        x = U.ClampX(bx);
                        y = U.ClampY(by);
                        return;
                    }
                }

                x = U.ClampX(bx);
                y = U.ClampY(by);
            }
        }
    }
}
