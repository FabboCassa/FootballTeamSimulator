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

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _title;
        private readonly Label _inviteCaption;
        private readonly Label _inviteValue;
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

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy) => _leaveButton.SetEnabled(!busy);

        public void UpdateTexts()
        {
            _inviteCaption.text = _tr("lobby.invite_code");
            _membersCaption.text = _tr("lobby.members");
            _clubsCaption.text = _tr("lobby.clubs");
            _leaveButton.text = _tr("lobby.leave");
            _backButton.text = _tr("common.back");
        }
    }
}
