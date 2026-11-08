using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>One match's headed attempts (free-ball spec R5).</summary>
    public sealed class HeadedAttemptMetrics
    {
        public int Shots { get; set; }

        /// <summary>Shots struck with the ball at or above <see cref="HeadedAttemptAnalyzer.HeadedFromDm"/>.</summary>
        public int Headed { get; set; }

        /// <summary>Headed shots whose previous action is a team-mate's cross.</summary>
        public int HeadedFromCross { get; set; }

        /// <summary>The frames of the headed shots that did not follow a cross.</summary>
        public List<int> Stray { get; } = new List<int>();
    }

    /// <summary>
    /// Headed attempts, read off the ball's height when it was struck (free-ball spec R5, issue #183):
    /// a shot is headed when the striker's touch that struck it met the ball at or above
    /// <see cref="HeadedFromDm"/>, the height the engine strikes a header from. It came from a cross
    /// when the action before it on the sheet is a team-mate's <see cref="BallActionKind.Cross"/>
    /// (a lofted cross, a corner or a crossed free kick), with nothing between. A measuring
    /// instrument: it reads the report and the events and touches nothing.
    /// </summary>
    public static class HeadedAttemptAnalyzer
    {
        public const int HeadedFromDm = MatchBall.HeadedFromDm;

        /// <summary>
        /// Measures one match. <paramref name="events"/> are its ball events in tick order and
        /// <paramref name="ticksPerFrame"/> the stream's decimation. Null when the report has no stream.
        /// </summary>
        public static HeadedAttemptMetrics? Analyze(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (ticksPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerFrame));
            PositionStream? s = report.Positions;
            if (s == null) return null;

            var m = new HeadedAttemptMetrics();
            List<BallAction> actions = s.Actions;
            int from = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                BallAction a = actions[i];
                if (a.Kind != BallActionKind.Shot) continue;
                m.Shots++;

                // The strike is filed on the frame it was struck in: the striker's last touch in it
                // before anybody of the other side touches the ball (a block, and his touch after it).
                int side = a.Home ? 0 : 1, z = -1;
                while (from < events.Count && events[from].Tick / ticksPerFrame < a.Tick) from++;
                for (int e = from; e < events.Count && events[e].Tick / ticksPerFrame == a.Tick; e++)
                {
                    BallEvent ev = events[e];
                    if (z >= 0 && ev.Side == 1 - side && ev.Kind != BallEventKind.Bounce) break;
                    if (ev.Kind == BallEventKind.Touch && ev.Side == side && ev.Slot == a.Slot) z = ev.ZDm;
                }
                if (z < HeadedFromDm) continue;

                m.Headed++;
                BallAction before = i > 0 ? actions[i - 1] : a;
                if (i > 0 && before.Kind == BallActionKind.Cross && before.Home == a.Home && before.Slot != a.Slot)
                    m.HeadedFromCross++;
                else m.Stray.Add(a.Tick);
            }

            return m;
        }
    }
}
