using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// Where a keeper stands in open play (real-match spec, R6). Pure and draw-free, in decimetres.
    ///
    /// He stands on the line from his goal centre to the ball, at a fixed distance from the goal
    /// centre along it: <see cref="MatchBalance.KeeperDepthDm"/> while the ball is within
    /// <see cref="MatchBalance.KeeperRushFromDm"/> of his goal, stepping up by up to
    /// <see cref="MatchBalance.KeeperRushDm"/> as it goes away, fully at
    /// <see cref="MatchBalance.KeeperRushFullDm"/>. An arc rather than a depth, so a ball out wide
    /// brings him across to his near post instead of out toward it. The ramp starts beyond the
    /// spec's 35 m on purpose: a ball played toward goal must find him back on his line by then.
    /// Claiming a loose ball, the one time he goes further, is the movement brain's call.
    /// </summary>
    public static class KeeperPositioning
    {
        public static void Target(MatchBalance cfg, bool home, int ballXDm, int ballYDm, out int xDm, out int yDm)
        {
            int goalX = MovementGeometry.OwnGoalX(home);
            int distance = MovementGeometry.Distance(ballXDm, ballYDm, goalX, Pitch.CenterY);

            int ramp = cfg.KeeperRushFullDm - cfg.KeeperRushFromDm;
            int radius = cfg.KeeperDepthDm;
            if (ramp > 0)
                radius += cfg.KeeperRushDm * MovementGeometry.Clamp(distance - cfg.KeeperRushFromDm, 0, ramp) / ramp;

            if (distance <= radius)
            {
                xDm = ballXDm;
                yDm = ballYDm;
                return;
            }

            xDm = goalX + (ballXDm - goalX) * radius / distance;
            yDm = Pitch.CenterY + (ballYDm - Pitch.CenterY) * radius / distance;
        }
    }
}
