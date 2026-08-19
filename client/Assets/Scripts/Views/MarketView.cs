using System;
using System.Collections.Generic;
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
    ///
    /// Layout (task 6.12): the wide page scaffold. The budget is the screen's headline, tabs and
    /// filters sit in toolbars that never shrink, and the listing panel fills the page — the row
    /// actions used to run off the right edge of the old 760px column.
    /// </summary>
    public sealed class MarketView
    {
        public event Action<int> TabSelected;          // 0 buy, 1 sell, 2 news
        /// <summary>A role filter chip was picked (-1 = every role).</summary>
        public event Action<int> RoleFilterSelected;
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
        private readonly VisualElement _roleBar;
        private readonly Button _sort;
        private readonly Button _shortlistOnly;
        private readonly ScrollView _content;

        public MarketView(Func<string, string> tr)
        {
            Root = UiKit.ScreenRoot();

            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            // ---- headline: the budget, then the window status line.
            _budget = UiKit.ScreenTitle(string.Empty);
            col.Add(_budget);

            _window = UiKit.HelpText(string.Empty);
            col.Add(_window);

            // ---- tabs
            VisualElement tabs = UiKit.Toolbar();
            _tabBuy = UiKit.TabButton(tr("market.tab.buy"), () => TabSelected?.Invoke(0));
            _tabSell = UiKit.TabButton(tr("market.tab.sell"), () => TabSelected?.Invoke(1));
            _tabNews = UiKit.TabButton(tr("market.tab.news"), () => TabSelected?.Invoke(2));
            _tabNews.style.marginRight = 0;
            tabs.Add(_tabBuy);
            tabs.Add(_tabSell);
            tabs.Add(_tabNews);
            col.Add(tabs);

            // ---- filters: a full row of role chips (every role visible, the picked one lit up —
            // task 6.12b) and, under it, the sort + shortlist toggles.
            _filterBar = new VisualElement();
            _filterBar.style.flexShrink = 0f;
            _roleBar = UiKit.Toolbar();
            _roleBar.style.marginBottom = UiKit.SpaceXs;
            _filterBar.Add(_roleBar);

            VisualElement optionBar = UiKit.Toolbar();
            _sort = UiKit.ChipButton(string.Empty, () => SortClicked?.Invoke());
            _shortlistOnly = UiKit.ChipButton(string.Empty, () => ShortlistOnlyClicked?.Invoke());
            optionBar.Add(_sort);
            optionBar.Add(_shortlistOnly);
            _filterBar.Add(optionBar);
            col.Add(_filterBar);

            // ---- listing panel fills the page
            VisualElement panel = UiKit.Panel(grow: true);
            panel.style.paddingTop = UiKit.SpaceSm;
            panel.style.paddingBottom = UiKit.SpaceSm;
            _content = UiKit.ListScroll();
            panel.Add(_content);
            col.Add(panel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetBudget(string text) => _budget.text = text;

        public void SetWindow(string text, bool open)
        {
            _window.text = text;
            _window.style.color = open ? UiKit.Positive : UiKit.Amber;
        }

        public void SetActiveTab(int active)
        {
            UiKit.SetTabActive(_tabBuy, active == 0);
            UiKit.SetTabActive(_tabSell, active == 1);
            UiKit.SetTabActive(_tabNews, active == 2);
        }

        public void SetFilterBar(
            bool visible, IReadOnlyList<FilterChipVm> roleChips,
            string sortText, string shortlistText, bool shortlistOn)
        {
            _filterBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            UiKit.FillFilterChips(_roleBar, roleChips, role => RoleFilterSelected?.Invoke(role));
            _sort.text = sortText;
            _shortlistOnly.text = shortlistText;
            UiKit.SetChipActive(_shortlistOnly, shortlistOn);
        }

        public void BeginContent() => _content.Clear();

        public void AddSectionLabel(string caption)
        {
            _content.Add(UiKit.SectionLabel(caption));
        }

        /// <summary>An empty-state illustration for a tab with nothing to show (task 6.8).</summary>
        public void AddEmptyState(string iconId, string message)
        {
            _content.Add(EmptyState.Build(iconId, message));
        }

        public void AddInfoLine(string text)
        {
            var label = new Label(text);
            label.style.color = UiKit.TextMuted;
            label.style.fontSize = 13;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = UiKit.SpaceXs;
            label.style.marginBottom = UiKit.SpaceXs;
            _content.Add(label);
        }

        public void AddPlayerRow(MarketRowVm vm)
        {
            int playerId = vm.PlayerId;

            VisualElement row = PlayerRowKit.Row();

            if (vm.Avatar != null)
            {
                vm.Avatar.style.marginRight = 5;
                vm.Avatar.style.flexShrink = 0f;
                row.Add(vm.Avatar);
            }

            row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));

            // Name cell (grows), with the optional LISTED/shortlisted tag muted after the name.
            VisualElement nameCell = PlayerRowKit.Cell(0, grow: true);
            nameCell.style.justifyContent = Justify.FlexStart;
            var name = new Label(vm.Name);
            name.style.fontSize = 14;
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
                tag.style.unityFontStyleAndWeight = FontStyle.Bold;
                tag.style.color = UiKit.Amber;
                tag.style.marginLeft = 8;
                tag.style.flexShrink = 0f;
                nameCell.Add(tag);
            }
            row.Add(nameCell);

            row.Add(PlayerRowKit.TextCell(vm.Age, 48f, TextAnchor.MiddleCenter));
            row.Add(PlayerRowKit.TextCell(vm.Ovr, 100f, TextAnchor.MiddleCenter));
            row.Add(PlayerRowKit.TextCell(vm.Value, 104f, TextAnchor.MiddleRight, bold: true, color: UiKit.Positive));

            if (!string.IsNullOrEmpty(vm.ActionAText))
            {
                Button a = UiKit.SmallButton(vm.ActionAText, () => RowActionA?.Invoke(playerId), 96f);
                a.style.height = PlayerRowKit.RowHeight;
                UiKit.SetSmallButtonOn(a, vm.ActionAHighlighted);
                row.Add(a);
            }

            if (!string.IsNullOrEmpty(vm.ActionBText))
            {
                Button b = UiKit.SmallButton(vm.ActionBText, () => RowActionB?.Invoke(playerId), 96f);
                b.style.height = PlayerRowKit.RowHeight;
                UiKit.SetSmallButtonAccent(b, vm.ActionBEnabled);
                b.SetEnabled(vm.ActionBEnabled);
                row.Add(b);
            }

            _content.Add(row);
        }

        public void AddPickRow(MarketPickVm vm)
        {
            int id = vm.Id;

            VisualElement row = UiKit.RowCard(48f);
            row.RegisterCallback<ClickEvent>(_ => PickClicked?.Invoke(id));

            var label = new Label(vm.Label);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.fontSize = 14;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = UiKit.TextPrimary;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            if (!string.IsNullOrEmpty(vm.Detail))
            {
                var detail = new Label(vm.Detail);
                detail.style.fontSize = 13;
                detail.style.color = UiKit.TextMuted;
                detail.style.flexShrink = 0f;
                detail.style.marginLeft = UiKit.SpaceSm;
                row.Add(detail);
            }

            _content.Add(row);
        }

        public void AddOfferRow(MarketOfferVm vm)
        {
            int index = vm.Index;

            VisualElement row = UiKit.RowCard(48f);

            var label = new Label(vm.Label);
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.fontSize = 14;
            label.style.color = UiKit.TextPrimary;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            var fee = new Label(vm.Fee);
            fee.style.fontSize = 15;
            fee.style.unityFontStyleAndWeight = FontStyle.Bold;
            fee.style.color = UiKit.Positive;
            fee.style.minWidth = 96;
            fee.style.flexShrink = 0f;
            fee.style.unityTextAlign = TextAnchor.MiddleRight;
            fee.style.marginRight = UiKit.SpaceSm;
            row.Add(fee);

            Button accept = UiKit.SmallButton("✓", () => OfferAccept?.Invoke(index), 52f);
            UiKit.SetSmallButtonAccent(accept, vm.ActionsEnabled);
            accept.SetEnabled(vm.ActionsEnabled);
            row.Add(accept);

            Button reject = UiKit.SmallButton("✕", () => OfferReject?.Invoke(index), 52f);
            row.Add(reject);

            _content.Add(row);
        }

        public void AddBackRow(string text)
        {
            Button row = UiKit.SmallButton(text, () => SubBackClicked?.Invoke(), 140f);
            row.style.height = 40;
            row.style.alignSelf = Align.FlexStart;
            row.style.marginLeft = 0;
            row.style.marginTop = UiKit.SpaceSm;
            row.style.marginBottom = UiKit.SpaceXs;
            _content.Add(row);
        }
    }
}
