using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One auction lot row as the view renders it — the presenter formats every string and
    /// resolves the reparto colour group.</summary>
    public sealed class AuctionRowVm
    {
        public string AuctionId;
        public int PlayerExternalId;
        public string PlayerName;
        /// <summary>0 GK · 1 def · 2 mid · 3 att → the role chip's colour.</summary>
        public int RoleGroup;
        public string RoleAbbr;
        public int Age;
        public int Overall;
        public string PriceInfo;  // "Base €300k · Offerta €1.1M"
        public string LeaderInfo; // "Offerta di Milano FC" / "Nessuna offerta"
        public string Countdown;  // "0:45" / "chiuso"
        /// <summary>Short badge on the right of the name: leading / outbid / won. Null = none.</summary>
        public string Badge;
        /// <summary>0 none · 1 you lead (green) · 2 you were outbid (red) · 3 you won him (accent).</summary>
        public int BadgeKind;
        public bool CanBid;       // window open AND lot still open AND you are not already leading
        public bool Dimmed;       // settled / unsold → greyed
        public bool Favorite;     // starred: followed without necessarily having bid
        public bool CanFavorite = true;
        /// <summary>Task 12.2 — a second action carried by the row itself ("put him up" on your own squad,
        /// "take it back" on a lot of yours nobody has bid on). Null = no action.</summary>
        public string ActionLabel;
        public Action RowAction;
    }

    /// <summary>A block of lots under a caption (used by the "my bids" tab: leading / outbid / bought).</summary>
    public sealed class AuctionGroupVm
    {
        public string Caption;
        public IReadOnlyList<AuctionRowVm> Rows;
        /// <summary>Shown when the block has no rows (null = hide the block entirely).</summary>
        public string EmptyText;
    }

    /// <summary>
    /// Online auction screen (task 8.5b), reworked so the room is readable: the budget picture as tiles
    /// (total · committed on lots you lead · what is actually left to bid), three tabs — every LOT, YOUR
    /// activity (leading / outbid / bought) and your FOLLOWED players — rows that carry the reparto colour
    /// and say plainly who bid and how much, and a bid control that raises in round steps instead of asking
    /// for a figure to the euro. Dumb view: the presenter owns all state, polls, and validates server-side.
    /// </summary>
    public sealed class AuctionView
    {
        public event Action OpenWindowClicked;
        public event Action CloseWindowClicked;
        public event Action RefreshClicked;
        public event Action BotBidClicked; // dev-only
        public event Action<int> TabSelected;                 // 0 lots, 1 mine, 2 followed
        public event Action<string> BidClicked;               // auctionId to bid on
        public event Action<int> FavoriteToggled;             // player external id
        public event Action<string, long> BidConfirmClicked;  // auctionId, amount
        public event Action BidCancelClicked;
        /// <summary>Task 12.2 — a value was tapped in the one-tap choice row (the auction's duration).</summary>
        public event Action<int> ChoiceClicked;
        public event Action ChoiceCancelClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _header;
        private readonly VisualElement _budgetTile;
        private readonly VisualElement _committedTile;
        private readonly VisualElement _availableTile;
        private readonly Label _banner;
        private readonly Button _openButton;
        private readonly Button _closeButton;
        private readonly Button _refreshButton;
        private readonly Button _botBidButton; // dev-only
        private readonly Button[] _tabs;
        private readonly string[] _tabKeys;
        private readonly ScrollView _lotList;
        private readonly BidPanel _bid;
        private readonly Label _status;
        private readonly Button _backButton;

        // Task 12.2: the one-tap choice row (how long the auction runs). Keyboard-free, like the ranked
        // report panel and the private-league contract-length picker.
        private readonly VisualElement _choicePanel;
        private readonly Label _choiceTitle;
        private readonly VisualElement _choiceRow;
        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly Button _choiceCancel;

        /// <summary>The tabs, as loc keys. The private-league board has three (all lots / my bids /
        /// followed); the ranked board adds a fourth for selling your own players (task 12.2). The view
        /// only knows how many there are and what they are called.</summary>
        public AuctionView(Func<string, string> tr, Func<long, string> money, string[] tabKeys = null)
        {
            _tr = tr;
            _tabKeys = tabKeys != null && tabKeys.Length > 0
                ? tabKeys
                : new[] { "auction.tab_lots", "auction.tab_mine", "auction.tab_followed" };

            Root = UiKit.ScreenRoot();
            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            // ---- budget: the answer to "how much can I actually bid?" ---------------------------
            VisualElement tiles = UiKit.TileRow();
            _budgetTile = UiKit.StatTile(tr("auction.tile_budget"), string.Empty, null, 180f);
            _committedTile = UiKit.StatTile(tr("auction.tile_committed"), string.Empty, UiKit.Warning, 180f);
            _availableTile = UiKit.StatTile(tr("auction.tile_available"), string.Empty, UiKit.Accent, 180f);
            tiles.Add(_budgetTile);
            tiles.Add(_committedTile);
            tiles.Add(_availableTile);
            col.Add(tiles);

            // ---- window state + controls ---------------------------------------------------------
            VisualElement head = UiKit.Panel();
            col.Add(head);
            _banner = UiKit.PanelLine(string.Empty);
            _banner.style.fontSize = 15;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(_banner);

            VisualElement controls = UiKit.Toolbar();
            controls.style.marginTop = UiKit.SpaceSm;
            controls.style.marginBottom = 0;
            head.Add(controls);
            _openButton = Control(controls, () => OpenWindowClicked?.Invoke(), 160f);
            UiKit.SetSmallButtonAccent(_openButton, true);
            _closeButton = Control(controls, () => CloseWindowClicked?.Invoke(), 160f);
            _refreshButton = Control(controls, () => RefreshClicked?.Invoke(), 120f);
            _botBidButton = Control(controls, () => BotBidClicked?.Invoke(), 150f);
            _botBidButton.style.display = DisplayStyle.None; // dev-only, shown by the presenter

            // ---- tabs -----------------------------------------------------------------------------
            VisualElement tabRow = UiKit.Toolbar();
            _tabs = new Button[_tabKeys.Length];
            for (int i = 0; i < _tabKeys.Length; i++)
            {
                int index = i;
                _tabs[i] = UiKit.TabButton(tr(_tabKeys[i]), () => TabSelected?.Invoke(index));
                tabRow.Add(_tabs[i]);
            }
            _tabs[_tabs.Length - 1].style.marginRight = 0;
            col.Add(tabRow);

            VisualElement listPanel = UiKit.Panel(grow: true);
            col.Add(listPanel);
            _lotList = UiKit.ListScroll();
            listPanel.Add(_lotList);

            // ---- bid control ------------------------------------------------------------------------
            _bid = new BidPanel(money);
            _bid.Confirmed += (id, amount) => BidConfirmClicked?.Invoke(id, amount);
            _bid.Cancelled += () => BidCancelClicked?.Invoke();
            col.Add(_bid.Root);

            // ---- one-tap choice row (task 12.2: the auction's duration) -----------------------------
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
            col.Add(_status);

            VisualElement footer = UiKit.FooterBar();
            _backButton = UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke());
            footer.Add(_backButton);
            col.Add(footer);

            SetActiveTab(0);
            UpdateTexts();
        }

        private static Button Control(VisualElement parent, Action onClick, float minWidth)
        {
            Button b = UiKit.SmallButton(string.Empty, onClick, minWidth);
            b.style.marginLeft = 0;
            b.style.marginRight = 6;
            b.style.marginBottom = 4;
            parent.Add(b);
            return b;
        }

        // --- presenter API -------------------------------------------------------------------------

        public void SetHeader(string text) => _header.text = text;
        public void SetStatus(string text) => _status.text = text;

        /// <summary>The three budget figures, already formatted.</summary>
        public void SetBudget(string budget, string committed, string available)
        {
            UiKit.SetStatTileValue(_budgetTile, budget);
            UiKit.SetStatTileValue(_committedTile, committed, UiKit.Warning);
            UiKit.SetStatTileValue(_availableTile, available, UiKit.Accent);
        }

        public void SetActiveTab(int index)
        {
            for (int i = 0; i < _tabs.Length; i++)
                UiKit.SetTabActive(_tabs[i], i == index);
        }

        /// <summary>Shows the dev-only "bots bid" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _botBidButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>The window banner text + whether it is open (drives the open/close/refresh controls).</summary>
        public void SetWindow(string banner, bool creator, bool windowOpen)
        {
            _banner.text = banner;
            _banner.style.color = windowOpen ? UiKit.Positive : UiKit.TextMuted;
            _openButton.style.display = creator && !windowOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _closeButton.style.display = creator && windowOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Renders the current tab as one or more captioned blocks of lots.</summary>
        public void SetGroups(IReadOnlyList<AuctionGroupVm> groups)
        {
            _lotList.Clear();
            if (groups == null) return;

            int index = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                AuctionGroupVm group = groups[g];
                bool empty = group.Rows == null || group.Rows.Count == 0;
                if (empty && string.IsNullOrEmpty(group.EmptyText)) continue;

                if (!string.IsNullOrEmpty(group.Caption))
                {
                    Label caption = UiKit.SectionLabel(group.Caption);
                    caption.style.marginTop = g == 0 ? 0 : UiKit.SpaceSm;
                    _lotList.Add(caption);
                }

                if (empty)
                {
                    Label none = UiKit.PanelLine(group.EmptyText);
                    none.style.color = UiKit.TextMuted;
                    none.style.marginBottom = UiKit.SpaceXs;
                    _lotList.Add(none);
                    continue;
                }

                foreach (AuctionRowVm vm in group.Rows)
                    _lotList.Add(BuildRow(vm, index++));
            }
        }

        /// <summary>Opens the bid control for a lot.</summary>
        public void ShowBidPanel(BidPanelVm vm)
        {
            HideChoicePanel();
            _bid.Show(vm);
        }

        public void HideBidPanel() => _bid.Hide();

        public bool BidPanelOpen => _bid.IsOpen;

        /// <summary>Opens the one-tap choice row (task 12.2). <paramref name="values"/> and
        /// <paramref name="labels"/> are parallel: the tapped value comes back on
        /// <see cref="ChoiceClicked"/>.</summary>
        public void ShowChoicePanel(string title, IReadOnlyList<int> values, IReadOnlyList<string> labels,
            string cancelLabel)
        {
            HideBidPanel();
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

        public void SetBusy(bool busy)
        {
            _refreshButton.SetEnabled(!busy);
            _openButton.SetEnabled(!busy);
            _closeButton.SetEnabled(!busy);
            _botBidButton.SetEnabled(!busy);
            _bid.SetBusy(busy);
            foreach (Button b in _choiceButtons) b.SetEnabled(!busy);
            _choiceCancel.SetEnabled(!busy);
        }

        public void UpdateTexts()
        {
            _openButton.text = _tr("auction.open_window");
            _closeButton.text = _tr("auction.close_window");
            _refreshButton.text = _tr("auction.refresh");
            _botBidButton.text = _tr("auction.bot_bid");
            _backButton.text = _tr("common.back");
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].text = _tr(_tabKeys[i]);
        }

        // --- rows ------------------------------------------------------------------------------------

        private VisualElement BuildRow(AuctionRowVm vm, int index)
        {
            string auctionId = vm.AuctionId;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 56;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            OnlineTableKit.Stripe(row, vm.BadgeKind == 1 || vm.BadgeKind == 3, index);
            if (vm.Dimmed) row.style.opacity = 0.55f;

            // Reparto colour first: the list reads as a squad, not as a spreadsheet.
            VisualElement chip = PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup, 46f);
            chip.style.height = 30;
            chip.style.marginRight = UiKit.SpaceSm;
            chip.pickingMode = PickingMode.Ignore;
            row.Add(chip);

            var info = new VisualElement();
            info.style.flexGrow = 1f;
            info.style.flexShrink = 1f;
            info.style.minWidth = 0f;
            row.Add(info);

            VisualElement nameRow = UiKit.Row();
            info.Add(nameRow);
            var name = new Label(vm.PlayerName ?? string.Empty);
            name.style.fontSize = 14;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = UiKit.TextPrimary;
            name.style.flexShrink = 1f;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            nameRow.Add(name);

            var meta = new Label(string.Format(_tr("auction.row_meta"), vm.Age, vm.Overall));
            meta.style.fontSize = 12;
            meta.style.color = UiKit.TextMuted;
            meta.style.marginLeft = UiKit.SpaceSm;
            meta.style.flexShrink = 0f;
            nameRow.Add(meta);

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
                nameRow.Add(badge);
            }

            var price = new Label(vm.PriceInfo ?? string.Empty);
            price.style.fontSize = 13;
            price.style.color = UiKit.TextPrimary;
            info.Add(price);

            var leader = new Label(vm.LeaderInfo ?? string.Empty);
            leader.style.fontSize = 12;
            leader.style.color = vm.BadgeKind == 1 ? UiKit.Positive : UiKit.TextMuted;
            info.Add(leader);

            // Right-hand column: the clock, then the actions.
            var right = new VisualElement();
            right.style.alignItems = Align.FlexEnd;
            right.style.flexShrink = 0f;
            row.Add(right);

            var clock = new Label(vm.Countdown ?? string.Empty);
            clock.style.fontSize = 12;
            clock.style.color = UiKit.TextMuted;
            clock.style.marginBottom = 3;
            right.Add(clock);

            VisualElement actions = UiKit.Row();
            right.Add(actions);

            if (vm.CanFavorite)
            {
                int playerId = vm.PlayerExternalId;
                Button star = UiKit.SmallButton(vm.Favorite ? "★" : "☆", () => FavoriteToggled?.Invoke(playerId), 44f);
                star.tooltip = _tr(vm.Favorite ? "auction.unfollow" : "auction.follow");
                UiKit.SetSmallButtonOn(star, vm.Favorite);
                actions.Add(star);
            }

            if (vm.CanBid && !string.IsNullOrEmpty(auctionId))
            {
                Button bid = UiKit.SmallButton(_tr("auction.bid"), () => BidClicked?.Invoke(auctionId), 96f);
                UiKit.SetSmallButtonAccent(bid, true);
                actions.Add(bid);
            }

            // Task 12.2: the row's own action — "put him up" on the sell tab, "take it back" on a lot of
            // yours nobody has bid on yet.
            if (!string.IsNullOrEmpty(vm.ActionLabel))
            {
                Action act = vm.RowAction;
                Button b = UiKit.SmallButton(vm.ActionLabel, () => act?.Invoke(), 120f);
                UiKit.SetSmallButtonAccent(b, !vm.CanBid);
                actions.Add(b);
            }

            return row;
        }
    }
}
