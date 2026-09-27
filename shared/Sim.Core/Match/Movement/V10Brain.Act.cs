namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            /// <summary>The one decision a player makes per tick: what to do with the ball if he has it.</summary>
            public void Act(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                if (_sentOff[k]) return;

                // Putting a dead ball back in play.
                if (_ball.Dead)
                {
                    if (_ctx.DeadSide == side && _ctx.DeadTaker == slot && tick >= _ctx.DeadAt
                        && U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= _kickU)
                        _sim.TakeRestart(tick, side, slot);
                    return;
                }

                if (_ball.OwnerSide != side || _ball.OwnerSlot != slot) return;

                if (_hold[k] > 0)
                {
                    _hold[k]--;
                    return;
                }

                // Engine phase 4: he WEIGHS what he can do instead of working down a fixed ladder.
                // A pass, a run and a hoof are all quoted in the same currency — the metres of
                // forward progress he expects out of it, less what giving it away where it would be
                // lost is worth to the other side — so they can be compared at all. What he can see
                // of that risk comes off his Positioning; what he can execute comes off his Passing,
                // Technique and Dribbling. That is where the difference between two players lives.
                //
                // ENGINE PHASE 6 put the SHOT in that same list, and it is the whole phase. Before it,
                // nobody on this pitch ever decided to shoot: a director elected a scorer off the 1.4
                // timeline and worked the ball to him. Now a goal has a price in the one currency —
                // what it is worth times the odds he gives himself — and a man takes it on when it
                // beats the ball he could play instead. Which is why a striker in the box shoots and
                // a full-back on the halfway line does not, without either being told to.
                int pressure = _sim.PressurePermille(side, slot);
                int vision = _vision[k];

                int passValue = FindPass(tick, side, slot, pressure, vision, out PassChoice pass);
                int carryValue = CarryValue(side, slot, pressure, vision);
                int clearValue = ClearValue(side, slot, vision);
                int shootValue = ShootValue(side, slot);

                if (shootValue > NoOption && shootValue >= passValue && shootValue >= carryValue && shootValue >= clearValue)
                {
                    if (_ctx.ShotLive) _sim.ForceResolveShot(tick);
                    _sim.TakeShot(tick, side, slot);
                    return;
                }

                if (pass.Slot >= 0 && passValue >= carryValue && passValue >= clearValue)
                {
                    _sim.PlayPass(tick, side, slot, pass, pressure);
                    return;
                }

                if (clearValue > carryValue)
                {
                    _sim.Clear(tick, side, slot);
                    return;
                }

                _sim.Carry(tick, side, slot);
            }

            /// <summary>
            /// What losing the ball HERE is worth to the other side, in decimetres of forward
            /// progress. Priced at the place the ball would be lost, not the place it is now — which
            /// is the whole reason a clearance can be the right answer: it moves the turnover forty
            /// metres up the pitch, where it costs a fraction of what it costs on your own box.
            /// </summary>
            private int TurnoverCostDm(int side, int x)
            {
                int depth = side == 0 ? U.Dm(x) : Pitch.LengthDm - U.Dm(x);
                int third = depth * 3 / Pitch.LengthDm;
                int[] table = _cfg.TurnoverCostDm;
                if (table.Length == 0) return 0;
                if (third < 0) third = 0;
                if (third >= table.Length) third = table.Length - 1;
                return table[third];
            }

            /// <summary>
            /// Running with it. Worth the ground he covers, at the odds he keeps it past the man in
            /// front of him — which is a duel, so a dribbler carries where a centre-back would not.
            /// </summary>
            private int CarryValue(int side, int slot, int pressure, int vision)
            {
                int k = side * _n + slot;
                if (_keeper[k]) return NoOption;

                int touch = _sim.CarryTouchDm(k, pressure);

                // Only as far as there is pitch left to run into. Without this a man carries the
                // ball to the byline and keeps carrying, because the option is priced on the touch
                // he would take rather than on the ground actually in front of him — which is how
                // the play deserted the middle of the pitch and the ball spent a match sitting on
                // a boundary.
                int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
                int room = U.Dm(goalX > _px[k] ? goalX - _px[k] : _px[k] - goalX);
                if (touch > room) touch = room;

                int keep = 1000;

                int opponent = 1 - side;
                int nearest = -1, gap = int.MaxValue;
                for (int j = 0; j < _n; j++)
                {
                    int ok = opponent * _n + j;
                    if (_sentOff[ok]) continue;
                    int distance = U.Distance(_px[k], _py[k], _px[ok], _py[ok]);
                    if (distance < gap) { gap = distance; nearest = ok; }
                }

                if (nearest >= 0 && gap < _pressureU)
                {
                    int perTick = _sim.DuelWinPermille(k, nearest);
                    int loss = perTick * _cfg.DribbleFlightTicks;
                    keep = 1000 - BallSkill.Clamp(loss, 0, 850);
                }

                int value = BallSkill.OptionValue(
                    keep, touch + _cfg.PossessionValueDm, TurnoverCostDm(side, _px[k]), vision);
                return value * _cfg.CarryValuePercent / 100;
            }

            /// <summary>
            /// Hoofing it. He keeps it only now and then, but the ball is forty metres up the pitch
            /// when he loses it, and that is what makes it the right answer with three men on him.
            /// </summary>
            private int ClearValue(int side, int slot, int vision)
            {
                int k = side * _n + slot;
                if (_keeper[k]) return NoOption;
                if (!_sim.ClearRoom(side, k, out int landing)) return NoOption;

                int dir = MovementGeometry.Direction(side == 0);
                int gain = dir * (landing - _px[k]) / U.Scale;
                return BallSkill.OptionValue(
                    _cfg.ClearanceRetentionPermille, gain + _cfg.PossessionValueDm,
                    TurnoverCostDm(side, landing), vision);
            }

            /// <summary>
            /// Is this a strike anyone would recognise as one? The ball has to be alive, inside
            /// shooting range of the goal being attacked, and at the feet of the man about to hit
            /// it. Until all three hold the move is still being built and the chance waits.
            /// </summary>
            /// <summary>
            /// What HAVING A GO is worth to him, in the same decimetres of forward progress a pass, a
            /// run and a clearance are quoted in (engine phase 6). A goal is worth
            /// <see cref="MatchBalance.GoalValueDm"/>; the odds he gives himself are his reading of
            /// the chance — distance, angle, bodies in the way, and his own finishing. Losing it is
            /// priced where the ball is, exactly as every other option is, which is why a shot from
            /// thirty-five metres is a bad idea and a shot from eight is not.
            ///
            /// He does not have to be RIGHT about the odds. The outcome is settled by the ball, the
            /// keeper's dive and the posts, and the gap between the two is what a poor finisher is.
            ///
            /// AND WHAT A GOAL IS WORTH IS THE COACH'S (engine phase 8). Mentality and Tempo
            /// multiplied together price it: attacking and fast at about half again what a neutral
            /// side prices it, defensive and slow at two thirds. Nothing about the DECISION changes —
            /// he still weighs it against the pass, the run and the clearance in one currency, and
            /// the ball still settles it — but where the threshold falls moves, and the threshold is
            /// the thing phase 6 measured as stuck on the penalty spot. "Have a go" is an
            /// instruction; this is what the instruction does.
            /// </summary>
            private int ShootValue(int side, int slot)
            {
                int k = side * _n + slot;
                if (_keeper[k]) return NoOption;

                int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
                if (U.Distance(_ball.X, _ball.Y, goalX, U.CenterYU) > _shootRangeU) return NoOption;

                int goalValue = _cfg.GoalValueDm * _tactics[side].ShotAppetitePercent / 100;
                int odds = BallSkill.GoalOddsPermille(_sim.ShotQuality(side, slot), _cfg);
                return BallSkill.OptionValue(odds, goalValue, TurnoverCostDm(side, _ball.X), _vision[k]);
            }
        }
    }
}
