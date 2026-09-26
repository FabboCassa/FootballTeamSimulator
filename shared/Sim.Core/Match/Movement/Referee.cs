namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The referee's whistle for the laws broken in open play (engine phase 5): a foul and its
    /// card (Law 12), and an offside flag answered (Law 11).
    /// </summary>
    internal sealed class Referee
    {
        private const int SideCount = 2;

        private readonly MatchContext _ctx;
        private readonly Restarts _restarts;
        private readonly Offside _offside;
        private readonly int[] _skDefending;
        private readonly bool[] _booked;
        private readonly int[] _tenMen = new int[SideCount];

        public Referee(MatchContext ctx, Restarts restarts, Offside offside, int[] skDefending)
        {
            _ctx = ctx;
            _restarts = restarts;
            _offside = offside;
            _skDefending = skDefending;
            _booked = new bool[skDefending.Length];
        }

        /// <summary>
        /// Was the challenge a foul? If it was, everything that follows from it is done here: the
        /// whistle, the card, and the free kick or the penalty. Returns true when the game has
        /// been stopped, in which case the caller must not go on resolving the ball.
        /// </summary>
        public bool GiveFoulIfCommitted(int tick, int side, int slot, int carrier)
        {
            int k = side * _ctx.N + slot;
            bool inOwnBox = MovementGeometry.InOwnBox(side == 0, U.Dm(_ctx.Ball.X), U.Dm(_ctx.Ball.Y));

            int odds = BallSkill.Lerp(
                BallSkill.Clamp(_skDefending[k], 1, 100), 100,
                _ctx.Cfg.FoulPermilleOfChallengesWorst, _ctx.Cfg.FoulPermilleOfChallengesBest);

            // In his own box he stays on his feet. It is why penalties are rare without being
            // impossible, and it is a real thing defenders do rather than a knob invented to keep
            // the count down.
            if (inOwnBox) odds = odds * _ctx.Cfg.FoulInBoxPermille / 1000;
            if (_ctx.Rng.NextInt(0, 1000) >= odds) return false;

            int victimSide = 1 - side;
            int victimSlot = carrier - victimSide * _ctx.N;
            int spotX = _ctx.Ball.X, spotY = _ctx.Ball.Y;
            bool cynical = StoppedAnAttack(victimSide, victimSlot);

            _offside.ClearFlags();
            _ctx.Sheet.Record(tick, BallActionKind.Foul, side == 0, slot, victimSlot);

            // The card. A second yellow is a red by the law and not by a knob, so a man already
            // booked who fouls cynically again walks.
            int yellow = _ctx.Cfg.YellowPercentOfFouls;
            if (cynical) yellow = yellow * _ctx.Cfg.YellowCynicalPercent / 100;
            if (_booked[k]) yellow = yellow * _ctx.Cfg.BookedCarePercent / 100;
            bool straightRed = _ctx.Rng.NextInt(0, 1000) < _ctx.Cfg.RedPermilleOfFouls;
            bool booked = _ctx.Rng.NextInt(0, 100) < yellow;

            if (straightRed || (booked && _booked[k]))
            {
                _booked[k] = true;
                _ctx.SentOff[k] = true;
                _tenMen[side]++;
                _ctx.Sheet.Record(tick, BallActionKind.RedCard, side == 0, slot, -1);
            }
            else if (booked)
            {
                _booked[k] = true;
                _ctx.Sheet.Record(tick, BallActionKind.YellowCard, side == 0, slot, -1);
            }

            if (inOwnBox)
            {
                int ownGoalX = U.Units(MovementGeometry.OwnGoalX(side == 0));
                int spot = side == 0
                    ? ownGoalX + U.Units(_ctx.Cfg.PenaltySpotDm)
                    : ownGoalX - U.Units(_ctx.Cfg.PenaltySpotDm);
                _restarts.Restart(tick, BallActionKind.Penalty, victimSide, spot, U.CenterYU);
            }
            else
            {
                _restarts.Restart(tick, BallActionKind.FreeKick, victimSide, spotX, spotY);
            }

            // A stoppage: the referee has a word, the wall is walked back, the ball is placed.
            _ctx.DeadAt += _ctx.Cfg.FoulStoppageTicks;
            return true;
        }

        /// <summary>
        /// Was that foul cynical — a man stopped while he was running at a defence with hardly
        /// anybody left between him and the goal? It is what turns a foul into a booking, and it
        /// is read off the pitch rather than rolled for.
        /// </summary>
        private bool StoppedAnAttack(int side, int slot)
        {
            int k = side * _ctx.N + slot;
            int dir = MovementGeometry.Direction(side == 0);
            if (dir * _ctx.Px[k] <= dir * U.CenterXU) return false;

            int opponent = 1 - side;
            int goalSide = 0;
            for (int j = 0; j < _ctx.N; j++)
            {
                int ok = opponent * _ctx.N + j;
                if (_ctx.SentOff[ok] || _ctx.Keeper[ok]) continue;
                if (dir * _ctx.Px[ok] > dir * _ctx.Px[k]) goalSide++;
            }

            return goalSide <= 2;
        }

        /// <summary>
        /// The flag goes up: an indirect free kick to the other side, from the place the man was
        /// standing when the ball was played to him — not from where he has run to since, which
        /// is the whole reason his position is remembered at the kick.
        /// </summary>
        public void GiveOffside(int tick, int side, int slot)
        {
            int k = side * _ctx.N + slot;
            _offside.FlaggedSpot(k, out int x, out int y);
            _offside.ClearFlags();

            _ctx.Sheet.Record(tick, BallActionKind.Offside, side == 0, slot, -1);
            _restarts.Restart(tick, BallActionKind.FreeKick, 1 - side, x, y);
        }
    }
}
