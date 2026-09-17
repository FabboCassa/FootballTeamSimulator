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
    /// PACING (the director). Playback advances in wall-clock time (scheduler delta) and
    /// interpolates linearly between stream ticks so motion stays smooth at any frame rate,
    /// but it does NOT advance at one rate throughout. Ninety minutes compressed evenly into
    /// a few real minutes is thirty times real speed, and at thirty times real speed a shot
    /// — two or three frames of the stream — is on screen for a twentieth of a second and
    /// nobody has ever seen one. So the frames AROUND a strike, a goal, a penalty or a
    /// sending-off play at REAL TIME, and the rest of the match is compressed harder to pay
    /// for them, landing the whole thing on <see cref="TargetSecondsAt1x"/>. See
    /// <see cref="SetPacing"/>: the online live screens, which follow a clock shared with
    /// another human being, opt OUT and keep the flat rate.
    ///
    /// Shirt numbers are real <see cref="Label"/> children rather than painter text: a label
    /// centres itself in the token's own box, which is the only way a two-digit number stays
    /// inside its circle at every pitch size.
    ///
    /// The stream carries no per-tick noise, so — unlike 3.1 — nothing is smoothed on the way
    /// in: what Sim.Core produced is what is drawn.
    /// </summary>
    public sealed class MatchRenderer : VisualElement
    {
        // 90 minutes play in this many real seconds at 1x when the pacing is FLAT (no director):
        // the shared-clock live screens derive the match minute from this number — do not change
        // it without changing LiveSecondsPerMinute there too.
        public const float BaseSecondsAt1x = 180f;

        /// <summary>
        /// What a watched match costs at 1x with the director on. The whole ninety minutes lands
        /// here — slow-motion included — because the rest of the match is compressed to pay for
        /// the slow-motion. Raise it for a more watchable match and a longer sitting: 420f is a
        /// seven-minute match with the same real-time highlights.
        /// </summary>
        public const float TargetSecondsAt1x = 300f;

        /// <summary>Frames of the stream before and after a highlight that play at real time (2 frames = 1s).</summary>
        private const int HighlightLeadFrames = 8;   // 4 seconds of football before the strike
        private const int HighlightTailFrames = 6;   // 3 seconds after it

        /// <summary>
        /// The director never spends more than this share of the budget on slow-motion. A match
        /// with thirty strikes would otherwise be nothing but slow-motion and would run twice as
        /// long as the caller asked for.
        /// </summary>
        private const float MaxHighlightShare = 0.55f;

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

        /// <summary>Frames per real second that IS real time (the stream runs at 120 frames a match minute).</summary>
        private readonly float _realTimeTicksPerSecond;

        /// <summary>True on the frames the director plays at real time. Empty when the director is off.</summary>
        private bool[] _highlight = Array.Empty<bool>();

        private float _fastTicksPerSecond;   // everything that is not a highlight
        private float _slowTicksPerSecond;   // the highlights themselves
        private bool _director;
        private bool _inSlowMotion;

        // Shirt numbers, one label a man. Kept as children so the text centres itself.
        private readonly Label[] _homeNumbers;
        private readonly Label[] _awayNumbers;

        /// <summary>Token radius the labels were last sized for; -1 forces the first write.</summary>
        private float _numberRadius = -1f;

        // The live figures and where the count has got to (see MatchLiveStats).
        private MatchLiveStats _stats;
        private int _statsTick = -1;

        /// <summary>A strike of this side's that nothing has settled yet — see <see cref="CountAction"/>.</summary>
        private bool _homeShotPending;
        private bool _awayShotPending;

        /// <summary>
        /// The frame the referee blew for half-time on (int.MaxValue when the stream has no interval —
        /// a replay stored before engine phase 5). From this frame on the picture is MIRRORED, which
        /// is how the two sides change ends (Law 7).
        ///
        /// The simulation does not swap them: the pitch is symmetric, home advantage is a strength
        /// bonus and not a place, and every part of the model carries its own attacking direction, so
        /// flipping the two sides in Sim.Core would change no football and would oblige every consumer
        /// of the stream — the analyzer, the harness's dump, this renderer — to flip back. Changing
        /// ends is therefore a property of the PICTURE, and it lives here: one rotation of the pitch
        /// through 180 degrees, applied in the two methods every drawn point goes through.
        /// </summary>
        private readonly int _secondHalfFrom = int.MaxValue;

        private IVisualElementScheduledItem _pump;
        private float _tickPos;
        private float _speed = 1f;
        private int _nextEvent;
        private int _nextAction;
        private int _lastMinute = -1;
        private bool _finished;

        /// <summary>Fired when the displayed clock minute changes (0..90).</summary>
        public event Action<int> MinuteChanged;

        /// <summary>Fired as playback crosses an event's tick (for toasts/score).</summary>
        public event Action<MatchEvent> EventReached;

        /// <summary>Fired as playback crosses a ball action — the commentary feed (13.1).</summary>
        public event Action<BallAction> ActionReached;

        /// <summary>Fired when the live figures move, so a panel can redraw without polling.</summary>
        public event Action<MatchLiveStats> StatsChanged;

        /// <summary>Fired when playback enters or leaves real-time slow-motion.</summary>
        public event Action<bool> SlowMotionChanged;

        /// <summary>Fired once when playback reaches full time (or on Skip).</summary>
        public event Action Finished;

        /// <summary>Shirt numbers by lineup slot, so a caller can name a player it cannot look up.</summary>
        public int[] HomeShirts => _stream.HomeShirts;
        public int[] AwayShirts => _stream.AwayShirts;
        public int[] HomePlayerIds => _stream.HomePlayerIds;
        public int[] AwayPlayerIds => _stream.AwayPlayerIds;

        /// <summary>The figures of the match SO FAR — never of the whole match (see MatchLiveStats).</summary>
        public MatchLiveStats Stats => _stats;

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
            _realTimeTicksPerSecond = _ticksPerMinute / 60f;

            foreach (BallAction action in _actions)
                if (action.Kind == BallActionKind.HalfTime)
                {
                    _secondHalfFrom = action.Tick;
                    break;
                }

            _homeNumbers = BuildNumbers(_stream.HomeShirts, _homeNumberColor, _homeColor, _homeKeeperColor);
            _awayNumbers = BuildNumbers(_stream.AwayShirts, _awayNumberColor, _awayColor, _awayKeeperColor);

            SetPacing(BaseSecondsAt1x, director: false);

            // Fill the host container (its alignItems must not shrink us to content).
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutNumbers());
        }

        /// <summary>True when there is something to play (a stripped report has nothing).</summary>
        public bool HasStream => _lastTick > 0 && _players > 0;

        /// <summary>True while the director is holding playback at real time.</summary>
        public bool InSlowMotion => _inSlowMotion;

        // ------------------------------------------------------------- pacing

        /// <summary>
        /// How long the match should take at 1x, and whether the director may spend part of that
        /// budget running the strikes at real time.
        ///
        /// <paramref name="director"/> MUST stay false on the shared-clock live screens: there the
        /// displayed minute is agreed with another client from wall-clock time, and a renderer that
        /// slowed down for a shot would drift away from it. A finished replay — the career watch
        /// screen, a stored replay — answers to nobody's clock and gets the director.
        /// </summary>
        public void SetPacing(float targetSecondsAt1x, bool director)
        {
            float target = targetSecondsAt1x > 1f ? targetSecondsAt1x : BaseSecondsAt1x;
            _director = director && HasStream;

            if (!_director)
            {
                _highlight = Array.Empty<bool>();
                _fastTicksPerSecond = _lastTick > 0 ? _lastTick / target : 0f;
                _slowTicksPerSecond = _fastTicksPerSecond;
                SetSlowMotion(false);
                return;
            }

            _highlight = BuildHighlights();

            int slowFrames = 0;
            for (int i = 0; i < _highlight.Length; i++)
                if (_highlight[i])
                    slowFrames++;

            int fastFrames = (_lastTick + 1) - slowFrames;

            // What the highlights would cost at true real time, capped so they cannot eat the match.
            float slowSeconds = slowFrames / _realTimeTicksPerSecond;
            float slowBudget = target * MaxHighlightShare;
            if (slowSeconds > slowBudget)
                slowSeconds = slowBudget;

            float fastSeconds = target - slowSeconds;
            _slowTicksPerSecond = slowSeconds > 0f ? slowFrames / slowSeconds : _realTimeTicksPerSecond;
            _fastTicksPerSecond = fastSeconds > 0f ? fastFrames / fastSeconds : _lastTick / target;

            // A pathological stream (all highlight, or none) must never stall playback.
            if (_slowTicksPerSecond <= 0f) _slowTicksPerSecond = _realTimeTicksPerSecond;
            if (_fastTicksPerSecond <= 0f) _fastTicksPerSecond = _lastTick / target;
        }

        /// <summary>
        /// The frames worth watching: a strike, the goal it becomes, a penalty, a sending-off —
        /// with the run-up to it, which is where the football actually is.
        ///
        /// Saves, misses and blocks are deliberately NOT in the list: each one lands within a frame
        /// or two of the shot that caused it and is already inside that shot's window. Corners and
        /// free kicks are left out too — there are twenty-odd a match and taking them at real time
        /// would spend the whole budget on restarts.
        /// </summary>
        private bool[] BuildHighlights()
        {
            var flags = new bool[_lastTick + 1];

            foreach (BallAction a in _actions)
            {
                if (a.Kind != BallActionKind.Shot && a.Kind != BallActionKind.Goal
                    && a.Kind != BallActionKind.Penalty && a.Kind != BallActionKind.RedCard)
                    continue;

                int from = Mathf.Max(0, a.Tick - HighlightLeadFrames);
                int to = Mathf.Min(_lastTick, a.Tick + HighlightTailFrames);

                // Never run the window across the change of ends: the two halves are drawn in
                // mirrored coordinates and the slow-motion would be of the mirror, not the ball.
                if (SecondHalf(a.Tick) && from < _secondHalfFrom) from = _secondHalfFrom;
                if (!SecondHalf(a.Tick) && to >= _secondHalfFrom) to = _secondHalfFrom - 1;

                for (int t = from; t <= to; t++)
                    flags[t] = true;
            }

            return flags;
        }

        private bool IsHighlight(int tick) =>
            _highlight.Length > 0 && tick >= 0 && tick < _highlight.Length && _highlight[tick];

        // ------------------------------------------------------------- playback

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
            RecountStats();
            LayoutNumbers();
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
            RecountStats();
            LayoutNumbers();
            MarkDirtyRepaint();
            Finish();
        }

        private void OnPump(TimerState ts)
        {
            if (_finished)
                return;

            float dt = Mathf.Min(ts.deltaTime / 1000f, 0.1f); // clamp long stalls
            int at = Mathf.Clamp(Mathf.FloorToInt(_tickPos), 0, _lastTick);
            bool slow = IsHighlight(at);
            SetSlowMotion(slow);

            _tickPos += dt * (slow ? _slowTicksPerSecond : _fastTicksPerSecond) * _speed;
            if (_tickPos > _lastTick)
                _tickPos = _lastTick;

            bool statsMoved = AdvancePossession();

            while (_nextEvent < _events.Count && _events[_nextEvent].Minute * _ticksPerMinute <= _tickPos)
                EventReached?.Invoke(_events[_nextEvent++]);

            while (_nextAction < _actions.Count && _actions[_nextAction].Tick <= _tickPos)
            {
                BallAction action = _actions[_nextAction++];
                statsMoved |= CountAction(action);
                ActionReached?.Invoke(action);
            }

            int minute = Mathf.Min(90, Mathf.FloorToInt(_tickPos / _ticksPerMinute));
            if (minute != _lastMinute)
                statsMoved = true; // the possession share is worth a redraw once a minute
            SetMinute(minute);

            if (statsMoved)
                StatsChanged?.Invoke(_stats);

            LayoutNumbers();
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

        private void SetSlowMotion(bool slow)
        {
            if (slow == _inSlowMotion)
                return;
            _inSlowMotion = slow;
            SlowMotionChanged?.Invoke(slow);
        }

        private void Finish()
        {
            if (_finished)
                return;
            _finished = true;
            _pump?.Pause();
            SetSlowMotion(false);
            Finished?.Invoke();
        }

        // ------------------------------------------------------------- live figures

        /// <summary>Adds the frames crossed since the last pump to the possession count.</summary>
        private bool AdvancePossession()
        {
            int upTo = Mathf.Clamp(Mathf.FloorToInt(_tickPos), 0, _lastTick);
            if (upTo <= _statsTick)
                return false;

            int[] owner = _stream.Owner;
            if (owner == null || owner.Length <= upTo)
            {
                _statsTick = upTo;
                return false;
            }

            for (int t = _statsTick + 1; t <= upTo; t++)
                AddOwnerFrame(owner[t]);

            _statsTick = upTo;
            return true;
        }

        private void AddOwnerFrame(int code)
        {
            if (code == PositionStream.NoOwner)
                return;

            _stats.OwnedFrames++;
            if (_stream.TryOwner(code, out bool home, out _))
            {
                if (home) _stats.Home.PossessionFrames++;
                else _stats.Away.PossessionFrames++;
            }
        }

        /// <summary>
        /// Folds one action into the live figures. Returns true when something moved.
        ///
        /// Which side an action belongs to is not uniform, and this is the one place that knows it:
        /// a shot, a goal and a miss are the ATTACKER's, a save and a block are the DEFENDER's — so
        /// a save is a shot on target for the other side, and a block is neither on target nor off
        /// it, which is what football counts.
        ///
        /// A strike counts ON TARGET when the thing that settles it is a goal or a save, which is
        /// <see cref="Sim.Core.Match.Analysis.MatchStatsBuilder"/>'s rule read FORWARDS: the builder
        /// looks ahead from the shot, and this waits for the same action to arrive — so the strip at
        /// full time carries the same figures as the match report, without ever looking at a frame
        /// the watcher has not reached. A goal the stream records without a strike of its own is a
        /// goal and not a shot, again exactly as the builder counts it.
        /// </summary>
        private bool CountAction(BallAction a)
        {
            switch (a.Kind)
            {
                case BallActionKind.Shot:
                    Side(a.Home).Shots++;
                    SetShotPending(a.Home, true);
                    return true;

                case BallActionKind.Goal:
                    Side(a.Home).Goals++;
                    if (ShotPending(a.Home))
                    {
                        Side(a.Home).ShotsOnTarget++;
                        SetShotPending(a.Home, false);
                    }

                    return true;

                case BallActionKind.Save:
                    // The keeper's side is the one that did NOT shoot.
                    if (ShotPending(!a.Home))
                    {
                        Side(!a.Home).ShotsOnTarget++;
                        SetShotPending(!a.Home, false);
                    }

                    return true;

                case BallActionKind.Miss:
                    SetShotPending(a.Home, false);   // wide: settled, and not on target
                    return false;

                case BallActionKind.Block:
                    SetShotPending(!a.Home, false);  // the blocker's side is recorded, not the striker's
                    return false;

                case BallActionKind.Corner:
                    Side(a.Home).Corners++;
                    return true;

                case BallActionKind.Foul:
                    Side(a.Home).Fouls++;
                    return true;

                case BallActionKind.YellowCard:
                    Side(a.Home).YellowCards++;
                    return true;

                case BallActionKind.RedCard:
                    Side(a.Home).RedCards++;
                    return true;

                case BallActionKind.Offside:
                    Side(a.Home).Offsides++;
                    return true;

                default:
                    return false;
            }
        }

        private ref TeamLiveStats Side(bool home)
        {
            if (home)
                return ref _stats.Home;
            return ref _stats.Away;
        }

        private bool ShotPending(bool home) => home ? _homeShotPending : _awayShotPending;

        private void SetShotPending(bool home, bool pending)
        {
            if (home) _homeShotPending = pending;
            else _awayShotPending = pending;
        }

        /// <summary>Rebuilds the figures from frame 0 up to where playback now is (a seek, or Skip).</summary>
        private void RecountStats()
        {
            _stats = default;
            _statsTick = -1;
            _homeShotPending = false;
            _awayShotPending = false;

            foreach (BallAction a in _actions)
            {
                if (a.Tick > _tickPos)
                    break;
                CountAction(a);
            }

            AdvancePossession();
            StatsChanged?.Invoke(_stats);
        }

        // ------------------------------------------------------------- shirt numbers

        private Label[] BuildNumbers(int[] shirts, Color numberColor, Color kit, Color keeperKit)
        {
            int count = _players > 0 ? _players : 0;
            var labels = new Label[count];

            for (int i = 0; i < count; i++)
            {
                string text = shirts != null && i < shirts.Length ? shirts[i].ToString() : string.Empty;
                var label = new Label(text);
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.marginLeft = 0;
                label.style.marginRight = 0;
                label.style.marginTop = 0;
                label.style.marginBottom = 0;
                label.style.paddingLeft = 0;
                label.style.paddingRight = 0;
                label.style.paddingTop = 0;
                label.style.paddingBottom = 0;

                // The label's box IS the token's box, so middle-centre is dead centre of the circle
                // whatever the number's width — which is what painter text could never promise.
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = IsKeeper(shirts, i) ? ReadableOn(keeperKit) : numberColor;
                label.style.display = DisplayStyle.None;
                labels[i] = label;
                Add(label);
            }

            return labels;
        }

        /// <summary>Puts every number back on top of its token. Called on every pump and on resize.</summary>
        private void LayoutNumbers()
        {
            Rect r = contentRect;
            if (!HasStream || float.IsNaN(r.width) || r.width <= 1f || r.height <= 1f)
                return;

            Rect fit = PitchGraphics.FitRect(r.width, r.height);
            float scale = fit.width / PitchGraphics.LengthDm;
            float radius = Mathf.Max(4f, TokenRadiusDm * scale);

            int ta = Mathf.Clamp(Mathf.FloorToInt(_tickPos), 0, _lastTick);
            int tb = Mathf.Min(ta + 1, _lastTick);
            float f = Mathf.Clamp01(_tickPos - ta);

            LayoutSide(_homeNumbers, _stream.HomeXY, _stream.HomeShirts, fit, radius, ta, tb, f);
            LayoutSide(_awayNumbers, _stream.AwayXY, _stream.AwayShirts, fit, radius, ta, tb, f);
            _numberRadius = radius;
        }

        private void LayoutSide(
            Label[] labels, int[] side, int[] shirts, Rect fit, float radius, int ta, int tb, float f)
        {
            // Below this the circle is a dot and a digit inside it would be a smudge; the token alone
            // has to do, exactly as the painter version did.
            bool readable = radius >= 7f;

            // The box and the font only change when the pitch is resized, so they are written once
            // per resize and not sixty times a second for twenty-two labels.
            bool resized = !Mathf.Approximately(radius, _numberRadius);
            float box = radius * 2f;

            for (int i = 0; i < labels.Length; i++)
            {
                Label label = labels[i];
                if (!readable)
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }

                if (resized)
                {
                    int digits = shirts != null && i < shirts.Length && shirts[i] >= 10 ? 2 : 1;
                    label.style.display = DisplayStyle.Flex;
                    label.style.width = box;
                    label.style.height = box;

                    // One digit can fill the circle; two have to share its width, so they step down.
                    label.style.fontSize = digits >= 2 ? radius * 0.95f : radius * 1.25f;
                }

                Vector2 pos = Lerp(fit, side, ta, tb, i, f);
                label.style.left = pos.x - radius;
                label.style.top = pos.y - radius;
            }
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

            DrawTeam(p, fit, scale, _stream.HomeXY, ta, tb, f, true,
                owned && ownerHome ? ownerSlot : -1);
            DrawTeam(p, fit, scale, _stream.AwayXY, ta, tb, f, false,
                owned && !ownerHome ? ownerSlot : -1);

            DrawBall(p, fit, scale, ta, tb, f);
        }

        private void DrawTeam(
            Painter2D p, Rect fit, float scale,
            int[] side, int ta, int tb, float f, bool home, int carrier)
        {
            float radius = Mathf.Max(4f, TokenRadiusDm * scale);
            Color fill = home ? _homeColor : _awayColor;
            Color keeper = home ? _homeKeeperColor : _awayKeeperColor;
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
            }
        }

        /// <summary>Shirt 1 is the keeper; the stream hands numbers over so this needs no lineup.</summary>
        private static bool IsKeeper(int[] shirts, int slot) =>
            shirts != null && slot < shirts.Length && shirts[slot] == 1;

        /// <summary>A short fading tail behind the ball, so a pass reads as a pass.</summary>
        private void DrawTrail(Painter2D p, Rect fit, int ta)
        {
            int from = Mathf.Max(0, ta - TrailTicks);

            // A trail drawn across the change of ends is a stripe across the pitch: it is the
            // mirror, not the ball.
            if (SecondHalf(ta) && from < _secondHalfFrom)
                from = _secondHalfFrom;
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
            Vector2 pos = SecondHalf(ta) != SecondHalf(tb)
                ? BallPixel(fit, tb)
                : Vector2.Lerp(BallPixel(fit, ta), BallPixel(fit, tb), f);
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

        /// <summary>True once the sides have changed ends (see <see cref="_secondHalfFrom"/>).</summary>
        private bool SecondHalf(int tick) => tick >= _secondHalfFrom;

        private Vector2 BallPixel(Rect fit, int tick) =>
            Pixel(fit, tick, _stream.BallXY[tick * 2], _stream.BallXY[tick * 2 + 1]);

        private Vector2 PlayerPixel(Rect fit, int[] side, int tick, int slot)
        {
            int i = (tick * _players + slot) * 2;
            return Pixel(fit, tick, side[i], side[i + 1]);
        }

        /// <summary>
        /// One point of the stream, on screen — rotated through 180 degrees in the second half, which
        /// is the change of ends. Both axes turn, because that is what swapping ends IS: the same
        /// football seen from the other touchline.
        /// </summary>
        private Vector2 Pixel(Rect fit, int tick, int xDm, int yDm) =>
            SecondHalf(tick)
                ? PitchGraphics.ToPixelDm(fit, Pitch.LengthDm - xDm, Pitch.WidthDm - yDm)
                : PitchGraphics.ToPixelDm(fit, xDm, yDm);

        private Vector2 Lerp(Rect fit, int[] side, int ta, int tb, int slot, float f)
        {
            // Never interpolate ACROSS the interval: the two frames are in mirrored coordinate
            // systems, and blending them would slide every player across the pitch for one frame.
            if (SecondHalf(ta) != SecondHalf(tb))
                return PlayerPixel(fit, side, tb, slot);

            return Vector2.Lerp(PlayerPixel(fit, side, ta, slot), PlayerPixel(fit, side, tb, slot), f);
        }

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
