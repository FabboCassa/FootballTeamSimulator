using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the career overview (the Hub, redesigned in task 6.6). Section navigation
    /// and the calendar controls moved to the persistent <see cref="AppShell"/> chrome; this
    /// screen shows the club identity plus the season status card, and surfaces the End Season
    /// call-to-action when the season is complete.
    /// </summary>
    public sealed class HubView
    {
        public event Action EndSeasonClicked;

        public VisualElement Root { get; }

        private readonly Label _clubLabel;
        private readonly Label _statusLabel;
        private readonly Button _endSeasonButton;
        private readonly VisualElement _crestSlot;
        private readonly VisualElement _accentBar;

        public HubView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.HubBlue);

            // Club crest (filled by the presenter from the generated identity).
            _crestSlot = new VisualElement();
            _crestSlot.style.alignItems = Align.Center;
            _crestSlot.style.justifyContent = Justify.Center;
            _crestSlot.style.marginBottom = UiKit.SpaceSm;
            Root.Add(_crestSlot);

            _clubLabel = UiKit.Header(string.Empty);
            _clubLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            Root.Add(_clubLabel);

            // Per-club accent underline, tinted by the presenter.
            _accentBar = new VisualElement();
            _accentBar.style.width = 160;
            _accentBar.style.height = 4;
            _accentBar.style.marginBottom = UiKit.SpaceMd;
            _accentBar.style.alignSelf = Align.Center;
            UiKit.Round(_accentBar, 2);
            Root.Add(_accentBar);

            // Season status card (day, next fixture, last result).
            var card = UiKit.Card();
            card.style.width = Length.Percent(100);
            card.style.maxWidth = 520;
            _statusLabel = UiKit.Subtitle(string.Empty);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 0;
            card.Add(_statusLabel);
            Root.Add(card);

            _endSeasonButton = UiKit.PrimaryButton(tr("hub.end_season"), () => EndSeasonClicked?.Invoke());
            _endSeasonButton.style.marginTop = UiKit.SpaceMd;
            _endSeasonButton.style.display = DisplayStyle.None;
            Root.Add(_endSeasonButton);
        }

        public void SetClubName(string clubName) => _clubLabel.text = clubName;

        /// <summary>Shows the user club's crest (a CrestRenderer built by the presenter).</summary>
        public void SetCrest(VisualElement crest)
        {
            _crestSlot.Clear();
            if (crest != null) _crestSlot.Add(crest);
        }

        /// <summary>Tints the overview's accent underline with the club's primary colour.
        /// The club NAME stays theme-white — a dark club primary was unreadable on navy.</summary>
        public void SetAccent(Color primary) => _accentBar.style.backgroundColor = primary;

        public void SetStatus(string status) => _statusLabel.text = status;

        /// <summary>Season over: shows the End Season call-to-action.</summary>
        public void SetSeasonComplete(bool complete) =>
            _endSeasonButton.style.display = complete ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
