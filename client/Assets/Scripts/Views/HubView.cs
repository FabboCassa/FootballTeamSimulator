using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>Dumb view for the career Hub: exposes navigation/calendar events only.</summary>
    public sealed class HubView
    {
        public event Action AdvanceDayClicked;
        public event Action NextMatchClicked;
        public event Action EndSeasonClicked;
        public event Action InboxClicked;
        public event Action SquadClicked;
        public event Action TacticsClicked;
        public event Action TrainingClicked;
        public event Action SupportClicked;
        public event Action MarketClicked;
        public event Action ScoutingClicked;
        public event Action ClubClicked;
        public event Action CareerClicked;
        public event Action LeagueClicked;
        public event Action ExitCareerClicked;

        public VisualElement Root { get; }

        private readonly Label _clubLabel;
        private readonly Label _statusLabel;
        private readonly Button _advanceDayButton;
        private readonly Button _nextMatchButton;
        private readonly Button _endSeasonButton;
        private readonly Button _inboxButton;
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

            Root.Add(UiKit.Title(tr("hub.title")));

            // Per-club accent underline, tinted by the presenter.
            _accentBar = new VisualElement();
            _accentBar.style.width = 160;
            _accentBar.style.height = 4;
            _accentBar.style.marginBottom = UiKit.SpaceMd;
            UiKit.Round(_accentBar, 2);
            Root.Add(_accentBar);

            _clubLabel = UiKit.Subtitle(string.Empty);
            Root.Add(_clubLabel);

            _statusLabel = UiKit.Subtitle(string.Empty);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.maxWidth = 420;
            Root.Add(_statusLabel);

            // Responsive menu (task 6.4): the buttons live in a wrapping flex row so they
            // form a multi-column grid on a wide landscape/PC viewport and collapse to a
            // single comfortable thumb-column in portrait — no orientation branching needed.
            var menu = new VisualElement();
            menu.style.flexDirection = FlexDirection.Row;
            menu.style.flexWrap = Wrap.Wrap;
            menu.style.justifyContent = Justify.Center;
            menu.style.alignItems = Align.Center;
            menu.style.width = Length.Percent(100);
            menu.style.maxWidth = 960;
            Root.Add(menu);

            _advanceDayButton = UiKit.MenuButton(tr("hub.advance_day"), () => AdvanceDayClicked?.Invoke());
            menu.Add(_advanceDayButton);
            _nextMatchButton = UiKit.MenuButton(tr("hub.next_match"), () => NextMatchClicked?.Invoke());
            menu.Add(_nextMatchButton);
            _endSeasonButton = UiKit.MenuButton(tr("hub.end_season"), () => EndSeasonClicked?.Invoke());
            menu.Add(_endSeasonButton);
            _inboxButton = UiKit.MenuButton(tr("hub.inbox"), () => InboxClicked?.Invoke());
            menu.Add(_inboxButton);
            menu.Add(UiKit.MenuButton(tr("hub.squad"), () => SquadClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.tactics"), () => TacticsClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.training"), () => TrainingClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.support"), () => SupportClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.market"), () => MarketClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.scouting"), () => ScoutingClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.club"), () => ClubClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.career"), () => CareerClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.league"), () => LeagueClicked?.Invoke()));
            menu.Add(UiKit.MenuButton(tr("hub.exit_career"), () => ExitCareerClicked?.Invoke()));
        }

        public void SetClubName(string clubName) => _clubLabel.text = clubName;

        /// <summary>Shows the user club's crest (a CrestRenderer built by the presenter).</summary>
        public void SetCrest(VisualElement crest)
        {
            _crestSlot.Clear();
            if (crest != null) _crestSlot.Add(crest);
        }

        /// <summary>Tints the hub with the club's primary colour (accent underline + club name).</summary>
        public void SetAccent(Color primary)
        {
            _accentBar.style.backgroundColor = primary;
            _clubLabel.style.color = primary;
        }

        public void SetStatus(string status) => _statusLabel.text = status;

        /// <summary>Sets the Inbox button label (the presenter adds the unread badge when there are unread messages).</summary>
        public void SetInbox(string label) => _inboxButton.text = label;

        /// <summary>Season over: calendar buttons make way for End Season.</summary>
        public void SetSeasonComplete(bool complete)
        {
            _advanceDayButton.style.display = complete ? DisplayStyle.None : DisplayStyle.Flex;
            _nextMatchButton.style.display = complete ? DisplayStyle.None : DisplayStyle.Flex;
            _endSeasonButton.style.display = complete ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
