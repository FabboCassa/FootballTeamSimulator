using System;
using System.Collections.Generic;
using Sim.Core.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.MatchView
{
    /// <summary>
    /// Top-down 2D match renderer (task 3.1). Pure presentation: it plays back
    /// the deterministic position stream produced by Sim.Core and can never
    /// change a result (ARCHITECTURE.md §5.4).
    ///
    /// The pitch and the player/ball tokens are drawn with the UI Toolkit
    /// painter2D (vector) API inside <see cref="OnGenerateVisualContent"/>, so
    /// the renderer is just another VisualElement in the screen stack - no extra
    /// Unity scene, no per-frame GameObject churn.
    ///
    /// Playback advances in wall-clock time (scheduler delta), scaled by the
    /// current speed, and interpolates linearly between stream frames so motion
    /// stays smooth at any frame rate. No allocations happen per frame: tokens
    /// are drawn from the existing frame arrays and all maths uses stack structs.
    /// </summary>
    public sealed class MatchRenderer : VisualElement
    {
        // 90 minutes play in this many real seconds at 1x (ARCHITECTURE.md §4.3:
        // "~3-5 real minutes when watched"). 2x/4x divide it down.
        private const float BaseSecondsAt1x = 180f;
        private const int PumpIntervalMs = 16; // ~60 fps

        // Geometry in decimetres (Sim.Core pitch space), scaled to pixels on draw.
        private const float TokenRadiusDm = 12f;
        private const float BallRadiusDm = 6f;

        // Stream frames carry independent per-tick noise (BalanceConfig
        // PlayerNoiseDm/BallNoiseDm); a small centred moving average over each
        // trajectory removes the visual vibration without touching Sim.Core.
        private const int PlayerSmoothingRadius = 2;
        private const int BallSmoothingRadius = 3;

        private static readonly Color PitchColor = new Color(0.16f, 0.42f, 0.20f);
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color BallColor = new Color(0.97f, 0.97f, 0.97f);
        private static readonly Color BallOutline = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        private readonly Color _homeColor;
        private readonly Color _awayColor;
        private readonly List<MatchEvent> _events;
        private readonly Vector2[] _ball;    // smoothed, indexed by tick (dm space)
        private readonly Vector2[][] _home;  // [tick][player], smoothed (dm space)
        private readonly Vector2[][] _away;
        private readonly int _homeCount;
        private readonly int _awayCount;
        private readonly int _ticksPerMinute;
        private readonly int _lastTick;
        private readonly float _ticksPerSecond;

        private IVisualElementScheduledItem _pump;
        private float _tickPos;
        private float _speed = 1f;
        private int _nextEvent;
        private int _lastMinute = -1;
        private bool _finished;

        /// <summary>Fired when the displayed clock minute changes (0..90).</summary>
        public event Action<int> MinuteChanged;

        /// <summary>Fired as playback crosses an event's tick (for toasts/score).</summary>
        public event Action<MatchEvent> EventReached;

        /// <summary>Fired once when playback reaches full time (or on Skip).</summary>
        public event Action Finished;

        public MatchRenderer(MatchReport report, Color homeColor, Color awayColor)
        {
            _homeColor = homeColor;
            _awayColor = awayColor;
            _events = report?.Events ?? new List<MatchEvent>();

            PositionStream stream = report?.Positions;
            List<PositionFrame> frames = stream?.Frames ?? new List<PositionFrame>();
            _ticksPerMinute = stream != null && stream.TicksPerMinute > 0 ? stream.TicksPerMinute : 1;
            _lastTick = frames.Count > 0 ? frames.Count - 1 : 0;
            _ticksPerSecond = _lastTick > 0 ? _lastTick / BaseSecondsAt1x : 0f;

            // Precompute smoothed trajectories once (no per-frame allocation).
            _homeCount = frames.Count > 0 ? frames[0].Home.Length : 0;
            _awayCount = frames.Count > 0 ? frames[0].Away.Length : 0;
            _ball = SmoothBall(frames, BallSmoothingRadius);
            _home = SmoothTeam(frames, true, _homeCount, PlayerSmoothingRadius);
            _away = SmoothTeam(frames, false, _awayCount, PlayerSmoothingRadius);

            // Fill the host container (its alignItems must not shrink us to content).
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Starts (or resumes) playback. Safe to call once on screen enter.</summary>
        public void Play()
        {
            if (_pump == null)
                _pump = schedule.Execute(OnPump).Every(PumpIntervalMs);
            else
                _pump.Resume();

            if (_ball.Length == 0)
                Finish(); // nothing to show (e.g. a stripped report)
        }

        /// <summary>Stops the playback pump; call on screen exit to avoid a leaked ticker.</summary>
        public void Stop() => _pump?.Pause();

        /// <summary>
        /// Jumps playback to the start of <paramref name="minute"/> without firing
        /// the toasts before it (task 3.4): after a substitution/tactic change the
        /// remainder is re-simulated into a fresh renderer that resumes from the
        /// pause minute, so the already-watched first part is not replayed.
        /// </summary>
        public void SeekToMinute(int minute)
        {
            int m = minute < 0 ? 0 : (minute > 90 ? 90 : minute);
            _tickPos = Mathf.Min(m * _ticksPerMinute, _lastTick);

            _nextEvent = 0;
            while (_nextEvent < _events.Count && _events[_nextEvent].Minute <= m)
                _nextEvent++;

            _lastMinute = m;
            _finished = false;
            MarkDirtyRepaint();
        }

        /// <summary>1x / 2x / 4x.</summary>
        public void SetSpeed(float speed) => _speed = speed > 0f ? speed : 1f;

        /// <summary>Jump straight to full time without replaying the remaining toasts.</summary>
        public void Skip()
        {
            if (_finished)
                return;

            _tickPos = _lastTick;
            _nextEvent = _events.Count;
            SetMinute(90);
            MarkDirtyRepaint();
            Finish();
        }

        private void OnPump(TimerState ts)
        {
            if (_finished)
                return;

            float dt = Mathf.Min(ts.deltaTime / 1000f, 0.1f); // clamp long stalls
            _tickPos += dt * _ticksPerSecond * _speed;
            if (_tickPos > _lastTick)
                _tickPos = _lastTick;

            while (_nextEvent < _events.Count && _events[_nextEvent].Minute * _ticksPerMinute <= _tickPos)
                EventReached?.Invoke(_events[_nextEvent++]);

            SetMinute(Mathf.Min(90, Mathf.FloorToInt(_tickPos / _ticksPerMinute)));
            MarkDirtyRepaint();

            if (_tickPos >= _lastTick)
                Finish();
        }

        private void SetMinute(int minute)
        {
            if (minute == _lastMinute)
                return;
            _lastMinute = minute;
            MinuteChanged?.Invoke(minute);
        }

        private void Finish()
        {
            if (_finished)
                return;
            _finished = true;
            _pump?.Pause();
            Finished?.Invoke();
        }

        // ------------------------------------------------------------- drawing

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (_ball.Length == 0 || r.width <= 1f || r.height <= 1f)
                return;

            // Fit the 1050x680 dm pitch into the element, letterboxed and centred.
            float scale = Mathf.Min(r.width / Pitch.LengthDm, r.height / Pitch.WidthDm);
            float offX = (r.width - Pitch.LengthDm * scale) * 0.5f;
            float offY = (r.height - Pitch.WidthDm * scale) * 0.5f;

            Painter2D p = mgc.painter2D;
            DrawPitch(p, offX, offY, scale);

            // Interpolate between the two surrounding (smoothed) stream frames.
            int ta = Mathf.Clamp(Mathf.FloorToInt(_tickPos), 0, _lastTick);
            int tb = Mathf.Min(ta + 1, _lastTick);
            float f = Mathf.Clamp01(_tickPos - ta);

            DrawTeam(p, _home[ta], _home[tb], f, offX, offY, scale, _homeColor);
            DrawTeam(p, _away[ta], _away[tb], f, offX, offY, scale, _awayColor);
            DrawBall(p, _ball[ta], _ball[tb], f, offX, offY, scale);
        }

        private static void DrawPitch(Painter2D p, float offX, float offY, float scale)
        {
            float x0 = offX, y0 = offY;
            float x1 = offX + Pitch.LengthDm * scale, y1 = offY + Pitch.WidthDm * scale;
            float midX = offX + Pitch.CenterX * scale, midY = offY + Pitch.CenterY * scale;

            // Turf.
            p.fillColor = PitchColor;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();

            // Markings.
            p.strokeColor = LineColor;
            p.lineWidth = Mathf.Max(1.5f, 2f * scale);

            Rectangle(p, x0, y0, x1, y1);                       // outline
            Line(p, midX, y0, midX, y1);                        // halfway line
            Circle(p, midX, midY, 91.5f * scale, fill: false);  // centre circle (9.15m)

            // Penalty boxes (16.5m deep, 40.3m wide).
            float boxDepth = 165f * scale;
            float boxHalf = 201.5f * scale;
            Rectangle(p, x0, midY - boxHalf, x0 + boxDepth, midY + boxHalf);
            Rectangle(p, x1 - boxDepth, midY - boxHalf, x1, midY + boxHalf);
        }

        private void DrawTeam(
            Painter2D p, Vector2[] a, Vector2[] b, float f,
            float offX, float offY, float scale, Color color)
        {
            int count = Mathf.Min(a.Length, b.Length);
            float radius = TokenRadiusDm * scale;
            p.fillColor = color;
            for (int i = 0; i < count; i++)
            {
                float px = offX + Mathf.Lerp(a[i].x, b[i].x, f) * scale;
                float py = offY + Mathf.Lerp(a[i].y, b[i].y, f) * scale;
                p.BeginPath();
                p.Arc(new Vector2(px, py), radius, 0f, 360f);
                p.Fill();
            }
        }

        private static void DrawBall(
            Painter2D p, Vector2 a, Vector2 b, float f,
            float offX, float offY, float scale)
        {
            float px = offX + Mathf.Lerp(a.x, b.x, f) * scale;
            float py = offY + Mathf.Lerp(a.y, b.y, f) * scale;
            float radius = Mathf.Max(2f, BallRadiusDm * scale);

            p.fillColor = BallColor;
            p.BeginPath();
            p.Arc(new Vector2(px, py), radius, 0f, 360f);
            p.Fill();

            p.strokeColor = BallOutline;
            p.lineWidth = 1f;
            p.BeginPath();
            p.Arc(new Vector2(px, py), radius, 0f, 360f);
            p.Stroke();
        }

        // -------------------------------------------------- trajectory smoothing

        private static Vector2[] SmoothBall(List<PositionFrame> frames, int radius)
        {
            int n = frames.Count;
            var outArr = new Vector2[n];
            for (int t = 0; t < n; t++)
            {
                int lo = t - radius < 0 ? 0 : t - radius;
                int hi = t + radius >= n ? n - 1 : t + radius;
                float sx = 0f, sy = 0f;
                for (int k = lo; k <= hi; k++) { sx += frames[k].Ball.X; sy += frames[k].Ball.Y; }
                int c = hi - lo + 1;
                outArr[t] = new Vector2(sx / c, sy / c);
            }
            return outArr;
        }

        private static Vector2[][] SmoothTeam(List<PositionFrame> frames, bool home, int count, int radius)
        {
            int n = frames.Count;
            var outArr = new Vector2[n][];
            for (int t = 0; t < n; t++)
            {
                int lo = t - radius < 0 ? 0 : t - radius;
                int hi = t + radius >= n ? n - 1 : t + radius;
                int c = hi - lo + 1;
                var row = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    float sx = 0f, sy = 0f;
                    for (int k = lo; k <= hi; k++)
                    {
                        PitchPoint pt = home ? frames[k].Home[i] : frames[k].Away[i];
                        sx += pt.X; sy += pt.Y;
                    }
                    row[i] = new Vector2(sx / c, sy / c);
                }
                outArr[t] = row;
            }
            return outArr;
        }

        private static void Rectangle(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Stroke();
        }

        private static void Line(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y1));
            p.Stroke();
        }

        private static void Circle(Painter2D p, float cx, float cy, float radius, bool fill)
        {
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), radius, 0f, 360f);
            if (fill) p.Fill(); else p.Stroke();
        }
    }
}
