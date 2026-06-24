using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A player row on the Buy or Sell tab: identity + value + two contextual actions.</summary>
    public sealed class MarketRowVm
    {
        public int PlayerId;
        public string Primary;
        public string Sub;
        public string Value;
        public string Tag;            // optional badge, e.g. "LISTED €5M" or "shortlisted"
        public string ActionAText;    // shortlist toggle (Buy) / List toggle (Sell)
        public bool ActionAHighlighted;
        public string ActionBText;    // Buy / Sell
        public bool ActionBEnabled;
    }

    /// <summary>A selectable club when the user is choosing whom to sell a player to.</summary>
    public sealed class MarketPickVm
    {
        public int Id;
        public string Label;
        public string Detail;
    }

    /// <summary>A pending incoming offer for one of the user's listed players (Accept / Reject by index).</summary>
    public sealed class MarketOfferVm
    {
        public int Index;
        public string Label;
        public string Fee;
        public bool ActionsEnabled;
    }

    /// <summary>
    /// Market screen (task 5.3): Buy / Sell / News tabs over the transfer market. Dumb view —
    /// the presenter owns all model access, computes every label and which actions are legal
    /// (window open, affordability, squad legality), and rebuilds the content list each refresh.
    /// The view only lays things out and emits clicks. No Sim.Core references.
    /// </summary>
    public sealed class MarketView
    {
        public event Action<int> TabSelected;          // 0 buy, 1 sell, 2 news
        public event Action RoleFilterClicked;
        public event Action SortClicked;
        public event Action ShortlistOnlyClicked;
        public event Action<int> RowActionA;           // shortlist toggle / list toggle (playerId)
        public event Action<int> RowActionB;           // buy / sell (playerId)
        public event Action<int> PickClicked;          // chose a buyer club (clubId)
        public event Action<int> OfferAccept;          // offer index
        public event Action<int> OfferReject;          // offer index
        public event Action SubBackClicked;            // leave the club picker
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _budget;
        private readonly Label _window;
        private readonly Button _tabBuy;
        private readonly Button _tabSell;
        private readonly Button _tabNews;
        private readonly VisualElement _filterBar;
        private readonly Button _roleFilter;
        private readonly Button _sort;
        private readonly Button _shortlistOnly;
        private readonly ScrollView _content;

        public MarketView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("market.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _budget = new Label(string.Empty);
            _budget.style.fontSize = 16;
            _budget.style.unityFontStyleAndWeight = FontStyle.Bold;
            _budget.style.color = Color.white;
            _budget.style.marginBottom = 2;
            Root.Add(_budget);

            _window = new Label(string.Empty);
            _window.style.fontSize = 13;
            _window.style.whiteSpace = WhiteSpace.Normal;
            _window.style.maxWidth = 460;
            _window.style.marginBottom = 6;
            Root.Add(_window);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 6;
            _tabBuy = Tab(tr("market.tab.buy"), () => TabSelected?.Invoke(0));
            _tabSell = Tab(tr("market.tab.sell"), () => TabSelected?.Invoke(1));
            _tabNews = Tab(tr("market.tab.news"), () => TabSelected?.Invoke(2));
            tabs.Add(_tabBuy);
            tabs.Add(_tabSell);
            tabs.Add(_tabNews);
            Root.Add(tabs);

            _filterBar = new VisualElement();
            _filterBar.style.flexDirection = FlexDirection.Row;
            _filterBar.style.flexWrap = Wrap.Wrap;
            _filterBar.style.marginBottom = 4;
            _roleFilter = Chip(() => RoleFilterClicked?.Invoke());
            _sort = Chip(() => SortClicked?.Invoke());
            _shortlistOnly = Chip(() => ShortlistOnlyClicked?.Invoke());
            _filterBar.Add(_roleFilter);
            _filterBar.Add(_sort);
            _filterBar.Add(_shortlistOnly);
            Root.Add(_filterBar);

            _content = new ScrollView();
            _content.style.flexGrow = 1f;
            Root.Add(_content);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 6;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 160;
            back.style.height = 44;
            back.style.fontSize = 16;
            footer.Add(back);
            Root.Add(footer);
        }

        public void SetBudget(string text) => _budget.text = text;

        public void SetWindow(string text, bool open)
        {
            _window.text = text;
            _window.style.color = open
                ? new Color(0.55f, 0.85f, 0.55f, 1f)   // green = open
                : new Color(0.90f, 0.65f, 0.45f, 1f);  // amber = closed
        }

        public void SetActiveTab(int active)
        {
            Highlight(_tabBuy, active == 0);
            Highlight(_tabSell, active == 1);
            Highlight(_tabNews, active == 2);
        }

        public void SetFilterBar(bool visible, string roleText, string sortText, string shortlistText)
        {
            _filterBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _roleFilter.text = roleText;
            _sort.text = sortText;
            _shortlistOnly.text = shortlistText;
        }

        public void BeginContent() => _content.Clear();

        public void AddSectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            _content.Add(label);
        }

        public void AddInfoLine(string text)
        {
            var label = new Label(text);
            label.style.color = new Color(1f, 1f, 1f, 0.55f);
            label.style.fontSize = 13;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 4;
            label.style.marginBottom = 4;
            _content.Add(label);
        }

        public void AddPlayerRow(MarketRowVm vm)
        {
            int playerId = vm.PlayerId;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 6;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);

            var info = new VisualElement();
            info.style.flexGrow = 1f;

            var primary = new Label(vm.Primary);
            primary.style.fontSize = 14;
            primary.style.color = Color.white;
            info.Add(primary);

            string subText = vm.Sub;
            if (!string.IsNullOrEmpty(vm.Tag))
                subText = string.IsNullOrEmpty(subText) ? vm.Tag : subText + "   " + vm.Tag;
            var sub = new Label(subText);
            sub.style.fontSize = 11;
            sub.style.color = new Color(1f, 1f, 1f, 0.6f);
            info.Add(sub);
            row.Add(info);

            var value = new Label(vm.Value);
            value.style.fontSize = 13;
            value.style.color = new Color(0.7f, 0.9f, 0.7f, 1f);
            value.style.minWidth = 70;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.marginRight = 8;
            row.Add(value);

            if (!string.IsNullOrEmpty(vm.ActionAText))
            {
                var a = SmallButton(vm.ActionAText, () => RowActionA?.Invoke(playerId));
                if (vm.ActionAHighlighted)
                    a.style.backgroundColor = new Color(0.30f, 0.45f, 0.70f, 0.9f);
                row.Add(a);
            }

            if (!string.IsNullOrEmpty(vm.ActionBText))
            {
                var b = SmallButton(vm.ActionBText, () => RowActionB?.Invoke(playerId));
                b.SetEnabled(vm.ActionBEnabled);
                row.Add(b);
            }

            _content.Add(row);
        }

        public void AddPickRow(MarketPickVm vm)
        {
            int id = vm.Id;
            var row = new Button(() => PickClicked?.Invoke(id));
            row.text = string.IsNullOrEmpty(vm.Detail) ? vm.Label : $"{vm.Label}   —   {vm.Detail}";
            row.style.height = 40;
            row.style.fontSize = 14;
            row.style.marginBottom = 3;
            row.style.unityTextAlign = TextAnchor.MiddleLeft;
            _content.Add(row);
        }

        public void AddOfferRow(MarketOfferVm vm)
        {
            int index = vm.Index;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.style.paddingLeft = 8;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.backgroundColor = new Color(0.2f, 0.3f, 0.2f, 0.4f);

            var label = new Label(vm.Label);
            label.style.flexGrow = 1f;
            label.style.fontSize = 13;
            label.style.color = Color.white;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            var fee = new Label(vm.Fee);
            fee.style.fontSize = 14;
            fee.style.color = new Color(0.7f, 0.9f, 0.7f, 1f);
            fee.style.minWidth = 70;
            fee.style.unityTextAlign = TextAnchor.MiddleRight;
            fee.style.marginRight = 8;
            row.Add(fee);

            var accept = SmallButton("✓", () => OfferAccept?.Invoke(index));
            accept.SetEnabled(vm.ActionsEnabled);
            row.Add(accept);
            var reject = SmallButton("✕", () => OfferReject?.Invoke(index));
            row.Add(reject);

            _content.Add(row);
        }

        public void AddBackRow(string text)
        {
            var row = new Button(() => SubBackClicked?.Invoke()) { text = text };
            row.style.height = 38;
            row.style.fontSize = 14;
            row.style.marginTop = 6;
            row.style.marginBottom = 4;
            _content.Add(row);
        }

        private static Button Tab(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.flexGrow = 1f;
            button.style.height = 40;
            button.style.fontSize = 15;
            button.style.marginRight = 4;
            return button;
        }

        private static Button Chip(Action onClick)
        {
            var button = new Button(onClick) { text = string.Empty };
            button.style.height = 32;
            button.style.fontSize = 12;
            button.style.marginRight = 4;
            button.style.marginTop = 2;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            return button;
        }

        private static Button SmallButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 32;
            button.style.minWidth = 56;
            button.style.fontSize = 13;
            button.style.marginLeft = 4;
            return button;
        }

        private static void Highlight(Button tab, bool active)
        {
            tab.style.backgroundColor = active
                ? new Color(0.30f, 0.45f, 0.70f, 0.9f)
                : new Color(1f, 1f, 1f, 0.10f);
        }
    }
}
