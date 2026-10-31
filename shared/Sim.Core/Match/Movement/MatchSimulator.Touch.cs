namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private int _firstTouchLooseU, _dribbleLeadMinU, _dribbleLeadFreeU, _dribbleLeadPressedU, _dribbleMinSpeedU, _dribbleClearU, _dribbleLeadShootingU;

        private void InitTouch()
        {
            _firstTouchLooseU = U.Units(_cfg.FirstTouchLooseDm);
            _dribbleLeadMinU = U.Units(_cfg.DribbleLeadMinDm);
            _dribbleLeadFreeU = U.Units(_cfg.DribbleLeadFreeDm);
            _dribbleLeadPressedU = U.Units(_cfg.DribbleLeadPressedDm);
            _dribbleMinSpeedU = U.PerTick(_cfg.DribbleMinSpeedDmPerSecond, _cfg);
            _dribbleClearU = U.Units(_cfg.DribbleClearOfOpponentDm);
            _dribbleLeadShootingU = U.Units(_cfg.DribbleLeadShootingDm);
        }

        /// <summary>His feet within reach of the ball: only then can he strike it.</summary>
        private bool BallAtFeet(int k) =>
            U.DistanceSq(_px[k], _py[k], _ball.X, _ball.Y) <= (long)_controlU * _controlU;

        /// <summary>
        /// The first touch (free-ball spec R3): the speed, in units per tick, the ball keeps off his
        /// foot — a share of its incoming speed that Technique shrinks, times a triangular draw.
        /// </summary>
        private int FirstTouchResidual(int k, int inSpeed)
        {
            int error = BallSkill.Lerp(_skTechnique[k], 100,
                _cfg.FirstTouchErrorPermilleAtZero, _cfg.FirstTouchErrorPermilleAtHundred);
            int draw = _rng.NextInt(0, 1001) + _rng.NextInt(0, 1001);
            return (int)((long)inSpeed * error * draw / 1_000_000);
        }

        /// <summary>
        /// A poor first touch: the ball runs on off his foot along its incoming line, scattered, and
        /// is anybody's. He may not touch it again for the release lock.
        /// </summary>
        private void LooseTouch(int tick, int side, int slot, int inVx, int inVy, int residual)
        {
            int spread = _cfg.FirstTouchSpreadPermille;
            int across = _rng.NextInt(-spread, spread + 1);
            int dx = inVx - (int)((long)inVy * across / 1000);
            int dy = inVy + (int)((long)inVx * across / 1000);
            _ball.Kick(side, slot, dx, dy, residual);
            Release(tick, side, slot);
            _lastTouch[side] = slot;
            _receiver[0] = -1;
            _receiver[1] = -1;
        }

        /// <summary>
        /// A ball played ahead and out of the carrier's reach is anybody's (free-ball spec R3): the
        /// opponent at it takes it with a first touch, without a duel with a man who is not there,
        /// and so without a foul on him.
        /// </summary>
        private void NipIn(int tick, int side, int slot)
        {
            int inVx = _ball.Vx, inVy = _ball.Vy;
            _receiver[0] = -1;
            _receiver[1] = -1;
            _tackleLock = tick + _cfg.TackleLockTicks;
            _offside.ClearFlags();
            _sheet.Record(tick, BallActionKind.Recovery, side == 0, slot, -1);

            int residual = FirstTouchResidual(side * _n + slot, U.Length(inVx, inVy));
            if (_ball.RollOut(residual) > _firstTouchLooseU)
            {
                LooseTouch(tick, side, slot, inVx, inVy, residual);
                return;
            }

            Collect(side, slot);
            if (residual > 0) U.Scaled(inVx, inVy, residual, out _ball.Vx, out _ball.Vy);
            _lastTouch[side] = slot;
            _hold[side * _n + slot] = HoldTicks(side);
        }

        /// <summary>
        /// The ball he controls, each tick (free-ball spec R3). A keeper holds it in his hands. Anybody
        /// else's rolls by its own physics, and he touches it on, the way he runs, weighted to run out
        /// to his lead ahead of him: never glued to his feet. Out of his reach he goes after it
        /// (<see cref="ChaseOwnBall"/>). False when it has run out of his reach onto a line and is his
        /// no longer.
        /// </summary>
        private bool RunWithBall(int owner)
        {
            if (_keeper[owner])
            {
                BallToFeet(owner);
                _ball.Vx = 0;
                _ball.Vy = 0;
                return true;
            }

            _ball.Roll();
            if (RunsOntoALine())
            {
                // Out of his reach it goes out; at his feet he stops it, or it is a restart's ball where
                // the referee put it, played from just inside.
                bool moving = _ball.Vx != 0 || _ball.Vy != 0;
                int x = MovementGeometry.Clamp(_ball.X, _ctx.InsetU, U.LengthU - _ctx.InsetU), y = _ctx.Inside(_ball.Y);
                if (moving && U.DistanceSq(_px[owner], _py[owner], x, y) > (long)_controlU * _controlU)
                {
                    _ball.LetGo();
                    return false;
                }

                _ball.X = x;
                _ball.Y = y;
                _ball.Vx = 0;
                _ball.Vy = 0;
                if (moving) _ball.Heard(BallEventKind.Touch, _ball.OwnerSide, _ball.OwnerSlot);
            }

            if (!BallAtFeet(owner)) return true;

            // Standing, he stops it at his feet.
            int side = _ball.OwnerSide, slot = _ball.OwnerSlot;
            int pace = U.Length(_vx[owner], _vy[owner]);
            if (pace < _dribbleMinSpeedU)
            {
                if (_ball.Vx == 0 && _ball.Vy == 0) return true;
                _ball.Vx = 0;
                _ball.Vy = 0;
                _ball.Heard(BallEventKind.Touch, side, slot);
                return true;
            }

            // Running, he plays it the way he runs: out to his lead, shorter with a man on him or a
            // shot on, never over a line nor to an opponent's feet.
            int dx = _vx[owner], dy = _vy[owner], span = pace;
            int gx = _ball.X - _px[owner], gy = _ball.Y - _py[owner];
            int ahead = (int)(((long)gx * dx + (long)gy * dy) / span);
            if (ahead < 0) ahead = 0;
            int lead = BallSkill.Lerp(PressurePermille(side, slot), 1000, _dribbleLeadFreeU, _dribbleLeadPressedU);
            int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
            if (lead > _dribbleLeadShootingU && U.DistanceSq(_px[owner], _py[owner], goalX, U.CenterYU) <= (long)_shootRangeU * _shootRangeU)
                lead = _dribbleLeadShootingU;
            int room = RoomInside(dx, dy, span) - _dribbleLeadMinU;
            if (lead > room) lead = room;
            lead = LeadClearOfOpponents(owner, side, dx, dy, span, ahead, lead);

            // He touches it again before it comes within DribbleLeadMinDm of him — and, keeping it
            // within his reach, before it runs out of it.
            int nextX = gx + _ball.Vx - _vx[owner], nextY = gy + _ball.Vy - _vy[owner];
            long aheadNext = ((long)nextX * dx + (long)nextY * dy) / span;
            bool leaving = lead <= _controlU && (long)nextX * nextX + (long)nextY * nextY > (long)_controlU * _controlU;
            if (aheadNext >= _dribbleLeadMinU && !leaving) return true;

            int speed = _ball.DribbleTouchSpeed(ahead, pace, lead);
            U.Scaled(dx, dy, speed, out _ball.Vx, out _ball.Vy);
            _ball.Heard(BallEventKind.Touch, side, slot);
            return true;
        }

        /// <summary>
        /// The longest lead, from <paramref name="lead"/> down in 2 dm steps, that puts the ball no
        /// nearer than DribbleClearOfOpponentDm to an opponent; none does, and he keeps it at his pace.
        /// </summary>
        private int LeadClearOfOpponents(int owner, int side, int dx, int dy, int span, int ahead, int lead)
        {
            long clear = (long)_dribbleClearU * _dribbleClearU;
            int opponent = 1 - side;
            for (; lead > ahead; lead -= U.Units(2))
            {
                int bx = _px[owner] + (int)((long)dx * lead / span);
                int by = _py[owner] + (int)((long)dy * lead / span);
                bool open = true;
                for (int j = 0; j < _n && open; j++)
                {
                    int ok = opponent * _n + j;
                    if (!_sentOff[ok] && U.DistanceSq(_px[ok], _py[ok], bx, by) < clear) open = false;
                }

                if (open) return lead;
            }

            return ahead;
        }

        /// <summary>
        /// How far the ball can run from where it is, along (<paramref name="dx"/>, <paramref name="dy"/>),
        /// before it reaches a line: his lead stops short of it, the way a man running with the ball
        /// keeps it in play.
        /// </summary>
        private int RoomInside(int dx, int dy, int span)
        {
            int room = int.MaxValue;
            if (dx > 0) room = Shorter(room, U.LengthU - _ctx.InsetU - _ball.X, dx, span);
            if (dx < 0) room = Shorter(room, _ball.X - _ctx.InsetU, -dx, span);
            if (dy > 0) room = Shorter(room, U.WidthU - _ctx.InsetU - _ball.Y, dy, span);
            if (dy < 0) room = Shorter(room, _ball.Y - _ctx.InsetU, -dy, span);
            return room;
        }

        private static int Shorter(int room, int toLine, int component, int span)
        {
            long along = toLine <= 0 ? 0 : (long)toLine * span / component;
            return along < room ? (int)along : room;
        }

        /// <summary>
        /// The ball he controls has run onto a line (within a decimetre of it): see <see cref="RunWithBall"/>.
        /// </summary>
        private bool RunsOntoALine() =>
            _ball.X < U.Scale || _ball.X > U.LengthU - U.Scale || _ball.Y < U.Scale || _ball.Y > U.WidthU - U.Scale;

        /// <summary>
        /// His own ball out of his reach: he runs through it, aimed far enough beyond it that the
        /// approach does not slow him down.
        /// </summary>
        private void ChaseOwnBall(int k, ref int tx, ref int ty)
        {
            if (_keeper[k] || BallAtFeet(k)) return;
            int dx = _ball.X - _px[k], dy = _ball.Y - _py[k];
            int gap = U.Length(dx, dy);
            if (gap <= 0) return;
            tx = U.ClampX(_px[k] + (int)((long)dx * U.Units(160) / gap));
            ty = _ctx.Inside(_py[k] + (int)((long)dy * U.Units(160) / gap));
        }
    }
}
