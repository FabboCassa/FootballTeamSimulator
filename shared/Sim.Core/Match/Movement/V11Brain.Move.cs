using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>Pulls a target back to within <paramref name="leash"/> of the spot.</summary>
        private static void Leash(int spotX, int spotY, int leash, ref int x, ref int y)
        {
            int dx = x - spotX, dy = y - spotY;
            if ((long)dx * dx + (long)dy * dy <= (long)leash * leash) return;
            U.Scaled(dx, dy, leash, out int lx, out int ly);
            x = spotX + lx;
            y = spotY + ly;
        }

        private sealed partial class V11Brain
        {
            /// <summary>
            /// Where this man goes (R2). A man placed for a V11 set piece (R6) is its own; the presser
            /// closes the man on the ball down goal-side of him; any other man the base brain has a
            /// job for is its, a press, cover or mark no further than V11JobLeashDm from his phase
            /// target spot (real-match spec R4); a supporter keeps its supporting run unless he is a
            /// forward with a run in behind on, and a pass meant for somebody else is chased only within
            /// reach of his place. Everybody else goes to his phase target spot — held onside when his
            /// side has the ball — or, for a full-back whose side asks for it, on the overlap.
            /// </summary>
            public void Move(int tick, int side, int slot)
            {
                if (_setPieces.Move(tick, side, slot))
                {
                    EndSpell(side, slot);
                    return;
                }

                BaseJob job = _base.JobOf(tick, side, slot);
                if (job == BaseJob.Chase && ChasesAPassOutOfReach(side, slot)) job = BaseJob.None;
                bool leashed = job == BaseJob.Press || job == BaseJob.Cover || job == BaseJob.Mark;
                if (job != BaseJob.None && job != BaseJob.Support && !leashed)
                {
                    EndSpell(side, slot);
                    _base.Move(side, slot, job, 0, 0, 0);
                    return;
                }

                int k = side * _sim._n + slot;
                bool home = side == 0;
                MatchBall ball = _sim._ball;
                V11Slot man = SlotOf(side, slot);
                TacticInstructions instructions = _sim._tactics[side].Instructions;
                TeamPhase phase = _phases.PhaseOf(side);
                bool inPossession = _phases.InPossession(side);

                _positioning.PhaseSpot(man, home, phase, _expansion[side], instructions,
                    U.Dm(ball.X), U.Dm(ball.Y), out int sx, out int sy);
                if (man.Role != Domain.PositionRole.Goalkeeper)
                    sx = Pitch.ClampX(sx + MovementGeometry.Direction(home) * _sim._tactics[side].ShoutLinePushDm);
                if (inPossession) sx = _positioning.Onside(home, sx, _lineX[side]);
                int spotX = U.ClampX(U.Units(sx));
                int spotY = _sim._ctx.Inside(U.Units(sy));

                if (_setPieces.Offers(side, slot))
                {
                    // R11: his place in the shape, held within reach of the throw.
                    EndSpell(side, slot);
                    RestartShape.OfferSpot(U.Dm(ball.X), U.Dm(ball.Y), U.Dm(spotX), U.Dm(spotY), _sim._cfg,
                        out int ox, out int oy);
                    _sim.Steer(k, U.ClampX(U.Units(ox)), _sim._ctx.Inside(U.Units(oy)), sprint: true);
                    return;
                }

                if (leashed)
                {
                    // R4: the job is done from his place in the block, not wherever it leads him.
                    EndSpell(side, slot);
                    int leash = U.Units(_sim._cfg.V11JobLeashDm);
                    if (job == BaseJob.Press && _sim._cfg.V11PressStandOffPercent > 0) PressGoalSide(side, slot, spotX, spotY, leash);
                    else _base.Move(side, slot, job, spotX, spotY, leash);
                    return;
                }

                int tx = spotX, ty = spotY;
                bool sprint = false;
                if (RunTarget(tick, side, slot, man, inPossession, out int rx, out int ry))
                {
                    tx = rx;
                    ty = ry;
                    sprint = true;
                }
                else if (job == BaseJob.Support)
                {
                    // He offers the angle the base brain found, on a leash from his own spot: a supporting
                    // run across the whole pitch leaves his place in the shape empty.
                    _base.SupportSpot(side, out tx, out ty);
                    Leash(spotX, spotY, U.Units(_sim._cfg.V11SupportLeashDm), ref tx, ref ty);
                    sprint = U.DistanceSq(_sim._px[k], _sim._py[k], tx, ty) > (long)_sim._approachU * _sim._approachU;
                }
                else if (OverlapTarget(side, slot, man, phase, inPossession, instructions, sx, out int ox, out int oy))
                {
                    tx = ox;
                    ty = oy;
                    Leash(spotX, spotY, U.Units(_sim._cfg.V11SupportLeashDm), ref tx, ref ty);
                    _overlaps[side]++;
                }

                // Far from where he is going he runs, whichever side has the ball: a shape that
                // re-forms at a jog is a shape with holes in it for a dozen seconds.
                tx = U.ClampX(tx);
                ty = _sim._ctx.Inside(ty);
                long catchUp = U.Units(_sim._cfg.V11CatchUpSprintDm);
                if (!sprint && U.DistanceSq(_sim._px[k], _sim._py[k], tx, ty) > catchUp * catchUp)
                    sprint = true;

                _sim.Steer(k, tx, ty, sprint);
                CountOffTarget(k, spotX, spotY);
            }

            private V11Slot SlotOf(int side, int slot)
            {
                int k = side * _sim._n + slot;
                Lineup lineup = side == 0 ? _sim._home : _sim._away;
                return new V11Slot(lineup.Slots[slot].Role, _sim._lineRank[k], _sim._lineCount[side],
                    _sim._baseY[k], _sim._offsetXDm[k]);
            }

            /// <summary>
            /// The presser closes the man on the ball down from the GOAL side, a stand-off short of
            /// him on the line from the ball to the centre of his own goal, rather than from wherever
            /// he happens to be: a carrier running at goal runs into him and has to beat him in a
            /// duel, instead of leaving a presser chasing his shadow from the side.
            /// </summary>
            private void PressGoalSide(int side, int slot, int spotX, int spotY, int leash)
            {
                int k = side * _sim._n + slot;
                MatchBall ball = _sim._ball;
                int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int standOff = _sim._tactics[side].PressStandOffU * _sim._cfg.V11PressStandOffPercent / 100;
                U.Scaled(goalX - ball.X, U.CenterYU - ball.Y, standOff, out int dx, out int dy);
                int tx = ball.X + dx, ty = ball.Y + dy;
                Leash(spotX, spotY, leash, ref tx, ref ty);
                _sim.Steer(k, U.ClampX(tx), _sim._ctx.Inside(ty), sprint: true);
            }

            /// <summary>
            /// Real-match spec R4: with a pass on its way to somebody else, a man goes after it only
            /// where it can be met within V11PassChaseLeashDm of his phase target spot. Further off
            /// he holds his place: a centre-back sprinting thirty metres after a ball the receiver
            /// will reach first is a centre-back thirty metres out of the line when it arrives.
            /// A loose ball nobody was meant to have is still chased wherever it is.
            /// </summary>
            private bool ChasesAPassOutOfReach(int side, int slot)
            {
                if (!_base.PassToSomebodyElse(side, slot)) return false;
                MatchBall ball = _sim._ball;
                _positioning.PhaseSpot(SlotOf(side, slot), side == 0, _phases.PhaseOf(side), _expansion[side],
                    _sim._tactics[side].Instructions, U.Dm(ball.X), U.Dm(ball.Y), out int sx, out int sy);
                _base.InterceptFor(side, slot, out int x, out int y);
                long leash = U.Units(_sim._cfg.V11PassChaseLeashDm);
                return U.DistanceSq(x, y, U.Units(sx), U.Units(sy)) > leash * leash;
            }

            private void EndSpell(int side, int slot)
            {
                if (_runner[side] == slot) _runner[side] = -1;
                _spell[side * _sim._n + slot] = 0;
            }

            /// <summary>
            /// R2: a man with no job who is further than V11OffTargetDm from his phase target is on
            /// an off-target spell; the spell ends when he is back within it or takes a job
            /// (chasing, pressing, covering, marking...). Every such tick is "far"; the ticks of a
            /// spell past V11OffTargetGraceMs are "off target" — the man sitting out of position
            /// for more than 5 s that R2 forbids, not the sprint back after a lost chase.
            /// </summary>
            private void CountOffTarget(int k, int spotX, int spotY)
            {
                long limit = U.Units(_sim._cfg.V11OffTargetDm);
                if (U.DistanceSq(_sim._px[k], _sim._py[k], spotX, spotY) <= limit * limit)
                {
                    _spell[k] = 0;
                    return;
                }

                _farTicks[k]++;
                if (++_spell[k] > _sim._cfg.V11OffTargetGraceTicks) _offTarget[k]++;
            }

            /// <summary>
            /// The run in behind: one runner a side at a time, kept for its window while his side
            /// has the ball live, and started only when <see cref="V11Positioning.RunInBehind"/>
            /// finds him onside with a lane into open space behind the line.
            /// </summary>
            private bool RunTarget(int tick, int side, int slot, V11Slot man, bool inPossession, out int x, out int y)
            {
                x = 0;
                y = 0;
                MatchBall ball = _sim._ball;
                if (!inPossession || ball.Dead)
                {
                    _runner[side] = -1;
                    return false;
                }

                if (_runner[side] == slot)
                {
                    if (tick < _runUntil[side])
                    {
                        x = _runX[side];
                        y = _runY[side];
                        return true;
                    }

                    _runner[side] = -1;
                }

                if (_runner[side] >= 0 || !man.IsForward) return false;
                if (ball.OwnerSide != side || ball.OwnerSlot == slot) return false;

                int n = _sim._n;
                int k = side * n + slot, carrier = side * n + ball.OwnerSlot;
                if (!_positioning.RunInBehind(side == 0,
                        U.Dm(_sim._px[k]), U.Dm(_sim._py[k]), U.Dm(_sim._px[carrier]), U.Dm(_sim._py[carrier]),
                        _lineX[side], _foeX[side], _foeY[side], _foeCount[side], out int rx, out int ry))
                    return false;

                _runner[side] = slot;
                _runUntil[side] = tick + _sim._cfg.V11RunTicks;
                _runX[side] = U.Units(rx);
                _runY[side] = U.Units(ry);
                _runs[side]++;
                x = _runX[side];
                y = _runY[side];
                return true;
            }

            private bool OverlapTarget(
                int side, int slot, V11Slot man, TeamPhase phase, bool inPossession, TacticInstructions instructions,
                int spotXDm, out int x, out int y)
            {
                x = 0;
                y = 0;
                MatchBall ball = _sim._ball;
                if (ball.Dead || ball.OwnerSide != side || ball.OwnerSlot == slot) return false;

                int n = _sim._n;
                int carrier = side * n + ball.OwnerSlot;
                if (!_positioning.Overlap(man, side == 0, phase, inPossession, instructions,
                        spotXDm, U.Dm(_sim._px[carrier]), U.Dm(_sim._py[carrier]),
                        out int ox, out int oy))
                    return false;

                x = U.Units(ox);
                y = U.Units(oy);
                return true;
            }
        }
    }
}
