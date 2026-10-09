using Sim.Core.Config;
using Sim.Core.Tactics;

namespace Sim.Core.Match.Movement
{
    /// <summary>What the taker of a free kick in range does with it (watchable-match spec, R6).</summary>
    public enum FreeKickOption
    {
        /// <summary>Too far out to be a set piece: it is played like any other free kick.</summary>
        None = 0,
        DirectShot = 1,
        Cross = 2
    }

    /// <summary>How a goal kick or a throw-in is played (the values of the Tempo tables in <see cref="MatchBalance"/>).</summary>
    public enum RestartLength
    {
        Short = 0,
        Long = 1,

        /// <summary>Short to the nearest man, long when an opponent presses him (real-match spec R11).</summary>
        ShortUnlessPressed = 2
    }

    /// <summary>
    /// The V11 set-piece decisions, pure and draw-free so they can be scripted in a test: whether
    /// a free kick is shot or crossed, and whether a goal kick or a throw-in goes short or long
    /// and to whom. Positions in any one unit; only the ranges have to be in the same unit.
    /// </summary>
    public static class SetPiecePlan
    {
        /// <summary>
        /// Direct shot or cross, by value: the goal odds the taker gives a strike from here (the
        /// same xG-shaped reading an open-play shot gets, with nobody closing him down) against
        /// what a ball swung into the box is worth. Beyond <see cref="MatchBalance.SetPieceRangeDm"/>
        /// it is not a set piece at all.
        /// </summary>
        public static FreeKickOption ChooseFreeKick(
            int distanceDm, int offCentreDm, int shooting, int technique, MatchBalance cfg,
            out int directOddsPermille, out int crossOddsPermille)
        {
            int quality = BallSkill.ShotQualityPermille(distanceDm, offCentreDm, 0, shooting, technique, cfg);
            directOddsPermille = BallSkill.GoalOddsPermille(quality, cfg);
            crossOddsPermille = cfg.FreeKickCrossOddsPermille;

            if (distanceDm >= cfg.SetPieceRangeDm) return FreeKickOption.None;
            return directOddsPermille >= crossOddsPermille ? FreeKickOption.DirectShot : FreeKickOption.Cross;
        }

        /// <summary>How the build-up instruction plays this restart; short when the table has no entry for it.</summary>
        public static RestartLength Length(BallActionKind kind, Tempo tempo, MatchBalance cfg)
        {
            int[] table = kind == BallActionKind.GoalKick ? cfg.GoalKickLongByTempo : cfg.ThrowInLongByTempo;
            int i = (int)tempo;
            if (table == null || i < 0 || i >= table.Length) return RestartLength.Short;
            return table[i] == (int)RestartLength.Long ? RestartLength.Long
                : table[i] == (int)RestartLength.ShortUnlessPressed ? RestartLength.ShortUnlessPressed
                : RestartLength.Short;
        }

        /// <summary>
        /// Whether an opponent still on the pitch stands within <paramref name="radius"/> of the man
        /// at (<paramref name="x"/>, <paramref name="y"/>). Positions and radius in any one unit.
        /// </summary>
        public static bool Pressed(int x, int y, int[] foeX, int[] foeY, bool[] foeOn, int radius)
        {
            long reach = (long)radius * radius;
            for (int i = 0; i < foeX.Length; i++)
            {
                if (!foeOn[i]) continue;
                long dx = foeX[i] - x, dy = foeY[i] - y;
                if (dx * dx + dy * dy <= reach) return true;
            }

            return false;
        }

        /// <summary>
        /// Who a restart is played to. Short: the nearest eligible team-mate. Long: the one
        /// furthest forward (along <paramref name="dir"/>). Either way he is at least
        /// <paramref name="minRange"/> and at most <paramref name="maxRange"/> from the taker;
        /// -1 when nobody is. Ties go to the lower slot.
        /// </summary>
        public static int PickRestartTarget(
            bool longBall, int takerX, int takerY, int dir,
            int[] xs, int[] ys, bool[] eligible, int minRange, int maxRange)
        {
            int best = -1;
            long bestScore = long.MaxValue;
            long minSq = (long)minRange * minRange, maxSq = (long)maxRange * maxRange;

            for (int i = 0; i < xs.Length; i++)
            {
                if (!eligible[i]) continue;
                long dx = xs[i] - takerX, dy = ys[i] - takerY;
                long sq = dx * dx + dy * dy;
                if (sq < minSq || sq > maxSq) continue;

                long score = longBall ? -(long)dir * xs[i] : sq;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }
    }
}
