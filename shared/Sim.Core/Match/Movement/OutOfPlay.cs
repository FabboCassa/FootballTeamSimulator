namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The referee on the lines (Laws 9, 10, 15, 16 and 17): a ball over a touchline is a throw-in,
    /// over a goal line it is a goal, a corner or a goal kick — whether it ran there loose or
    /// was carried there at a man's feet.
    /// </summary>
    internal sealed class OutOfPlay
    {
        private readonly MatchContext _ctx;
        private readonly Restarts _restarts;
        private readonly int[] _stepFromX;
        private readonly int[] _stepFromY;
        private readonly int[] _stepToX;
        private readonly int[] _stepToY;

        public OutOfPlay(
            MatchContext ctx, Restarts restarts,
            int[] stepFromX, int[] stepFromY, int[] stepToX, int[] stepToY)
        {
            _ctx = ctx;
            _restarts = restarts;
            _stepFromX = stepFromX;
            _stepFromY = stepFromY;
            _stepToX = stepToX;
            _stepToY = stepToY;
        }

        public void ResolveOutOfPlay(int tick)
        {
            if (_ctx.Ball.Dead) return;

            // A ball at a player's FEET is out the moment it crosses a line, exactly like a ball
            // running free (Law 9). Until engine phase 5 this method gave up here, so a carrier
            // who ran over the touchline was quietly clamped back inside with the ball still his
            // — the §1.7 defect, measured at fifty ticks a match by the harness's own contract
            // check, and the reason there were eighteen throw-ins a match instead of forty.
            if (!_ctx.Ball.Free)
            {
                CarrierOutOfPlay(tick);
                return;
            }

            int half = U.Units(MovementGeometry.GoalHalfWidthDm);

            // Nothing to resolve while the ball is still on the pitch. This test used to be
            // missing under the goal rule below, so every scripted goal was awarded on the tick
            // it was struck and the ball was snapped to the line from wherever it had been —
            // the strike had no flight at all, and from thirty metres out it read as a teleport.
            bool over = _ctx.Ball.X < 0 || _ctx.Ball.X > U.LengthU || _ctx.Ball.Y < 0 || _ctx.Ball.Y > U.WidthU;
            if (!over) return;

            // Over a touchline: a throw-in from the spot it actually crossed.
            if (_ctx.Ball.Y < 0 || _ctx.Ball.Y > U.WidthU)
            {
                // A live strike that runs out over the SIDE still has to become the thing the
                // timeline says it was. Restart() clears the strike silently, so before phase 1
                // caught it a shot that drifted wide lost its save or its miss altogether — the
                // chance was on the scoresheet and nowhere in the picture. It costs nothing when
                // no strike is live, and it cannot swallow a goal: a goal crosses a GOAL line.
                SettleStrayStrike(tick);

                int line = _ctx.Ball.Y < 0 ? 0 : U.WidthU;
                _ctx.Ball.CrossingOfY(line, out int outX);
                _restarts.Restart(tick, BallActionKind.ThrowIn, 1 - _ctx.Ball.LastTouchSide, U.ClampX(outX), line);
                return;
            }

            if (_ctx.Ball.X >= 0 && _ctx.Ball.X <= U.LengthU) return;

            bool overHomeGoal = _ctx.Ball.X < 0;                 // the goal the HOME side defends
            int defending = overHomeGoal ? 0 : 1;
            int attacking = 1 - defending;
            int goalX = overHomeGoal ? 0 : U.LengthU;

            // Judged ON the goal line, not wherever the ball had got to by the end of the tick.
            _ctx.Ball.CrossingOfX(goalX, out int crossY);
            bool betweenPosts = crossY > U.CenterYU - half && crossY < U.CenterYU + half;

            // Did a DEFENDER put it behind? That is the question a corner turns on (Law 17), and
            // until engine phase 5 it was asked only of the last man to kick the ball — so a
            // keeper who turned a shot round his own post was never the last toucher, the striker
            // was, and every save that left the pitch was given as a goal kick. Which is why the
            // engine produced 0.3 corners and 48 goal kicks a match against football's 8-13 and
            // 8-16: the save IS the defender's touch.
            bool defenderPutItBehind = _ctx.Ball.LastTouchSide == defending;

            // THE GOAL (engine phase 6). Not "the timeline said so" and not "the keeper kept it
            // out because the scoresheet has no goal here": the ball crossed the line between the
            // posts, and that is the entire test. It counts off a strike, off a deflection and
            // off a defender putting it into his own net, because the laws do not care either.
            if (betweenPosts)
            {
                _restarts.ScoreGoal(tick, attacking, defending, crossY);
                return;
            }

            // Wide, and it was a strike: his miss.
            if (_ctx.ShotLive)
            {
                _ctx.ShotLive = false;
                _ctx.Sheet.RecordMiss(tick, attacking, _ctx.ShotSlot);
            }

            if (defenderPutItBehind)
                _restarts.Restart(tick, BallActionKind.Corner, attacking, goalX, crossY < U.CenterYU ? 0 : U.WidthU);
            else
                _restarts.Restart(tick, BallActionKind.GoalKick, defending,
                    overHomeGoal ? U.Units(55) : U.LengthU - U.Units(55), U.CenterYU);
        }

        /// <summary>
        /// The man on the ball has run over a line. Which line he crossed and WHERE is read off
        /// his unclamped step (see <see cref="_stepToX"/>), interpolated to the crossing point
        /// the same way <see cref="MatchBall.CrossingOfX"/> does it for a loose ball — so a
        /// throw-in is taken from the spot the ball actually left the pitch rather than from
        /// wherever the man had got to by the end of the tick.
        ///
        /// He is the last man to touch it by definition, so the restart always goes the other
        /// way: a throw-in, a corner when he has carried it over his OWN line, a goal kick when
        /// he has run it over the one he is attacking.
        /// </summary>
        private void CarrierOutOfPlay(int tick)
        {
            int side = _ctx.Ball.OwnerSide;
            int k = side * _ctx.N + _ctx.Ball.OwnerSlot;

            // The man who has just put the ball back in play gets a moment's grace, and only he
            // does: a throw-in is taken from beside the touchline, so on the tick he collects it
            // a step of his that ends outside is him reaching for the ball rather than him
            // carrying it out. Without this the two sides trade throw-ins from the same spot.
            if (k == _ctx.RestartTaker && tick < _ctx.RestartGrace) return;

            int fromX = _stepFromX[k], fromY = _stepFromY[k];
            int toX = _stepToX[k], toY = _stepToY[k];
            if (toX >= 0 && toX <= U.LengthU && toY >= 0 && toY <= U.WidthU) return;

            // Which line he reached FIRST, in the fraction of the step at which he reached it.
            // A man running into a corner crosses both, and the laws care which came first.
            int firstAt = 1001;
            bool overTouchline = false;
            int line = 0;

            if (toY < 0) Earliest(fromY, toY, 0, ref firstAt, ref overTouchline, ref line, true, 0);
            else if (toY > U.WidthU) Earliest(fromY, toY, U.WidthU, ref firstAt, ref overTouchline, ref line, true, U.WidthU);

            if (toX < 0) Earliest(fromX, toX, 0, ref firstAt, ref overTouchline, ref line, false, 0);
            else if (toX > U.LengthU) Earliest(fromX, toX, U.LengthU, ref firstAt, ref overTouchline, ref line, false, U.LengthU);

            if (firstAt > 1000) return;

            int crossX = fromX + (int)((long)(toX - fromX) * firstAt / 1000);
            int crossY = fromY + (int)((long)(toY - fromY) * firstAt / 1000);

            if (overTouchline)
            {
                _restarts.Restart(tick, BallActionKind.ThrowIn, 1 - side, U.ClampX(crossX), line);
                return;
            }

            // A goal line. Whose it is decides everything: his own is a corner against him, the
            // one he is attacking is a goal kick. A ball CARRIED between the posts is not a goal
            // here — the score belongs to the 1.4 result model until phase 6 — and a carrier who
            // gets that far is aiming at the goal from eleven metres out, not at the line behind
            // it (phase 4's CarryTarget), so it is a corner or a goal kick like any other.
            int defending = line == 0 ? 0 : 1;
            if (side == defending)
                _restarts.Restart(tick, BallActionKind.Corner, 1 - side, line, U.ClampY(crossY) < U.CenterYU ? 0 : U.WidthU);
            else
                _restarts.GoalKick(tick, defending);
        }

        /// <summary>
        /// Keeps the earliest of the boundaries a step crossed. <paramref name="at"/> is in
        /// thousandths of the step, which is exact integer arithmetic on the two endpoints.
        /// </summary>
        private static void Earliest(
            int from, int to, int boundary,
            ref int firstAt, ref bool touchline, ref int line, bool isTouchline, int lineValue)
        {
            int span = to - from;
            if (span == 0) return;

            int at = (int)((long)(boundary - from) * 1000 / span);
            if (at < 0) at = 0;
            if (at > 1000) at = 1000;
            if (at >= firstAt) return;

            firstAt = at;
            touchline = isTouchline;
            line = lineValue;
        }

        /// <summary>
        /// A strike that has left the pitch anywhere but through a goal: it is called as the
        /// save or the miss the timeline made it, and the restart follows.
        /// </summary>
        /// <summary>A strike that ran out over a TOUCHLINE: off target, and the throw-in follows.</summary>
        private void SettleStrayStrike(int tick)
        {
            if (!_ctx.ShotLive) return;

            _ctx.ShotLive = false;
            _ctx.Sheet.RecordMiss(tick, _ctx.ShotHome ? 0 : 1, _ctx.ShotSlot);
        }
    }
}
