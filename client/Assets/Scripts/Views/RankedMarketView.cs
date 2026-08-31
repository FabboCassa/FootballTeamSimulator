using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked market (Phase 9.2b): direct OFFERS (incoming/outgoing) and BROWSE (a rival
    /// club's squad, to offer for a player). The AUCTIONS left this screen in task 12.2 — a board where you
    /// can also SELL, on a timer of your own, needed the auction room the private leagues already had, so
    /// the tab became a button that opens it. The presenter formats
    /// every string, owns the state, polls, and validates server-side; the view only emits events and renders
    /// the rows it is handed. An inline amount panel serves both bidding and offering.
    ///
    /// On the shared page scaffold: a status panel, segmented tabs, one panel of striped rows filling the
    /// page, the inline panels above the footer, and Back / Refresh in the footer.
    /// </summary>
    public sealed class RankedMarketView
    {
        // Per-row actions (bid / offer / accept / reject / withdraw / view squad) are carried by
        // <see cref="RowVm"/> callbacks, so the view only needs the screen-level events here.
        public event Action<int> TabSelected;          // 0 offers, 1 browse
        /// <summary>Task 12.2 — open the auction board (its own screen since the lots got their own timers).</summary>
        public event Action AuctionsClicked;
        public event Action RefreshClicked;
        public event Action BotMarketClicked;          // dev-only
        public event Action BackClicked;
        public event Action<long> AmountConfirmClicked;
        public event Action AmountCancelClicked;
        /// <summary>A reason was picked in the report panel (Phase 9.5) — the value is the server's
        /// RankedReportReason integer.</summary>
        public event Action<int> ReportReasonClicked;
        public event Action ReportCancelClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _header;
        private readonly Label _budget;
        private readonly Label _banner;
        private readonly Button _tabOffers;
        private readonly Button _tabBrowse;
        private readonly Button _auctionsButton;
        private readonly Button _refreshButton;
        private readonly Button _botButton; // dev-only
        private readonly ScrollView _list;
        private readonly Label _status;
        private readonly Button _backButton;

        // Inline bid/offer control: round raises sized to the lot, never a figure to the euro.
        private readonly BidPanel _bid;

        // Inline report panel (Phase 9.5): pick a reason, no keyboard.
        private readonly VisualElement _reportPanel;
        private readonly Label _reportTitle;
        private readonly List<Button> _reportReasons = new List<Button>();
        private readonly Button _reportCancel;

        /// <summary>The report reasons, in the server's enum order (Collusion, Inactivity, OffensiveName,
        /// Cheating, Other) — the view only knows their loc keys and their integer values.</summary>
        private static readonly string[] ReasonKeys =
        {
            "ranked.report.reason_collusion",
            "ranked.report.reason_inactivity",
            "ranked.report.reason_name",
            "ranked.report.reason_cheating",
            "ranked.report.reason_other",
        };

        /// <summary>A generic market row: a title + detail line, plus up to two actions.</summary>
        public sealed class RowVm
        {
            public string Title;
            public string Detail;
            /// <summary>0 GK · 1 def · 2 mid · 3 att → the reparto colour chip; -1 = no chip.</summary>
            public int RoleGroup = -1;
            public string RoleAbbr;
            /// <summary>Short state badge next to the title (leading / outbid / pending). Null = none.</summary>
            public string Badge;
            /// <summary>0 neutral · 1 good (green) · 2 bad (red) · 3 accent.</summary>
            public int BadgeKind;
            public string PrimaryLabel;      // null = no primary action
            public Action PrimaryAction;
            public string SecondaryLabel;    // null = no secondary action
            public Action SecondaryAction;
            public bool Dimmed;
        }

        public RankedMarketView(Func<string, string> tr, Func<long, string> money)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            // ---- budget + window ----------------------------------------------------------------
            VisualElement head = UiKit.Panel();
            col.Add(head);
            _budget = UiKit.PanelLine(string.Empty);
            _budget.style.fontSize = 15;
            _budget.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(_budget);
            _banner = UiKit.Caption(string.Empty);
            _banner.style.whiteSpace = WhiteSpace.Normal;
            head.Add(_banner);

            VisualElement controls = UiKit.Toolbar();
            controls.style.marginTop = UiKit.SpaceSm;
            controls.style.marginBottom = 0;
            head.Add(controls);
            // The way into the auction room (task 12.2): the lots are no longer a list on this screen.
            _auctionsButton = UiKit.SmallButton(string.Empty, () => AuctionsClicked?.Invoke(), 160f);
            _auctionsButton.style.marginLeft = 0;
            UiKit.SetSmallButtonAccent(_auctionsButton, true);
            controls.Add(_auctionsButton);
            _botButton = UiKit.SmallButton(string.Empty, () => BotMarketClicked?.Invoke(), 160f);
            _botButton.style.display = DisplayStyle.None; // dev-only
            controls.Add(_botButton);

            // ---- tabs -----------------------------------------------------------------------------
            VisualElement tabs = UiKit.Toolbar();
            _tabOffers = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(0));
            _tabBrowse = UiKit.TabButton(string.Empty, () => TabSelected?.Invoke(1));
            _tabBrowse.style.marginRight = 0;
            tabs.Add(_tabOffers);
            tabs.Add(_tabBrowse);
            col.Add(tabs);

            VisualElement listPanel = UiKit.Panel(grow: true);
            col.Add(listPanel);
            _list = UiKit.ListScroll();
            listPanel.Add(_list);

            // ---- inline bid/offer control -----------------------------------------------------------
            _bid = new BidPanel(money);
            _bid.Confirmed += (_, amount) => AmountConfirmClicked?.Invoke(amount);
            _bid.Cancelled += () => AmountCancelClicked?.Invoke();
            col.Add(_bid.Root);

            // ---- inline report panel (Phase 9.5) ----------------------------------------------------
            // One tap per reason, deliberately keyboard-free so it works the same on a phone as on a desktop.
            _reportPanel = UiKit.Panel();
            _reportPanel.style.display = DisplayStyle.None;
            col.Add(_reportPanel);
            _reportTitle = UiKit.PanelLine(string.Empty);
            _reportPanel.Add(_reportTitle);
            VisualElement reasons = UiKit.Toolbar();
            reasons.style.marginTop = UiKit.SpaceXs;
            reasons.style.marginBottom = 0;
            _reportPanel.Add(reasons);
            for (int i = 0; i < ReasonKeys.Length; i++)
            {
                int reason = i;
                Button b = UiKit.SmallButton(string.Empty, () => ReportReasonClicked?.Invoke(reason), 130f);
                b.style.marginLeft = 0;
                b.style.marginRight = 6;
                b.style.marginBottom = 4;
                _reportReasons.Add(b);
                reasons.Add(b);
            }
            _reportCancel = UiKit.SmallButton(string.Empty, () => ReportCancelClicked?.Invoke(), 110f);
            _reportCancel.style.marginLeft = 0;
            _reportCancel.style.marginBottom = 4;
            reasons.Add(_reportCancel);

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

        public void SetActiveTab(int tab)
        {
            UiKit.SetTabActive(_tabOffers, tab == 0);
            UiKit.SetTabActive(_tabBrowse, tab == 1);
        }

        public void SetRows(IReadOnlyList<RowVm> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                Label empty = UiKit.PanelLine(_tr("ranked.market.empty"));
                empty.style.color = UiKit.TextMuted;
                _list.Add(empty);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RowVm vm = rows[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 46;
                row.style.flexShrink = 0f;
                row.style.paddingLeft = UiKit.SpaceSm;
                row.style.paddingRight = UiKit.SpaceSm;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                OnlineTableKit.Stripe(row, vm.BadgeKind == 1, i);
                if (vm.Dimmed) row.style.opacity = 0.6f;

                // Reparto colour first, same language as every other player list in the game.
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

                if (vm.PrimaryLabel != null || vm.SecondaryLabel != null)
                {
                    var actions = new VisualElement();
                    actions.style.flexDirection = FlexDirection.Row;
                    actions.style.alignItems = Align.Center;
                    actions.style.flexShrink = 0f;
                    row.Add(actions);

                    if (vm.PrimaryLabel != null)
                    {
                        Action p = vm.PrimaryAction;
                        Button b = UiKit.SmallButton(vm.PrimaryLabel, () => p?.Invoke(), 100f);
                        UiKit.SetSmallButtonAccent(b, true);
                        actions.Add(b);
                    }
                    if (vm.SecondaryLabel != null)
                    {
                        Action s = vm.SecondaryAction;
                        actions.Add(UiKit.SmallButton(vm.SecondaryLabel, () => s?.Invoke(), 100f));
                    }
                }

                _list.Add(row);
            }
        }

        /// <summary>Opens the inline bid/offer control on a lot or a player.</summary>
        public void ShowAmountPanel(BidPanelVm vm) => _bid.Show(vm);

        public void HideAmountPanel() => _bid.Hide();

        /// <summary>Opens the report panel for one club (Phase 9.5). <paramref name="title"/> already names
        /// the club, so the panel needs no other context.</summary>
        public void ShowReportPanel(string title)
        {
            _reportTitle.text = title;
            _reportPanel.style.display = DisplayStyle.Flex;
        }

        public void HideReportPanel() => _reportPanel.style.display = DisplayStyle.None;

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
            _botButton.SetEnabled(!busy);
            _auctionsButton.SetEnabled(!busy);
            _bid.SetBusy(busy);
            foreach (Button b in _reportReasons) b.SetEnabled(!busy);
        }

        /// <summary>Shows the dev-only "bots react" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _botButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void UpdateTexts()
        {
            _header.text = _tr("ranked.market.title");
            _auctionsButton.text = _tr("ranked.market.open_auctions");
            _tabOffers.text = _tr("ranked.market.tab_offers");
            _tabBrowse.text = _tr("ranked.market.tab_browse");
            _refreshButton.text = _tr("ranked.refresh");
            _botButton.text = _tr("ranked.market.dev_bots");
            for (int i = 0; i < _reportReasons.Count; i++) _reportReasons[i].text = _tr(ReasonKeys[i]);
            _reportCancel.text = _tr("ranked.market.cancel");
            _backButton.text = _tr("common.back");
        }
    }
}
