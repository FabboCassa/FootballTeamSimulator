using Sim.Core.Config;

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

        /// <summary>How close to goal a free kick gets a wall (the set-piece range).</summary>
        private readonly int _wallRangeDm;

        /// <summary>A ball in the net stays this far inside a post, and one gone wide this far outside it.</summary>
        private const int PostClearanceDm = 2;

        // The restart owed for a ball lying behind a goal line, and when it may be taken.
        private BallActionKind _behindKind;
        private int _behindSide, _behindX, _behindY, _behindTakeAt;

        public Restarts(MatchContext ctx, Offside offside, FreeKickWall wall, int wallRangeDm)
        {
            _ctx = ctx;
            _offside = offside;
            _wall = wall;
            _wallRangeDm = wallRangeDm;
        }

        /// <summary>
        /// The ball ends up IN THE NET and stays there while it is celebrated: a goal frame
        /// showing the ball already back on the centre spot is a goal the viewer never sees.
        /// The kickoff is set up when the celebration is over.
        ///
        /// It runs on from where it crossed into the back of the net, and at least a metre behind
        /// the line (believable-match R5): laid on the line itself, as phase 8 did, a goal and a
        /// shot cleared off the line looked the same. The ball is dead from this tick to the
        /// kickoff and nobody may play it, so the only thing this moves is the picture.
        /// </summary>
        public void ScoreGoal(int tick, int attacking, int defending)
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

            MatchBalance cfg = _ctx.Cfg;
            int inside = U.Units(MovementGeometry.GoalHalfWidthDm - PostClearanceDm);
            _ctx.Ball.RunBehind(goalX, U.Units(cfg.GoalRestDepthDm), U.Units(cfg.GoalNetDepthDm),
                U.CenterYU - inside, U.CenterYU + inside, U.PerTick(cfg.BehindLineCreepDmPerSecond, cfg));
            _ctx.Ball.LastTouchSide = attacking;
            _ctx.Receiver[0] = -1;
            _ctx.Receiver[1] = -1;

            _ctx.DeadKind = BallActionKind.Goal;      // celebrating; the kickoff follows
            _ctx.DeadSide = defending;
            _ctx.DeadTaker = -1;
            _ctx.DeadAt = tick + _ctx.Cfg.GoalCelebrationTicks;
        }

        /// <summary>
        /// A strike wide of the goal, or a save turned round the post, has just crossed the goal
        /// line at <paramref name="lineX"/>. The ball runs on behind it and lies there BehindLineMs
        /// before the restart is given (believable-match R5): given on the tick it crossed, the
        /// ball was already on the goal-kick spot in the frame that should show it going wide. While
        /// it lies there the dead ball reads as a Miss with nobody to take it. It stays on the side
        /// of the goal it crossed at <paramref name="crossY"/>: one wide of a post running across
        /// went on through the side netting and lay behind the line inside the posts, a ball in
        /// the net.
        /// </summary>
        public void RestartFromBehind(int tick, BallActionKind kind, int side, int x, int y, int lineX, int crossY)
        {
            MatchBalance cfg = _ctx.Cfg;
            _ctx.ShotLive = false;
            _offside.ClearFlags();
            int outside = U.Units(MovementGeometry.GoalHalfWidthDm + PostClearanceDm);
            bool low = crossY < U.CenterYU;
            _ctx.Ball.RunBehind(lineX, U.Units(cfg.WideRestDepthDm), U.Units(cfg.RunOffDepthDm),
                low ? 0 : U.CenterYU + outside, low ? U.CenterYU - outside : U.WidthU,
                U.PerTick(cfg.BehindLineCreepDmPerSecond, cfg));
            _ctx.Receiver[0] = -1;
            _ctx.Receiver[1] = -1;

            _behindKind = kind;
            _behindSide = side;
            _behindX = x;
            _behindY = y;
            _behindTakeAt = tick + cfg.DeadBallTicks;

            // Actions are filed on the first frame at or after their tick, so the pause is padded
            // by a frame less a tick: otherwise an odd tick shows the ball out a frame too briefly.
            int stride = cfg.StreamTicksPerFrame < 1 ? 1 : cfg.StreamTicksPerFrame;
            _ctx.DeadKind = BallActionKind.Miss;
            _ctx.DeadSide = side;
            _ctx.DeadTaker = -1;
            _ctx.DeadAt = tick + cfg.TicksOfMs(cfg.BehindLineMs) + stride - 1;
        }

        /// <summary>The ball has lain behind the line long enough: the restart it is owed, taken when it would have been.</summary>
        public void RestartAfterBehind(int tick)
        {
            if (!_ctx.Ball.Dead || _ctx.DeadKind != BallActionKind.Miss || tick < _ctx.DeadAt) return;

            Restart(tick, _behindKind, _behindSide, _behindX, _behindY);
            int arrival = tick + _ctx.Ball.TransportTicks;
            _ctx.DeadAt = _behindTakeAt > arrival ? _behindTakeAt : arrival;
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
            // Never placed in the picture (R5): rolled there, and taken once it has arrived.
            _ctx.Ball.SendTo(
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
            _ctx.DeadAt = tick + DeadBallOrTransportTicks(_ctx);

            // And the wall, decided here and once (Law 13): the three men nearest the ball at the
            // whistle, when the kick is inside range of the goal they are defending.
            int defending = 1 - side;
            int defendedGoalX = U.Units(MovementGeometry.OwnGoalX(defending == 0));
            bool walled = kind == BallActionKind.FreeKick
                && U.StreamDistanceDm(_ctx.Ball.X, _ctx.Ball.Y, defendedGoalX, U.CenterYU) < _wallRangeDm;
            _wall.FormWall(walled ? defending : -1);

            _ctx.Sheet.Record(tick, kind, side == 0, _ctx.DeadTaker, -1);
        }

        /// <summary>The referee's pause, or longer while the ball is still rolling to its spot.</summary>
        public static int DeadBallOrTransportTicks(MatchContext ctx)
        {
            int transport = ctx.Ball.TransportTicks;
            return transport > ctx.Cfg.DeadBallTicks ? transport : ctx.Cfg.DeadBallTicks;
        }
    }
}
