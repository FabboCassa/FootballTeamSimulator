using System;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>Dumb view for the career Hub: exposes navigation/calendar events only.</summary>
    public sealed class HubView
    {
        public event Action AdvanceDayClicked;
        public event Action NextMatchClicked;
        public event Action EndSeasonClicked;
        public event Action SquadClicked;
        public event Action TacticsClicked;
        public event Action TrainingClicked;
        public event Action SupportClicked;
        public event Action MarketClicked;
        public event Action LeagueClicked;
        public event Action ExitCareerClicked;

        public VisualElement Root { get; }

        private readonly Label _clubLabel;
        private readonly Label _statusLabel;
        private readonly Button _advanceDayButton;
        private readonly Button _nextMatchButton;
        private readonly Button _endSeasonButton;

        public HubView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.HubBlue);
            Root.Add(UiKit.Title(tr("hub.title")));

            _clubLabel = UiKit.Subtitle(string.Empty);
            Root.Add(_clubLabel);

            _statusLabel = UiKit.Subtitle(string.Empty);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.maxWidth = 420;
            Root.Add(_statusLabel);

            _advanceDayButton = UiKit.MenuButton(tr("hub.advance_day"), () => AdvanceDayClicked?.Invoke());
            Root.Add(_advanceDayButton);
            _nextMatchButton = UiKit.MenuButton(tr("hub.next_match"), () => NextMatchClicked?.Invoke());
            Root.Add(_nextMatchButton);
            _endSeasonButton = UiKit.MenuButton(tr("hub.end_season"), () => EndSeasonClicked?.Invoke());
            Root.Add(_endSeasonButton);
            Root.Add(UiKit.MenuButton(tr("hub.squad"), () => SquadClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.tactics"), () => TacticsClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.training"), () => TrainingClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.support"), () => SupportClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.market"), () => MarketClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.league"), () => LeagueClicked?.Invoke()));
            Root.Add(UiKit.MenuButton(tr("hub.exit_career"), () => ExitCareerClicked?.Invoke()));
        }

        public void SetClubName(string clubName) => _clubLabel.text = clubName;

        public void SetStatus(string status) => _statusLabel.text = status;

        /// <summary>Season over: calendar buttons make way for End Season.</summary>
        public void SetSeasonComplete(bool complete)
        {
            _advanceDayButton.style.display = complete ? DisplayStyle.None : DisplayStyle.Flex;
            _nextMatchButton.style.display = complete ? DisplayStyle.None : DisplayStyle.Flex;
            _endSeasonButton.style.display = complete ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
