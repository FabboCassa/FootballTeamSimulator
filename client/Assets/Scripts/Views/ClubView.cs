using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One facility row on the Club screen: name, current tier, its effect and an upgrade action.</summary>
    public sealed class FacilityRowVm
    {
        public int Index;          // 0..3 → FacilityType (the presenter maps it; the view stays Sim.Core-free)
        public string Name;
        public string Tier;
        public int TierValue;      // for the level pips (task 14.4)
        public int TierMax;
        public string Effect;
        public string ActionLabel; // "Upgrade (€4M)" or "Max"
        public bool CanUpgrade;    // false → maxed or unaffordable (button disabled)
    }

    /// <summary>The Club screen's money, already formatted (task 14.4).</summary>
    public sealed class ClubFinancesVm
    {
        public string Balance;
        public string Income;
        public string Expense;
        public string Net;
        public bool NetPositive;
        public string Gate;
        public string Sponsor;
        public string Prize;
    }

    /// <summary>
    /// Club screen (task 5.5), redrawn in task 14.4 on the standard page. Two questions, two blocks:
    ///   1. How much money is there? → four tiles (cash, season income, season wages, season net in
    ///      green or red) and a small card that splits the income into gate / sponsor / prizes.
    ///   2. What can I build?        → one row per facility: name, a row of level pips, what the
    ///      level does, and the upgrade button (accent when affordable).
    /// The status sentence of the last action sits under the facilities, where the eye already is.
    /// Dumb view — facilities are addressed by an int index, so the view references no Sim.Core type.
    /// </summary>
    public sealed class ClubView
    {
        public event Action<int> UpgradeClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _title;
        private readonly VisualElement _tiles;
        private readonly VisualElement _balanceTile;
        private readonly VisualElement _incomeTile;
        private readonly VisualElement _expenseTile;
        private readonly VisualElement _netTile;
        private readonly Label _gate;
        private readonly Label _sponsor;
        private readonly Label _prize;
        private readonly VisualElement _facilities;
        private readonly Label _status;

        public ClubView(Func<string, string> tr)
        {
            PageParts page = UiKit.StandardPage(tr("club.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            _title = page.Title;
            VisualElement col = page.Column;

            // ---- 1. money
            _tiles = UiKit.TileRow();
            _balanceTile = UiKit.StatTile(tr("club.tile.balance"), "—", UiKit.Accent);
            _incomeTile = UiKit.StatTile(tr("club.tile.income"), "—");
            _expenseTile = UiKit.StatTile(tr("club.tile.expense"), "—");
            _netTile = UiKit.StatTile(tr("club.tile.net"), "—");
            _tiles.Add(_balanceTile);
            _tiles.Add(_incomeTile);
            _tiles.Add(_expenseTile);
            _tiles.Add(_netTile);
            col.Add(_tiles);

            VisualElement income = UiKit.OptionCard();
            income.Add(UiKit.BlockHead(tr("club.income_caption")));
            _gate = UiKit.SummaryRow(income, tr("club.income.gate"), "—");
            _sponsor = UiKit.SummaryRow(income, tr("club.income.sponsor"), "—");
            _prize = UiKit.SummaryRow(income, tr("club.income.prize"), "—");
            col.Add(income);

            // ---- 2. facilities
            VisualElement facilities = UiKit.OptionCard();
            facilities.Add(UiKit.BlockHead(tr("club.facilities_caption")));
            _facilities = new VisualElement();
            facilities.Add(_facilities);

            _status = UiKit.HelpText(string.Empty);
            _status.AddToClassList("fts-club__status");
            _status.style.marginBottom = 0;
            _status.style.display = DisplayStyle.None;
            facilities.Add(_status);
            col.Add(facilities);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        // ---------------------------------------------------------------- presenter API

        /// <summary>The page title — the club's name.</summary>
        public void SetHeader(string text) => _title.text = text ?? string.Empty;

        public void SetStatus(string text)
        {
            _status.text = text ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetFinances(ClubFinancesVm vm)
        {
            UiKit.SetStatTileValue(_balanceTile, vm.Balance);
            UiKit.SetStatTileValue(_incomeTile, vm.Income);
            UiKit.SetStatTileValue(_expenseTile, vm.Expense);
            UiKit.SetStatTileValue(_netTile, vm.Net, vm.NetPositive ? UiKit.Positive : UiKit.Danger);
            _gate.text = vm.Gate ?? string.Empty;
            _sponsor.text = vm.Sponsor ?? string.Empty;
            _prize.text = vm.Prize ?? string.Empty;
        }

        public void SetFacilities(IReadOnlyList<FacilityRowVm> rows)
        {
            _facilities.Clear();
            foreach (FacilityRowVm vm in rows)
            {
                int index = vm.Index;

                var row = new VisualElement();
                // Direction lives in the sheet, NOT inline: a phone stacks the button under the text,
                // and an inline flexDirection would beat that rule.
                row.AddToClassList("fts-club__facility");

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.minWidth = 0f;

                var nameLine = new VisualElement();
                nameLine.style.flexDirection = FlexDirection.Row;
                nameLine.style.alignItems = Align.Center;
                nameLine.style.flexWrap = Wrap.Wrap;
                var name = new Label(vm.Name ?? string.Empty);
                name.AddToClassList("fts-selectrow__name");
                name.AddToClassList("fts-club__name");
                nameLine.Add(name);
                nameLine.Add(Pips(vm.TierValue, vm.TierMax, vm.Tier));
                text.Add(nameLine);

                var effect = new Label(vm.Effect ?? string.Empty);
                effect.AddToClassList("fts-selectrow__meta");
                effect.AddToClassList("fts-club__effect");
                effect.style.whiteSpace = WhiteSpace.Normal;
                text.Add(effect);
                row.Add(text);

                Button upgrade = UiKit.GhostButton(vm.ActionLabel, () => UpgradeClicked?.Invoke(index));
                upgrade.AddToClassList("fts-club__upgrade");
                upgrade.EnableInClassList("fts-club__upgrade--on", vm.CanUpgrade);
                upgrade.SetEnabled(vm.CanUpgrade);
                row.Add(upgrade);

                _facilities.Add(row);
            }
        }

        // ---------------------------------------------------------------- layout

        private void Layout(Viewport viewport)
        {
            bool mobile = viewport == Viewport.Mobile;
            foreach (VisualElement tile in _tiles.Children())
            {
                tile.style.minWidth = 0f;
                tile.style.flexBasis = mobile ? Length.Percent(40) : Length.Percent(20);
            }
        }

        /// <summary>Filled squares for the built levels, hollow ones for the rest — readable at a glance.</summary>
        private static VisualElement Pips(int value, int max, string tooltip)
        {
            var pips = new VisualElement();
            pips.AddToClassList("fts-club__pips");
            pips.style.flexDirection = FlexDirection.Row;
            pips.style.alignItems = Align.Center;
            pips.tooltip = tooltip ?? string.Empty;
            for (int i = 0; i < max; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("fts-club__pip");
                if (i < value) pip.AddToClassList("fts-club__pip--on");
                pips.Add(pip);
            }
            return pips;
        }
    }
}
