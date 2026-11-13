using Sim.Core.Config;
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
            /// target spot (real-match spec R4); in the build-up and the progression the men nearest
            /// the ball offer the short option (R9, V11Brain.Support), ahead of a supporting run; a
            /// supporter keeps its supporting run unless he is a forward with a run in behind on, and a
            /// pass meant for somebody else is chased only within reach of his place. Everybody else
            /// goes to his phase target spot — held onside when his side has the ball — or, for a
            /// full-back whose side asks for it, on the overlap; without the ball, to his role job
            /// (free-ball spec R7: step, tuck, screen). His place is read off the ball as he last saw
            /// it (staggered re-reads), and a run bends round an opponent in its way.
            /// </summary>
            public void Move(int tick, int side, int slot)
            {
                int k = side * _sim._n + slot;
                if (_setPieces.Move(tick, side, slot))
                {
                    _sim.TraceJob(k, MovementJob.SetPiece, MovementJob.SetPiece);
                    EndSpell(side, slot);
                    return;
                }

                BaseJob asked = _base.JobOf(tick, side, slot);
                BaseJob job = asked;
                if (job == BaseJob.Chase && ChasesAPassOutOfReach(side, slot)) job = BaseJob.None;
                bool leashed = job == BaseJob.Press || job == BaseJob.Cover || job == BaseJob.Mark;
                _sim.TraceJob(k, (MovementJob)(int)asked, (MovementJob)(int)job);
                // Issue #206: the opponent in the central zone is closed before anything else, unless
                // the man closing him can get to the ball itself first.
                if ((job != BaseJob.Chase || !_base.InterceptFor(side, slot, out _, out _))
                    && ZoneCloser(side, slot, out int zx, out int zy))
                {
                    _sim.TraceJob(k, MovementJob.ZoneClose);
                    EndSpell(side, slot);
                    _sim.Steer(k, zx, zy, sprint: true);
                    return;
                }

                if (job == BaseJob.Chase && RunsOnto(side, slot))
                {
                    EndSpell(side, slot);
                    return;
                }

                if (job != BaseJob.None && job != BaseJob.Support && !leashed)
                {
                    EndSpell(side, slot);
                    _base.Move(tick, side, slot, job, 0, 0, 0);
                    return;
                }

                bool home = side == 0;
                MatchBall ball = _sim._ball;
                V11Slot man = SlotOf(side, slot);
                TacticInstructions instructions = _sim._tactics[side].Instructions;
                TeamPhase phase = _phases.PhaseOf(side);
                bool inPossession = _phases.InPossession(side);

                ReRead(tick, side, k);
                _positioning.PhaseSpot(man, home, phase, _expansion[side], instructions,
                    U.Dm(_seenBallX[k]), U.Dm(_seenBallY[k]), out int sx, out int sy);
                if (man.Role != Domain.PositionRole.Goalkeeper)
                    sx = Pitch.ClampX(sx + MovementGeometry.Direction(home) * _sim._tactics[side].ShoutLinePushDm);
                int rawSx = sx;
                if (inPossession) sx = _positioning.Onside(home, sx, _lineX[side]);
                int spotX = U.ClampX(U.Units(sx));
                int spotY = _sim._ctx.Inside(U.Units(sy));
                _sim.TraceSpot(k, rawSx, U.Dm(spotX), U.Dm(spotY));

                if (_setPieces.Offers(side, slot))
                {
                    // R11: his place in the shape, held within reach of the throw.
                    _sim.TraceJob(k, MovementJob.RestartOffer);
                    EndSpell(side, slot);
                    RestartShape.OfferSpot(U.Dm(ball.X), U.Dm(ball.Y), U.Dm(spotX), U.Dm(spotY), _sim._cfg,
                        out int ox, out int oy);
                    _sim.SteerVia(k, U.ClampX(U.Units(ox)), _sim._ctx.Inside(U.Units(oy)), true, -1, -1, run: true);
                    return;
                }

                if (!inPossession && job != BaseJob.Press && ShotLine(side, slot, out int lx, out int ly))
                {
                    _sim.TraceJob(k, MovementJob.ShotLine);
                    EndSpell(side, slot);
                    _sim.Steer(k, lx, ly, sprint: true);
                    return;
                }

                if (!inPossession && job != BaseJob.Press && BoxMark(side, slot, out int mx, out int my))
                {
                    _sim.TraceJob(k, MovementJob.BoxMark);
                    EndSpell(side, slot);
                    _sim.Steer(k, mx, my, sprint: true);
                    return;
                }

                if (leashed)
                {
                    // R4: the job is done from his place in the block, not wherever it leads him.
                    EndSpell(side, slot);
                    int leash = U.Units(JobLeashDm(side, job));
                    if (job == BaseJob.Press && _sim._cfg.V11PressStandOffPercent > 0)
                    {
                        _sim.TraceJob(k, MovementJob.PressGoalSide);
                        PressGoalSide(side, slot, spotX, spotY, leash);
                    }
                    else _base.Move(tick, side, slot, job, spotX, spotY, leash);
                    return;
                }

                int tx = spotX, ty = spotY;
                bool sprint = false;
                bool running = RunTarget(tick, side, slot, man, inPossession, out int rx, out int ry);
                bool arriving = !running && _arriver[side] == slot;
                int px = 0, py = 0;
                MovementJob post = MovementJob.NearPost;
                bool posting = !running && !arriving && PostRun(side, slot, out px, out py, out post);
                if (running)
                {
                    tx = rx;
                    ty = ry;
                    sprint = true;
                    _sim.TraceJob(k, MovementJob.Run);
                }
                else if (arriving)
                {
                    _roleJobs.BoxSpot(home, out int ax, out int ay);
                    tx = U.ClampX(U.Units(ax));
                    ty = _sim._ctx.Inside(U.Units(ay));
                    sprint = true;
                    _sim.TraceJob(k, MovementJob.BoxArrival);
                }
                else if (posting)
                {
                    tx = px;
                    ty = py;
                    sprint = true;
                    _sim.TraceJob(k, post);
                }
                else if (OverlapTarget(tick, side, slot, man, phase, inPossession, instructions, sx, out int ox, out int oy))
                {
                    tx = ox;
                    ty = oy;
                    Leash(spotX, spotY, U.Units(_sim._cfg.V11SupportLeashDm), ref tx, ref ty);
                    _overlaps[side]++;
                    _sim.TraceJob(k, MovementJob.Overlap);
                }
                else if (inPossession && OfferTarget(side, slot, out int fx, out int fy))
                {
                    // R9: the short option, a pass away from the ball at an angle nobody shuts, on a
                    // leash from his own spot.
                    tx = fx;
                    ty = fy;
                    Leash(spotX, spotY, U.Units(_sim._cfg.V11SupportOfferLeashDm), ref tx, ref ty);
                    sprint = U.DistanceSq(_sim._px[k], _sim._py[k], tx, ty) > (long)_sim._approachU * _sim._approachU;
                    _offerTicks[side]++;
                    _sim.TraceJob(k, MovementJob.Offer);
                }
                else if (inPossession && _roleJobs.HoldWidth(man, home, _expansion[side], instructions, U.Dm(_seenBallY[k]), out int laneY))
                {
                    // Before the supporting run: the run's shared grid spot is what pulled him off his
                    // flank and back again (oscillation trace section 1).
                    tx = U.ClampX(U.Units(_roleJobs.WideManX(home, sx, rawSx)));
                    ty = _sim._ctx.Inside(U.Units(laneY));
                    _sim.TraceJob(k, MovementJob.Width);
                }
                else if (job == BaseJob.Support)
                {
                    // He offers the angle the base brain found, on a leash from his own spot: a supporting
                    // run across the whole pitch leaves his place in the shape empty.
                    tx = _seenSupportX[k];
                    ty = _seenSupportY[k];
                    Leash(spotX, spotY, U.Units(_sim._cfg.V11SupportLeashDm), ref tx, ref ty);
                    sprint = U.DistanceSq(_sim._px[k], _sim._py[k], tx, ty) > (long)_sim._approachU * _sim._approachU;
                }
                else if (!inPossession && RoleJob(side, man, k, spotX, spotY, out int jx, out int jy, out sprint, out MovementJob role))
                {
                    tx = jx;
                    ty = jy;
                    _sim.TraceJob(k, role);
                }

                int viaX = -1, viaY = -1;
                bool back = inPossession && !running && (man.Role == Domain.PositionRole.Striker || arriving || posting)
                            && HoldOnside(home, side, k, ref tx, ref sprint, out viaX, out viaY);
                if (inPossession && !running && _roleJobs.HoldBehind(man, home, U.Dm(tx), U.Dm(_seenBallX[k]), out int behindX))
                {
                    tx = U.Units(behindX);
                    _sim.TraceJob(k, MovementJob.HoldBehind);
                }

                // Far from where he is going he runs, whichever side has the ball: a shape that
                // re-forms at a jog is a shape with holes in it for a dozen seconds.
                tx = U.ClampX(tx);
                ty = _sim._ctx.Inside(ty);
                long catchUp = U.Units(inPossession ? _sim._cfg.V11CatchUpSprintDm : _sim._cfg.V11RecoverySprintDm);
                if (!sprint && U.DistanceSq(_sim._px[k], _sim._py[k], tx, ty) > catchUp * catchUp)
                    sprint = true;

                // R7: a run bends round an opponent in its way; a man jogging into his place does not.
                if (sprint && !back) CurveVia(side, k, tx, ty, out viaX, out viaY);
                // Issue #215: the run in behind, the box runs and a striker caught well beyond the line
                // go flat out; any other sprint here is a positional run (PlayerRunPacePercent).
                bool sprintBack = back
                    && V11Positioning.BeyondLine(home, U.Dm(_sim._px[k]), _lineX[side]) > _sim._cfg.V11HoldBackSprintDm;
                _sim.SteerVia(k, tx, ty, sprint, viaX, viaY, run: sprint && !running && !arriving && !posting && !sprintBack);
                CountOffTarget(k, spotX, spotY);
            }

            /// <summary>
            /// Free-ball spec R6: with his side on the ball and none played to him, the striker waits
            /// on the offside line, not beyond it. The hold binds where he is going, a supporting
            /// run or a short offer as much as his place in the shape (it bound only the place, and
            /// the support spot that replaced it parked him offside for 18-22% of his side's
            /// possession, docs/engine/oscillation-trace.md). Caught beyond the line, he runs back,
            /// straight to it first (the waypoint), rather than easing onto his spot at a walk or
            /// crossing the pitch offside. True when he is running back.
            /// </summary>
            private bool HoldOnside(bool home, int side, int k, ref int tx, ref bool sprint, out int viaX, out int viaY)
            {
                viaX = -1;
                viaY = -1;
                int line = _lineX[side];
                int held = _positioning.Onside(home, U.Dm(tx), line);
                if (held != U.Dm(tx)) tx = U.Units(held);
                int x = U.Dm(_sim._px[k]);
                if (V11Positioning.BeyondLine(home, x, line) <= _sim._cfg.V11HoldBeyondDm) return false;
                viaX = U.Units(_positioning.Onside(home, x, line));
                viaY = _sim._py[k];
                sprint = true;
                return true;
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
                _sim.ManOnTheBall(out int bx, out int by);
                int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int standOff = _sim._tactics[side].PressStandOffU * _sim._cfg.V11PressStandOffPercent / 100
                               - ZonePressCloseInU(goalX, bx, by);
                if (standOff < 0) standOff = 0;
                U.Scaled(goalX - bx, U.CenterYU - by, standOff, out int dx, out int dy);
                int tx = bx + dx, ty = by + dy + PressTouchlineU(side, by);
                Leash(spotX, spotY, leash, ref tx, ref ty);
                _sim.Steer(k, U.ClampX(tx), _sim._ctx.Inside(ty), sprint: true);
            }

            /// <summary>
            /// Issue #206: how much closer than his instruction's stand-off the presser stands (units):
            /// none far from his own goal, up to V11ZonePressCloseInDm as the man on the ball nears
            /// the central zone.
            /// </summary>
            private int ZonePressCloseInU(int goalX, int bx, int by)
            {
                MatchBalance cfg = _sim._cfg;
                int from = cfg.V11ZonePressFromDm, to = cfg.V11ZonePressToDm, closeIn = cfg.V11ZonePressCloseInDm;
                int d = U.Dm(U.Length(bx - goalX, by - U.CenterYU));
                if (d >= from || from <= to) return 0;
                if (d <= to) return U.Units(closeIn);
                return U.Units(closeIn * (from - d) / (from - to));
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
                    // R6: the ball must come before he is past the line; once he is, the run is over.
                    int me = side * _sim._n + slot;
                    bool overran = V11Positioning.BeyondLine(side == 0, U.Dm(_sim._px[me]), _lineX[side])
                                   > _sim._cfg.V11HoldBeyondDm;
                    if (tick < _runUntil[side] && !overran)
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

            /// <summary>
            /// R7's overlap: one full-back a side, started by <see cref="V11Positioning.Overlap"/> and
            /// held for V11OverlapCommitMs with the ball alive and his side keeping it, on a target
            /// fixed when it starts and never beyond the offside line.
            /// </summary>
            private bool OverlapTarget(
                int tick, int side, int slot, V11Slot man, TeamPhase phase, bool inPossession, TacticInstructions instructions,
                int spotXDm, out int x, out int y)
            {
                x = 0;
                y = 0;
                MatchBall ball = _sim._ball;
                if (!inPossession || ball.Dead) return false;

                bool home = side == 0;
                if (_overlapper[side] == slot)
                {
                    x = U.Units(_positioning.Onside(home, U.Dm(_overlapX[side]), _lineX[side]));
                    y = _overlapY[side];
                    return true;
                }

                if (_overlapper[side] >= 0 || ball.OwnerSide != side || ball.OwnerSlot == slot) return false;

                int carrier = side * _sim._n + ball.OwnerSlot;
                if (!_positioning.Overlap(man, home, phase, inPossession, instructions,
                        spotXDm, U.Dm(_sim._px[carrier]), U.Dm(_sim._py[carrier]),
                        out int ox, out int oy))
                    return false;

                _overlapper[side] = slot;
                _overlapUntil[side] = tick + _sim._cfg.V11OverlapCommitTicks;
                _overlapX[side] = U.Units(ox);
                _overlapY[side] = U.Units(oy);
                x = U.Units(_positioning.Onside(home, ox, _lineX[side]));
                y = _overlapY[side];
                return true;
            }
        }
    }
}
