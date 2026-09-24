using System;
using System.Collections.Generic;
using System.Linq;

namespace Sim.Core.Match.Broadcast
{
    /// <summary>
    /// Reads a stream into frame classes (spec R12). Integer-only and a function of the stream
    /// alone, so the same report always reads the same way.
    /// </summary>
    internal static class FrameClassifier
    {
        private const int HalfwayDm = Pitch.LengthDm / 2;
        private const int FinalThirdDm = Pitch.LengthDm * 2 / 3;

        public static FrameReading Read(PositionStream s, BroadcastSettings settings)
        {
            int n = s.TickCount;
            int framesPerSecond = s.TicksPerMinute / 60;
            int after = settings.KeyAftermathSeconds * framesPerSecond;
            List<BallAction> actions = s.Actions.OrderBy(a => a.Tick).ToList();

            var reading = new FrameReading(n, SecondHalfStart(actions, n, s.TicksPerMinute));
            int[] side = Sides(s, actions);
            bool[] dead = ReadDeadBalls(s, actions, reading);
            ReadOpenPlay(s, side, dead, reading);
            ReadCounters(s, side, dead, reading, settings.CounterWindowSeconds * framesPerSecond);
            ReadKeyEvents(s, actions, reading, after);
            return reading;
        }

        /// <summary>1 = home, 2 = away, 0 = nobody yet: the owner, carried through loose frames.</summary>
        private static int[] Sides(PositionStream s, List<BallAction> actions)
        {
            var side = new int[s.TickCount];
            int current = 0, next = 0;
            for (int f = 0; f < side.Length; f++)
            {
                for (; next < actions.Count && actions[next].Tick <= f; next++)
                    if (IsRestart(actions[next].Kind))
                        current = actions[next].Home ? 1 : 2;

                int code = f < s.Owner.Length ? s.Owner[f] : PositionStream.NoOwner;
                if (s.TryOwner(code, out bool home, out _)) current = home ? 1 : 2;
                side[f] = current;
            }

            return side;
        }

        internal static int Progress(int ballX, bool home) => home ? ballX : Pitch.LengthDm - ballX;

        private static int SecondHalfStart(List<BallAction> actions, int frames, int framesPerMinute)
        {
            foreach (BallAction a in actions)
                if (a.Kind == BallActionKind.HalfTime)
                    return Math.Min(a.Tick, frames);
            return Math.Min(45 * framesPerMinute, frames);
        }

        /// <summary>
        /// A dead ball runs from the whistle (the restart, or the goal) to the first touch that puts
        /// it back in play. In the attacking half of the side restarting it is a set piece (hot);
        /// anywhere else it is cut.
        /// </summary>
        private static bool[] ReadDeadBalls(PositionStream s, List<BallAction> actions, FrameReading r)
        {
            var dead = new bool[r.Classes.Length];
            bool open = false;
            int start = 0;
            BallAction last = default;
            foreach (BallAction a in actions)
            {
                if (IsRestart(a.Kind) || a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.HalfTime)
                {
                    if (!open) start = a.Tick;
                    open = true;
                    last = a;
                }
                else if (open && IsPlay(a.Kind))
                {
                    CloseDeadBall(s, r, dead, start, a.Tick, last);
                    open = false;
                }
            }

            if (open) CloseDeadBall(s, r, dead, start, r.Classes.Length, last);
            return dead;
        }

        private static void CloseDeadBall(PositionStream s, FrameReading r, bool[] dead, int start, int end, BallAction restart)
        {
            int n = r.Classes.Length;
            if (end > n) end = n;
            if (start >= end) return;

            // The last frame before the touch: actions land on the first frame at or after their
            // tick, so by frame `end` the ball may already have left the spot.
            int taking = end - 1;
            bool setPiece = IsSetPiece(restart.Kind) && Progress(s.BallXY[taking * 2], restart.Home) > HalfwayDm;
            for (int f = start; f < end; f++)
            {
                dead[f] = true;
                r.Classes[f] = setPiece ? FrameClass.Hot : FrameClass.Dead;
            }
        }

