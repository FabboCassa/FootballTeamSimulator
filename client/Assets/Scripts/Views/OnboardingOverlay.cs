using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Guided first-run tutorial (task 6.2): a modal step carousel shown once over the Hub. Each step
    /// is a title + body; the player walks through with Back / Next (Start on the last step) or Skip.
    /// Self-contained — it removes itself from the hierarchy when finished and reports completion via a
    /// callback. Dumb view: the presenter passes the already-translated step text and the button labels.
    /// The dimmed backdrop captures input so nothing behind it can be clicked mid-tutorial.
    /// </summary>
    public sealed class OnboardingOverlay
    {
        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;
        private readonly IReadOnlyList<string> _titles;
        private readonly IReadOnlyList<string> _bodies;
        private readonly Action _onComplete;

        private readonly Label _progress;
        private readonly Label _title;
        private readonly Label _body;
        private readonly Button _backButton;
        private readonly Button _nextButton;

        private int _step;

        public OnboardingOverlay(
            Func<string, string> tr,
            IReadOnlyList<string> titles,
            IReadOnlyList<string> bodies,
            Action onComplete)
        {
            _tr = tr;
            _titles = titles;
            _bodies = bodies;
            _onComplete = onComplete;

            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.top = 0;
            Root.style.right = 0;
            Root.style.bottom = 0;
            Root.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;
            // Modal: swallow clicks so the Hub behind can't be touched during the tutorial.
            Root.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            var card = UiKit.Card();
            card.style.maxWidth = 460;
            card.style.minWidth = 320;

            _progress = UiKit.Caption(string.Empty);
            _progress.style.marginBottom = UiKit.SpaceXs;
            card.Add(_progress);

            _title = UiKit.Header(string.Empty);
            _title.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_title);

            _body = new Label(string.Empty);
            _body.style.fontSize = UiKit.FontBody;
            _body.style.color = UiKit.TextMuted;
            _body.style.whiteSpace = WhiteSpace.Normal;
            _body.style.marginBottom = UiKit.SpaceMd;
            card.Add(_body);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;

            _backButton = UiKit.MenuButton(tr("onboarding.back"), OnBack);
            _backButton.style.width = 110;
            _backButton.style.height = 46;
            buttons.Add(_backButton);

            var skip = UiKit.MenuButton(tr("onboarding.skip"), Complete);
            skip.style.width = 110;
            skip.style.height = 46;
            skip.style.marginLeft = UiKit.SpaceSm;
            buttons.Add(skip);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            buttons.Add(spacer);

            _nextButton = UiKit.PrimaryButton(tr("onboarding.next"), OnNext);
            _nextButton.style.width = 140;
            _nextButton.style.height = 46;
            buttons.Add(_nextButton);

            card.Add(buttons);
            Root.Add(card);

            Render();
        }

        private void Render()
        {
            int count = _titles.Count;
            // Progress is locale-neutral ("x/N"); the view only has a single-key translate delegate.
            _progress.text = $"{_step + 1}/{count}";
            _title.text = _step < _titles.Count ? _titles[_step] : string.Empty;
            _body.text = _step < _bodies.Count ? _bodies[_step] : string.Empty;

            _backButton.style.visibility = _step > 0 ? Visibility.Visible : Visibility.Hidden;
            _nextButton.text = _step >= count - 1 ? _tr("onboarding.start") : _tr("onboarding.next");
        }

        private void OnNext()
        {
            if (_step >= _titles.Count - 1)
            {
                Complete();
                return;
            }

            _step++;
            Render();
        }

        private void OnBack()
        {
            if (_step > 0)
            {
                _step--;
                Render();
            }
        }

        private void Complete()
        {
            Root.RemoveFromHierarchy();
            _onComplete?.Invoke();
        }
    }
}
