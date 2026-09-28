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
                if (_ctx.DeadSide == side && _ctx.DeadTaker == slot && tick >= _ctx.DeadAt
                    && U.Distance(_px[k], _py[k], _ball.X, _ball.Y) <= _kickU)
                    _sim.TakeRestart(tick, side, slot);
            }
        }
    }
}
