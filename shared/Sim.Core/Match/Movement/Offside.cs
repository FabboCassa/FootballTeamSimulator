namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Law 11, as the assistant referee keeps it: at the moment a ball is played, which of
    /// the passing side's men were beyond the offside line. The flag only matters if one of
    /// them then plays the ball, so it is raised here and answered in the simulator's
    /// ResolveControl — and it is wiped by the next touch, whoever takes it.
    /// </summary>
    internal sealed class Offside
    {
        private readonly MatchContext _ctx;
        private readonly int[] _skPositioning;
        private readonly bool[] _flagged;
        private readonly int[] _flaggedX;
        private readonly int[] _flaggedY;
        private bool _anyFlag;

        public Offside(MatchContext ctx, int[] skPositioning)
        {
            _ctx = ctx;
            _skPositioning = skPositioning;
            int total = skPositioning.Length;
            _flagged = new bool[total];
            _flaggedX = new int[total];
            _flaggedY = new int[total];
        }

        public bool AnyFlag => _anyFlag;

        public bool IsFlagged(int k) => _flagged[k];

        /// <summary>Where he was standing when the ball was played to him.</summary>
        public void FlaggedSpot(int k, out int x, out int y)
        {
            x = _flaggedX[k];
            y = _flaggedY[k];
        }

        /// <summary>
        /// The offside line for the side in possession, in DEPTH — how far up the pitch it is,
        /// measured in that side's own attacking direction, so one formula serves both sides.
        ///
        /// Law 11: the line is the second-rearmost opponent, and a man is only offside if he is
        /// also ahead of the ball and in the opponents' half. The keeper is one of the two, which
        /// is why it is the SECOND-rearmost and not simply the last defender.
        /// </summary>
        public int OffsideLineDepth(int side)
        {
            int dir = MovementGeometry.Direction(side == 0);
            int opponent = 1 - side;

            // The two opponents nearest their own goal, i.e. with the greatest depth.
            int first = int.MinValue, second = int.MinValue;
            for (int j = 0; j < _ctx.N; j++)
            {
                int ok = opponent * _ctx.N + j;
                if (_ctx.SentOff[ok]) continue;
                int depth = dir * _ctx.Px[ok];
                if (depth > first) { second = first; first = depth; }
                else if (depth > second) second = depth;
            }

            if (second == int.MinValue) second = first;

            int ball = dir * _ctx.Ball.X;
            if (ball > second) second = ball;

            int halfway = dir * U.CenterXU;
            if (halfway > second) second = halfway;
            return second;
        }

        /// <summary>
        /// Where THIS player thinks the line is. The whole model of why offsides happen: the man
        /// on the ball plays what he believes is on, the referee judges what actually was, and
        /// the gap between the two is the flag. A poor reader of the game (Positioning) is out by
        /// several metres either way; a good one is barely out at all — which is why an offside
        /// is a mistake by the passer and his runner rather than a dice roll.
        /// </summary>
        public int PerceivedOffsideLine(int k, int trueDepth)
        {
            int span = _ctx.Cfg.OffsideJudgementDm - _ctx.Cfg.OffsideJudgementFloorDm;
            if (span < 0) span = 0;
            int judgement = _ctx.Cfg.OffsideJudgementFloorDm
                            + span * (100 - BallSkill.Clamp(_skPositioning[k], 1, 100)) / 100;
            int error = (_ctx.Rng.NextInt(-judgement, judgement + 1) + _ctx.Rng.NextInt(-judgement, judgement + 1)) / 2;
            return trueDepth + U.Units(error);
        }

        /// <summary>Raises the flag on every man of the passing side who was beyond the line.</summary>
        public void FlagOffside(int side, int passer)
        {
            int dir = MovementGeometry.Direction(side == 0);
            int line = OffsideLineDepth(side) + U.Units(_ctx.Cfg.OffsideMarginDm);

            for (int i = 0; i < _ctx.N; i++)
            {
                int k = side * _ctx.N + i;
                if (i == passer || _ctx.SentOff[k]) continue;
                if (dir * _ctx.Px[k] <= line) continue;

                _flagged[k] = true;
                _flaggedX[k] = _ctx.Px[k];
                _flaggedY[k] = _ctx.Py[k];
                _anyFlag = true;
            }
        }

        public void ClearFlags()
        {
            if (!_anyFlag) return;
            for (int i = 0; i < _flagged.Length; i++) _flagged[i] = false;
            _anyFlag = false;
        }
    }
}
