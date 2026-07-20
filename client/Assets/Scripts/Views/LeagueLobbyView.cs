using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for a league lobby (task 8.1b): the league name, the invite code to share, the member
    /// list, the generated squads (one collapsible foldout per club), and a Leave button. No logic —
    /// the presenter fills it from the league detail and drives the API.
    /// </summary>
    public sealed class LeagueLobbyView
    {
        public event Action LeaveClicked;
        public event Action BackClicked;
        public event Action StartDraftClicked;
        public event Action RefreshClicked;
        public event Action<int> PickClicked;
        public event Action SeasonClicked;
        public event Action TrainingClicked;
        public event Action AuctionsClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _inviteCaption;
        private readonly Label _inviteValue;
        private readonly VisualElement _draftCard;
        private readonly Label _draftCaption;
        private readonly Label _draftBanner;
        private readonly Button _startButton;
        private readonly Label _startHint;
        private readonly VisualElement _pickContainer;
        private readonly Button _refreshButton;
        private readonly Button _seasonButton;
        private readonly Button _trainingButton;
        private readonly Button _auctionButton;
        private readonly Label _membersCaption;
        private readonly VisualElement _membersContainer;
        private readonly Label _clubsCaption;
        private readonly VisualElement _clubsContainer;
        private readonly Button _leaveButton;
        private readonly Label _status;
        private readonly Button _backButton;

        /// <summary>A club and its squad, both preformatted by the presenter.</summary>
        public readonly struct ClubVm
        {
            public readonly string Header;
            public readonly IReadOnlyList<string> Players;
            public ClubVm(string header, IReadOnlyList<string> players) { Header = header; Players = players; }
        }

        /// <summary>A pickable club during the draft: the id to send + a preformatted label (8.2b).</summary>
        public readonly struct PickVm
        {
            public readonly int ExternalId;
            public readonly string Label;
            public PickVm(int externalId, string label) { ExternalId = externalId; Label = label; }
        }

        public LeagueLobbyView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(560f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);

            var inviteCard = UiKit.Card();
            col.Add(inviteCard);
            _inviteCaption = UiKit.Caption(string.Empty);
            inviteCard.Add(_inviteCaption);
            _inviteValue = UiKit.Title(string.Empty);
            _inviteValue.style.color = UiKit.Accent;
            inviteCard.Add(_inviteValue);

            // --- draft (8.2b): shown while forming/drafting; hidden once the season is active ---
            _draftCard = UiKit.Card();
            _draftCard.style.marginTop = UiKit.SpaceMd;
            col.Add(_draftCard);
            _draftCaption = UiKit.Subtitle(string.Empty);
            _draftCard.Add(_draftCaption);
            _draftBanner = UiKit.Caption(string.Empty);
            _draftBanner.style.whiteSpace = WhiteSpace.Normal;
            _draftBanner.style.marginBottom = UiKit.SpaceXs;
            _draftCard.Add(_draftBanner);
            _startButton = UiKit.PrimaryButton(string.Empty, () => StartDraftClicked?.Invoke());
            _draftCard.Add(_startButton);
            _startHint = UiKit.Caption(string.Empty);
            _startHint.style.whiteSpace = WhiteSpace.Normal;
            _draftCard.Add(_startHint);
            _pickContainer = new VisualElement();
            _pickContainer.style.marginTop = UiKit.SpaceXs;
            _draftCard.Add(_pickContainer);
            _refreshButton = UiKit.MenuButton(string.Empty, () => RefreshClicked?.Invoke());
            _refreshButton.style.marginTop = UiKit.SpaceXs;
            _draftCard.Add(_refreshButton);

            // Season (8.3b): opens the schedule/standings/advance screen once the league is active.
            _seasonButton = UiKit.PrimaryButton(string.Empty, () => SeasonClicked?.Invoke());
            _seasonButton.style.marginTop = UiKit.SpaceMd;
            _seasonButton.style.display = DisplayStyle.None;
            col.Add(_seasonButton);

            // Training (8.4b): opens the online training editor for your drafted club once active.
            _trainingButton = UiKit.MenuButton(string.Empty, () => TrainingClicked?.Invoke());
            _trainingButton.style.marginTop = UiKit.SpaceXs;
            _trainingButton.style.display = DisplayStyle.None;
            col.Add(_trainingButton);

            // Auctions (8.5b): opens the live free-agent auction screen once active.
            _auctionButton = UiKit.MenuButton(string.Empty, () => AuctionsClicked?.Invoke());
            _auctionButton.style.marginTop = UiKit.SpaceXs;
            _auctionButton.style.display = DisplayStyle.None;
            col.Add(_auctionButton);

            _membersCaption = UiKit.Subtitle(string.Empty);
            _membersCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_membersCaption);
            _membersContainer = new VisualElement();
            col.Add(_membersContainer);

            _clubsCaption = UiKit.Subtitle(string.Empty);
            _clubsCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_clubsCaption);
            _clubsContainer = new VisualElement();
            col.Add(_clubsContainer);

            _leaveButton = UiKit.PrimaryButton(string.Empty, () => LeaveClicked?.Invoke());
            _leaveButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_leaveButton);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

            UpdateTexts();
        }

        public void SetHeader(string name) => _title.text = name;

        public void SetInviteCode(string code) => _inviteValue.text = code;

        public void SetMembers(IReadOnlyList<string> members)
        {
            _membersContainer.Clear();
            if (members == null) return;
            foreach (var m in members)
            {
                var label = UiKit.Caption(m);
                label.style.marginBottom = UiKit.SpaceXs;
                _membersContainer.Add(label);
            }
        }

        public void SetClubs(IReadOnlyList<ClubVm> clubs)
        {
            _clubsContainer.Clear();
            if (clubs == null) return;
            foreach (var club in clubs)
            {
                var foldout = new Foldout { text = club.Header, value = false };
                foldout.style.marginBottom = UiKit.SpaceXs;
                if (club.Players != null)
                {
                    foreach (var p in club.Players)
                    {
                        var label = UiKit.Caption(p);
                        foldout.Add(label);
                    }
                }
                _clubsContainer.Add(foldout);
            }
        }

        // --- draft (8.2b) -------------------------------------------------------------------------

        public void SetDraftVisible(bool visible) =>
            _draftCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetDraftBanner(string text, bool visible)
        {
            _draftBanner.text = text;
            _draftBanner.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetStartButton(bool visible, bool enabled)
        {
            _startButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _startButton.SetEnabled(enabled);
        }

        public void SetStartHint(string text, bool visible)
        {
            _startHint.text = text;
            _startHint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetRefreshVisible(bool visible) =>
            _refreshButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Shows the "open season" button once the league is active (8.3b).</summary>
        public void SetSeasonButtonVisible(bool visible) =>
            _seasonButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Shows the "training" button once the league is active (8.4b).</summary>
        public void SetTrainingButtonVisible(bool visible) =>
            _trainingButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Shows the "auctions" button once the league is active (8.5b).</summary>
        public void SetAuctionsButtonVisible(bool visible) =>
            _auctionButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetPickList(IReadOnlyList<PickVm> picks)
        {
            _pickContainer.Clear();
            if (picks == null) return;
            foreach (var p in picks)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = UiKit.SpaceXs;

                var label = UiKit.Caption(p.Label);
                label.style.flexGrow = 1;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                int id = p.ExternalId;
                var btn = new Button(() => PickClicked?.Invoke(id)) { text = _tr("lobby.pick_button") };
                row.Add(btn);

                _pickContainer.Add(row);
            }
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
            _leaveButton.SetEnabled(!busy);
            _startButton.SetEnabled(!busy);
            _refreshButton.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _inviteCaption.text = _tr("lobby.invite_code");
            _draftCaption.text = _tr("lobby.draft_caption");
            _startButton.text = _tr("lobby.start_draft");
            _refreshButton.text = _tr("lobby.refresh");
            _membersCaption.text = _tr("lobby.members");
            _clubsCaption.text = _tr("lobby.clubs");
            _seasonButton.text = _tr("lobby.open_season");
            _trainingButton.text = _tr("lobby.training");
            _auctionButton.text = _tr("lobby.auctions");
            _leaveButton.text = _tr("lobby.leave");
            _backButton.text = _tr("common.back");
        }
    }
}
