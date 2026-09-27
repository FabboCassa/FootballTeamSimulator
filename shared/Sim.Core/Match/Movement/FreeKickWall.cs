namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Ten yards and the wall (Laws 13 and 14): where the defending side stands while a free
    /// kick or a penalty is taken.
    /// </summary>
    internal sealed class FreeKickWall
    {
        private readonly MatchContext _ctx;

        /// <summary>
        /// In the wall, this tick. A wall is made of men standing on each other's shoulders, so the
        /// one thing that must not apply to them is the separation that keeps team-mates apart — at
        /// a six-metre radius it opens the wall before it has finished forming, which is exactly
        /// what the eye caught in the replay dump.
        /// </summary>
        private readonly bool[] _inWall;

        /// <summary>The lineup slots making up the wall at the current free kick, or -1.</summary>
        private readonly int[] _wall = { -1, -1, -1, -1, -1 };

        public FreeKickWall(MatchContext ctx)
        {
            _ctx = ctx;
            _inWall = new bool[ctx.N * 2];
        }

        public bool IsInWall(int k) => _inWall[k];

        /// <summary>
        /// Where a defender must stand while a free kick or a penalty is being taken (Laws 13 and
        /// 14): nine and a bit metres away, and out of the penalty area for a penalty. The men
        /// nearest the ball form the WALL when the kick is within range of their goal — on the
        /// line between the ball and the middle of it, shoulder to shoulder.
        /// </summary>
        public bool RetreatSpot(int side, int slot, out int x, out int y)
        {
            x = 0;
            y = 0;
            int k = side * _ctx.N + slot;
            _inWall[k] = false;
            if (!_ctx.Ball.Dead || _ctx.DeadSide == side) return false;
            if (_ctx.DeadKind != BallActionKind.FreeKick && _ctx.DeadKind != BallActionKind.Penalty) return false;
            if (_ctx.Keeper[k]) return false;

            // A penalty: everybody but the taker and the keeper waits outside the box.
            if (_ctx.DeadKind == BallActionKind.Penalty)
            {
                int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int dir = MovementGeometry.Direction(side == 0);
                int edge = ownGoalX + dir * U.Units(MovementGeometry.BoxDepthDm + 20);
                if (dir * _ctx.Px[k] >= dir * edge) return false;
                x = edge;
                y = _ctx.Py[k];
                return true;
            }

            int retreat = U.Units(_ctx.Cfg.FreeKickRetreatDm);
            int away = U.Distance(_ctx.Px[k], _ctx.Py[k], _ctx.Ball.X, _ctx.Ball.Y);

            // The wall — the three men picked at the whistle, each keeping the place he was given.
            int goalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
            int place = WallPlace(slot);

            if (place >= 0)
            {
                _inWall[k] = true;
                int dx = goalX - _ctx.Ball.X, dy = U.CenterYU - _ctx.Ball.Y;
                int span = U.Length(dx, dy);
                if (span <= 0) span = 1;

                int alongX = _ctx.Ball.X + (int)((long)dx * retreat / span);
                int alongY = _ctx.Ball.Y + (int)((long)dy * retreat / span);

                // Shoulder to shoulder ACROSS the line of the kick, centred on it.
                int step = U.Units(_ctx.Cfg.WallSpacingDm);
                int offset = (place - (_ctx.Cfg.WallMen - 1) / 2) * step;
                x = U.ClampX(alongX + (int)((long)(-dy) * offset / span));
                y = _ctx.Inside(alongY + (int)((long)dx * offset / span));
                return true;
            }

            if (away >= retreat) return false;

            // Anybody else simply retires the required distance, straight back from the ball.
            int ox = _ctx.Px[k] - _ctx.Ball.X, oy = _ctx.Py[k] - _ctx.Ball.Y;
            if (ox == 0 && oy == 0) ox = MovementGeometry.Direction(side == 0);
            U.Scaled(ox, oy, retreat, out int rx, out int ry);
            x = U.ClampX(_ctx.Ball.X + rx);
            y = _ctx.Inside(_ctx.Ball.Y + ry);
            return true;
        }

        /// <summary>
        /// Who makes up the wall, decided ONCE when the free kick is given — the three men nearest
        /// the ball at the whistle, and they keep their places.
        ///
        /// Re-ranking them every tick, which is what this replaced, made the wall chase itself: two
        /// men swap places as they run, so each sets off for the spot the other has just left and
        /// neither arrives. Deciding it at the whistle is also what happens on a pitch — the
        /// referee walks THOSE men back — and it is one O(n) pass a restart instead of one per man
        /// per tick.
        /// </summary>
        public void FormWall(int side)
        {
            for (int w = 0; w < _wall.Length; w++) _wall[w] = -1;
            if (side < 0) return;

            for (int w = 0; w < _ctx.Cfg.WallMen && w < _wall.Length; w++)
            {
                int best = -1;
                long bestDistance = long.MaxValue;
                for (int i = 0; i < _ctx.N; i++)
                {
                    int k = side * _ctx.N + i;
                    if (_ctx.Keeper[k] || _ctx.SentOff[k] || InWall(i)) continue;
                    long d = U.DistanceSq(_ctx.Px[k], _ctx.Py[k], _ctx.Ball.X, _ctx.Ball.Y);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }

                if (best < 0) break;
                _wall[w] = best;
            }
        }

        /// <summary>His place in the wall, or -1 if he is not in it.</summary>
        private int WallPlace(int slot)
        {
            for (int w = 0; w < _wall.Length; w++)
                if (_wall[w] == slot) return w;
            return -1;
        }

        private bool InWall(int slot) => WallPlace(slot) >= 0;
    }
}
