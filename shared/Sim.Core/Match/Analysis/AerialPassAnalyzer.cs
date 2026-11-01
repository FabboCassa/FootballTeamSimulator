using System;
using System.Collections.Generic;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The long, high and aerial event readings of free-ball spec R2/R4, on the definitions of
    /// docs/research/football-reference.md §11.3:
    ///   * A pass: every <see cref="BallActionKind.Pass"/>, <see cref="BallActionKind.LongBall"/> or
    ///     <see cref="BallActionKind.Cross"/> action, restarts' kicks included (StatsBomb counts them),
    ///     whose passer's touch the sink heard on its frame.
    ///   * Long: origin to arrival over <see cref="LongPassDm"/> (origin and arrival as
    ///     <see cref="GeometricCrossAnalyzer"/> reads them).
    ///   * High: the ball peaks at <see cref="GeometricCrossAnalyzer.HighPeakDm"/> or more on the way,
    ///     the height the harness already calls a high cross.
    ///   * Aerial duels: the headers the engine stamps as winning one. Each duel has a man of each
    ///     side, so a team-match's count (won plus lost) is the match's.
    /// A measuring instrument: it reads the report and the events and touches nothing.
    /// </summary>
    public sealed class AerialPassAnalyzer
    {
        public const int LongPassDm = 300;

        private bool[] _dead = Array.Empty<bool>();
        private int[] _eventFrames = Array.Empty<int>();

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static AerialPassMetrics? Analyze(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame) =>
            new AerialPassAnalyzer().Measure(report, events, ticksPerFrame);

        public AerialPassMetrics? Measure(MatchReport report, IReadOnlyList<BallEvent> events, int ticksPerFrame)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (events == null) throw new ArgumentNullException(nameof(events));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0 || s.TicksPerMinute <= 0) return null;

            int ticks = s.TickCount;
            _dead = StreamPlay.Dead(s, _dead);
            _eventFrames = StreamPlay.Frames(events, ticksPerFrame, ticks - 1, _eventFrames);

            var m = new AerialPassMetrics();
            foreach (BallEvent e in events)
                if (e.AerialDuel) m.AerialDuels++;

            int from = 0;
            foreach (BallAction a in s.Actions)
            {
                if (!GeometricCrossAnalyzer.IsPass(a.Kind)) continue;
                int f = a.Tick;
                while (from < events.Count && _eventFrames[from] < f) from++;
                if (f < 0 || f >= ticks) continue;

                int kick = GeometricCrossAnalyzer.Kick(events, _eventFrames, from, f, a.Home ? 0 : 1, a.Slot);
                if (kick < 0) continue;

                GeometricCrossAnalyzer.Arrival(s, _dead, _eventFrames, events, kick, f, out int endFrame, out int endX, out int endY);
                long dx = endX - events[kick].XDm, dy = endY - events[kick].YDm;
                m.Passes++;
                if (dx * dx + dy * dy > (long)LongPassDm * LongPassDm) m.LongPasses++;
                if (GeometricCrossAnalyzer.Peak(s, f, endFrame) >= GeometricCrossAnalyzer.HighPeakDm) m.HighPasses++;
            }

            return m;
        }
    }

    /// <summary>One match's passes, long and high passes, and aerial duels. See <see cref="AerialPassAnalyzer"/>.</summary>
    public sealed class AerialPassMetrics
    {
        public int Passes { get; set; }
        public int LongPasses { get; set; }
        public int HighPasses { get; set; }
        public int AerialDuels { get; set; }

        public double LongPercent => Passes == 0 ? 0 : 100.0 * LongPasses / Passes;
        public double HighPercent => Passes == 0 ? 0 : 100.0 * HighPasses / Passes;
    }
}
