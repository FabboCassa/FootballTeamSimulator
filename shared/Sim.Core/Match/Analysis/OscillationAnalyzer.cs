using System;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The oscillation reading of the free-ball spec (R2, against the R1 band
    /// <see cref="RealismReference.Tracking.ReversalsPerPlayerMinute"/>): per player, his direction
    /// reversals of more than 120° within 2 s, per player-minute. The stream carries no movement
    /// targets and the tracking data neither, so a reversal is read off the positions with the
    /// heading-reversal definition of docs/research/football-reference.md §9.2, exactly as
    /// <see cref="MovementFeelAnalyzer"/> reads it: two consecutive 1 s steps of one outfielder
    /// (2 s), both over 0.5 m, at more than 120° to each other (cosine &lt; -0.5). What this adds
    /// is the split by man, so one oscillating player is not averaged away by twenty steady ones.
    ///
    /// Same frames as <see cref="MovementFeelAnalyzer"/>: 1 s steps on a grid from frame 0, used
    /// when every frame of the step is open play (<see cref="OpenPlayFrames"/>); outfielders are
    /// everybody but the inferred keeper and men already sent off; a player-minute is one open-play
    /// minute of an outfielder with a speed (frames 1 to the second-last).
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class OscillationAnalyzer
    {
        private const long TurnStep2Dm = 5 * 5;          // over 0.5 m

        private readonly int[] _keeper = new int[2];
        private bool[] _open = Array.Empty<bool>();
        private int[] _sentOffFrom = Array.Empty<int>();
        private int[] _stepX = Array.Empty<int>();
        private int[] _stepY = Array.Empty<int>();
        private bool[] _hasStep = Array.Empty<bool>();
        private int[] _reversals = Array.Empty<int>();
        private int[] _frames = Array.Empty<int>();
        private int _n;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static OscillationMetrics? Analyze(MatchReport report) => new OscillationAnalyzer().Measure(report);

        /// <summary>
        /// Measures one match. Returns null when the report carries no position stream or its rate
        /// is not a whole number of frames per second.
        /// </summary>
        public OscillationMetrics? Measure(MatchReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            PositionStream? s = report.Positions;
            if (s == null || s.PlayerCount <= 0 || s.TickCount <= 0) return null;
            if (s.TicksPerMinute < 60 || s.TicksPerMinute % 60 != 0) return null;

            int ticks = s.TickCount;
            int fps = s.TicksPerMinute / 60;
            Ensure(s.PlayerCount, ticks);
            _keeper[0] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: true);
            _keeper[1] = MatchAnalyzer.InferKeeperSlot(s, ticks, home: false);
            OpenPlayFrames.Mark(s, ticks, _open);
            OpenPlayFrames.SentOff(s, _sentOffFrom);

            CountFrames(ticks);
            CountReversals(s, ticks, fps);

            var m = new OscillationMetrics();
            for (int side = 0; side < 2; side++)
            for (int k = 0; k < _n; k++)
            {
                int i = side * _n + k;
                if (_frames[i] == 0) continue;
                m.Add(new PlayerOscillation(side == 0, k, _reversals[i], (double)_frames[i] / s.TicksPerMinute));
            }

            return m;
        }

        private void Ensure(int n, int ticks)
        {
            _n = n;
            if (_open.Length < ticks) _open = new bool[ticks];
            if (_stepX.Length != 2 * n)
            {
                _sentOffFrom = new int[2 * n];
                _stepX = new int[2 * n];
                _stepY = new int[2 * n];
                _hasStep = new bool[2 * n];
                _reversals = new int[2 * n];
                _frames = new int[2 * n];
            }

            for (int i = 0; i < 2 * n; i++)
            {
                _hasStep[i] = false;
                _reversals[i] = _frames[i] = 0;
            }
        }

        /// <summary>Whether man k of a side is an outfielder still on the pitch at frame t.</summary>
        private bool Outfielder(int side, int k, int t) => k != _keeper[side] && t < _sentOffFrom[side * _n + k];

        /// <summary>Player-minutes as <see cref="MovementFeelAnalyzer"/> counts them: open frames with a ±1 frame speed.</summary>
        private void CountFrames(int ticks)
        {
            for (int t = 1; t < ticks - 1; t++)
            {
                if (!_open[t]) continue;
                for (int side = 0; side < 2; side++)
                for (int k = 0; k < _n; k++)
                    if (Outfielder(side, k, t + 1)) _frames[side * _n + k]++;
            }
        }

        private void CountReversals(PositionStream s, int ticks, int fps)
        {
            for (int w = 0; w + fps < ticks; w += fps)
            {
                int end = w + fps;
                bool valid = true;
                for (int t = w; t <= end && valid; t++) valid = _open[t];

                for (int side = 0; side < 2; side++)
                {
                    int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                    for (int k = 0; k < _n; k++)
                    {
                        int i = side * _n + k;
                        if (!valid || !Outfielder(side, k, end))
                        {
                            _hasStep[i] = false;
                            continue;
                        }

                        int dx = s.PlayerX(xy, end, k) - s.PlayerX(xy, w, k);
                        int dy = s.PlayerY(xy, end, k) - s.PlayerY(xy, w, k);
                        if (_hasStep[i] && Reverses(_stepX[i], _stepY[i], dx, dy)) _reversals[i]++;
                        _stepX[i] = dx;
                        _stepY[i] = dy;
                        _hasStep[i] = true;
                    }
                }
            }
        }

        /// <summary>Both steps over 0.5 m and at more than 120° to each other: cos &lt; -0.5 ⇔ dot &lt; 0 and 4·dot² &gt; |a|²|b|².</summary>
        private static bool Reverses(long ax, long ay, long bx, long by)
        {
            long a2 = ax * ax + ay * ay, b2 = bx * bx + by * by;
            if (a2 <= TurnStep2Dm || b2 <= TurnStep2Dm) return false;
            long dot = ax * bx + ay * by;
            return dot < 0 && 4 * dot * dot > a2 * b2;
        }
    }
}
