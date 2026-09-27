namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            /// <summary>
            /// Reading the game. Split in two on purpose (engine phase 1): who is nearest the ball
            /// changes every tenth of a second and is cheap, but who picks up whom, and where the
            /// supporting run goes, are DECISIONS — a defender does not repick his man ten times a
            /// second, and re-deciding at 10 Hz would cost fifty times what it costs at 2 Hz for a
            /// picture nobody could tell apart.
            /// </summary>
            public void UpdateTeams(int tick)
            {
                int brain = _cfg.TeamBrainTicks < 1 ? 1 : _cfg.TeamBrainTicks;
                for (int side = 0; side < SideCount; side++)
                {
                    bool wasAttacking = _attacking[side];
                    _attacking[side] = _ball.OwnerSide == side
                        || (_ball.Free && _ball.LastTouchSide == side);

                    // A side that has just lost or won the ball re-reads the game at once, whatever
                    // the cadence says: waiting half a second to notice would be a hole in the block.
                    bool rethink = tick % brain == 0 || wasAttacking != _attacking[side];

                    _chaser[side] = _sim.NearestToBall(side, includeKeeper: false);
                    _second[side] = SecondNearestToBall(side, _chaser[side]);

                    // A side WITH the ball pushes its block up (engine phase 6). This used to be the
                    // director's extra shove for the forty-five ticks before a scripted chance; now
                    // it is simply what having the ball means, and it fades in and out with the
                    // expansion rather than switching on for a minute at a time.
                    _driving[side] = _attacking[side];

                    // The shape, once for the tick, and FIRST: everything that asks where a man
                    // belongs reads it and nothing recomputes it, and the duties below are read off
                    // it — where the back line is decides who has got in behind it.
                    UpdateBlock(side);

                    if (_attacking[side])
                    {
                        if (rethink) UpdateSupport(tick, side);
                    }
                    else if (rethink)
                    {
                        AssignDuties(side);
                    }
                }
            }

            /// <summary>
            /// The supporting run. A grid of spots in the attacking half is scored on whether the
            /// man on the ball could find it, whether a goal could be struck from it, and whether
            /// it is a comfortable distance away; the best of them is where the attackers run.
            /// This is what a viewer reads as a pattern of play rather than as milling about.
            /// </summary>
            private void UpdateSupport(int tick, int side)
            {
                // A destination, not a reflex: recomputed on its own slower clock, and only ever on
                // a tick the brain is already awake for, so the two cadences cannot drift apart.
                int period = _cfg.SupportRecalcTicks < 1 ? 1 : _cfg.SupportRecalcTicks;
                if (tick % period == 0 || _supportX[side] == 0)
                    FindSupportSpot(side);

                int carrier = _ball.OwnerSide == side ? _ball.OwnerSlot : -1;
                int wanted = _tactics[side].Supporters;
                if (wanted > MaxSupporters) wanted = MaxSupporters;

                for (int s = 0; s < MaxSupporters; s++) _supporters[side * MaxSupporters + s] = -1;

                // The most advanced men who are neither on the ball nor chasing it go and support.
                for (int s = 0; s < wanted; s++)
                {
                    int best = -1, bestScore = int.MinValue;
                    for (int i = 0; i < _n; i++)
                    {
                        int k = side * _n + i;
                        if (_keeper[k] || _sentOff[k] || i == carrier || i == _chaser[side]) continue;
                        if (AlreadySupporting(side, i)) continue;

                        int distance = U.Distance(_px[k], _py[k], _supportX[side], _supportY[side]);
                        int score = _forward[k] * 4 - distance / U.Scale;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = i;
                        }
                    }

                    _supporters[side * MaxSupporters + s] = best;
                }
            }

            private bool AlreadySupporting(int side, int slot)
            {
                for (int s = 0; s < MaxSupporters; s++)
                    if (_supporters[side * MaxSupporters + s] == slot) return true;
                return false;
            }

            private void FindSupportSpot(int side)
            {
                bool home = side == 0;
                int dir = MovementGeometry.Direction(home);
                int goalX = U.Units(MovementGeometry.AttackedGoalX(home));

                int columns = _cfg.SupportSpotColumns, rows = _cfg.SupportSpotRows;
                int fromX = home ? U.CenterXU : U.Units(60);
                int spanX = home ? U.LengthU - U.Units(60) - fromX : U.CenterXU - fromX;

                int carrierX = _ball.X, carrierY = _ball.Y;
                int bestX = U.CenterXU + dir * U.Units(200), bestY = U.CenterYU, bestScore = int.MinValue;

                for (int c = 0; c < columns; c++)
                {
                    int x = fromX + spanX * (c + 1) / (columns + 1);
                    for (int r = 0; r < rows; r++)
                    {
                        int y = U.Units(80) + (U.WidthU - U.Units(160)) * r / (rows - 1);

                        int score = 0;
                        if (PassSafe(side, carrierX, carrierY, x, y, _maxPassForce)) score += 200;
                        if (U.Distance(x, y, goalX, U.CenterYU) < _shootRangeU
                            && PassSafe(side, x, y, goalX, U.CenterYU, _maxShootForce)) score += 150;

                        int gap = U.Distance(x, y, carrierX, carrierY);
                        int ideal = U.Units(_cfg.SupportIdealDistanceDm);
                        int off = gap > ideal ? gap - ideal : ideal - gap;
                        score += 120 - off / U.Scale;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }

                _supportX[side] = bestX;
                _supportY[side] = bestY;
            }

            /// <summary>
            /// Who does what, for a side without the ball (engine phase 3).
            ///
            /// What this replaced put a marker on every one of the ten opponents, wherever he was
            /// (§1.5): ten duels roaming the pitch, a Zone branch that ran 0.0% of the time, and a
            /// defending shape that was the attacking shape moved five metres back — which is why the
            /// two rows of the measurement agreed to a tenth of a metre. Football is zonal. One man
            /// goes to the ball (decided every tick, not here), one covers him, an opponent is picked
            /// up only where he is genuinely dangerous — inside our own third, or already through the
            /// back line — and everybody else holds his place in the block.
            /// </summary>
            private void AssignDuties(int side)
            {
                int opponent = 1 - side;
                bool home = side == 0;

                for (int i = 0; i < _n; i++)
                {
                    _duty[side * _n + i] = DutyZone;
                    _mark[side * _n + i] = -1;
                }

                // The line, in depth from our own goal: past it an opponent is in behind.
                int lineDepth = home
                    ? U.Dm(_blockLineU[side])
                    : Pitch.LengthDm - U.Dm(_blockLineU[side]);

                bool[] taken = _markTaken;
                for (int i = 0; i < _n; i++) taken[i] = false;

                // The dangerous ones first — nearest our goal wins, and a man the timeline says has
                // found a yard counts as nearer than he is.
                for (int pass = 0; pass < _cfg.MaxMarkers; pass++)
                {
                    int worst = -1, worstDanger = int.MaxValue;
                    for (int j = 0; j < _n; j++)
                    {
                        int ok = opponent * _n + j;
                        if (_keeper[ok] || _sentOff[ok] || taken[j]) continue;

                        int depth = home ? U.Dm(_px[ok]) : Pitch.LengthDm - U.Dm(_px[ok]);
                        bool inOurThird = depth <= _cfg.MarkOwnThirdDepthDm;
                        bool inBehind = depth < lineDepth - _cfg.MarkBehindLineDm;
                        if (!inOurThird && !inBehind) continue;

                        int danger = depth;
                        if (danger < worstDanger) { worstDanger = danger; worst = j; }
                    }

                    if (worst < 0) break;
                    taken[worst] = true;

                    // The nearest man who has nothing else to do takes him.
                    int foe = opponent * _n + worst;
                    int best = -1;
                    long bestDistance = long.MaxValue;
                    for (int i = 0; i < _n; i++)
                    {
                        int k = side * _n + i;
                        if (_keeper[k] || _sentOff[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                        long d = U.DistanceSq(_px[k], _py[k], _px[foe], _py[foe]);
                        if (d < bestDistance) { bestDistance = d; best = i; }
                    }

                    if (best < 0) break;
                    _duty[side * _n + best] = DutyMark;
                    _mark[side * _n + best] = worst;
                }

                // And one man covers the space behind whoever goes to the ball.
                CoverSpot(side, out int cx, out int cy);
                int cover = -1;
                long coverRange = (long)U.Units(_cfg.CoverMaxRangeDm) * U.Units(_cfg.CoverMaxRangeDm);
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (_keeper[k] || _sentOff[k] || _duty[k] != DutyZone || _chaser[side] == i) continue;
                    if (_lineRank[k] == 0) continue;   // a centre-back covering is a centre-back out of the line
                    long d = U.DistanceSq(_px[k], _py[k], cx, cy);
                    if (d < coverRange) { coverRange = d; cover = i; }
                }

                if (cover >= 0) _duty[side * _n + cover] = DutyCover;
            }

            private int SecondNearestToBall(int side, int excluded)
            {
                int best = -1;
                long bestDistance = long.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    int k = side * _n + i;
                    if (i == excluded || _keeper[k] || _sentOff[k]) continue;
                    long d = U.DistanceSq(_px[k], _py[k], _ball.X, _ball.Y);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }

                return best;
            }
        }
    }
}
