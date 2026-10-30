namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        private sealed partial class BaseBrain
        {
            /// <summary>Putting a dead ball back in play, for a restart V11's set pieces do not take.</summary>
            public void Restart(int tick, int side, int slot)
            {
                int k = side * _n + slot;
                int markX = _ctx.DeadKind == BallActionKind.Kickoff
                    ? _ball.X - MovementGeometry.Direction(side == 0) * U.Units(_cfg.KickoffStandOffDm)
                    : _ball.X;
                // A penalty is struck from where the ball lies as he runs onto it; the rest he holds.
                int reach = _ctx.DeadKind == BallActionKind.Penalty ? _kickU : U.Units(_cfg.RestartOnBallDm);
                if (_ctx.DeadSide == side && _ctx.DeadTaker == slot && tick >= _ctx.DeadAt
                    && U.Distance(_px[k], _py[k], markX, _ball.Y) <= reach)
                    _sim.TakeRestart(tick, side, slot);
            }
        }
    }
}
