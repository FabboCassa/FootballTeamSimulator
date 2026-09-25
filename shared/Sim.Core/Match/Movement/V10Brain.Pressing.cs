namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class V10Brain
        {
            /// <summary>
            /// Is this man going to the ball? Hands back his defensive home spot either way, because
            /// the caller needs it whichever answer it gets and it is not cheap enough to work out
            /// twice for eleven men, ten times a second.
            /// </summary>
            private bool Pressing(int tick, int side, int slot, out int homeX, out int homeY)
            {
                int k = side * _n + slot;
                HomeSpot(side, slot, out homeX, out homeY);

                if (_ball.OwnerSide == side || _ball.Dead) return false;

                // A SECOND man closes in when the ball is in the third the side is defending: one
                // presser is easy to play around, and until engine phase 6 the only thing that ever
                // sent a second man was the director's urgency window before a scripted chance. Where
                // it matters is exactly where it matters in football — near your own goal.
                //
                // AND HOW FAR UP THE PITCH THAT IS, IS THE PRESSING INSTRUCTION (engine phase 8). It
                // is the lever that moves WHERE the ball is won rather than merely how often: doubling
                // up in the other side's half is what turns a press into recoveries in the final
                // third, and a low block that only ever doubles up on the edge of its own box wins
                // the ball back deep by construction. Neutral is the config value unchanged.
                if (_chaser[side] != slot)
                {
                    if (_keeper[k] || _second[side] != slot) return false;
                    int dir = MovementGeometry.Direction(side == 0);
                    int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                    if (dir * (_ball.X - ownGoalX) > U.Units(_tactics[side].SecondPressDepthDm)) return false;
                }

                long reach = _tactics[side].PressReachU;

                // The TRIGGER (engine phase 3). A side does not chase the man on the ball
                // wherever he stands: it has a zone it presses in, which is what the coach's
                // Pressing instruction has always meant, plus the two situations that switch the
                // press on outside it — a ball played backwards, and a man receiving with a
                // touchline behind him. Outside both, the block holds its shape and lets him
                // have it. (A loose ball is not in here on purpose: it is chased by the branch
                // above this one, whoever owns the trigger.)
                int ballDepth = side == 0 ? U.Dm(_ball.X) : Pitch.LengthDm - U.Dm(_ball.X);
                if (ballDepth > _tactics[side].PressTriggerDepthDm) return false;

                int pressBoost = 100;
                if (_pressUntil[side] > tick) pressBoost = pressBoost * _cfg.PressBackPassPercent / 100;
                int pressOffCentre = _ball.Y - U.CenterYU;
                if (pressOffCentre < 0) pressOffCentre = -pressOffCentre;
                if (pressOffCentre > U.Units(_cfg.PressWideThresholdDm))
                    pressBoost = pressBoost * _cfg.PressWideReceptionPercent / 100;
                reach = reach * pressBoost / 100;

                return U.DistanceSq(homeX, homeY, _ball.X, _ball.Y) <= reach * reach;
            }

            /// <summary>
            /// Where the covering man stands: goal-side of the ball and a few metres off it. Not on
            /// the ball — that is the presser's job — but in the space the presser left behind him.
            /// </summary>
            private void CoverSpot(int side, out int x, out int y)
            {
                bool home = side == 0;
                int goalX = U.Units(MovementGeometry.OwnGoalX(home));
                U.Scaled(goalX - _ball.X, U.CenterYU - _ball.Y, U.Units(_cfg.CoverDistanceDm),
                    out int gx, out int gy);
                x = U.ClampX(_ball.X + gx);
                y = U.ClampY(_ball.Y + gy);
            }

            /// <summary>
            /// Where the presser puts himself: a stride off the ball, on the line between him and it.
            /// HOW BIG a stride is the Pressing instruction (engine phase 8) — a high press is
            /// touch-tight and a low block CONTAINS, standing off him and keeping its shape. This is
            /// the number `[press]` reads: the space left to the man on the ball.
            /// </summary>
            private void PressSpot(int side, int k, out int x, out int y)
            {
                int dx = _px[k] - _ball.X, dy = _py[k] - _ball.Y;
                int distance = U.Length(dx, dy);
                if (distance <= 0)
                {
                    x = _ball.X;
                    y = _ball.Y;
                    return;
                }

                int standOff = _tactics[side].PressStandOffU;
                int reach = distance < standOff ? distance : standOff;
                x = _ball.X + (int)((long)dx * reach / distance);
                y = _ball.Y + (int)((long)dy * reach / distance);
            }

            private void MarkSpot(int side, int k, out int x, out int y)
            {
                int opponent = 1 - side;
                int ok = opponent * _n + _mark[k];
                bool home = side == 0;
                int goalX = U.Units(MovementGeometry.OwnGoalX(home));

                // Goal-side of his man.
                int dx = goalX - _px[ok], dy = U.CenterYU - _py[ok];
                U.Scaled(dx, dy, U.Units(_cfg.MarkDistanceDm), out int gx, out int gy);
                x = _px[ok] + gx;
                y = _py[ok] + gy;

                // A man who is NOT on the back line does not drop in behind it (engine phase 3).
                // He takes his man goal-side inside his own zone; the space between the back line and
                // the goal belongs to the back four, and a midfielder dropping into it is what put
                // eight metres of daylight through a line that is supposed to be flat.
                int ownLineU = _blockLineU[side];
                if (_lineRank[k] != 0)
                {
                    int spotDepth = home ? x : U.LengthU - x;
                    int floor = home ? ownLineU : U.LengthU - ownLineU;
                    if (spotDepth < floor) x = ownLineU;
                    return;
                }

                // A man on the back line HOLDS THE LINE (engine phase 2). He picks his opponent up
                // across the pitch, but he does not follow him up and down it: four defenders each
                // tracking his own man's depth is exactly how a back four stops being a line and
                // becomes four separate duels — the fourteen metres of "back line spread" the
                // harness was reading. He leaves the line only for a man who has already got behind
                // it, which is the one thing the line exists to deal with.
                int lineU = ownLineU;
                int lineDepth = home ? lineU : U.LengthU - lineU;
                int manDepth = home ? _px[ok] : U.LengthU - _px[ok];
                if (manDepth >= lineDepth - U.Units(_cfg.MarkBehindLineDm)) x = lineU;
            }
        }
    }
}
