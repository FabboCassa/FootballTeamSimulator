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
        public string Age;            // its own cell (kept; the meta line carries it since 14.4)
        public string Meta;           // quiet second line under the name: "club · 28 anni" (task 14.4)
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
        public event Action PagePreviousClicked;
        public event Action PageNextClicked;
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
        private readonly VisualElement _content;

        public MarketView(Func<string, string> tr)
        {
            // Task 14.4: the standard page. The budget is the title (the number every decision on
            // this screen is weighed against), the window is a coloured line under it, tabs are a
            // segmented row, and the listing is one card of two-line rows.
            PageParts page = UiKit.StandardPage(tr("market.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke());
            Root = page.Root;
            Root.AddToClassList("fts-market");
            VisualElement col = page.Column;
            _budget = page.Title;

            _window = new Label(string.Empty);
            _window.AddToClassList("fts-market__window");
            _window.style.whiteSpace = WhiteSpace.Normal;
            page.Head.Left.Add(_window);

            // ---- tabs
            _tabBuy = UiKit.SegChip(tr("market.tab.buy"), null, () => TabSelected?.Invoke(0));
            _tabSell = UiKit.SegChip(tr("market.tab.sell"), null, () => TabSelected?.Invoke(1));
            _tabNews = UiKit.SegChip(tr("market.tab.news"), null, () => TabSelected?.Invoke(2));
            VisualElement tabs = UiKit.SegRow(_tabBuy, _tabSell, _tabNews);
            tabs.AddToClassList("fts-market__tabs");
            col.Add(tabs);

            // ---- filters: role chips, then sort + shortlist toggles
            _filterBar = new VisualElement();
            _filterBar.AddToClassList("fts-market__filters");
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

            // ---- the listing
            VisualElement card = UiKit.OptionCard();
            card.AddToClassList("fts-market__list");
            // Not a ScrollView: the page already scrolls, and a nested scroller steals the wheel.
            _content = new VisualElement();
            _content.AddToClassList("fts-market__content");
            card.Add(_content);
            col.Add(card);
        }

        public void SetBudget(string text) => _budget.text = text;

        public void SetWindow(string text, bool open)
        {
            _window.text = text;
            _window.EnableInClassList("fts-market__window--open", open);
            _window.EnableInClassList("fts-market__window--closed", !open);
        }

        public void SetActiveTab(int active)
        {
            UiKit.SetSegChipState(_tabBuy, active == 0);
            UiKit.SetSegChipState(_tabSell, active == 1);
            UiKit.SetSegChipState(_tabNews, active == 2);
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
            Label label = UiKit.HelpText(text);
            label.AddToClassList("fts-market__info");
            _content.Add(label);
        }

        public void AddPlayerRow(MarketRowVm vm)
        {
            int playerId = vm.PlayerId;

            VisualElement row = PlayerRowKit.Row();
            row.AddToClassList("fts-market__row");

            if (vm.Avatar != null)
            {
                vm.Avatar.AddToClassList("fts-market__avatar");
                vm.Avatar.style.flexShrink = 0f;
                row.Add(vm.Avatar);
            }

            // Name + quiet meta line (club · age), with the optional LISTED / shortlisted tag.
            var stack = new VisualElement();
            stack.AddToClassList("fts-market__who");
            stack.style.flexGrow = 1f;
            stack.style.flexShrink = 1f;
            stack.style.minWidth = 0f;

            var nameLine = new VisualElement();
            nameLine.style.flexDirection = FlexDirection.Row;
            nameLine.style.alignItems = Align.Center;
            var name = new Label(vm.Name);
            name.AddToClassList("fts-market__name");
            name.style.flexShrink = 1f;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            nameLine.Add(name);
            if (!string.IsNullOrEmpty(vm.Tag))
            {
                var tag = new Label(vm.Tag);
                tag.AddToClassList("fts-market__tag");
                tag.style.flexShrink = 0f;
                nameLine.Add(tag);
            }
            stack.Add(nameLine);

            string metaText = !string.IsNullOrEmpty(vm.Meta) ? vm.Meta : vm.Age;
            if (!string.IsNullOrEmpty(metaText))
            {
                var meta = new Label(metaText);
                meta.AddToClassList("fts-market__meta");
                meta.style.whiteSpace = WhiteSpace.NoWrap;
                meta.style.overflow = Overflow.Hidden;
                meta.style.textOverflow = TextOverflow.Ellipsis;
                stack.Add(meta);
            }
            row.Add(stack);

            row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));

            VisualElement ovr = PlayerRowKit.TextCell(vm.Ovr, 0, TextAnchor.MiddleCenter, bold: true);
            ovr.style.width = StyleKeyword.Null;
            ovr.AddToClassList("fts-market__ovr");
            row.Add(ovr);

            var value = new Label(vm.Value);
            value.AddToClassList("fts-market__value");
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.flexShrink = 0f;
            row.Add(value);

            if (!string.IsNullOrEmpty(vm.ActionAText))
            {
                Button a = UiKit.SmallButton(vm.ActionAText, () => RowActionA?.Invoke(playerId), 0f);
                a.AddToClassList("fts-market__actiona");
                UiKit.SetSmallButtonOn(a, vm.ActionAHighlighted);
                row.Add(a);
            }

            if (!string.IsNullOrEmpty(vm.ActionBText))
            {
                Button b = UiKit.SmallButton(vm.ActionBText, () => RowActionB?.Invoke(playerId), 0f);
                b.AddToClassList("fts-market__actionb");
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
            label.AddToClassList("fts-market__name");
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            if (!string.IsNullOrEmpty(vm.Detail))
            {
                var detail = new Label(vm.Detail);
                detail.AddToClassList("fts-market__meta");
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
            label.AddToClassList("fts-market__name");
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            var fee = new Label(vm.Fee);
            fee.AddToClassList("fts-market__value");
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
            Button row = UiKit.GhostButton(text, () => SubBackClicked?.Invoke());
            row.style.alignSelf = Align.FlexStart;
            row.style.marginLeft = 0;
            row.style.marginTop = UiKit.SpaceSm;
            row.style.marginBottom = UiKit.SpaceXs;
            _content.Add(row);
        }

        public void AddPager(string summaryText, bool hasPrevious, bool hasNext)
        {
            var bar = UiKit.Toolbar();
            bar.AddToClassList("fts-market__pager");
            bar.style.marginTop = UiKit.SpaceSm;
            bar.style.alignItems = Align.Center;

            Button previous = UiKit.SmallButton("\u25C0", () => PagePreviousClicked?.Invoke(), 72f);
            previous.SetEnabled(hasPrevious);
            bar.Add(previous);

            var summary = new Label(summaryText ?? string.Empty);
            summary.style.flexGrow = 1f;
            summary.AddToClassList("fts-t-meta");
            summary.style.color = UiKit.TextMuted;
            summary.style.unityTextAlign = TextAnchor.MiddleCenter;
            bar.Add(summary);

            Button next = UiKit.SmallButton("\u25B6", () => PageNextClicked?.Invoke(), 72f);
            next.SetEnabled(hasNext);
            bar.Add(next);

            _content.Add(bar);
        }
    }
}
