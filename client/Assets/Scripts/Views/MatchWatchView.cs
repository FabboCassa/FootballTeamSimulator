using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Chrome for the watchable match (task 3.1): a top HUD (score + clock),
    /// the pitch area (the renderer is inserted by the presenter), an event
    /// toast overlay, and a bottom control bar (speed 1x/2x/4x, Skip, Continue).
    /// Dumb view: it raises events and renders what the presenter tells it.
    /// </summary>
    public sealed class MatchWatchView
    {
        private static readonly Color BarColor = new Color(0.07f, 0.11f, 0.20f);
        private static readonly Color ToastColor = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color ActiveSpeed = new Color(0.20f, 0.55f, 0.30f);
        private static readonly Color IdleSpeed = new Color(0.20f, 0.24f, 0.34f);

        public event Action<float> SpeedClicked;
        public event Action SkipClicked;
        public event Action PauseClicked;
        public event Action ContinueClicked;

        public VisualElement Root { get; }

        /// <summary>Container the presenter drops the MatchRenderer into.</summary>
        public VisualElement PitchContainer { get; }

        private Label _score;
        private Label _clock;
        private readonly Label _toast;
        private Button _skip;
        private Button _pause;
        private Button _continue;
        private readonly Button[] _speedButtons;
        private readonly float[] _speeds = { 1f, 2f, 4f };
        private IVisualElementScheduledItem _toastHide;

        public MatchWatchView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.HubBlue;

            Root.Add(BuildHud(tr));

            PitchContainer = new VisualElement();
            PitchContainer.style.flexGrow = 1f; // takes the space between HUD and controls

            // Centered toast overlay: a full-width absolute row so the label
            // centres horizontally and draws on top of the renderer.
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

            Root.Add(PitchContainer);

            _speedButtons = new Button[_speeds.Length];
            Root.Add(BuildControls(tr));

            SetFinished(false);
        }

        private VisualElement BuildHud(Func<string, string> tr)
        {
            var hud = new VisualElement();
            hud.style.flexDirection = FlexDirection.Row;
            hud.style.justifyContent = Justify.Center;
            hud.style.alignItems = Align.Center;
            hud.style.backgroundColor = BarColor;
            hud.style.paddingTop = 10;
            hud.style.paddingBottom = 10;

            _score = new Label(string.Empty);
            _score.style.fontSize = 24;
            _score.style.unityFontStyleAndWeight = FontStyle.Bold;
            _score.style.color = Color.white;
            _score.style.marginRight = 20;
            hud.Add(_score);

            _clock = new Label("0'");
            _clock.style.fontSize = 20;
            _clock.style.color = new Color(1f, 1f, 1f, 0.8f);
            _clock.style.minWidth = 48;
            _clock.style.unityTextAlign = TextAnchor.MiddleLeft;
            hud.Add(_clock);

            return hud;
        }

        private VisualElement BuildControls(Func<string, string> tr)
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = BarColor;
            bar.style.paddingTop = 8;
            bar.style.paddingBottom = 8;

            string[] speedKeys = { "match.speed_1x", "match.speed_2x", "match.speed_4x" };
            for (int i = 0; i < _speeds.Length; i++)
            {
                float speed = _speeds[i];
                var b = new Button(() => SpeedClicked?.Invoke(speed)) { text = tr(speedKeys[i]) };
                StyleSmall(b);
                _speedButtons[i] = b;
                bar.Add(b);
            }

            _pause = new Button(() => PauseClicked?.Invoke()) { text = tr("match.pause") };
            StyleSmall(_pause);
            _pause.style.marginLeft = 16;
            bar.Add(_pause);

            _skip = new Button(() => SkipClicked?.Invoke()) { text = tr("match.skip") };
            StyleSmall(_skip);
            _skip.style.marginLeft = 16;
            bar.Add(_skip);

            _continue = new Button(() => ContinueClicked?.Invoke()) { text = tr("match.continue") };
            StyleSmall(_continue);
            _continue.style.marginLeft = 16;
            _continue.style.width = 150;
            bar.Add(_continue);

            return bar;
        }

        public void SetScore(string score) => _score.text = score;

        public void SetClock(string clock) => _clock.text = clock;

        /// <summary>Highlights the active speed button.</summary>
        public void SetActiveSpeed(float speed)
        {
            for (int i = 0; i < _speeds.Length; i++)
                _speedButtons[i].style.backgroundColor =
                    Mathf.Approximately(_speeds[i], speed) ? ActiveSpeed : IdleSpeed;
        }

        public void ShowToast(string text)
        {
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.style.display = DisplayStyle.None).StartingIn(2500);
        }

        /// <summary>At full time: hide speed/skip, reveal Continue.</summary>
        public void SetFinished(bool finished)
        {
            foreach (Button b in _speedButtons)
                b.style.display = finished ? DisplayStyle.None : DisplayStyle.Flex;
            _skip.style.display = finished ? DisplayStyle.None : DisplayStyle.Flex;
            _pause.style.display = finished ? DisplayStyle.None : DisplayStyle.Flex;
            _continue.style.display = finished ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void StyleSmall(Button b)
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
