using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for a league lobby (task 8.1b), on the shared page scaffold: the invite code to share,
    /// the draft panel while the league is forming, one row of compact actions once it is active, the
    /// member list and the generated squads (a foldout per club), with Back / Leave in the footer.
    /// No logic — the presenter fills it from the league detail and drives the API.
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
        private readonly VisualElement _actionsCard;
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

        // Which of the active-league actions are on offer — the plate hides itself when none are.
        private bool _seasonVisible, _trainingVisible, _auctionsVisible;

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

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            ScrollView body = UiKit.ListScroll();
            col.Add(body);

            // ---- invite code --------------------------------------------------------------------
            VisualElement invitePanel = UiKit.Panel();
            body.Add(invitePanel);
            _inviteCaption = UiKit.SectionLabel(string.Empty);
            _inviteCaption.style.marginTop = 0;
            invitePanel.Add(_inviteCaption);
            _inviteValue = new Label(string.Empty);
            _inviteValue.style.fontSize = 26;
            _inviteValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            _inviteValue.style.color = UiKit.Accent;
            invitePanel.Add(_inviteValue);

            // ---- draft (8.2b) --------------------------------------------------------------------
            _draftCard = UiKit.Panel();
            body.Add(_draftCard);
            _draftCaption = UiKit.SectionLabel(string.Empty);
            _draftCaption.style.marginTop = 0;
            _draftCard.Add(_draftCaption);
            _draftBanner = UiKit.PanelLine(string.Empty);
            _draftBanner.style.marginBottom = UiKit.SpaceXs;
            _draftCard.Add(_draftBanner);

            VisualElement draftActions = UiKit.Toolbar();
            draftActions.style.marginBottom = 0;
            _draftCard.Add(draftActions);
            _startButton = Compact(draftActions, () => StartDraftClicked?.Invoke(), 170f);
            UiKit.SetSmallButtonAccent(_startButton, true);
            _refreshButton = Compact(draftActions, () => RefreshClicked?.Invoke(), 130f);

            _startHint = UiKit.HelpText(string.Empty);
            _startHint.style.marginTop = UiKit.SpaceXs;
            _startHint.style.marginBottom = 0;
            _draftCard.Add(_startHint);

            _pickContainer = new VisualElement();
            _pickContainer.style.marginTop = UiKit.SpaceSm;
            _draftCard.Add(_pickContainer);

            // ---- active-league actions ------------------------------------------------------------
            _actionsCard = UiKit.Panel();
            _actionsCard.style.display = DisplayStyle.None;
            body.Add(_actionsCard);
            VisualElement actions = UiKit.Toolbar();
            actions.style.marginBottom = 0;
            _actionsCard.Add(actions);
            _seasonButton = Compact(actions, () => SeasonClicked?.Invoke(), 160f);
            UiKit.SetSmallButtonAccent(_seasonButton, true);
            _seasonButton.style.display = DisplayStyle.None;
            _trainingButton = Compact(actions, () => TrainingClicked?.Invoke(), 150f);
            _trainingButton.style.display = DisplayStyle.None;
            _auctionButton = Compact(actions, () => AuctionsClicked?.Invoke(), 150f);
            _auctionButton.style.display = DisplayStyle.None;

            // ---- members ---------------------------------------------------------------------------
            VisualElement membersPanel = UiKit.Panel();
            body.Add(membersPanel);
            _membersCaption = UiKit.SectionLabel(string.Empty);
            _membersCaption.style.marginTop = 0;
            membersPanel.Add(_membersCaption);
            _membersContainer = new VisualElement();
            membersPanel.Add(_membersContainer);

            // ---- clubs -----------------------------------------------------------------------------
            VisualElement clubsPanel = UiKit.Panel();
            body.Add(clubsPanel);
            _clubsCaption = UiKit.SectionLabel(string.Empty);
            _clubsCaption.style.marginTop = 0;
            clubsPanel.Add(_clubsCaption);
            _clubsContainer = new VisualElement();
            clubsPanel.Add(_clubsContainer);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _leaveButton = UiKit.FooterButton(string.Empty, () => LeaveClicked?.Invoke());
            _leaveButton.style.color = UiKit.Danger;
            footer.Add(_leaveButton);
            col.Add(footer);

            UpdateTexts();
        }

        private static Button Compact(VisualElement parent, Action onClick, float minWidth)
        {
            Button b = UiKit.SmallButton(string.Empty, onClick, minWidth);
            b.style.marginLeft = 0;
            b.style.marginRight = 6;
            b.style.marginBottom = 4;
            parent.Add(b);
            return b;
        }

        public void SetHeader(string name) => _title.text = name;

        public void SetInviteCode(string code) => _inviteValue.text = code;

        public void SetMembers(IReadOnlyList<string> members)
        {
            _membersContainer.Clear();
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 30;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = UiKit.SpaceSm;
                row.style.paddingRight = UiKit.SpaceSm;
                OnlineTableKit.Stripe(row, false, i);

                var label = new Label(members[i] ?? string.Empty);
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                _membersContainer.Add(row);
            }
        }

        public void SetClubs(IReadOnlyList<ClubVm> clubs)
        {
            _clubsContainer.Clear();
            if (clubs == null) return;
            foreach (ClubVm club in clubs)
            {
                var foldout = new Foldout { text = club.Header, value = false };
                foldout.style.marginBottom = UiKit.SpaceXs;
                if (club.Players != null)
                {
                    foreach (string p in club.Players)
                    {
                        Label label = UiKit.Caption(p);
                        label.style.marginBottom = 1;
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
        public void SetSeasonButtonVisible(bool visible)
        {
            _seasonVisible = visible;
            _seasonButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshActionsVisibility();
        }

        /// <summary>Shows the "training" button once the league is active (8.4b).</summary>
        public void SetTrainingButtonVisible(bool visible)
        {
            _trainingVisible = visible;
            _trainingButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshActionsVisibility();
        }

        /// <summary>Shows the "auctions" button once the league is active (8.5b).</summary>
        public void SetAuctionsButtonVisible(bool visible)
        {
            _auctionsVisible = visible;
            _auctionButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshActionsVisibility();
        }

        /// <summary>The actions plate only exists while it has something on it.</summary>
        private void RefreshActionsVisibility()
        {
            bool any = _seasonVisible || _trainingVisible || _auctionsVisible;
            _actionsCard.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetPickList(IReadOnlyList<PickVm> picks)
        {
            _pickContainer.Clear();
            if (picks == null) return;
            for (int i = 0; i < picks.Count; i++)
            {
                PickVm p = picks[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 40;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = UiKit.SpaceSm;
                row.style.paddingRight = UiKit.SpaceSm;
                OnlineTableKit.Stripe(row, false, i);

                var label = new Label(p.Label ?? string.Empty);
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                int id = p.ExternalId;
                Button pick = UiKit.SmallButton(_tr("lobby.pick_button"), () => PickClicked?.Invoke(id), 90f);
                UiKit.SetSmallButtonAccent(pick, true);
                row.Add(pick);

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
