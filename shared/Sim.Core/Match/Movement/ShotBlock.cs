using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The odds that an outfield defender gets something on a strike as it comes level with him
    /// (real-match spec R2, issue #80): one go per man per strike. In engine units. Public so the
    /// odds can be tested on their own; nothing outside the simulator needs them.
    ///
    /// Two things decide it. How far he is from the line of the ball: a man within a stride of it
    /// has the ball struck at him and the best of it, and beyond that the odds fall away to
    /// nothing at the edge of his reach, where he can only stretch. And where he stands: the man closing the striker down is set and square to
    /// it, with the ball still at his feet's height, so he charges it down more often than one
    /// further down the lane, who sees it go past him at full speed.
    /// </summary>
    public static class ShotBlock
    {
        /// <summary>
        /// Whether a defender at (<paramref name="px"/>, <paramref name="py"/>) gets his go at the
        /// strike this tick: he stands past the strike point along the ball's flight, and the
        /// ball, now at (<paramref name="ballX"/>, <paramref name="ballY"/>) moving by
        /// (<paramref name="vx"/>, <paramref name="vy"/>) a tick, has come level with him. A man
        /// behind the striker, however near the line, is never offered one.
        /// </summary>
        public static bool IsOffered(int px, int py, int fromX, int fromY, int ballX, int ballY, int vx, int vy)
        {
            bool pastStrike = (long)(px - fromX) * vx + (long)(py - fromY) * vy > 0;
            bool ballLevel = (long)(px - ballX) * vx + (long)(py - ballY) * vy <= 0;
            return pastStrike && ballLevel;
        }

        /// <param name="laneU">His distance from the ball's line.</param>
        /// <param name="reachU">The farthest from the line he can get anything on it.</param>
        /// <remarks>Within <see cref="MatchBalance.BlockFullOddsDm"/> of the line the odds are the full
        /// <see cref="MatchBalance.BlockPermilleOnLine"/>; from there they fall linearly to none at the reach.</remarks>
        /// <param name="fromStrikeU">His distance from the point the strike was struck from.</param>
        public static int Permille(int laneU, int reachU, int fromStrikeU, MatchBalance cfg)
        {
            if (reachU <= 0 || laneU >= reachU) return 0;

            int full = U.Units(cfg.BlockFullOddsDm);
            long permille = laneU <= full
                ? cfg.BlockPermilleOnLine
                : (long)cfg.BlockPermilleOnLine * (reachU - laneU) / (reachU - full);
            if (fromStrikeU <= U.Units(cfg.BlockCloseRangeDm)) permille = permille * cfg.BlockClosePercent / 100;
            return permille > 1000 ? 1000 : (int)permille;
        }
    }
}