        /// <summary>
        /// Open play by where the side on the ball has it, except a spell that never crossed
        /// halfway, which is sterile possession however long it lasted.
        /// </summary>
        private static void ReadOpenPlay(PositionStream s, int[] side, bool[] dead, FrameReading r)
        {
            int n = side.Length;
            int spellStart = 0;
            for (int f = 1; f <= n; f++)
            {
                if (f < n && side[f] == side[spellStart]) continue;

                bool home = side[spellStart] == 1;
                int furthest = 0;
                for (int g = spellStart; g < f; g++)
                    if (!dead[g]) furthest = Math.Max(furthest, Progress(s.BallXY[g * 2], home));

                bool sterile = side[spellStart] != 0 && furthest <= HalfwayDm;
                for (int g = spellStart; g < f; g++)
                {
                    if (dead[g]) continue;
                    r.Classes[g] = sterile ? FrameClass.Sterile
                        : Progress(s.BallXY[g * 2], home) >= FinalThirdDm ? FrameClass.Hot
                        : FrameClass.Warm;
                }

                spellStart = f;
            }
        }

        /// <summary>A ball won in the own half and carried into the final third inside the window is a counter.</summary>
        private static void ReadCounters(PositionStream s, int[] side, bool[] dead, FrameReading r, int window)
        {
            for (int f = 1; f < side.Length; f++)
            {
                if (dead[f] || side[f] == 0 || side[f] == side[f - 1]) continue;
                bool home = side[f] == 1;
                if (Progress(s.BallXY[f * 2], home) >= HalfwayDm) continue;

                int last = Math.Min(f + window, r.HalfEnd(f) - 1);
                for (int g = f + 1; g <= last && !dead[g] && side[g] == side[f]; g++)
                {
                    if (Progress(s.BallXY[g * 2], home) < FinalThirdDm) continue;
                    for (int h = f; h <= g; h++) r.Classes[h] = FrameClass.Hot;
                    break;
                }
            }
        }

        /// <summary>Shots, goals, cards, penalties and substitutions, each with its aftermath, at 1x.</summary>
        private static void ReadKeyEvents(PositionStream s, List<BallAction> actions, FrameReading r, int after)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                switch (a.Kind)
                {
                    case BallActionKind.Shot:
                    case BallActionKind.Goal:
                    case BallActionKind.YellowCard:
                    case BallActionKind.RedCard:
                        MarkKey(r, a.Tick, a.Tick + after);
                        break;
                    case BallActionKind.Penalty:
                        MarkKey(r, a.Tick, Math.Max(a.Tick + after, NextShot(actions, i)));
                        break;
                }
            }

            foreach (SlotChange c in s.Changes)
                MarkKey(r, c.Frame, c.Frame + after);
        }

        /// <summary>The award and the kick are one moment: the whole wait between them plays.</summary>
        private static int NextShot(List<BallAction> actions, int from)
        {
            for (int j = from + 1; j < actions.Count; j++)
                if (actions[j].Kind == BallActionKind.Shot)
                    return actions[j].Tick;
            return actions[from].Tick;
        }

        private static void MarkKey(FrameReading r, int from, int to)
        {
            if (from < 0 || from >= r.Classes.Length) return;
            int last = Math.Min(to, r.HalfEnd(from) - 1);
            for (int f = from; f <= last; f++) r.Key[f] = true;
            r.Anchor[from] = true;
        }

        private static bool IsRestart(BallActionKind kind) =>
            kind == BallActionKind.Kickoff || kind == BallActionKind.ThrowIn || kind == BallActionKind.GoalKick
            || kind == BallActionKind.FreeKick || kind == BallActionKind.Corner || kind == BallActionKind.Penalty;

        private static bool IsSetPiece(BallActionKind kind) =>
            kind == BallActionKind.Corner || kind == BallActionKind.FreeKick
            || kind == BallActionKind.ThrowIn || kind == BallActionKind.Penalty;

        private static bool IsPlay(BallActionKind kind) =>
            kind == BallActionKind.Pass || kind == BallActionKind.LongBall || kind == BallActionKind.Cross
            || kind == BallActionKind.Dribble || kind == BallActionKind.Recovery || kind == BallActionKind.Interception
            || kind == BallActionKind.Clearance || kind == BallActionKind.Shot || kind == BallActionKind.Save
            || kind == BallActionKind.Miss || kind == BallActionKind.Block;
    }
}
