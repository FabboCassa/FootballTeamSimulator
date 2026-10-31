using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The cause trace of fast free-ball turns (free-ball-real-movement spec R2), with the
    /// definition of docs/research/football-reference.md §10.3 as tools/tracking_reference.py
    /// computes it: two consecutive 0.2 s displacement steps around a frame count when every frame
    /// of both is in play, both are faster than 5 m/s, nobody is within 2 m at the middle frame and
    /// the ball is below 1 m there (the engine has a height, like DFL). A turn is a pair whose
    /// headings differ by more than 20 degrees.
    ///
    /// Its cause comes from the engine's own events (<see cref="IBallEventSink"/>), not from the
    /// geometry: the highest-precedence event filed on a frame of either step (after the first
    /// frame, up to the last). A frame's position already holds every event of its ticks. A turn
    /// nothing explains has cause <see cref="BallEventKind.None"/>: a real ball does not change
    /// course untouched, so each one is a defect.
    ///
    /// In play and sent-off men as <see cref="BallFreedomAnalyzer"/>. A measuring instrument: it
    /// reads the report and the events and touches nothing. Reusable: one analyzer, one
    /// <see cref="Measure"/> per match.
    /// </summary>
    public sealed class FreeBallTurnAnalyzer
    {
        public const int StepMs = 200;
        public const int MinSpeedDmPerSecond = 50;
        public const int ClearDm = 20;
        public const int LowDm = 10;

        /// <summary>cos 20 degrees: a pair below it has turned by more.</summary>
        public const double MaxStraightCosine = 0.93969262078590838;

        private bool[] _dead = Array.Empty<bool>();
        private int[] _sentOffFrom = Array.Empty<int>();
        private int[] _eventFrames = Array.Empty<int>();

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static FreeBallTurnMetrics? Analyze(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame) =>
            new FreeBallTurnAnalyzer().Measure(report, events, ticksPerFrame);

        /// <summary>
        /// Measures one match. <paramref name="events"/> are the match's ball events in the order the
        /// sink heard them (tick order); <paramref name="ticksPerFrame"/> the stream's decimation.
        /// Returns null when the report carries no position stream.
        /// </summary>
        public FreeBallTurnMetrics? Measure(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (events == null) throw new ArgumentNullException(nameof(events));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0 || s.TicksPerMinute <= 0) return null;

            int ticks = s.TickCount, n = s.PlayerCount;
            _dead = StreamPlay.Dead(s, _dead);
            _sentOffFrom = StreamPlay.SentOffFrom(s, _sentOffFrom);
            _eventFrames = StreamPlay.Frames(events, ticksPerFrame, ticks - 1, _eventFrames);

            var m = new FreeBallTurnMetrics { FramesPerMinute = s.TicksPerMinute };
            int step = (int)Math.Round((double)StepMs * s.TicksPerMinute / 60_000);
            if (step < 1) step = 1;
            double seconds = StepMs / 1000.0, minSpeed = MinSpeedDmPerSecond;
            const long clear2 = (long)ClearDm * ClearDm;

            // The latest dead frame up to the window's end: the window is all in play when it is before the start.
            int lastDead = -1, first = 0;
            for (int t = 0; t < 2 * step && t < ticks; t++)
                if (_dead[t]) lastDead = t;

            for (int t = step; t + step < ticks; t++)
            {
                if (_dead[t + step]) lastDead = t + step;
                if (lastDead >= t - step) continue;
                if (s.BallZAt(t) >= LowDm) continue;

                int nearest = StreamPlay.Nearest(s, t, _sentOffFrom, out long nearest2);
                if (nearest < 0 || nearest2 <= clear2) continue;

                double vx0 = (s.BallXY[t * 2] - s.BallXY[(t - step) * 2]) / seconds;
                double vy0 = (s.BallXY[t * 2 + 1] - s.BallXY[(t - step) * 2 + 1]) / seconds;
                double vx1 = (s.BallXY[(t + step) * 2] - s.BallXY[t * 2]) / seconds;
                double vy1 = (s.BallXY[(t + step) * 2 + 1] - s.BallXY[t * 2 + 1]) / seconds;
                double sp0 = Math.Sqrt(vx0 * vx0 + vy0 * vy0), sp1 = Math.Sqrt(vx1 * vx1 + vy1 * vy1);
                if (sp0 <= minSpeed || sp1 <= minSpeed) continue;

                m.Steps++;
                if ((vx0 * vx1 + vy0 * vy1) / (sp0 * sp1) >= MaxStraightCosine) continue;

                while (first < events.Count && _eventFrames[first] <= t - step) first++;
                m.Add(new FreeBallTurn(t, Cause(events, first, t + step), nearest < n, nearest % n,
                    (int)Math.Round(Math.Sqrt(nearest2))));
            }

            return m;
        }

        /// <summary>The highest-precedence event from <paramref name="first"/> filed on or before <paramref name="lastFrame"/>.</summary>
        private BallEventKind Cause(IReadOnlyList<BallEvent> events, int first, int lastFrame)
        {
            BallEventKind cause = BallEventKind.None;
            for (int i = first; i < events.Count && _eventFrames[i] <= lastFrame; i++)
                if (events[i].Kind > cause) cause = events[i].Kind;
            return cause;
        }
    }
}
