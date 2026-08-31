using System;
using System.Collections.Generic;
using Fts.Views;
using Sim.Core.Match;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.MatchView
{
    /// <summary>
    /// Top-down 2D match renderer (tasks 3.1 · 13.1). Pure presentation: it plays back
    /// the deterministic movement stream produced by Sim.Core and can never change a
    /// result (ARCHITECTURE.md §5.4).
    ///
    /// Everything is drawn with the UI Toolkit painter2D (vector) API inside
    /// <see cref="OnGenerateVisualContent"/>, so the renderer is just another
    /// VisualElement in the screen stack — no extra Unity scene, no per-frame GameObject
    /// churn. The pitch itself comes from <see cref="PitchGraphics"/>, the same painter
    /// the squad and tactics screens use, so there is one pitch in the whole game.
    ///
    /// What the stream now carries, and what is drawn from it: the ball is at a player's
    /// feet (<see cref="PositionStream.Owner"/> — ringed), in flight (a line to whoever
    /// it is going to, or the trajectory of a shot), or dead on a spot. Players wear
    /// their shirt numbers. A short trail follows the ball so a pass reads as a pass.
    ///
    /// Playback advances in wall-clock time (scheduler delta), scaled by the current
    /// speed, and interpolates linearly between stream ticks so motion stays smooth at
    /// any frame rate. The stream carries no per-tick noise any more, so — unlike 3.1 —
    /// nothing is smoothed on the way in: what Sim.Core produced is what is drawn.
    /// </summary>
    public sealed class MatchRenderer : VisualElement
    {
        // 90 minutes play in this many real seconds at 1x (ARCHITECTURE.md §4.3:
        // "~3-5 real minutes when watched"). 2x/4x divide it down. The online live
        // screens derive the shared match minute from this number — do not change it
        // without changing LiveSecondsPerMinute there too.
        private const float BaseSecondsAt1x = 180f;
        private const int PumpIntervalMs = 16; // ~60 fps

        // Geometry in decimetres (Sim.Core pitch space), scaled to pixels on draw.
        private const float TokenRadiusDm = 15f;
        private const float BallRadiusDm = 7f;
        private const int TrailTicks = 8;

        private static readonly Color BallColor = new Color(0.98f, 0.98f, 0.98f);
        private static readonly Color BallOutline = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        private static readonly Color TokenOutline = new Color(0.06f, 0.07f, 0.10f, 0.85f);
        private static readonly Color CarrierRing = new Color(1f, 0.95f, 0.5f, 0.95f);
        private static readonly Color TrailColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color ShotColor = new Color(1f, 0.85f, 0.35f, 0.9f);

        private readonly Color _homeColor;
        private readonly Color _awayColor;
        private readonly Color _homeKeeperColor;
        private readonly Color _awayKeeperColor;
        private readonly Color _homeNumberColor;
        private readonly Color _awayNumberColor;

        private readonly List<MatchEvent> _events;
        private readonly PositionStream _stream;
        private readonly List<BallAction> _actions;
        private readonly int _players;
        private readonly int _ticksPerMinute;
        private readonly int _lastTick;
        private readonly float _ticksPerSecond;

        private IVisualElementScheduledItem _pump;
        private float _tickPos;
        private float _speed = 1f;
        private int _nextEvent;
        private int _nextAction;
        private int _lastMinute = -1;
        private bool _finished;
        private bool _canDrawText = true;

        /// <summary>Fired when the displayed clock minute changes (0..90).</summary>
        public event Action<int> MinuteChanged;

        /// <summary>Fired as playback crosses an event's tick (for toasts/score).</summary>
        public event Action<MatchEvent> EventReached;

        /// <summary>Fired as playback crosses a ball action — the commentary feed (13.1).</summary>
        public event Action<BallAction> ActionReached;

        /// <summary>Fired once when playback reaches full time (or on Skip).</summary>
        public event Action Finished;

        /// <summary>Shirt numbers by lineup slot, so a caller can name a player it cannot look up.</summary>
        public int[] HomeShirts => _stream.HomeShirts;
        public int[] AwayShirts => _stream.AwayShirts;
        public int[] HomePlayerIds => _stream.HomePlayerIds;
        public int[] AwayPlayerIds => _stream.AwayPlayerIds;

        public MatchRenderer(MatchReport report, Color homeColor, Color awayColor)
        {
            _homeColor = homeColor;
            _awayColor = awayColor;
            _homeKeeperColor = KeeperColor(homeColor);
            _awayKeeperColor = KeeperColor(awayColor);
            _homeNumberColor = ReadableOn(homeColor);
            _awayNumberColor = ReadableOn(awayColor);

            _events = report?.Events ?? new List<MatchEvent>();
            _stream = report?.Positions ?? new PositionStream();
            _actions = _stream.Actions ?? new List<BallAction>();
            _players = _stream.PlayerCount;
            _ticksPerMinute = _stream.TicksPerMinute > 0 ? _stream.TicksPerMinute : 1;
            _lastTick = _stream.TickCount > 0 ? _stream.TickCount - 1 : 0;
            _ticksPerSecond = _lastTick > 0 ? _lastTick / BaseSecondsAt1x : 0f;

            // Fill the host container (its alignItems must not shrink us to content).
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>True when there is something to play (a stripped report has nothing).</summary>
        public bool HasStream => _lastTick > 0 && _players > 0;

        /// <summary>Starts (or resumes) playback. Safe to call once on screen enter.</summary>
        public void Play()
        {
            if (_pump == null)
                _pump = schedule.Execute(OnPump).Every(PumpIntervalMs);
            else
                _pump.Resume();

            if (!HasStream)
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

            _nextAction = 0;
            while (_nextAction < _actions.Count && _actions[_nextAction].Tick <= _tickPos)
                _nextAction++;

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
            _nextAction = _actions.Count;
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

            while (_nextAction < _actions.Count && _actions[_nextAction].Tick <= _tickPos)
                ActionReached?.Invoke(_actions[_nextAction++]);

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
            if (!HasStream || r.width <= 1f || r.height <= 1f)
                return;

            Rect fit = PitchGraphics.FitRect(r.width, r.height);
            float scale = fit.width / PitchGraphics.LengthDm;

            Painter2D p = mgc.painter2D;
            PitchGraphics.Draw(p, fit);

            int ta = Mathf.Clamp(Mathf.FloorToInt(_tickPos), 0, _lastTick);
            int tb = Mathf.Min(ta + 1, _lastTick);
            float f = Mathf.Clamp01(_tickPos - ta);

            DrawTrail(p, fit, ta);
            DrawFlight(p, fit, ta, scale);

            _stream.TryOwner(_stream.Owner[ta], out bool ownerHome, out int ownerSlot);
            bool owned = _stream.Owner[ta] != PositionStream.NoOwner;

            DrawTeam(mgc, p, fit, scale, _stream.HomeXY, ta, tb, f, true,
                owned && ownerHome ? ownerSlot : -1);
            DrawTeam(mgc, p, fit, scale, _stream.AwayXY, ta, tb, f, false,
                owned && !ownerHome ? ownerSlot : -1);

            DrawBall(p, fit, scale, ta, tb, f);
        }

        private void DrawTeam(
            MeshGenerationContext mgc, Painter2D p, Rect fit, float scale,
            int[] side, int ta, int tb, float f, bool home, int carrier)
        {
            float radius = Mathf.Max(4f, TokenRadiusDm * scale);
            Color fill = home ? _homeColor : _awayColor;
            Color keeper = home ? _homeKeeperColor : _awayKeeperColor;
            Color numbers = home ? _homeNumberColor : _awayNumberColor;
            int[] shirts = home ? _stream.HomeShirts : _stream.AwayShirts;

            for (int i = 0; i < _players; i++)
            {
                Vector2 pos = Lerp(fit, side, ta, tb, i, f);

                if (i == carrier)
                {
                    p.strokeColor = CarrierRing;
                    p.lineWidth = Mathf.Max(1.5f, radius * 0.22f);
                    p.BeginPath();
                    p.Arc(pos, radius * 1.55f, 0f, 360f);
                    p.Stroke();
                }

                p.fillColor = IsKeeper(shirts, i) ? keeper : fill;
                p.BeginPath();
                p.Arc(pos, radius, 0f, 360f);
                p.Fill();

                p.strokeColor = TokenOutline;
                p.lineWidth = Mathf.Max(1f, radius * 0.14f);
                p.BeginPath();
                p.Arc(pos, radius, 0f, 360f);
                p.Stroke();

                DrawNumber(mgc, shirts, i, pos, radius, numbers);
            }
        }

        /// <summary>Shirt 1 is the keeper; the stream hands numbers over so this needs no lineup.</summary>
        private static bool IsKeeper(int[] shirts, int slot) =>
            shirts != null && slot < shirts.Length && shirts[slot] == 1;

        private void DrawNumber(
            MeshGenerationContext mgc, int[] shirts, int slot, Vector2 pos, float radius, Color color)
        {
            if (!_canDrawText || shirts == null || slot >= shirts.Length)
                return;

            float fontSize = radius * 1.15f;
            if (fontSize < 7f)
                return; // unreadable at this size; the token alone has to do

            string text = shirts[slot].ToString();
            var at = new Vector2(pos.x - fontSize * 0.31f * text.Length, pos.y - fontSize * 0.62f);

            try
            {
                mgc.DrawText(text, at, fontSize, color);
            }
            catch (Exception)
            {
                // No font resolved for this panel: drop numbers for the rest of the match
                // rather than throwing once per token per frame.
                _canDrawText = false;
            }
        }

        /// <summary>A short fading tail behind the ball, so a pass reads as a pass.</summary>
        private void DrawTrail(Painter2D p, Rect fit, int ta)
        {
            int from = Mathf.Max(0, ta - TrailTicks);
            if (ta - from < 2)
                return;

            for (int t = from; t < ta; t++)
            {
                float age = (float)(t - from) / (ta - from);
                p.strokeColor = new Color(TrailColor.r, TrailColor.g, TrailColor.b, TrailColor.a * age * 0.8f);
                p.lineWidth = Mathf.Max(1f, 2.5f * age);
                p.BeginPath();
                p.MoveTo(BallPixel(fit, t));
                p.LineTo(BallPixel(fit, t + 1));
                p.Stroke();
            }
        }

        /// <summary>The line of a pass in the air, or the trajectory of a shot.</summary>
        private void DrawFlight(Painter2D p, Rect fit, int ta, float scale)
        {
            if (_stream.Owner[ta] != PositionStream.NoOwner)
                return;

            int index = _nextAction - 1;
            if (index < 0 || index >= _actions.Count)
                return;

            BallAction a = _actions[index];
            if (ta - a.Tick > _ticksPerMinute)
                return;

            int[] side = a.Home ? _stream.HomeXY : _stream.AwayXY;
            Vector2 ball = BallPixel(fit, ta);

            if (a.Kind == BallActionKind.Shot)
            {
                p.strokeColor = ShotColor;
                p.lineWidth = Mathf.Max(1.5f, 4f * scale);
                p.BeginPath();
                p.MoveTo(PlayerPixel(fit, side, a.Tick, a.Slot));
                p.LineTo(ball);
                p.Stroke();
                return;
            }

            if (a.TargetSlot < 0 || a.TargetSlot >= _players)
                return;

            Color line = a.Home ? _homeColor : _awayColor;
            p.strokeColor = new Color(line.r, line.g, line.b, 0.55f);
            p.lineWidth = Mathf.Max(1f, 2.5f * scale);
            p.BeginPath();
            p.MoveTo(ball);
            p.LineTo(PlayerPixel(fit, side, ta, a.TargetSlot));
            p.Stroke();
        }

        private void DrawBall(Painter2D p, Rect fit, float scale, int ta, int tb, float f)
        {
            Vector2 pos = Vector2.Lerp(BallPixel(fit, ta), BallPixel(fit, tb), f);
            float radius = Mathf.Max(2.5f, BallRadiusDm * scale);

            p.fillColor = BallColor;
            p.BeginPath();
            p.Arc(pos, radius, 0f, 360f);
            p.Fill();

            p.strokeColor = BallOutline;
            p.lineWidth = 1f;
            p.BeginPath();
            p.Arc(pos, radius, 0f, 360f);
            p.Stroke();
        }

        // ------------------------------------------------------------- coordinates

        private Vector2 BallPixel(Rect fit, int tick) =>
            PitchGraphics.ToPixelDm(fit, _stream.BallXY[tick * 2], _stream.BallXY[tick * 2 + 1]);

        private Vector2 PlayerPixel(Rect fit, int[] side, int tick, int slot)
        {
            int i = (tick * _players + slot) * 2;
            return PitchGraphics.ToPixelDm(fit, side[i], side[i + 1]);
        }

        private Vector2 Lerp(Rect fit, int[] side, int ta, int tb, int slot, float f) =>
            Vector2.Lerp(PlayerPixel(fit, side, ta, slot), PlayerPixel(fit, side, tb, slot), f);

        // ------------------------------------------------------------- colours

        /// <summary>A darkened kit for the keeper, so he reads apart from his ten team-mates.</summary>
        private static Color KeeperColor(Color kit) =>
            Color.Lerp(kit, new Color(0.10f, 0.11f, 0.14f), 0.55f);

        /// <summary>Black or white, whichever is legible on the kit colour.</summary>
        private static Color ReadableOn(Color kit) =>
            kit.r * 0.299f + kit.g * 0.587f + kit.b * 0.114f > 0.55f
                ? new Color(0.08f, 0.09f, 0.12f)
                : Color.white;
    }
}
