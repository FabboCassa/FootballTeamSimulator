using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Chrome for a live-controlled online match (task 8.6b): a score+clock HUD with a live/waiting banner,
    /// the pitch area (the presenter drops the MatchRenderer in), an event toast, and the control bar —
    /// "Modifica" (opens the sub/instruction panel), "Fine partita" (confirm full-time) and Back. Unlike a
    /// replay there is no speed/skip: a live match runs in real time, synced to the shared kickoff. Dumb
    /// view: raises events, renders what the presenter sets; the presenter overlays the InMatchPanel on Root.
    /// </summary>
    public sealed class LiveMatchView
    {
        private static readonly Color BarColor = new Color(0.07f, 0.11f, 0.20f);
        private static readonly Color ToastColor = new Color(0f, 0f, 0f, 0.75f);

        public event Action ModifyClicked;
        public event Action FinishClicked;
        public event Action BackClicked;
        public event Action BotJoinClicked; // dev-only
        public event Action BotSubClicked;  // dev-only

        public VisualElement Root { get; }
        public VisualElement PitchContainer { get; }

        private readonly Label _score;
        private readonly Label _clock;
        private readonly Label _banner;
        private readonly Label _toast;
        private readonly Label _status;
        private readonly Button _modify;
        private readonly Button _finish;
        private readonly VisualElement _devRow;
        private IVisualElementScheduledItem _toastHide;

        public LiveMatchView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;

            // HUD: score + clock, with a live/waiting banner underneath.
            var hud = new VisualElement();
            hud.style.backgroundColor = BarColor;
            hud.style.paddingTop = 8;
            hud.style.paddingBottom = 8;
            hud.style.alignItems = Align.Center;

            var scoreRow = new VisualElement();
            scoreRow.style.flexDirection = FlexDirection.Row;
            scoreRow.style.justifyContent = Justify.Center;
            scoreRow.style.alignItems = Align.Center;
            _score = new Label(string.Empty);
            _score.style.fontSize = 22;
            _score.style.unityFontStyleAndWeight = FontStyle.Bold;
            _score.style.color = Color.white;
            _score.style.marginRight = 18;
            scoreRow.Add(_score);
            _clock = new Label("0'");
            _clock.style.fontSize = 18;
            _clock.style.color = new Color(1f, 1f, 1f, 0.8f);
            _clock.style.minWidth = 44;
            scoreRow.Add(_clock);
            hud.Add(scoreRow);

            _banner = new Label(string.Empty);
            _banner.style.fontSize = 13;
            _banner.style.color = new Color(1f, 1f, 1f, 0.7f);
            _banner.style.marginTop = 4;
            _banner.style.whiteSpace = WhiteSpace.Normal;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            hud.Add(_banner);
            Root.Add(hud);

            // Pitch + toast overlay.
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
            Root.Add(PitchContainer);

            _status = new Label(string.Empty);
            _status.style.color = UiKit.TextMuted;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.paddingTop = 6;
            _status.style.paddingBottom = 6;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            Root.Add(_status);

            // Controls.
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.Center;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = BarColor;
            bar.style.paddingTop = 8;
            bar.style.paddingBottom = 8;

            _modify = Wide(tr("live.modify"), () => ModifyClicked?.Invoke());
            bar.Add(_modify);
            _finish = Wide(tr("live.finish"), () => FinishClicked?.Invoke());
            bar.Add(_finish);
            var back = Wide(tr("common.back"), () => BackClicked?.Invoke());
            bar.Add(back);
            Root.Add(bar);

            // Dev-only row (shown by the presenter when DevFlags.OnlineTestTools): simulate the opponent.
            _devRow = new VisualElement();
            _devRow.style.flexDirection = FlexDirection.Row;
            _devRow.style.justifyContent = Justify.Center;
            _devRow.style.backgroundColor = BarColor;
            _devRow.style.paddingBottom = 8;
            _devRow.style.display = DisplayStyle.None;
            _devRow.Add(Wide(tr("live.bot_join"), () => BotJoinClicked?.Invoke()));
            _devRow.Add(Wide(tr("live.bot_sub"), () => BotSubClicked?.Invoke()));
            Root.Add(_devRow);
        }

        public void SetDevToolsVisible(bool visible) =>
            _devRow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetScore(string score) => _score.text = score;
        public void SetClock(string clock) => _clock.text = clock;
        public void SetBanner(string text) => _banner.text = text;
        public void SetModifyEnabled(bool enabled) => _modify.SetEnabled(enabled);
        public void SetFinishEnabled(bool enabled) => _finish.SetEnabled(enabled);

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

        private static Button Wide(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = 44;
            b.style.minWidth = 120;
            b.style.fontSize = 16;
            b.style.marginLeft = 6;
            b.style.marginRight = 6;
            b.style.color = Color.white;
            b.style.backgroundColor = new Color(0.20f, 0.24f, 0.34f);
            return b;
        }
    }
}
