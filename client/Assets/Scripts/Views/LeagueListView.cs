using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the "Online Leagues" list (task 8.1b), on the shared page scaffold: a join panel
    /// (code field + button on one line), the account's leagues as tappable row cards inside a panel that
    /// fills the page, and a footer carrying Back + Create. No logic — exposes events + a translate
    /// delegate; the presenter drives <c>LeagueApiService</c>.
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
        private readonly ScrollView _list;
        private readonly Label _emptyLabel;
        private readonly Label _status;
        private readonly Button _backButton;
        private readonly Button _devSeedButton; // dev-only

        /// <summary>One row in the leagues list — the presenter supplies a preformatted label. The
        /// optional <see cref="Badge"/> is the Phase 12.1 count of transfer negotiations waiting on this
        /// coach: an unanswered offer expires when the round resolves, so it belongs where he lands.</summary>
        public readonly struct LeagueRow
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string Badge;
            public LeagueRow(string id, string label, string badge = null)
            {
                Id = id;
                Label = label;
                Badge = badge;
            }
        }

        public LeagueListView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthMedium);
            Root.Add(col);

            _title = UiKit.ScreenTitle(string.Empty);
            _title.style.marginBottom = UiKit.SpaceSm;
            col.Add(_title);

            // ---- join by code ------------------------------------------------------------------
            VisualElement joinPanel = UiKit.Panel();
            col.Add(joinPanel);
            _joinCaption = UiKit.SectionLabel(string.Empty);
            _joinCaption.style.marginTop = 0;
            joinPanel.Add(_joinCaption);

            VisualElement joinRow = UiKit.Row();
            joinPanel.Add(joinRow);
            _codeField = new TextField { maxLength = 16 };
            _codeField.style.flexGrow = 1f;
            _codeField.style.flexShrink = 1f;
            _codeField.style.minHeight = 34;
            _codeField.style.marginLeft = 0;
            _codeField.style.marginRight = 0;
            _codeField.style.marginTop = 0;
            _codeField.style.marginBottom = 0;
            joinRow.Add(_codeField);
            _joinButton = UiKit.SmallButton(string.Empty, () => JoinClicked?.Invoke(), 120f);
            joinRow.Add(_joinButton);

            // ---- my leagues --------------------------------------------------------------------
            VisualElement listPanel = UiKit.Panel(grow: true);
            col.Add(listPanel);
            _myLeaguesCaption = UiKit.SectionLabel(string.Empty);
            _myLeaguesCaption.style.marginTop = 0;
            listPanel.Add(_myLeaguesCaption);
            _list = UiKit.ListScroll();
            listPanel.Add(_list);
            // Shown INSIDE the list when the account has no leagues yet (added on demand in SetLeagues).
            _emptyLabel = UiKit.PanelLine(string.Empty);
            _emptyLabel.style.color = UiKit.TextMuted;

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            // Dev-only shortcut: seed a ready test league (hidden unless DevFlags.OnlineTestTools).
            _devSeedButton = UiKit.FooterButton(string.Empty, () => CreateTestLeagueClicked?.Invoke());
            _devSeedButton.style.display = DisplayStyle.None;
            footer.Add(_devSeedButton);
            _createButton = UiKit.FooterPrimaryButton(string.Empty, () => CreateClicked?.Invoke());
            footer.Add(_createButton);
            col.Add(footer);

            UpdateTexts();
        }

        public string JoinCode => (_codeField.value ?? string.Empty).Trim();

        public void SetLeagues(IReadOnlyList<LeagueRow> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                _list.Add(_emptyLabel);
                return;
            }

            foreach (LeagueRow row in rows)
            {
                string id = row.Id;
                VisualElement card = UiKit.RowCard();
                UiKit.EnsureTapTarget(card);

                var label = new Label(row.Label ?? string.Empty);
                label.style.fontSize = 15;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = UiKit.TextPrimary;
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.pickingMode = PickingMode.Ignore;
                card.Add(label);

                if (!string.IsNullOrEmpty(row.Badge))
                {
                    Label badge = UiKit.Pill(row.Badge, UiKit.Accent, UiKit.TextPrimary);
                    badge.style.fontSize = 12;
                    badge.style.marginLeft = UiKit.SpaceSm;
                    badge.style.flexShrink = 0f;
                    badge.pickingMode = PickingMode.Ignore;
                    card.Add(badge);
                }

                var chevron = new Label("›");
                chevron.style.fontSize = 20;
                chevron.style.color = UiKit.TextMuted;
                chevron.style.marginLeft = UiKit.SpaceSm;
                chevron.style.flexShrink = 0f;
                chevron.pickingMode = PickingMode.Ignore;
                card.Add(chevron);

                card.RegisterCallback<ClickEvent>(_ => LeagueSelected?.Invoke(id));
                _list.Add(card);
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
