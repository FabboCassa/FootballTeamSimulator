namespace Sim.Core.Match.Movement
{
    public sealed partial class MatchSimulator
    {
        /// <summary>
        /// A V10 figure as V11 reads it (a keeper's dive, stop, hold and parry, a deflection):
        /// <paramref name="v11Percent"/> of it. V10 keeps its own, which the golden master pins.
        /// </summary>
        private int V11Scaled(int value, int v11Percent) => IsV11 ? value * v11Percent / 100 : value;

        /// <summary>
        /// V11: a shout on the side of the man on the ball (Encourage anywhere, Keep the ball
        /// anywhere, Concentrate in his own half) cuts the odds a challenger takes it off him.
        /// V10 keeps the odds as they are.
        /// </summary>
        private int V11DuelOdds(int odds, int owner)
        {
            if (!IsV11) return odds;
            int side = owner / _n;
            MovementTactics t = _tactics[side];
            odds = odds * t.DuelLossPercent / 100;
            if (t.OwnHalfDuelLossPercent != 100 && MovementGeometry.Direction(side == 0) * (_px[owner] - U.CenterXU) < 0)
                odds = odds * t.OwnHalfDuelLossPercent / 100;
            return odds;
        }
    }
}
