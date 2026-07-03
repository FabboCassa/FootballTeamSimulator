using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A player row on the Buy or Sell tab: identity + value + two contextual actions.</summary>
    public sealed class MarketRowVm
    {
        public int PlayerId;
        public string Name;           // "player — club" (buy) or "player" (sell) — its own cell
        public string RoleAbbr;       // localized role abbreviation (coloured cell)
        public int RoleGroup;         // 0 GK · 1 def · 2 mid · 3 att (cell colour)
        public string Age;            // its own cell
        public string Ovr;            // scouted/exact overall — its own cell
        public string Value;          // its own cell
        public string Tag;            // optional badge, e.g. "LISTED €5M" or "shortlisted" (muted after name)
        public string ActionAText;    // shortlist toggle (Buy) / List toggle (Sell)
        public bool ActionAHighlighted;
        public string ActionBText;    // Buy / Sell
        public bool ActionBEnabled;
        public VisualElement Avatar;  // player portrait placeholder in club kit colours (task 6.8)
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
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            // Centred, capped-width column so the market reads like the rest of the app (6.9).
            var col = UiKit.CenteredColumn(760f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            // Header block that never shrinks — this is what kept the filter chips from being
            // squeezed up into the tab row / first list row on short viewports (6.9 overlap bug).
            var header = new VisualElement();
            header.style.flexShrink = 0f;
            col.Add(header);

            _budget = new Label(string.Empty);
            _budget.style.fontSize = 18;
            _budget.style.unityFontStyleAndWeight = FontStyle.Bold;
            _budget.style.color = UiKit.TextPrimary;
            _budget.style.marginBottom = 2;
            header.Add(_budget);

            _window = new Label(string.Empty);
            _window.style.fontSize = 13;
            _window.style.whiteSpace = WhiteSpace.Normal;
            _window.style.marginBottom = UiKit.SpaceSm;
            header.Add(_window);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexShrink = 0f;
            tabs.style.marginBottom = UiKit.SpaceSm;
            _tabBuy = Tab(tr("market.tab.buy"), () => TabSelected?.Invoke(0));
            _tabSell = Tab(tr("market.tab.sell"), () => TabSelected?.Invoke(1));
            _tabNews = Tab(tr("market.tab.news"), () => TabSelected?.Invoke(2));
            tabs.Add(_tabBuy);
            tabs.Add(_tabSell);
            tabs.Add(_tabNews);
            header.Add(tabs);

            _filterBar = new VisualElement();
            _filterBar.style.flexDirection = FlexDirection.Row;
            _filterBar.style.flexWrap = Wrap.Wrap;
            _filterBar.style.flexShrink = 0f;
            _filterBar.style.marginBottom = UiKit.SpaceSm;
            _roleFilter = Chip(() => RoleFilterClicked?.Invoke());
            _sort = Chip(() => SortClicked?.Invoke());
            _shortlistOnly = Chip(() => ShortlistOnlyClicked?.Invoke());
            _filterBar.Add(_roleFilter);
            _filterBar.Add(_sort);
            _filterBar.Add(_shortlistOnly);
            header.Add(_filterBar);

            _content = new ScrollView();
            _content.style.flexGrow = 1f;
            _content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_content);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.flexShrink = 0f;
            footer.style.marginTop = UiKit.SpaceSm;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 160;
            back.style.height = 44;
            back.style.fontSize = 16;
            col.Add(footer);
            footer.Add(back);
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

        /// <summary>An empty-state illustration for a tab with nothing to show (task 6.8).</summary>
        public void AddEmptyState(string iconId, string message)
        {
            _content.Add(EmptyState.Build(iconId, message));
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

            VisualElement row = PlayerRowKit.Row();

            if (vm.Avatar != null)
            {
                vm.Avatar.style.marginRight = 5;
                row.Add(vm.Avatar);
            }

            // Name cell (grows), with the optional LISTED/shortlisted tag muted after the name.
            VisualElement nameCell = PlayerRowKit.Cell(0, grow: true);
            nameCell.style.justifyContent = Justify.FlexStart;
            var name = new Label(vm.Name);
            name.style.fontSize = 13;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = UiKit.TextPrimary;
            name.style.flexShrink = 1f;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            nameCell.Add(name);
            if (!string.IsNullOrEmpty(vm.Tag))
            {
                var tag = new Label(vm.Tag);
                tag.style.fontSize = 11;
                tag.style.color = UiKit.Amber;
                tag.style.marginLeft = 8;
                tag.style.flexShrink = 0f;
                nameCell.Add(tag);
            }
            row.Add(nameCell);

            row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));
            row.Add(PlayerRowKit.TextCell(vm.Age, 42f, TextAnchor.MiddleCenter));
            row.Add(PlayerRowKit.TextCell(vm.Ovr, 92f, TextAnchor.MiddleCenter));
            row.Add(PlayerRowKit.TextCell(vm.Value, 86f, TextAnchor.MiddleRight, bold: true, color: UiKit.Positive));

            if (!string.IsNullOrEmpty(vm.ActionAText))
            {
                var a = SmallButton(vm.ActionAText, () => RowActionA?.Invoke(playerId));
                a.style.height = PlayerRowKit.RowHeight;
                if (vm.ActionAHighlighted)
                    a.style.backgroundColor = new Color(0.30f, 0.45f, 0.70f, 0.9f);
                row.Add(a);
            }

            if (!string.IsNullOrEmpty(vm.ActionBText))
            {
                var b = SmallButton(vm.ActionBText, () => RowActionB?.Invoke(playerId));
                b.style.height = PlayerRowKit.RowHeight;
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
