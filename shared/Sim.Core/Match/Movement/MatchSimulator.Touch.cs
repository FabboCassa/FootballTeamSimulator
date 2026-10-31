namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private int _firstTouchLooseU, _dribbleOffLineU, _dribbleContestU, _foulReachU, _dribbleSpaceU, _dribbleLeadMinU, _dribbleLeadFreeU, _dribbleLeadPressedU, _dribbleMinSpeedU, _dribbleLeadShootingU;

        private int[] _aimTick = System.Array.Empty<int>(), _aimX = System.Array.Empty<int>(), _aimY = System.Array.Empty<int>();

        private void InitTouch()
        {
            int total = SideCount * _n;
            _aimTick = new int[total];
            _aimX = new int[total];
            _aimY = new int[total];
            for (int k = 0; k < total; k++) _aimTick[k] = -1;
            _firstTouchLooseU = U.Units(_cfg.FirstTouchLooseDm);
            _dribbleLeadMinU = U.Units(_cfg.DribbleLeadMinDm);
            _dribbleOffLineU = U.Units(_cfg.DribbleOffLineDm);
            _dribbleContestU = U.Units(_cfg.DribbleContestDm);
            _foulReachU = U.Units(_cfg.ChallengeFoulReachDm);
            _dribbleSpaceU = U.Units(_cfg.DribbleSpaceDm);
            _dribbleLeadFreeU = U.Units(_cfg.DribbleLeadFreeDm);
            _dribbleLeadPressedU = U.Units(_cfg.DribbleLeadPressedDm);
            _dribbleMinSpeedU = U.PerTick(_cfg.DribbleMinSpeedDmPerSecond, _cfg);
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
        private bool RunWithBall(int tick, int owner)
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

            // Standing, he stops it at his feet. Turning off his run (where he means to go is more than
            // DribbleTurnPermille off it) he keeps it where it is, moving with him, round the turn;
            // running, he plays it the way he runs, out ahead of him.
            int side = _ball.OwnerSide, slot = _ball.OwnerSlot;
            int dx = _vx[owner], dy = _vy[owner];
            int pace = U.Length(dx, dy), span = pace;
            if (pace < _dribbleMinSpeedU)
            {
                if (_ball.Vx == 0 && _ball.Vy == 0) return true;
                _ball.Vx = 0;
                _ball.Vy = 0;
                _ball.Heard(BallEventKind.Touch, side, slot);
                return true;
            }

            if (Turning(tick, owner, pace))
            {
                if (_ball.Vx == _vx[owner] && _ball.Vy == _vy[owner]) return true;
                _ball.Vx = _vx[owner];
                _ball.Vy = _vy[owner];
                _ball.Heard(BallEventKind.Touch, side, slot);
                return true;
            }

            // Out to his lead running forward with nobody near him (so never to an opponent's feet);
            // within his reach with a man on him, running across or back, or with a shot on; never
            // over a line.
            int gx = _ball.X - _px[owner], gy = _ball.Y - _py[owner];
            int ahead = (int)(((long)gx * dx + (long)gy * dy) / span);
            if (ahead < 0) ahead = 0;
            int forward = MovementGeometry.Direction(side == 0) * dx;
            int lead = !InSpace(owner, side) || 2 * forward < span ? _dribbleLeadPressedU : _dribbleLeadFreeU;
            int goalX = U.Units(MovementGeometry.AttackedGoalX(side == 0));
            if (lead > _dribbleLeadShootingU && U.DistanceSq(_px[owner], _py[owner], goalX, U.CenterYU) <= (long)_shootRangeU * _shootRangeU)
                lead = _dribbleLeadShootingU;
            int room = RoomInside(dx, dy, span) - _dribbleLeadMinU;
            if (lead > room) lead = room;

            // He touches it again as it comes back within DribbleLeadMinDm of him, once it is off the
            // line he runs (he has turned), and, keeping it within his reach, before it runs out of it.
            int nextX = gx + _ball.Vx - _vx[owner], nextY = gy + _ball.Vy - _vy[owner];
            long aheadNext = ((long)nextX * dx + (long)nextY * dy) / span;
            long across = ((long)nextX * dy - (long)nextY * dx) / span;
            bool leaving = (long)nextX * nextX + (long)nextY * nextY > (long)_controlU * _controlU;
            bool offLine = across > _dribbleOffLineU || across < -_dribbleOffLineU;
            bool coming = (long)(_ball.Vx - _vx[owner]) * dx + (long)(_ball.Vy - _vy[owner]) * dy <= 0;
            if ((aheadNext >= _dribbleLeadMinU || !coming) && !offLine && !(leaving && lead <= _controlU)) return true;

            // Played out of his reach, it has to stay in play until he is back on it.
            int speed = _ball.DribbleTouchSpeed(ahead, pace, lead);
            while (lead > _controlU && _ball.TravelUntilBackInReach(speed, ahead, pace, _controlU) > room)
            {
                lead -= U.Units(2);
                if (lead <= _controlU) lead = _dribbleLeadPressedU;
                speed = _ball.DribbleTouchSpeed(ahead, pace, lead);
            }

            // A set-up touch: he chooses only with the ball at his feet (V11Brain.Act), so with his
            // choice due before he would catch a long touch up, he keeps this one within reach.
            if (lead > _controlU && _ball.TicksUntilBackInReach(speed, ahead, pace, _controlU) > _hold[owner])
            {
                lead = _dribbleLeadPressedU;
                speed = _ball.DribbleTouchSpeed(ahead, pace, lead);
            }

            // Along his run, and back onto its line by half of what it is off it.
            U.Scaled(dx, dy, speed, out _ball.Vx, out _ball.Vy);
            long side0 = ((long)gx * dy - (long)gy * dx) / span;
            if (side0 > _dribbleOffLineU || side0 < -_dribbleOffLineU)
            {
                _ball.Vx -= (int)(side0 * dy / span / 2);
                _ball.Vy += (int)(side0 * dx / span / 2);
            }

            if (!_runCalled[owner] && TakesRestartRunning(tick, owner))
            {
                _runCalled[owner] = true;
                _sheet.Record(tick, BallActionKind.Dribble, side == 0, slot, -1);
            }

            _ball.Heard(BallEventKind.Touch, side, slot);
            return true;
        }

        /// <summary>
        /// He has just put the ball back in play and runs with it: the restart is played with this
        /// touch, and filed as his dribble now, the frame the stream reads the restart off — the
        /// ball runs at a dribble's pace from here, not at a transport's.
        /// </summary>
        private bool TakesRestartRunning(int tick, int k) => k == _ctx.RestartTaker && tick < _ctx.RestartGrace;

        /// <summary>Where the man on the ball means to run with it this tick (see <see cref="RunWithBall"/>).</summary>
        private void AimCarry(int tick, int k, int tx, int ty)
        {
            _aimTick[k] = tick;
            _aimX[k] = tx;
            _aimY[k] = ty;
        }

        /// <summary>Where he means to go this tick is further off the way he runs than DribbleTurnPermille (a cosine).</summary>
        private bool Turning(int tick, int k, int pace)
        {
            if (_aimTick[k] != tick) return false;
            int ax = _aimX[k] - _px[k], ay = _aimY[k] - _py[k];
            int aim = U.Length(ax, ay);
            if (aim <= 0) return false;
            return ((long)ax * _vx[k] + (long)ay * _vy[k]) * 1000 < (long)_cfg.DribbleTurnPermille * aim * pace;
        }

        /// <summary>No opponent within DribbleSpaceDm of him: room to play it out of his reach.</summary>
        private bool InSpace(int owner, int side)
        {
            long space = (long)_dribbleSpaceU * _dribbleSpaceU;
            int opponent = 1 - side;
            for (int j = 0; j < _n; j++)
            {
                int ok = opponent * _n + j;
                if (!_sentOff[ok] && U.DistanceSq(_px[ok], _py[ok], _px[owner], _py[owner]) < space) return false;
            }

            return true;
        }

        /// <summary>
        /// How far the ball can run from where it is, along (<paramref name="dx"/>, <paramref name="dy"/>),
        /// before it reaches a line: his lead stops short of it, the way a man running with the ball
        /// keeps it in play.
        /// </summary>
        private int RoomInside(int dx, int dy, int span)
        {
            // To the line itself (see RunsOntoALine), not to the inset a carrier's feet are held to: a
            // ball at the inside foot of a man on the inset is already beyond it, and measured from
            // there the slightest drift toward the line left it no room at all, so he stopped it dead
            // and ran on, and ran back, for minutes.
            int room = int.MaxValue;
            if (dx > 0) room = Shorter(room, U.LengthU - U.Scale - _ball.X, dx, span);
            if (dx < 0) room = Shorter(room, _ball.X - U.Scale, -dx, span);
            if (dy > 0) room = Shorter(room, U.WidthU - U.Scale - _ball.Y, dy, span);
            if (dy < 0) room = Shorter(room, _ball.Y - U.Scale, -dy, span);
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
        /// Where a presser closes down: the man on the ball, whose ball runs ahead of him between
        /// touches (free-ball spec R3), or the ball itself when it is loose or in a keeper's hands.
        /// </summary>
        private void ManOnTheBall(out int x, out int y)
        {
            x = _ball.X;
            y = _ball.Y;
            if (_ball.Free) return;
            int owner = _ball.OwnerSide * _n + _ball.OwnerSlot;
            if (_keeper[owner]) return;
            x = _px[owner];
            y = _py[owner];
        }

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
