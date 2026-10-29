using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>
    /// The keeper against a strike at his goal (believable-match R6). Pure, integer and draw-free;
    /// positions in any one length unit, speeds in that unit per second.
    ///
    /// From the release he stands set for his reaction time (<see cref="ReactionMs"/>), then goes
    /// for the predicted crossing point: to the point of the ball's path to it nearest him, which
    /// is where he meets the ball soonest and every step of which takes him nearer the crossing
    /// point. He runs there at his own speed and dives the last <see cref="MatchBalance.KeeperDiveFromDm"/>.
    /// </summary>
    public static class KeeperReaction
    {
        /// <summary>How long after the release he moves, in ms: slow at Goalkeeping 1, fast at 100.</summary>
        public static int ReactionMs(MatchBalance cfg, int goalkeeping)
        {
            int span = cfg.KeeperReactionSlowMs - cfg.KeeperReactionFastMs;
            return cfg.KeeperReactionSlowMs - span * (MovementGeometry.Clamp(goalkeeping, 1, 100) - 1) / 99;
        }

        /// <summary>
        /// The ms of a <paramref name="tickMs"/> tick that starts <paramref name="sinceReleaseMs"/>
        /// after the release that he spends moving, with a <paramref name="reactionMs"/> reaction.
        /// </summary>
        public static int MovingMs(int sinceReleaseMs, int tickMs, int reactionMs) =>
            MovementGeometry.Clamp(sinceReleaseMs + tickMs - reactionMs, 0, tickMs);

        /// <summary>
        /// Where the ball at (<paramref name="ballX"/>, <paramref name="ballY"/>), moving
        /// (<paramref name="vx"/>, <paramref name="vy"/>), crosses the goal line at
        /// <paramref name="goalX"/>; false when it is not travelling toward that line.
        /// </summary>
        public static bool CrossingPoint(int ballX, int ballY, int vx, int vy, int goalX, out int y)
        {
            long toLine = goalX - ballX;
            y = ballY;
            if (vx == 0 || toLine * vx <= 0) return false;
            y = (int)(ballY + (long)vy * toLine / vx);
            return true;
        }

        /// <summary>
        /// The point he goes for: on the ball's path from (<paramref name="ballX"/>, <paramref name="ballY"/>)
        /// to the crossing point (<paramref name="goalX"/>, <paramref name="crossY"/>), the one nearest
        /// him, with its y kept within [<paramref name="minY"/>, <paramref name="maxY"/>] so a strike
        /// going well wide does not drag him to the corner flag.
        /// </summary>
        public static void Target(int keeperX, int keeperY, int ballX, int ballY, int goalX, int crossY,
            int minY, int maxY, out int x, out int y)
        {
            long dx = goalX - ballX, dy = crossY - ballY;
            long lengthSq = dx * dx + dy * dy;
            long t = (keeperX - ballX) * dx + (keeperY - ballY) * dy;
            if (t < 0 || lengthSq == 0) t = 0;
            if (t > lengthSq) t = lengthSq;

            x = lengthSq == 0 ? ballX : (int)(ballX + dx * t / lengthSq);
            y = lengthSq == 0 ? ballY : (int)(ballY + dy * t / lengthSq);
            y = MovementGeometry.Clamp(y, minY, maxY);
        }

        /// <summary>
        /// One tick's move toward (<paramref name="targetX"/>, <paramref name="targetY"/>) for
        /// <paramref name="movingMs"/>: at <paramref name="runPerSecond"/> until he is
        /// <paramref name="diveFrom"/> from it, at <paramref name="divePerSecond"/> (never slower
        /// than he runs) for the rest. He stops on the target.
        /// </summary>
        public static void Step(ref int x, ref int y, int targetX, int targetY, int movingMs,
            int runPerSecond, int divePerSecond, int diveFrom)
        {
            if (movingMs <= 0) return;
            long dx = targetX - x, dy = targetY - y;
            long gapSq = dx * dx + dy * dy;
            int gap = MovementGeometry.Sqrt(gapSq > int.MaxValue ? int.MaxValue : (int)gapSq);
            if (gap <= 0) return;

            int run = runPerSecond < 1 ? 1 : runPerSecond;
            int dive = divePerSecond < run ? run : divePerSecond;
            long ms = movingMs;
            long moved = 0;

            int runPart = gap - diveFrom;
            if (runPart > 0)
            {
                long runReach = run * ms / 1000;
                if (runReach <= runPart)
                {
                    moved = runReach;
                    ms = 0;
                }
                else
                {
                    moved = runPart;
                    ms -= runPart * 1000L / run;
                }
            }

            moved += dive * ms / 1000;
            if (moved >= gap)
            {
                x = targetX;
                y = targetY;
                return;
            }

            x += (int)(dx * moved / gap);
            y += (int)(dy * moved / gap);
        }
    }
}
