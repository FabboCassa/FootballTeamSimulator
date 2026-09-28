namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// The man (side * n + slot) the V11 brain last sent running with the ball, -1 for nobody.
        /// </summary>
        private int _v11Carrier = -1;

        /// <summary>
        /// A man running with the ball is slower than one running without it (V11 only): he is
        /// capped at <see cref="Config.MatchBalance.V11CarrierSpeedPercent"/> of his top speed while
        /// the ball is his — at his feet, or knocked ahead and not yet touched by anybody else.
        /// At full speed a carrier ran through a flat back line that could never turn and catch
        /// him, and walked in on the keeper a hundred times a match.
        /// </summary>
        private int CarrierTop(int k, int top)
        {
            if (k != _v11Carrier || _ball.Dead || _ball.LastTouchSide < 0) return top;
            int toucher = _ball.Free
                ? _ball.LastTouchSide * _n + _ball.LastTouchSlot
                : _ball.OwnerSide * _n + _ball.OwnerSlot;
            if (toucher != k) return top;

            int capped = top * _cfg.V11CarrierSpeedPercent / 100;
            return capped < 1 ? 1 : capped;
        }
    }
}
