using System;
using Sim.Core.Match.Movement;

namespace Sim.Core.Match.Analysis
{
    /// <summary>
    /// The movement-feel readings of the believable-match spec (R2, against the R1 bands of
    /// <see cref="RealismReference.Movement"/>), counted off a finished match's position stream with
    /// the definitions of docs/research/football-reference.md §9.2, so the engine and the tracking
    /// data are read alike. A measuring instrument: it reads the report and touches nothing.
    ///
    /// Where the stream differs from tracking data:
    ///   * Rate: the stream holds whole frames per second (5 at TicksPerMinute 300), so every span
    ///     of the reference is the same span of time here: a 1 s step is TicksPerMinute ÷ 60 frames.
    ///   * Smoothing: the reference fits a 0.52 s Savitzky-Golay window to 25 fps tracking to remove
    ///     jitter. The engine has no jitter (it steers whole decimetres), so speed is the central
    ///     difference over ±1 frame (a 0.4 s span, the nearest to that window's slope), and
    ///     acceleration the central difference of that speed over ±1 frame.
    ///   * Open play: the convention of <see cref="ShapeMovementAnalyzer"/>: dead from a restart's
    ///     whistle, a goal or half-time until somebody holds the ball after the restart's whistle.
    ///     Like the reference, the restart delivery itself is in play.
    ///   * Outfielders: everybody but the inferred keeper and men already sent off.
    ///
    /// Readings ("player-minute" = one open-play minute of one outfielder with a speed):
    ///   * Direction agreement R: 1 s steps on a grid from frame 0, used when every frame of the
    ///     step is open play. Per side, the outfielders whose step is over 1 m; with at least six,
    ///     R = |Σ unit step vectors| ÷ their count.
    ///   * Heading reversal: two consecutive steps of one outfielder, both over 0.5 m, at more than
    ///     120° to each other (cosine &lt; -0.5).
    ///   * Speed bands: share of outfielder open-play frames by speed, lower bound inclusive.
    ///   * Acceleration: one effort per run of open-play frames with acceleration over 2 m/s² lasting
    ///     at least the reference's 13 frames at 25 fps (0.52 s), rounded up to whole frames.
    ///
    /// Reusable: keep one analyzer and call <see cref="Measure"/> per match.
    /// </summary>
    public sealed class MovementFeelAnalyzer
    {
        private const int MinMovers = 6;
        private const long MoverStep2Dm = 10 * 10;       // over 1 m in a 1 s step
        private const long TurnStep2Dm = 5 * 5;          // over 0.5 m
        // Speed over the ±1 frame span is |Δ| dm × fps ÷ 20 m/s; these are the band edges × 20.
        private static readonly long[] SpeedEdges20 = { 4, 40, 80, 110, 140 };
        // Acceleration is Δ (cm over ±1 frame) × fps² ÷ 400 m/s², so over 2 m/s² ⇔ Δ × fps² > 800.
        private const long AccelThreshold = 800;
        private const int RefAccelFrames = 13, RefFps = 25;
        private const int MaxStep2Dm = int.MaxValue / 100;

        private readonly int[] _keeper = new int[2];
        private int[] _sentOffFrom = Array.Empty<int>();
        private bool[] _open = Array.Empty<bool>();
        private int[] _openBefore = Array.Empty<int>();  // open frames before frame t
        private int[] _speedBefore = Array.Empty<int>(); // per man: speed (cm over ±1 frame) on the frame before, -1 = none
        private int[] _speedNow = Array.Empty<int>();
        private int[] _hotRun = Array.Empty<int>();
        private int[] _stepX = Array.Empty<int>();       // per man: his last 1 s step, when it was usable
        private int[] _stepY = Array.Empty<int>();
        private bool[] _hasStep = Array.Empty<bool>();
        private int _n;

        /// <summary>Measures one match. Returns null when the report carries no position stream.</summary>
        public static MovementFeelMetrics? Analyze(MatchReport report) => new MovementFeelAnalyzer().Measure(report);

        /// <summary>
        /// Measures one match. Returns null when the report carries no position stream or its rate
        /// is not a whole number of frames per second.
        /// </summary>
        public MovementFeelMetrics? Measure(MatchReport report)
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
            FindSentOff(s);
            MarkOpenPlay(s, ticks);

            var m = new MovementFeelMetrics();
            SpeedsAndAccelerations(s, ticks, fps, m);
            Steps(s, ticks, fps, m);
            m.PlayerMinutes = (double)m.PlayerFrames / s.TicksPerMinute;
            return m;
        }

        private void Ensure(int n, int ticks)
        {
            _n = n;
            if (_open.Length < ticks)
            {
                _open = new bool[ticks];
                _openBefore = new int[ticks + 1];
            }

            if (_speedNow.Length != 2 * n)
            {
                _sentOffFrom = new int[2 * n];
                _speedBefore = new int[2 * n];
                _speedNow = new int[2 * n];
                _hotRun = new int[2 * n];
                _stepX = new int[2 * n];
                _stepY = new int[2 * n];
                _hasStep = new bool[2 * n];
            }
        }

