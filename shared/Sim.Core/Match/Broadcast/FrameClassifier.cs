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

        public static FrameReading Read(PositionStream s, BroadcastSettings settings)
        {
            int n = s.TickCount;
            int framesPerSecond = s.TicksPerMinute / 60;
            int after = settings.KeyAftermathSeconds * framesPerSecond;
            List<BallAction> actions = s.Actions.OrderBy(a => a.Tick).ToList();

            var reading = new FrameReading(n, SecondHalfStart(actions, n, s.TicksPerMinute));
            int[] side = Sides(s, actions);
            bool[] dead = ReadDeadBalls(actions, reading, settings.DeadTimeShownSeconds * framesPerSecond);
            ReadOpenPlay(s, side, dead, reading);
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
        /// it back in play. Its last <paramref name="shown"/> frames are the set-up, shown at dead
        /// time; everything before them is idle and cut.
        /// </summary>
        private static bool[] ReadDeadBalls(List<BallAction> actions, FrameReading r, int shown)
        {
            var dead = new bool[r.Classes.Length];
            bool open = false;
            int start = 0;
            foreach (BallAction a in actions)
            {
                if (IsRestart(a.Kind) || a.Kind == BallActionKind.Goal || a.Kind == BallActionKind.HalfTime)
                {
                    if (!open) start = a.Tick;
                    open = true;
                }
                else if (open && IsPlay(a.Kind))
                {
                    CloseDeadBall(r, dead, start, a.Tick, shown);
                    open = false;
                }
            }

            if (open) CloseDeadBall(r, dead, start, r.Classes.Length, shown);
            return dead;
        }

        private static void CloseDeadBall(FrameReading r, bool[] dead, int start, int end, int shown)
        {
            if (end > r.Classes.Length) end = r.Classes.Length;
            for (int f = start; f < end; f++)
            {
                dead[f] = true;
                r.Classes[f] = f >= end - shown ? FrameClass.DeadTime : FrameClass.Idle;
            }
        }

        /// <summary>
        /// Open play, except a spell that never crossed halfway, which is sterile possession however
        /// long it lasted.
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
                    if (!dead[g])
                        r.Classes[g] = sterile ? FrameClass.Sterile : FrameClass.Open;

                spellStart = f;
            }
        }

        /// <summary>Shots, goals, cards, penalties and substitutions, each with its aftermath, always shown.</summary>
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

        private static bool IsPlay(BallActionKind kind) =>
            kind == BallActionKind.Pass || kind == BallActionKind.LongBall || kind == BallActionKind.Cross
            || kind == BallActionKind.Dribble || kind == BallActionKind.Recovery || kind == BallActionKind.Interception
            || kind == BallActionKind.Clearance || kind == BallActionKind.Shot || kind == BallActionKind.Save
            || kind == BallActionKind.Miss || kind == BallActionKind.Block;
    }
}
