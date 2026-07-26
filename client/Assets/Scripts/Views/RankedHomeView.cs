using System;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked home (Phase 9.2): shows the caller's ladder state, an Enrol button when not
    /// yet on the ladder, and — once enrolled — a "go to season" button + an auto re-enrolment toggle. No
    /// logic; exposes events + a translate delegate. The presenter drives <c>RankedApiService</c>.
    /// </summary>
    public sealed class RankedHomeView
    {
        public event Action EnrolClicked;
        public event Action SeasonClicked;
        public event Action LeaderboardClicked;
        public event Action AutoEnrolClicked;
        public event Action FillDevClicked; // dev-only
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _info;
        private readonly Button _enrolButton;
        private readonly Button _seasonButton;
        private readonly Button _leaderboardButton;
        private readonly Button _autoEnrolButton;
        private readonly Label _status;
        private readonly Button _fillDevButton; // dev-only
        private readonly Button _backButton;

        public RankedHomeView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(560f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);
            _subtitle = UiKit.Caption(string.Empty);
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_subtitle);

            var card = UiKit.Card();
            col.Add(card);
            _info = UiKit.Caption(string.Empty);
            _info.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_info);

            _enrolButton = UiKit.PrimaryButton(string.Empty, () => EnrolClicked?.Invoke());
            col.Add(_enrolButton);

            _seasonButton = UiKit.PrimaryButton(string.Empty, () => SeasonClicked?.Invoke());
            _seasonButton.style.display = DisplayStyle.None;
            col.Add(_seasonButton);

            // The global ladder + the caller's palmarès (Phase 9.3). Always available once enrolled.
            _leaderboardButton = UiKit.MenuButton(string.Empty, () => LeaderboardClicked?.Invoke());
            _leaderboardButton.style.marginTop = UiKit.SpaceXs;
            _leaderboardButton.style.display = DisplayStyle.None;
            col.Add(_leaderboardButton);

            _autoEnrolButton = UiKit.MenuButton(string.Empty, () => AutoEnrolClicked?.Invoke());
            _autoEnrolButton.style.display = DisplayStyle.None;
            col.Add(_autoEnrolButton);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            // Dev-only: fill the placement group with bots + start the season (hidden unless DevFlags).
            _fillDevButton = UiKit.MenuButton(string.Empty, () => FillDevClicked?.Invoke());
            _fillDevButton.style.marginTop = UiKit.SpaceMd;
            _fillDevButton.style.display = DisplayStyle.None;
            col.Add(_fillDevButton);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

            UpdateTexts();
        }

        public void SetInfo(string text) => _info.text = text;

        public void SetEnrolVisible(bool visible) =>
            _enrolButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetSeasonVisible(bool visible) =>
            _seasonButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetLeaderboardVisible(bool visible) =>
            _leaderboardButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetAutoEnrol(bool visible, string label)
        {
            _autoEnrolButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _autoEnrolButton.text = label;
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _enrolButton.SetEnabled(!busy);
            _seasonButton.SetEnabled(!busy);
            _leaderboardButton.SetEnabled(!busy);
            _autoEnrolButton.SetEnabled(!busy);
            _fillDevButton.SetEnabled(!busy);
        }

        /// <summary>Shows the dev-only "fill with bots" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _fillDevButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void UpdateTexts()
        {
            _title.text = _tr("ranked.title");
            _subtitle.text = _tr("ranked.subtitle");
            _enrolButton.text = _tr("ranked.enrol");
            _seasonButton.text = _tr("ranked.open_season");
            _leaderboardButton.text = _tr("ranked.board.open");
            _fillDevButton.text = _tr("ranked.dev_fill");
            _backButton.text = _tr("common.back");
        }
    }
}
