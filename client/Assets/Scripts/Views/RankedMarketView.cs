using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the ranked market (Phase 9.2b): three tabs — free-agent AUCTIONS (bid), direct OFFERS
    /// (incoming/outgoing), and BROWSE (a rival club's squad, to offer for a player). The presenter formats
    /// every string, owns the state, polls, and validates server-side; the view only emits events and renders
    /// the rows it is handed. An inline amount panel serves both bidding and offering.
    /// </summary>
    public sealed class RankedMarketView
    {
        // Per-row actions (bid / offer / accept / reject / withdraw / view squad) are carried by
        // <see cref="RowVm"/> callbacks, so the view only needs the screen-level events here.
        public event Action<int> TabSelected;          // 0 auctions, 1 offers, 2 browse
        public event Action RefreshClicked;
        public event Action BotMarketClicked;          // dev-only
        public event Action BackClicked;
        public event Action<long> AmountConfirmClicked;
        public event Action AmountCancelClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _header;
        private readonly Label _budget;
        private readonly Label _banner;
        private readonly Button _tabAuctions;
        private readonly Button _tabOffers;
        private readonly Button _tabBrowse;
        private readonly Button _refreshButton;
        private readonly Button _botButton; // dev-only
        private readonly ScrollView _list;
        private readonly Label _status;
        private readonly Button _backButton;

        // Inline amount panel (bid or offer).
        private readonly VisualElement _amountPanel;
        private readonly Label _amountTitle;
        private readonly TextField _amountField;
        private readonly Button _amountConfirm;
        private readonly Button _amountCancel;

        /// <summary>A generic market row: a title + detail line, plus up to two actions.</summary>
        public sealed class RowVm
        {
            public string Title;
            public string Detail;
            public string PrimaryLabel;      // null = no primary action
            public Action PrimaryAction;
            public string SecondaryLabel;    // null = no secondary action
            public Action SecondaryAction;
            public bool Dimmed;
        }

        public RankedMarketView(Func<string, string> tr)
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
            col.Add(_header);

            _budget = UiKit.Caption(string.Empty);
            _budget.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(_budget);

            _banner = UiKit.Caption(string.Empty);
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_banner);

            // Tabs.
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.justifyContent = Justify.Center;
            tabs.style.flexShrink = 0f;
            tabs.style.marginTop = UiKit.SpaceXs;
            _tabAuctions = TabButton(() => TabSelected?.Invoke(0));
            _tabOffers = TabButton(() => TabSelected?.Invoke(1));
            _tabBrowse = TabButton(() => TabSelected?.Invoke(2));
            tabs.Add(_tabAuctions);
            tabs.Add(_tabOffers);
            tabs.Add(_tabBrowse);
            col.Add(tabs);

            // Controls.
            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.justifyContent = Justify.Center;
            controls.style.flexShrink = 0f;
            _refreshButton = TabButton(() => RefreshClicked?.Invoke());
            _botButton = TabButton(() => BotMarketClicked?.Invoke());
            _botButton.style.display = DisplayStyle.None; // dev-only
            controls.Add(_refreshButton);
            controls.Add(_botButton);
            col.Add(controls);

            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_list);

            // Inline amount panel.
            _amountPanel = UiKit.Card();
            _amountPanel.style.display = DisplayStyle.None;
            col.Add(_amountPanel);
            _amountTitle = UiKit.Caption(string.Empty);
            _amountTitle.style.whiteSpace = WhiteSpace.Normal;
            _amountPanel.Add(_amountTitle);
            _amountField = new TextField { maxLength = 12 };
            _amountField.style.minHeight = 40;
            _amountPanel.Add(_amountField);
            var amountRow = new VisualElement();
            amountRow.style.flexDirection = FlexDirection.Row;
            _amountPanel.Add(amountRow);
            _amountConfirm = UiKit.MenuButton(string.Empty, () =>
            {
                long.TryParse(DigitsOnly(_amountField.value), out long amount);
                AmountConfirmClicked?.Invoke(amount);
            });
            _amountCancel = UiKit.MenuButton(string.Empty, () => AmountCancelClicked?.Invoke());
            amountRow.Add(_amountConfirm);
            amountRow.Add(_amountCancel);

            _status = UiKit.Caption(string.Empty);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            col.Add(_status);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceSm;
            col.Add(_backButton);

            UpdateTexts();
        }

        private static Button TabButton(Action onClick)
        {
            var b = UiKit.MenuButton(string.Empty, onClick);
            b.style.marginLeft = 4;
            b.style.marginRight = 4;
            b.style.paddingLeft = UiKit.SpaceSm;
            b.style.paddingRight = UiKit.SpaceSm;
            return b;
        }

        private static string DigitsOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
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
            _tabAuctions.style.backgroundColor = tab == 0 ? UiKit.AccentDark : UiKit.SurfaceAlt;
            _tabOffers.style.backgroundColor = tab == 1 ? UiKit.AccentDark : UiKit.SurfaceAlt;
            _tabBrowse.style.backgroundColor = tab == 2 ? UiKit.AccentDark : UiKit.SurfaceAlt;
        }

        public void SetRows(IReadOnlyList<RowVm> rows)
        {
            _list.Clear();
            if (rows == null || rows.Count == 0)
            {
                _list.Add(UiKit.Caption(_tr("ranked.market.empty")));
                return;
            }

            foreach (var row in rows)
            {
                var card = UiKit.Card();
                if (row.Dimmed) card.style.opacity = 0.6f;

                var title = UiKit.Caption(row.Title);
                title.style.whiteSpace = WhiteSpace.Normal;
                title.style.color = UiKit.TextPrimary;
                card.Add(title);

                if (!string.IsNullOrEmpty(row.Detail))
                {
                    var detail = UiKit.Caption(row.Detail);
                    detail.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(detail);
                }

                if (row.PrimaryLabel != null || row.SecondaryLabel != null)
                {
                    var actions = new VisualElement();
                    actions.style.flexDirection = FlexDirection.Row;
                    card.Add(actions);
                    if (row.PrimaryLabel != null)
                    {
                        var p = row.PrimaryAction;
                        actions.Add(UiKit.MenuButton(row.PrimaryLabel, () => p?.Invoke()));
                    }
                    if (row.SecondaryLabel != null)
                    {
                        var s = row.SecondaryAction;
                        actions.Add(UiKit.MenuButton(row.SecondaryLabel, () => s?.Invoke()));
                    }
                }

                _list.Add(card);
            }
        }

        /// <summary>Opens the inline amount panel (bidding or offering), pre-filled with a suggestion.</summary>
        public void ShowAmountPanel(string title, long suggested)
        {
            _amountTitle.text = title;
            _amountField.SetValueWithoutNotify(suggested.ToString());
            _amountPanel.style.display = DisplayStyle.Flex;
        }

        public void HideAmountPanel() => _amountPanel.style.display = DisplayStyle.None;

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
            _amountConfirm.SetEnabled(!busy);
        }

        /// <summary>Shows the dev-only "bots react" button (DevFlags-gated by the presenter).</summary>
        public void SetDevToolsVisible(bool visible) =>
            _botButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void UpdateTexts()
        {
            _header.text = _tr("ranked.market.title");
            _tabAuctions.text = _tr("ranked.market.tab_auctions");
            _tabOffers.text = _tr("ranked.market.tab_offers");
            _tabBrowse.text = _tr("ranked.market.tab_browse");
            _refreshButton.text = _tr("ranked.refresh");
            _botButton.text = _tr("ranked.market.dev_bots");
            _amountConfirm.text = _tr("ranked.market.confirm");
            _amountCancel.text = _tr("ranked.market.cancel");
            _backButton.text = _tr("common.back");
        }
    }
}
