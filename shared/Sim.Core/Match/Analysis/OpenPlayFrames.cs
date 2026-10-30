using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The open-play convention of <see cref="MovementFeelAnalyzer"/> (and of
    /// <see cref="ShapeMovementAnalyzer"/>) for the readings of the free-ball spec, so they count
    /// the same frames: dead from a restart's whistle, a goal or half-time until somebody holds the
    /// ball after the restart's whistle; the restart delivery itself is in play. Also who was sent
    /// off from which frame.
    /// </summary>
    internal static class OpenPlayFrames
    {
        /// <summary>Fills <paramref name="open"/>[t] for every t below <paramref name="ticks"/>.</summary>
        public static void Mark(PositionStream s, int ticks, bool[] open)
        {
            bool dead = false, awaitingTaker = false;
            int restartTick = -1, next = 0;
            for (int t = 0; t < ticks; t++)
            {
                for (; next < s.Actions.Count && s.Actions[next].Tick <= t; next++)
                {
                    BallActionKind k = s.Actions[next].Kind;
                    if (IsRestart(k))
                    {
                        dead = awaitingTaker = true;
                        restartTick = s.Actions[next].Tick;
                    }
                    else if (k == BallActionKind.Goal || k == BallActionKind.HalfTime)
                    {
                        dead = true;
                        awaitingTaker = false;
                    }
                }

                if (dead && awaitingTaker && t > restartTick && s.Owner[t] != PositionStream.NoOwner)
                    dead = awaitingTaker = false;
                open[t] = !dead;
            }
        }

        /// <summary>
        /// Per man (home slots first, then away), the frame his red card is filed on; int.MaxValue
        /// for a man never sent off. <paramref name="from"/> holds 2 × PlayerCount entries.
        /// </summary>
        public static void SentOff(PositionStream s, int[] from)
        {
            int n = s.PlayerCount;
            for (int i = 0; i < from.Length; i++) from[i] = int.MaxValue;
            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= n) continue;
                int k = (a.Home ? 0 : n) + a.Slot;
                from[k] = Math.Min(from[k], a.Tick);
            }
        }

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.Kickoff || k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick
            || k == BallActionKind.FreeKick || k == BallActionKind.Corner || k == BallActionKind.Penalty;
    }
}
