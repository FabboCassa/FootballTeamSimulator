using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the private-league transfer market (Phase 12.1b). Four tabs — TRATTATIVE (the
    /// negotiations, the ones waiting on YOU first, with the recent deals underneath), COMPRA (browse a
    /// club, then offer for a player), VENDI (your own squad, on and off the transfer list) and SVINCOLATI
    /// (agree terms with an unattached player).
    ///
    /// Built on the same scaffold as the ranked market so the two online markets read the same: a status
    /// panel, segmented tabs, one panel of striped rows, the inline panels above the footer. Two shared
    /// inline controls do all the input: <see cref="BidPanel"/> for any figure (an offer, a counter, an
    /// asking price, a wage) and a one-tap choice row for the contract length — no keyboard anywhere, so a
    /// phone and a desktop behave identically.
    ///
    /// The presenter formats every string, owns the state and validates server-side; the view only emits
    /// events and renders the rows it is handed. Rows carry up to THREE actions because a live negotiation
    /// genuinely has three answers (accept / counter / refuse), which is what separates this market from
    /// the ranked one.
    /// </summary>
    public sealed class LeagueMarketView
    {
        public event Action<int> TabSelected;      // 0 negotiations · 1 buy · 2 sell · 3 free agents
        public event Action RefreshClicked;
        public event Action BackClicked;
        public event Action<long> AmountConfirmClicked;
        public event Action AmountCancelClicked;
        /// <summary>A contract length was picked in the choice row (the value is the number of seasons).</summary>
        public event Action<int> ChoiceClicked;
        public event Action ChoiceCancelClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _header;
        private readonly Label _budget;
        private readonly Label _banner;
        private readonly Button _tabOffers;
        private readonly Button _tabBuy;
        private readonly Button _tabSell;
        private readonly Button _tabFree;
        private readonly ScrollView _list;
        private readonly Label _status;
        private readonly Button _refreshButton;
        private readonly Button _backButton;

        private readonly BidPanel _amount;

        // The one-tap choice row (contract length). Deliberately keyboard-free, like the ranked report panel.
        private readonly VisualElement _choicePanel;
        private readonly Label _choiceTitle;
        private readonly VisualElement _choiceRow;
        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly Button _choiceCancel;

        /// <summary>A market row. A HEADER row is a section title inside the list — it carries no actions
        /// and no chip, and it is what lets one tab hold "waiting on you", "sent" and "recent deals"
        /// without pretending they are the same kind of thing.</summary>
        public sealed class RowVm
        {
            public string Title;
            public string Detail;
            public bool IsHeader;
            /// <summary>0 GK · 1 def · 2 mid · 3 att → the reparto colour chip; -1 = no chip.</summary>
            public int RoleGroup = -1;
            public string RoleAbbr;
            /// <summary>Short state badge next to the title. Null = none.</summary>
            public string Badge;
            /// <summary>0 neutral · 1 good (green) · 2 bad (red) · 3 accent.</summary>
            public int BadgeKind;
            public string PrimaryLabel;
            public Action PrimaryAction;
            public string SecondaryLabel;
            public Action SecondaryAction;
            public string TertiaryLabel;
            public Action TertiaryAction;
            public bool Dimmed;
            /// <summary>Draws attention to a row that needs an answer.</summary>
            public bool Highlight;
        }

        public LeagueMarketView(Func<string, string> tr, Func<long, string> money)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            // ---- budget + window ------------------------------------------------------------------
            VisualElement head = UiKit.Panel();
            head.style.flexShrink = 0f;
            col.Add(head);
            _budget = UiKit.PanelLine(string.Empty);
            _budget.style.fontSize = 15;
            _budget.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(_budget);
            _banner = UiKit.Caption(string.Empty);
            _banner.style.whiteSpace = WhiteSpace.Normal;
            head.Add(_banner);

            // ---- tabs -------------------------------------------------------------------------------
            VisualElement tabs = UiKit.Toolbar();
            tabs.style.flexShrink = 0f;
            _tabOffers = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(0));
            _tabBuy = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(1));
            _tabSell = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(2));
            _tabFree = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(3));
            _tabFree.style.marginRight = 0;
            tabs.Add(_tabOffers);
            tabs.Add(_tabBuy);
            tabs.Add(_tabSell);
            tabs.Add(_tabFree);
            col.Add(tabs);

            // The list is the only thing that gives when the page is short (the 11.1b setup-screen lesson):
            // everything else is flexShrink = 0, so a squashed label can never paint over the footer.
            VisualElement listPanel = UiKit.Panel(grow: true);
            listPanel.style.minHeight = 0f;
            col.Add(listPanel);
            _list = UiKit.ListScroll();
            listPanel.Add(_list);

            // ---- inline amount control (offer / counter / asking price / wage) -----------------------
            _amount = new BidPanel(money);
            _amount.Confirmed += (_, value) => AmountConfirmClicked?.Invoke(value);
            _amount.Cancelled += () => AmountCancelClicked?.Invoke();
            _amount.Root.style.flexShrink = 0f;
            col.Add(_amount.Root);

            // ---- inline choice row (contract length) -------------------------------------------------
            _choicePanel = UiKit.Panel();
            _choicePanel.style.display = DisplayStyle.None;
            _choicePanel.style.flexShrink = 0f;
            col.Add(_choicePanel);
            _choiceTitle = UiKit.PanelLine(string.Empty);
            _choiceTitle.style.whiteSpace = WhiteSpace.Normal;
            _choicePanel.Add(_choiceTitle);
            _choiceRow = UiKit.Toolbar();
            _choiceRow.style.marginTop = UiKit.SpaceXs;
            _choiceRow.style.marginBottom = 0;
            _choicePanel.Add(_choiceRow);
            _choiceCancel = UiKit.SmallButton(string.Empty, () => ChoiceCancelClicked?.Invoke(), 110f);
            _choiceCancel.style.marginLeft = 0;
            _choiceCancel.style.marginBottom = 4;

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(string.Empty, () => BackClicked?.Invoke());
            footer.Add(_backButton);
            _refreshButton = UiKit.FooterButton(string.Empty, () => RefreshClicked?.Invoke());
            footer.Add(_refreshButton);
            col.Add(footer);

            SetActiveTab(0);
            UpdateTexts();
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetBudget(string text) => _budget.text = text;

        public void SetBanner(string text, bool open)
        {
            _banner.text = text;
            _banner.style.color = open ? UiKit.Positive : UiKit.Warning;
        }

        /// <summary>Puts a count next to the negotiations tab — the same number the leagues list badges.</summary>
        public void SetPendingCount(int count) =>
            _tabOffers.text = count > 0
                ? _tr("league.market.tab_offers") + " (" + count + ")"
                : _tr("league.market.tab_offers");

        public void SetActiveTab(int tab)
        {
            UiKit.SetTabActive(_tabOffers, tab == 0);
            UiKit.SetTabActive(_tabBuy, tab == 1);
            UiKit.SetTabActive(_tabSell, tab == 2);
            UiKit.SetTabActive(_tabFree, tab == 3);
        }

        public void SetRows(IReadOnlyList<RowVm> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                Label empty = UiKit.PanelLine(_tr("league.market.empty"));
                empty.style.color = UiKit.TextMuted;
                _list.Add(empty);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RowVm vm = rows[i];

                if (vm.IsHeader)
                {
                    Label header = UiKit.Caption(vm.Title ?? string.Empty);
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.color = UiKit.TextPrimary;
                    header.style.marginTop = i == 0 ? 0 : UiKit.SpaceSm;
                    header.style.marginLeft = UiKit.SpaceSm;
                    header.style.marginBottom = 2;
                    header.style.flexShrink = 0f;
                    _list.Add(header);
                    continue;
                }

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 46;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = UiKit.SpaceSm;
                row.style.paddingRight = UiKit.SpaceSm;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                OnlineTableKit.Stripe(row, vm.Highlight, i);
                if (vm.Dimmed) row.style.opacity = 0.6f;

                if (vm.RoleGroup >= 0)
                {
                    VisualElement chip = PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup, 46f);
                    chip.style.height = 30;
                    chip.style.marginRight = UiKit.SpaceSm;
                    row.Add(chip);
                }

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.minWidth = 0f;
                row.Add(text);

                VisualElement titleRow = UiKit.Row();
                text.Add(titleRow);
                var title = new Label(vm.Title ?? string.Empty);
                title.style.fontSize = 14;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = UiKit.TextPrimary;
                title.style.flexShrink = 1f;
                title.style.whiteSpace = WhiteSpace.Normal;
                titleRow.Add(title);

                if (!string.IsNullOrEmpty(vm.Badge))
                {
                    Color background =
                        vm.BadgeKind == 1 ? UiKit.AccentDark :
                        vm.BadgeKind == 2 ? UiKit.Danger :
                        vm.BadgeKind == 3 ? UiKit.Accent : UiKit.SurfaceAlt;
                    Label badge = UiKit.Pill(vm.Badge, background, UiKit.TextPrimary);
                    badge.style.fontSize = 11;
                    badge.style.marginLeft = UiKit.SpaceSm;
                    badge.style.flexShrink = 0f;
                    titleRow.Add(badge);
                }

                if (!string.IsNullOrEmpty(vm.Detail))
                {
                    var detail = new Label(vm.Detail);
                    detail.style.fontSize = 13;
                    detail.style.color = UiKit.TextMuted;
                    detail.style.whiteSpace = WhiteSpace.Normal;
                    text.Add(detail);
                }

                if (vm.PrimaryLabel != null || vm.SecondaryLabel != null || vm.TertiaryLabel != null)
                {
                    var actions = new VisualElement();
                    // Three answers do not fit side by side on a phone, so the action block wraps.
                    actions.style.flexDirection = FlexDirection.Row;
                    actions.style.flexWrap = Wrap.Wrap;
                    actions.style.justifyContent = Justify.FlexEnd;
                    actions.style.alignItems = Align.Center;
                    actions.style.flexShrink = 0f;
                    actions.style.maxWidth = 220;
                    row.Add(actions);

                    if (vm.PrimaryLabel != null)
                    {
                        Action p = vm.PrimaryAction;
                        Button b = UiKit.SmallButton(vm.PrimaryLabel, () => p?.Invoke(), 96f);
                        UiKit.SetSmallButtonAccent(b, true);
                        actions.Add(b);
                    }
                    if (vm.SecondaryLabel != null)
                    {
                        Action s = vm.SecondaryAction;
                        actions.Add(UiKit.SmallButton(vm.SecondaryLabel, () => s?.Invoke(), 96f));
                    }
                    if (vm.TertiaryLabel != null)
                    {
                        Action t = vm.TertiaryAction;
                        actions.Add(UiKit.SmallButton(vm.TertiaryLabel, () => t?.Invoke(), 96f));
                    }
                }

                _list.Add(row);
            }
        }

        /// <summary>Opens the inline figure control (an offer, a counter, an asking price, a wage).</summary>
        public void ShowAmountPanel(BidPanelVm vm)
        {
            HideChoicePanel();
            _amount.Show(vm);
        }

        public void HideAmountPanel() => _amount.Hide();

        /// <summary>Opens the one-tap choice row. <paramref name="values"/> and <paramref name="labels"/>
        /// are parallel: the tapped value comes back on <see cref="ChoiceClicked"/>.</summary>
        public void ShowChoicePanel(string title, IReadOnlyList<int> values, IReadOnlyList<string> labels,
            string cancelLabel)
        {
            HideAmountPanel();
            _choiceTitle.text = title;
            _choiceRow.Clear();
            _choiceButtons.Clear();
            for (int i = 0; i < values.Count; i++)
            {
                int value = values[i];
                Button b = UiKit.SmallButton(labels[i], () => ChoiceClicked?.Invoke(value), 90f);
                b.style.marginLeft = 0;
                b.style.marginRight = 6;
                b.style.marginBottom = 4;
                _choiceButtons.Add(b);
                _choiceRow.Add(b);
            }
            _choiceCancel.text = cancelLabel;
            _choiceRow.Add(_choiceCancel);
            _choicePanel.style.display = DisplayStyle.Flex;
        }

        public void HideChoicePanel() => _choicePanel.style.display = DisplayStyle.None;

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _refreshButton.SetEnabled(!busy);
            _amount.SetBusy(busy);
            foreach (Button b in _choiceButtons) b.SetEnabled(!busy);
            _choiceCancel.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _header.text = _tr("league.market.title");
            _tabOffers.text = _tr("league.market.tab_offers");
            _tabBuy.text = _tr("league.market.tab_buy");
            _tabSell.text = _tr("league.market.tab_sell");
            _tabFree.text = _tr("league.market.tab_free");
            _refreshButton.text = _tr("lobby.refresh");
            _backButton.text = _tr("common.back");
        }
    }
}
