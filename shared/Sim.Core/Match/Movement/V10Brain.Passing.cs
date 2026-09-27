namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            /// <summary>
            /// Looking up. Every team-mate is considered three ways — to his feet and either side of
            /// him, because a ball into his path is often on when a ball to his feet is not — and
            /// each option is priced: the odds it comes off, the ground it gains, the cost of losing
            /// it where it would be lost.
            ///
            /// Before this phase the test was binary and geometric: a lane no opponent could reach
            /// was "safe" and anything else was not, and the last seven metres in front of the
            /// receiver were excluded from the test altogether so a marked man could still be found.
            /// That exclusion is why 44% of passes went straight to an opponent — the marker standing
            /// on the receiver was invisible to the only test being run. He is now priced instead of
            /// ignored, which is a different thing entirely.
            /// </summary>
            private int FindPass(int tick, int side, int slot, int pressure, int vision, out PassChoice best)
            {
                best = new PassChoice { Slot = -1 };
                int k = side * _n + slot;
                bool home = side == 0;
                int dir = MovementGeometry.Direction(home);
                int bias = _tactics[side].ForwardBias;
                int widePassBiasDm = _tactics[side].WidePassBiasDm;

                int bestValue = int.MinValue;
                int tolerance = _controlU + U.Units(20);

                // Law 11, from the passer's own eyes (engine phase 5). He will not knowingly play a
                // man offside, so every option beyond the line he BELIEVES is there is struck off —
                // and when what he believes is wrong, the flag goes up. One draw per decision, taken
                // here rather than per option so that the line he is playing to is one line.
                int offsideLine = _offside.PerceivedOffsideLine(k, _offside.OffsideLineDepth(side)) + U.Units(_cfg.OffsideMarginDm);

                for (int j = 0; j < _n; j++)
                {
                    if (j == slot) continue;
                    int rk = side * _n + j;
                    if (_sentOff[rk]) continue;
                    if (dir * _px[rk] > offsideLine) continue;
                    int gap = U.Distance(_px[k], _py[k], _px[rk], _py[rk]);
                    if (gap < U.Units(_cfg.MinPassDm)) continue;
                    if (gap > _maxPassRange) continue;

                    int lead = U.Units(_cfg.PassLeadDm);
                    for (int option = 0; option < 3; option++)
                    {
                        int tx = _px[rk], ty = _py[rk];
                        if (option == 1) tx += dir * lead;
                        else if (option == 2) ty += _py[rk] < U.CenterYU ? -lead : lead;

                        tx = U.ClampX(tx);
                        ty = _ctx.Inside(ty);

                        int distance = U.Distance(_px[k], _py[k], tx, ty);
                        if (distance < U.Units(_cfg.MinPassDm)) continue;

                        // Only a ball that ARRIVES about when it is meant to, and arrives DYING —
                        // weighted so the man it is for can take it in his stride rather than
                        // stepping into the path of something still travelling at seventeen metres
                        // a second. Beyond the range of a ball struck as hard as it can be struck
                        // the option is not a pass at all.
                        if (distance > _ball.RangeOf(_maxPassForce)) continue;
                        int force = _ball.ForceToArrive(
                            distance, _arrivalStepU, _maxPassForce, out int flight);
                        if (_ball.RangeInTicks(force, flight) < distance - _controlU) continue;

                        // Three things have to go right, and they are three different questions:
                        // does it get through the lane, does the man it is for have room to take it,
                        // and can this player actually hit it.
                        int completion = LaneCompletion(side, _px[k], _py[k], tx, ty, force);
                        completion = completion * BallSkill.ReceptionPermille(NearestOpponentDm(side, tx, ty), _cfg) / 1000;

                        bool longBall = distance > U.Units(_cfg.LongBallFromDm);
                        int error = BallSkill.PassErrorPermille(_skPassing[k], _skTechnique[k], pressure, longBall, _cfg);
                        int miss = (int)((long)distance * error / 1000 * 375 / 1000);
                        if (miss > tolerance)
                            completion = completion * BallSkill.Lerp(miss - tolerance, tolerance * 3, 1000, 250) / 1000;

                        if (completion < _cfg.MinPassCompletionPermille) continue;

                        // WIDTH, PRICED (engine phase 8). A team-mate standing in a wide channel is
                        // worth something extra to a side told to play wide and something less to one
                        // told to play through the middle — in the same decimetres of forward progress
                        // everything else here is quoted in. It is the lever behind "wide → more
                        // crosses", because a cross in this engine IS a pass from a wide position near
                        // the goal (see IsCross): make the wide man the better option often enough and
                        // the crosses follow from the football rather than from a counter. Zero at
                        // neutral, so the price is exactly the one phase 4 settled on.
                        int gainDm = dir * (tx - _px[k]) / U.Scale * bias / 10;
                        int wide = widePassBiasDm != 0 && IsWideChannel(ty) ? widePassBiasDm : 0;
                        int value = BallSkill.OptionValue(
                            completion, gainDm + wide + _cfg.PossessionValueDm, TurnoverCostDm(side, tx), vision);

                        if (_keeper[rk]) value -= _cfg.BackToKeeperCostDm;
                        if (AlreadySupporting(side, j)) value += _cfg.SupportingRunBonusDm;

                        if (value > bestValue)
                        {
                            bestValue = value;
                            best = new PassChoice
                            {
                                Slot = j,
                                X = tx,
                                Y = ty,
                                Force = force,
                                Completion = completion
                            };
                        }
                    }
                }

                return best.Slot >= 0 ? bestValue : int.MinValue;
            }

            /// <summary>The nearest opponent to a point, in whole decimetres.</summary>
            private int NearestOpponentDm(int side, int x, int y)
            {
                int opponent = 1 - side;
                int best = int.MaxValue;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_sentOff[ok]) continue;
                    int distance = U.Distance(x, y, _px[ok], _py[ok]);
                    if (distance < best) best = distance;
                }

                return best == int.MaxValue ? Pitch.LengthDm : U.Dm(best);
            }

            /// <summary>
            /// Can this ball be cut out — and by how much? For each opponent, where he would meet
            /// the line of the pass, and how far past that point the ball has already rolled by the
            /// time he gets there. Opponents behind the passer are ignored: they are chasing it, not
            /// intercepting it. The answer is odds rather than a yes or a no, because a lane a man
            /// reaches half a second late is not the same pass as one nobody is near.
            /// </summary>
            private int LaneCompletion(int side, int fromX, int fromY, int toX, int toY, int force)
            {
                int opponent = 1 - side;
                int dx = toX - fromX, dy = toY - fromY;
                int length = U.Length(dx, dy);
                if (length <= 0) return 0;

                int receiverSpace = U.Units(_cfg.ReceiverSpaceDm);
                int worst = 1000;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_sentOff[ok]) continue;
                    long along = ((long)(_px[ok] - fromX) * dx + (long)(_py[ok] - fromY) * dy) / length;
                    if (along <= 0) continue;                       // behind the ball: he is chasing, not cutting it out

                    // The last stretch belongs to the receiver — a man marked at three metres can
                    // still be passed to, he shields it, and his marker has to TACKLE him for it.
                    // What that stretch is NOT is free: the room the receiver has is priced by
                    // BallSkill.ReceptionPermille, next to this. Leaving it unpriced, which is what
                    // the old yes/no test did, is what made a ball into a marker's feet "safe".
                    if (along > length - receiverSpace) continue;

                    int meetX = fromX + (int)((long)dx * along / length);
                    int meetY = fromY + (int)((long)dy * along / length);

                    int reach = U.Distance(_px[ok], _py[ok], meetX, meetY) - _interceptU;
                    if (reach < 0) return _cfg.CutOutCompletionPermille;

                    // Asked the other way round — how far has the ball got by the time he is there —
                    // this is one lookup in the ball's roll table instead of a search for the tick on
                    // which it arrives. Same question, and it is asked for every opponent on every
                    // candidate pass, which at 10 Hz is where the passing model's cost lives.
                    int manTicks = reach / _maxSpeed[ok] + _cfg.PassReactionTicks;
                    int slack = BallSkill.LaneCompletionPermille(
                        (_ball.RangeInTicks(force, manTicks) - (int)along) / U.Scale, _cfg);
                    if (slack < worst) worst = slack;
                }

                return worst;
            }

            /// <summary>Yes or no, for the support grid, which only wants to know if a lane is open.</summary>
            private bool PassSafe(int side, int fromX, int fromY, int toX, int toY, int force)
                => LaneCompletion(side, fromX, fromY, toX, toY, force) >= _cfg.ClearLaneCompletionPermille;
        }
    }
}
