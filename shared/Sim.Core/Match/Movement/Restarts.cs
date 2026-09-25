namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The whistle that stops play and sets the ball down again: a goal and its celebration,
    /// a goal kick, and every other restart with its taker and, for a free kick in range, its
    /// wall.
    /// </summary>
    internal sealed class Restarts
    {
        private readonly MatchContext _ctx;
        private readonly Offside _offside;
        private readonly FreeKickWall _wall;

        /// <summary>How close to goal a free kick gets a wall: v10's shooting range, v11's set-piece range.</summary>
        private readonly int _wallRangeU;

        public Restarts(MatchContext ctx, Offside offside, FreeKickWall wall, int wallRangeDm)
        {
            _ctx = ctx;
            _offside = offside;
            _wall = wall;
            _wallRangeU = U.Units(wallRangeDm);
        }

        /// <summary>
        /// The ball ends up IN THE NET and stays there while it is celebrated: a goal frame
        /// showing the ball already back on the centre spot is a goal the viewer never sees.
        /// The kickoff is set up when the celebration is over.
        /// </summary>
        /// <summary>
        /// <paramref name="crossY"/> is where the ball crossed the line, and the ball is laid to
        /// rest THERE for the celebration rather than in the middle of the goal (engine phase 8).
        /// Phase 6 wrote this down as cosmetic and true: every goal in its dump went in dead
        /// centre, which the eye sees at once. The ball is dead from this tick to the kickoff and
        /// nobody may play it, so the only thing this moves is the picture — and the golden
        /// master, since the resting frames are in the stream the hash covers.
        /// </summary>
        public void ScoreGoal(int tick, int attacking, int defending, int crossY)
        {
            int goalX = U.Units(MovementGeometry.AttackedGoalX(attacking == 0));

            // WHO IT IS CREDITED TO. The man who struck it when a strike was live; otherwise the
            // last man of the scoring side to touch it, which is how a deflected goal and a
            // scramble find a name. A goal that only a defender touched — an own goal — falls
            // back to the side's furthest man forward rather than being credited to the opponent.
            int scorer = _ctx.ShotLive && (_ctx.ShotHome ? 0 : 1) == attacking ? _ctx.ShotSlot : _ctx.LastTouch[attacking];
            if (scorer < 0 || _ctx.SentOff[attacking * _ctx.N + scorer]) scorer = _ctx.BestStriker(attacking);

            _ctx.ShotLive = false;
            _ctx.ShotSlot = scorer;
            _ctx.Sheet.RecordGoal(tick, attacking, scorer);

            _ctx.Ball.Place(goalX, U.ClampY(crossY));
            _ctx.Ball.Dead = true;
            _ctx.Ball.OwnerSide = -1;
            _ctx.Ball.OwnerSlot = -1;
            _ctx.Ball.LastTouchSide = attacking;
            _ctx.Receiver[0] = -1;
            _ctx.Receiver[1] = -1;

            _ctx.DeadKind = BallActionKind.Goal;      // celebrating; the kickoff follows
            _ctx.DeadSide = defending;
            _ctx.DeadTaker = -1;
            _ctx.DeadAt = tick + _ctx.Cfg.GoalCelebrationTicks;
        }

        public void GoalKick(int tick, int defending)
        {
            int spot = defending == 0 ? U.Units(55) : U.LengthU - U.Units(55);
            Restart(tick, BallActionKind.GoalKick, defending, spot, U.CenterYU);
        }

        public void Restart(int tick, BallActionKind kind, int side, int x, int y)
        {
            _ctx.ShotLive = false;
            _offside.ClearFlags();

            // Put back in play from a spot that is ON the pitch. The laws have a throw-in taken
            // from the touchline and a corner from the corner arc — with the taker standing OFF
            // the field of play, which a top-down picture of twenty-two dots has nowhere to put.
            // So the ball goes down a stride inside the line instead: the same spot to the eye,
            // and it keeps "a held ball is never sitting on a line of the pitch" a real invariant
            // instead of a check that fires every time the referee gets a restart right.
            _ctx.Ball.Place(
                MovementGeometry.Clamp(x, _ctx.InsetU, U.LengthU - _ctx.InsetU),
                _ctx.Inside(y));
            _ctx.Ball.Dead = true;
            _ctx.Ball.OwnerSide = -1;
            _ctx.Ball.OwnerSlot = -1;
            _ctx.Receiver[0] = -1;
            _ctx.Receiver[1] = -1;

            _ctx.DeadKind = kind;
            _ctx.DeadSide = side;
            _ctx.DeadTaker = kind == BallActionKind.GoalKick
                ? _ctx.KeeperOf(side)
                : kind == BallActionKind.Penalty
                    ? _ctx.BestStriker(side)
                    : _ctx.NearestTo(side, _ctx.Ball.X, _ctx.Ball.Y, includeKeeper: false);
            _ctx.DeadAt = tick + _ctx.Cfg.DeadBallTicks;

            // And the wall, decided here and once (Law 13): the three men nearest the ball at the
            // whistle, when the kick is inside range of the goal they are defending.
            int defending = 1 - side;
            int defendedGoalX = U.Units(MovementGeometry.OwnGoalX(defending == 0));
            bool walled = kind == BallActionKind.FreeKick
                && U.Distance(_ctx.Ball.X, _ctx.Ball.Y, defendedGoalX, U.CenterYU) < _wallRangeU;
            _wall.FormWall(walled ? defending : -1);

            _ctx.Sheet.Record(tick, kind, side == 0, _ctx.DeadTaker, -1);
        }
    }
}
