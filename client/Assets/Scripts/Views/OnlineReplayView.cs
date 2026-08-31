using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Chrome for a downloaded league-match replay (task 8.3b): a score+clock HUD, the pitch area (the
    /// presenter drops the MatchRenderer in), an event toast, and speed/skip/close controls. Unlike the
    /// career MatchWatchView there is no pause/intervention — a replay is read-only, so Close is always
    /// available. Dumb view: raises events, renders what the presenter sets.
    /// </summary>
    public sealed class OnlineReplayView
    {
        private static readonly Color BarColor = new Color(0.07f, 0.11f, 0.20f);
        private static readonly Color ToastColor = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color ActiveSpeed = new Color(0.20f, 0.55f, 0.30f);
        private static readonly Color IdleSpeed = new Color(0.20f, 0.24f, 0.34f);

        public event Action<float> SpeedClicked;
        public event Action SkipClicked;
        public event Action CloseClicked;

        public VisualElement Root { get; }
        public VisualElement PitchContainer { get; }

        private readonly Label _score;
        private readonly Label _clock;
        private readonly Label _toast;
        private readonly ActionFeed _feed = new ActionFeed();
        private readonly Label _status;
        private readonly Button[] _speedButtons;
        private readonly float[] _speeds = { 1f, 2f, 4f };
        private IVisualElementScheduledItem _toastHide;

        public OnlineReplayView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;

            // HUD
            var hud = new VisualElement();
            hud.style.flexDirection = FlexDirection.Row;
            hud.style.justifyContent = Justify.Center;
            hud.style.alignItems = Align.Center;
            hud.style.backgroundColor = BarColor;
            hud.style.paddingTop = 10;
            hud.style.paddingBottom = 10;
            _score = new Label(string.Empty);
            _score.style.fontSize = 22;
            _score.style.unityFontStyleAndWeight = FontStyle.Bold;
            _score.style.color = Color.white;
            _score.style.marginRight = 18;
            hud.Add(_score);
            _clock = new Label("0'");
            _clock.style.fontSize = 18;
            _clock.style.color = new Color(1f, 1f, 1f, 0.8f);
            _clock.style.minWidth = 44;
            hud.Add(_clock);
            Root.Add(hud);

            // Pitch + toast overlay
            PitchContainer = new VisualElement();
            PitchContainer.style.flexGrow = 1f;

            var toastRow = new VisualElement();
            toastRow.style.position = Position.Absolute;
            toastRow.style.left = 0;
            toastRow.style.right = 0;
            toastRow.style.top = 12;
            toastRow.style.flexDirection = FlexDirection.Row;
            toastRow.style.justifyContent = Justify.Center;
            toastRow.pickingMode = PickingMode.Ignore;
            _toast = new Label(string.Empty);
            _toast.style.color = Color.white;
            _toast.style.fontSize = 16;
            _toast.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toast.style.backgroundColor = ToastColor;
            _toast.style.paddingLeft = 12;
            _toast.style.paddingRight = 12;
            _toast.style.paddingTop = 6;
            _toast.style.paddingBottom = 6;
            _toast.style.display = DisplayStyle.None;
            toastRow.Add(_toast);
            PitchContainer.Add(toastRow);
            PitchContainer.Add(_feed.Root);
            _feed.Clear();
            Root.Add(PitchContainer);

            _status = new Label(string.Empty);
            _status.style.color = UiKit.TextMuted;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.paddingTop = 6;
            _status.style.paddingBottom = 6;
            _status.style.display = DisplayStyle.None;
            Root.Add(_status);

            // Controls
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = BarColor;
            bar.style.paddingTop = 8;
            bar.style.paddingBottom = 8;

            string[] speedKeys = { "match.speed_1x", "match.speed_2x", "match.speed_4x" };
            _speedButtons = new Button[_speeds.Length];
            for (int i = 0; i < _speeds.Length; i++)
            {
                float speed = _speeds[i];
                var b = new Button(() => SpeedClicked?.Invoke(speed)) { text = tr(speedKeys[i]) };
                Small(b);
                _speedButtons[i] = b;
                bar.Add(b);
            }

            var skip = new Button(() => SkipClicked?.Invoke()) { text = tr("match.skip") };
            Small(skip);
            skip.style.marginLeft = 16;
            bar.Add(skip);

            var close = new Button(() => CloseClicked?.Invoke()) { text = tr("common.back") };
            Small(close);
            close.style.marginLeft = 16;
            close.style.width = 140;
            bar.Add(close);

            Root.Add(bar);
        }

        public void SetScore(string score) => _score.text = score;
        public void SetClock(string clock) => _clock.text = clock;

        public void SetActiveSpeed(float speed)
        {
            for (int i = 0; i < _speeds.Length; i++)
                _speedButtons[i].style.backgroundColor =
                    Mathf.Approximately(_speeds[i], speed) ? ActiveSpeed : IdleSpeed;
        }

        /// <summary>Adds a line to the running commentary beside the pitch (task 13.1).</summary>
        public void PushAction(string text) => _feed.Push(text);

        /// <summary>Empties the commentary (a re-simulated remainder starts fresh).</summary>
        public void ClearActions() => _feed.Clear();

        public void ShowToast(string text)
        {
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.style.display = DisplayStyle.None).StartingIn(2500);
        }

        public void ShowStatus(string message)
        {
            _status.text = message;
            _status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void Small(Button b)
        {
            b.style.height = 44;
            b.style.minWidth = 56;
            b.style.fontSize = 18;
            b.style.marginLeft = 4;
            b.style.marginRight = 4;
            b.style.color = Color.white;
            b.style.backgroundColor = IdleSpeed;
        }
    }
}