        private void FindSentOff(PositionStream s)
        {
            for (int i = 0; i < _sentOffFrom.Length; i++) _sentOffFrom[i] = int.MaxValue;
            foreach (BallAction a in s.Actions)
            {
                if (a.Kind != BallActionKind.RedCard || a.Slot < 0 || a.Slot >= _n) continue;
                int k = (a.Home ? 0 : _n) + a.Slot;
                if (a.Tick < _sentOffFrom[k]) _sentOffFrom[k] = a.Tick;
            }
        }

        /// <summary>Whether man k of a side is an outfielder still on the pitch at frame t.</summary>
        private bool Outfielder(int side, int k, int t) => k != _keeper[side] && t < _sentOffFrom[side * _n + k];

        private void MarkOpenPlay(PositionStream s, int ticks)
        {
            bool dead = false, awaitingTaker = false;
            int restartTick = -1, next = 0;
            _openBefore[0] = 0;
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
                _open[t] = !dead;
                _openBefore[t + 1] = _openBefore[t] + (dead ? 0 : 1);
            }
        }

        // ------------------------------------------------------------------ speed bands and accelerations

        /// <summary>
        /// Frame by frame: the speed at t (from t-1 to t+1) goes into its band, and the acceleration at
        /// t-1 (from the speeds at t-2 and t) extends or ends that man's run over 2 m/s².
        /// </summary>
        private void SpeedsAndAccelerations(PositionStream s, int ticks, int fps, MovementFeelMetrics m)
        {
            int minRun = (RefAccelFrames * fps + RefFps - 1) / RefFps;
            long fps2 = (long)fps * fps;
            for (int i = 0; i < _speedNow.Length; i++)
            {
                _speedBefore[i] = _speedNow[i] = -1;
                _hotRun[i] = 0;
            }

            for (int t = 1; t < ticks - 1; t++)
            for (int side = 0; side < 2; side++)
            {
                int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                for (int k = 0; k < _n; k++)
                {
                    int i = side * _n + k;
                    int speed = -1;
                    if (Outfielder(side, k, t + 1))
                    {
                        long dx = s.PlayerX(xy, t + 1, k) - s.PlayerX(xy, t - 1, k);
                        long dy = s.PlayerY(xy, t + 1, k) - s.PlayerY(xy, t - 1, k);
                        long d2 = dx * dx + dy * dy;
                        speed = MovementGeometry.Sqrt((int)Math.Min(d2, MaxStep2Dm) * 100);
                        if (_open[t]) m.AddSpeedFrame(Band(d2 * fps2));
                    }

                    bool hot = _open[t - 1] && _speedBefore[i] >= 0 && speed >= 0
                        && (speed - _speedBefore[i]) * fps2 > AccelThreshold;
                    if (hot) _hotRun[i]++;
                    else EndRun(i, minRun, m);

                    _speedBefore[i] = _speedNow[i];
                    _speedNow[i] = speed;
                }
            }

            for (int i = 0; i < _hotRun.Length; i++) EndRun(i, minRun, m);
        }

        private void EndRun(int i, int minRun, MovementFeelMetrics m)
        {
            if (_hotRun[i] >= minRun) m.Accelerations++;
            _hotRun[i] = 0;
        }

        /// <summary>The band of a speed given as (|Δ| dm over ±1 frame × fps)².</summary>
        private static int Band(long scaled2)
        {
            int band = 0;
            while (band < SpeedEdges20.Length && scaled2 >= SpeedEdges20[band] * SpeedEdges20[band]) band++;
            return band;
        }

        // ------------------------------------------------------------------ 1 s steps: agreement and reversals

        private void Steps(PositionStream s, int ticks, int fps, MovementFeelMetrics m)
        {
            for (int i = 0; i < _hasStep.Length; i++) _hasStep[i] = false;

            for (int w = 0; w + fps < ticks; w += fps)
            {
                int end = w + fps;
                bool valid = _openBefore[end + 1] - _openBefore[w] == fps + 1;
                for (int side = 0; side < 2; side++)
                {
                    int[] xy = side == 0 ? s.HomeXY : s.AwayXY;
                    double sumX = 0, sumY = 0;
                    int movers = 0;
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
                        long d2 = (long)dx * dx + (long)dy * dy;
                        if (d2 > MoverStep2Dm)
                        {
                            double len = Math.Sqrt(d2);
                            sumX += dx / len;
                            sumY += dy / len;
                            movers++;
                        }

                        if (_hasStep[i] && Reverses(_stepX[i], _stepY[i], dx, dy)) m.Reversals++;
                        _stepX[i] = dx;
                        _stepY[i] = dy;
                        _hasStep[i] = true;
                    }

                    if (movers >= MinMovers) m.AddWindow(Math.Sqrt(sumX * sumX + sumY * sumY) / movers);
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

        private static bool IsRestart(BallActionKind k) =>
            k == BallActionKind.Kickoff || k == BallActionKind.ThrowIn || k == BallActionKind.GoalKick
            || k == BallActionKind.FreeKick || k == BallActionKind.Corner || k == BallActionKind.Penalty;
    }
}
