using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The cross label by geometry only (free-ball-real-movement spec R2; the same rule
    /// tools/event_reference.py applies to StatsBomb, docs/research/football-reference.md §11.3):
    /// an open-play pass struck in the final third, in the wide channel (≤ 13.84 m from a
    /// touchline: outside the penalty area's side lines), that arrives inside the penalty area.
    /// High when the ball peaks at 2 m or more on the way, else low or a cut-back.
    ///
    /// Read off the engine's own events and the stream:
    ///   * A pass: a <see cref="BallActionKind.Pass"/>, <see cref="BallActionKind.LongBall"/> or
    ///     <see cref="BallActionKind.Cross"/> action, whatever the engine called it. Open play: the
    ///     frame before it is in play (a restart's kick ends a dead spell, <see cref="BallFreedomAnalyzer"/>).
    ///   * Origin: where the ball was when the passer struck it, his last touch filed on the pass's frame.
    ///   * Arrival: where the next man touched it, deflected it or the post met it, or, if the ball
    ///     went dead first, where it was on the last frame in play (StatsBomb's end location: received,
    ///     intercepted or out).
    ///   * Peak: the highest BallZ from the pass's frame to the arrival's.
    /// A measuring instrument: it reads the report and the events and touches nothing.
    /// </summary>
    public sealed class GeometricCrossAnalyzer
    {
        public const int HighPeakDm = 20;

        private bool[] _dead = Array.Empty<bool>();
        private int[] _eventFrames = Array.Empty<int>();

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static GeometricCrossMetrics? Analyze(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame) =>
            new GeometricCrossAnalyzer().Measure(report, events, ticksPerFrame);

        /// <summary>
        /// Measures one match. <paramref name="events"/> are the match's ball events in the order the
        /// sink heard them (tick order); <paramref name="ticksPerFrame"/> the stream's decimation.
        /// Returns null when the report carries no position stream.
        /// </summary>
        public GeometricCrossMetrics? Measure(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (events == null) throw new ArgumentNullException(nameof(events));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0 || s.TicksPerMinute <= 0) return null;

            int ticks = s.TickCount;
            _dead = StreamPlay.Dead(s, _dead);
            _eventFrames = StreamPlay.Frames(events, ticksPerFrame, ticks - 1, _eventFrames);

            var m = new GeometricCrossMetrics();
            int from = 0;
            foreach (BallAction a in s.Actions)
            {
                if (a.Kind == BallActionKind.Cross)
                {
                    if (a.Home) m.HomeLabelled++;
                    else m.AwayLabelled++;
                }

                if (!IsPass(a.Kind)) continue;
                int f = a.Tick;
                while (from < events.Count && _eventFrames[from] < f) from++;
                if (f < 1 || f >= ticks || _dead[f - 1] || _dead[f]) continue;

                int kick = Kick(events, from, f, a.Home ? 0 : 1, a.Slot);
                if (kick < 0 || !WideInFinalThird(a.Home, events[kick].XDm, events[kick].YDm)) continue;

                Arrival(s, events, kick, f, out int endFrame, out int endX, out int endY);
                if (!InAttackedBox(a.Home, endX, endY)) continue;

                bool high = Peak(s, f, endFrame) >= HighPeakDm;
                if (a.Home)
                {
                    if (high) m.HomeHigh++;
                    else m.HomeLow++;
                }
                else if (high) m.AwayHigh++;
                else m.AwayLow++;
            }

            return m;
        }

        /// <summary>The passer's last touch filed on frame <paramref name="f"/>, -1 if the sink heard none.</summary>
        private int Kick(IReadOnlyList<BallEvent> events, int from, int f, int side, int slot)
        {
            int kick = -1;
            for (int i = from; i < events.Count && _eventFrames[i] == f; i++)
                if (events[i].Kind == BallEventKind.Touch && events[i].Side == side && events[i].Slot == slot) kick = i;
            return kick;
        }

        private void Arrival(PositionStream s, IReadOnlyList<BallEvent> events, int kick, int f,
            out int endFrame, out int endX, out int endY)
        {
            int ticks = s.TickCount, deadAt = f + 1;
            while (deadAt < ticks && !_dead[deadAt]) deadAt++;

            for (int i = kick + 1; i < events.Count && _eventFrames[i] <= deadAt; i++)
            {
                if (events[i].Kind == BallEventKind.Bounce) continue;
                endFrame = _eventFrames[i];
                endX = events[i].XDm;
                endY = events[i].YDm;
                return;
            }

            endFrame = deadAt - 1;
            endX = s.BallXY[endFrame * 2];
            endY = s.BallXY[endFrame * 2 + 1];
        }

        private static int Peak(PositionStream s, int from, int to)
        {
            int peak = 0;
            for (int t = from; t <= to; t++)
                if (s.BallZAt(t) > peak) peak = s.BallZAt(t);
            return peak;
        }

        private static bool IsPass(BallActionKind k) =>
            k == BallActionKind.Pass || k == BallActionKind.LongBall || k == BallActionKind.Cross;

        /// <summary>The final third the side attacks, outside the penalty area's side lines (13.84 m from the touchline).</summary>
        private static bool WideInFinalThird(bool home, int x, int y)
        {
            bool finalThird = home ? x >= 2 * Pitch.LengthDm / 3 : x <= Pitch.LengthDm / 3;
            int across = y - Pitch.CenterY;
            return finalThird && (across > MovementGeometry.BoxHalfWidthDm || -across > MovementGeometry.BoxHalfWidthDm);
        }

        private static bool InAttackedBox(bool home, int x, int y)
        {
            bool deep = home ? x >= Pitch.LengthDm - MovementGeometry.BoxDepthDm : x <= MovementGeometry.BoxDepthDm;
            int across = y - Pitch.CenterY;
            return deep && across <= MovementGeometry.BoxHalfWidthDm && -across <= MovementGeometry.BoxHalfWidthDm;
        }
    }
}
