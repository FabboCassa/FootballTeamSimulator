using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One auction lot row as the view renders it — the presenter formats every string.</summary>
    public sealed class AuctionRowVm
    {
        public string AuctionId;
        public string Title;      // "Sandro Roversi · ST · OVR 78"
        public string PriceInfo;  // "Base €300k · Offerta €1.1M"
        public string LeaderInfo; // "Leader: Milano FC" / "—"
        public string Countdown;  // "0:45" / "chiuso"
        public bool CanBid;       // window open AND lot still open
        public bool Dimmed;       // settled / unsold → greyed
    }

    /// <summary>
    /// Online auction screen (task 8.5b): the current window's free-agent lots, the caller's budget, the
    /// creator's open/close controls, and an inline bid panel. Dumb view — the presenter owns all state,
    /// runs the ~1s polling refresh, formats every label, and validates bids server-side; the view only
    /// emits events and renders the strings it is handed. Reached from the league lobby when the season is
    /// active.
    /// </summary>
    public sealed class AuctionView
    {
        public event Action OpenWindowClicked;
        public event Action CloseWindowClicked;
        public event Action RefreshClicked;
        public event Action BotBidClicked; // dev-only
        public event Action<string> BidClicked;               // auctionId to bid on
        public event Action<string, long> BidConfirmClicked;  // auctionId, amount
        public event Action BidCancelClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Label _budget;
        private readonly Label _banner;
        private readonly Button _openButton;
        private readonly Button _closeButton;
        private readonly Button _refreshButton;
        private readonly Button _botBidButton; // dev-only
        private readonly ScrollView _lotList;
        private readonly Label _status;

        // Inline bid panel.
        private readonly VisualElement _bidPanel;
        private readonly Label _bidTitle;
        private readonly Label _bidInfo;
        private readonly TextField _bidField;
        private readonly Button _bidConfirm;
        private readonly Func<string, string> _tr;

        private string _bidAuctionId;
        private long _bidStep = 25_000;

        public AuctionView(Func<string, string> tr)
        {
            _tr = tr;

            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(720f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceXs;
            col.Add(_header);

            _budget = new Label(string.Empty);
            _budget.style.unityTextAlign = TextAnchor.MiddleCenter;
            _budget.style.fontSize = 14;
            _budget.style.marginBottom = 4;
            col.Add(_budget);

            _banner = new Label(string.Empty);
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.fontSize = 13;
            _banner.style.color = UiKit.TextMuted;
            _banner.style.marginBottom = UiKit.SpaceXs;
            col.Add(_banner);

            // Creator / refresh controls.
            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.justifyContent = Justify.Center;
            controls.style.marginBottom = UiKit.SpaceXs;
            controls.style.flexShrink = 0f;
            _openButton = ControlButton(tr("auction.open_window"), () => OpenWindowClicked?.Invoke());
            _closeButton = ControlButton(tr("auction.close_window"), () => CloseWindowClicked?.Invoke());
            _refreshButton = ControlButton(tr("auction.refresh"), () => RefreshClicked?.Invoke());
            _botBidButton = ControlButton(tr("auction.bot_bid"), () => BotBidClicked?.Invoke());
            _botBidButton.style.display = DisplayStyle.None; // dev-only, shown by the presenter
            controls.Add(_openButton);
            controls.Add(_closeButton);
            controls.Add(_refreshButton);
            controls.Add(_botBidButton);
            col.Add(controls);

            _lotList = new ScrollView();
            _lotList.style.flexGrow = 1f;
            _lotList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_lotList);

            // Inline bid panel (hidden until a lot is chosen).
            _bidPanel = new VisualElement();
            _bidPanel.style.display = DisplayStyle.None;
            _bidPanel.style.marginTop = UiKit.SpaceXs;
            _bidPanel.style.paddingTop = UiKit.SpaceSm;
            _bidPanel.style.paddingBottom = UiKit.SpaceSm;
            _bidPanel.style.paddingLeft = UiKit.SpaceSm;
            _bidPanel.style.paddingRight = UiKit.SpaceSm;
            _bidPanel.style.backgroundColor = UiKit.Surface;
            _bidPanel.style.flexShrink = 0f;
            UiKit.Round(_bidPanel, UiKit.RadiusSm);

            _bidTitle = new Label(string.Empty);
            _bidTitle.style.fontSize = 14;
            _bidTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bidPanel.Add(_bidTitle);

            _bidInfo = new Label(string.Empty);
            _bidInfo.style.fontSize = 12;
            _bidInfo.style.color = UiKit.TextMuted;
            _bidInfo.style.marginBottom = 4;
            _bidPanel.Add(_bidInfo);

            var amountRow = new VisualElement();
            amountRow.style.flexDirection = FlexDirection.Row;
            amountRow.style.alignItems = Align.Center;
            amountRow.Add(StepButton("-", () => Nudge(-1)));
            _bidField = new TextField();
            _bidField.style.flexGrow = 1f;
            _bidField.style.marginLeft = 4;
            _bidField.style.marginRight = 4;
            _bidField.RegisterValueChangedCallback(OnBidFieldChanged);
            amountRow.Add(_bidField);
            amountRow.Add(StepButton("+", () => Nudge(1)));
            _bidPanel.Add(amountRow);

            var bidButtons = new VisualElement();
            bidButtons.style.flexDirection = FlexDirection.Row;
            bidButtons.style.justifyContent = Justify.Center;
            bidButtons.style.marginTop = 6;
            _bidConfirm = ControlButton(tr("auction.confirm_bid"), OnConfirm);
            bidButtons.Add(_bidConfirm);
            bidButtons.Add(ControlButton(tr("auction.cancel"), () => { HideBidPanel(); BidCancelClicked?.Invoke(); }));
            _bidPanel.Add(bidButtons);

            col.Add(_bidPanel);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = UiKit.SpaceSm;
            footer.style.flexShrink = 0f;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.alignSelf = Align.Center;
            _status.style.flexShrink = 0f;
            col.Add(_status);
        }

        // --- presenter API -------------------------------------------------------------------------

        public void SetHeader(string text) => _header.text = text;
        public void SetBudget(string text) => _budget.text = text;
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Shows the dev-only "bots bid" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _botBidButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>The window banner text + whether it is open (drives the open/close/refresh controls).</summary>
        public void SetWindow(string banner, bool creator, bool windowOpen)
        {
            _banner.text = banner;
            _openButton.style.display = creator && !windowOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _closeButton.style.display = creator && windowOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetLots(IReadOnlyList<AuctionRowVm> lots)
        {
            _lotList.Clear();
            foreach (AuctionRowVm vm in lots) _lotList.Add(BuildRow(vm));
        }

        /// <summary>Opens the inline bid panel for a lot. <paramref name="suggested"/> pre-fills the field
        /// (the minimum legal bid); <paramref name="step"/> is the +/- increment.</summary>
        public void ShowBidPanel(string auctionId, string title, string info, long suggested, long step)
        {
            _bidAuctionId = auctionId;
            _bidStep = step > 0 ? step : 25_000;
            _bidTitle.text = title;
            _bidInfo.text = info;
            _bidField.SetValueWithoutNotify(suggested.ToString());
            _bidPanel.style.display = DisplayStyle.Flex;
        }

        public void HideBidPanel()
        {
            _bidAuctionId = null;
            _bidPanel.style.display = DisplayStyle.None;
        }

        // --- internals -----------------------------------------------------------------------------

        private VisualElement BuildRow(AuctionRowVm vm)
        {
            string auctionId = vm.AuctionId;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 6;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.backgroundColor = UiKit.Surface;
            row.style.opacity = vm.Dimmed ? 0.55f : 1f;
            UiKit.Round(row, UiKit.RadiusSm);

            var info = new VisualElement();
            info.style.flexGrow = 1f;
            var title = new Label(vm.Title);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            info.Add(title);
            var price = new Label(vm.PriceInfo);
            price.style.fontSize = 12;
            price.style.color = UiKit.TextMuted;
            info.Add(price);
            var leader = new Label(vm.LeaderInfo);
            leader.style.fontSize = 12;
            leader.style.color = UiKit.TextMuted;
            info.Add(leader);
            row.Add(info);

            var right = new VisualElement();
            right.style.alignItems = Align.FlexEnd;
            right.style.flexShrink = 0f;
            var clock = new Label(vm.Countdown);
            clock.style.fontSize = 12;
            clock.style.marginBottom = 2;
            right.Add(clock);
            if (vm.CanBid)
            {
                var bid = new Button(() => BidClicked?.Invoke(auctionId)) { text = _tr("auction.bid") };
                bid.style.height = 30;
                bid.style.width = 96;
                bid.style.fontSize = 13;
                right.Add(bid);
            }
            row.Add(right);

            return row;
        }

        private void Nudge(int direction)
        {
            long current = ParseAmount();
            long next = current + direction * _bidStep;
            if (next < 0) next = 0;
            _bidField.SetValueWithoutNotify(next.ToString());
        }

        private void OnConfirm()
        {
            if (string.IsNullOrEmpty(_bidAuctionId)) return;
            BidConfirmClicked?.Invoke(_bidAuctionId, ParseAmount());
        }

        private long ParseAmount()
        {
            long value = 0;
            string t = _bidField.value;
            if (!string.IsNullOrEmpty(t))
                foreach (char c in t)
                    if (c >= '0' && c <= '9') value = value * 10 + (c - '0');
            return value;
        }

        // Keep the field digits-only without fighting the caret.
        private void OnBidFieldChanged(ChangeEvent<string> evt)
        {
            string cleaned = string.Empty;
            if (!string.IsNullOrEmpty(evt.newValue))
                foreach (char c in evt.newValue)
                    if (c >= '0' && c <= '9') cleaned += c;
            if (cleaned != evt.newValue) _bidField.SetValueWithoutNotify(cleaned);
        }

        private static Button ControlButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.height = 38;
            button.style.minWidth = 120;
            button.style.fontSize = 14;
            button.style.marginLeft = 4;
            button.style.marginRight = 4;
            return button;
        }

        private static Button StepButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.width = 40;
            button.style.height = 34;
            button.style.fontSize = 18;
            return button;
        }

        private static Button FooterButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.width = 150;
            button.style.height = 44;
            button.style.fontSize = 16;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            return button;
        }
    }
}
