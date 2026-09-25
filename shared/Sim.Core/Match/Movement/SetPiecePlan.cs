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

        /// <summary>Whether the build-up instruction plays this restart long.</summary>
        public static bool LongRestart(BallActionKind kind, Tempo tempo, MatchBalance cfg)
        {
            int[] table = kind == BallActionKind.GoalKick ? cfg.GoalKickLongByTempo : cfg.ThrowInLongByTempo;
            int i = (int)tempo;
            return table != null && i >= 0 && i < table.Length && table[i] != 0;
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
