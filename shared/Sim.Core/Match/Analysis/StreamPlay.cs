using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The frame-level reads the ball-event analyzers share with <see cref="BallFreedomAnalyzer"/>:
    /// the same ball-in-play rule, the same sent-off men left out, and an event's tick filed on the
    /// first frame at or after it, as the scoresheet files actions.
    /// </summary>
    internal static class StreamPlay
    {
        /// <summary>
        /// dead[t] for every frame: from a restart's whistle, a goal or half-time until the next action
        /// that plays the ball (<see cref="BallFreedomAnalyzer"/>'s ball-in-play rule).
        /// </summary>
        public static bool[] Dead(PositionStream s, bool[] dead)
        {
            int ticks = s.TickCount;
            if (dead.Length < ticks) dead = new bool[ticks];
            bool isDead = false;
            int next = 0;
            for (int t = 0; t < ticks; t++)
            {
                for (; next < s.Actions.Count && s.Actions[next].Tick <= t; next++)
                {
                    BallActionKind k = s.Actions[next].Kind;
                    if (BallFreedomAnalyzer.IsRestart(k) || k == BallActionKind.Goal || k == BallActionKind.HalfTime) isDead = true;
                    else if (BallFreedomAnalyzer.IsPlay(k)) isDead = false;
                }

                dead[t] = isDead;
            }

            return dead;
        }

        /// <summary>from[i] is the frame man i (home 0..n-1, away n..2n-1) was sent off at, int.MaxValue if never.</summary>
        public static int[] SentOffFrom(PositionStream s, int[] from)
        {
            int n = s.PlayerCount;
            if (from.Length != 2 * n) from = new int[2 * n];
            for (int i = 0; i < from.Length; i++) from[i] = int.MaxValue;
            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= n) continue;
                int k = (a.Home ? 0 : n) + a.Slot;
                if (a.Tick < from[k]) from[k] = a.Tick;
            }

            return from;
        }

        /// <summary>The nearest man on the pitch to the ball at frame t (ties to the lower index, home first), -1 if none.</summary>
        public static int Nearest(PositionStream s, int t, int[] sentOffFrom, out long distanceSq)
        {
            int n = s.PlayerCount, bx = s.BallXY[t * 2], by = s.BallXY[t * 2 + 1], nearest = -1;
            distanceSq = long.MaxValue;
            for (int i = 0; i < 2 * n; i++)
            {
                if (t >= sentOffFrom[i]) continue;
                int[] xy = i < n ? s.HomeXY : s.AwayXY;
                long dx = s.PlayerX(xy, t, i % n) - bx, dy = s.PlayerY(xy, t, i % n) - by;
                long d2 = dx * dx + dy * dy;
                if (d2 >= distanceSq) continue;
                distanceSq = d2;
                nearest = i;
            }

            return nearest;
        }

        /// <summary>Each event's frame: the first at or after its tick, capped at the last frame.</summary>
        public static int[] Frames(IReadOnlyList<BallEvent> events, int ticksPerFrame, int lastFrame, int[] frames)
        {
            if (ticksPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerFrame));
            if (frames.Length < events.Count) frames = new int[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                int frame = (events[i].Tick + ticksPerFrame - 1) / ticksPerFrame;
                frames[i] = frame > lastFrame ? lastFrame : frame;
            }

            return frames;
        }
    }
}
