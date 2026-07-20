using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the "Online Leagues" list (task 8.1b): a Create button, an inline "join by code"
    /// row, and the list of the account's leagues (each a tappable row → lobby). No logic — exposes
    /// events + a translate delegate; the presenter drives <c>LeagueApiService</c>.
    /// </summary>
    public sealed class LeagueListView
    {
        public event Action CreateClicked;
        public event Action JoinClicked;
        public event Action BackClicked;
        public event Action<string> LeagueSelected;
        public event Action CreateTestLeagueClicked; // dev-only

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Button _createButton;
        private readonly Label _joinCaption;
        private readonly TextField _codeField;
        private readonly Button _joinButton;
        private readonly Label _myLeaguesCaption;
        private readonly VisualElement _listContainer;
        private readonly Label _emptyLabel;
        private readonly Label _status;
        private readonly Button _backButton;
        private readonly Button _devSeedButton; // dev-only

        /// <summary>One row in the leagues list — the presenter supplies a preformatted label.</summary>
        public readonly struct LeagueRow
        {
            public readonly string Id;
            public readonly string Label;
            public LeagueRow(string id, string label) { Id = id; Label = label; }
        }

        public LeagueListView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(560f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);

            _createButton = UiKit.PrimaryButton(string.Empty, () => CreateClicked?.Invoke());
            col.Add(_createButton);

            var joinCard = UiKit.Card();
            col.Add(joinCard);
            _joinCaption = UiKit.Caption(string.Empty);
            joinCard.Add(_joinCaption);
            _codeField = new TextField { maxLength = 16 };
            _codeField.style.marginBottom = UiKit.SpaceSm;
            _codeField.style.minHeight = 40;
            joinCard.Add(_codeField);
            _joinButton = UiKit.MenuButton(string.Empty, () => JoinClicked?.Invoke());
            joinCard.Add(_joinButton);

            _myLeaguesCaption = UiKit.Subtitle(string.Empty);
            _myLeaguesCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_myLeaguesCaption);

            _listContainer = new VisualElement();
            col.Add(_listContainer);

            _emptyLabel = UiKit.Caption(string.Empty);
            _emptyLabel.style.display = DisplayStyle.None;
            col.Add(_emptyLabel);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

            // Dev-only shortcut: seed a ready test league (hidden unless DevFlags.OnlineTestTools).
            _devSeedButton = UiKit.MenuButton(string.Empty, () => CreateTestLeagueClicked?.Invoke());
            _devSeedButton.style.marginTop = UiKit.SpaceXs;
            _devSeedButton.style.display = DisplayStyle.None;
            col.Add(_devSeedButton);

            UpdateTexts();
        }

        public string JoinCode => (_codeField.value ?? string.Empty).Trim();

        public void SetLeagues(IReadOnlyList<LeagueRow> rows)
        {
            _listContainer.Clear();
            _emptyLabel.style.display = (rows == null || rows.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
            if (rows == null) return;

            foreach (var row in rows)
            {
                var id = row.Id;
                var button = UiKit.MenuButton(row.Label, () => LeagueSelected?.Invoke(id));
                button.style.marginBottom = UiKit.SpaceSm;
                _listContainer.Add(button);
            }
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        /// <summary>Shows the dev-only "seed test league" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _devSeedButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _createButton.SetEnabled(!busy);
            _joinButton.SetEnabled(!busy);
            _devSeedButton.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _title.text = _tr("leagues.title");
            _createButton.text = _tr("leagues.create");
            _joinCaption.text = _tr("leagues.join_caption");
            _joinButton.text = _tr("leagues.join");
            _myLeaguesCaption.text = _tr("leagues.mine_caption");
            _emptyLabel.text = _tr("leagues.none");
            _backButton.text = _tr("common.back");
            _devSeedButton.text = _tr("leagues.dev_seed");
        }
    }
}
