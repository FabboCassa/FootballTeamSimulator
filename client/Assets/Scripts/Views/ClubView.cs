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
        public string Effect;
        public string ActionLabel; // "Upgrade (€4M)" or "Max"
        public bool CanUpgrade;    // false → maxed or unaffordable (button disabled)
    }

    /// <summary>
    /// Club screen (task 5.5): a finances panel (cash balance + this season's income/expense
    /// breakdown) and the four upgradeable facilities, each with its tier, effect and an upgrade
    /// button. Dumb view — the presenter owns the club, translates every label and decides what
    /// is affordable; the view only emits upgrade/back events and renders the strings it's handed
    /// (facilities are addressed by an int index, so the view references no Sim.Core types).
    /// </summary>
    public sealed class ClubView
    {
        public event Action<int> UpgradeClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Label _balance;
        private readonly Label _income;
        private readonly Label _expense;
        private readonly Label _net;
        private readonly ScrollView _facilities;
        private readonly Label _status;

        public ClubView(Func<string, string> tr)
        {
            Root = UiKit.ScreenRoot();

            var col = UiKit.PageColumn(UiKit.WidthWide);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            // ---- finances panel
            VisualElement panel = UiKit.Panel();
            Label financesCaption = SectionLabel(tr("club.finances_caption"));
            financesCaption.style.marginTop = 0;
            panel.Add(financesCaption);
            _balance = PanelLine(panel);
            _balance.style.fontSize = 22;
            _balance.style.unityFontStyleAndWeight = FontStyle.Bold;
            _balance.style.color = UiKit.Accent;
            _balance.style.marginBottom = UiKit.SpaceXs;
            _income = PanelLine(panel);
            _expense = PanelLine(panel);
            _net = PanelLine(panel);
            col.Add(panel);

            // ---- facilities fill the rest of the page
            VisualElement facilitiesPanel = UiKit.Panel(grow: true);
            Label facilitiesCaption = SectionLabel(tr("club.facilities_caption"));
            facilitiesCaption.style.marginTop = 0;
            facilitiesPanel.Add(facilitiesCaption);
            _facilities = UiKit.ListScroll();
            facilitiesPanel.Add(_facilities);
            col.Add(facilitiesPanel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.alignSelf = Align.Center;
            _status.style.flexShrink = 0f;
            col.Add(_status);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetStatus(string text) => _status.text = text;

        public void SetFinances(string balance, string income, string expense, string net)
        {
            _balance.text = balance;
            _income.text = income;
            _expense.text = expense;
            _net.text = net;
        }

        public void SetFacilities(IReadOnlyList<FacilityRowVm> rows)
        {
            _facilities.Clear();
            foreach (FacilityRowVm vm in rows)
            {
                int index = vm.Index;

                VisualElement row = UiKit.RowCard(64f);

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                var name = new Label($"{vm.Name} · {vm.Tier}");
                name.style.fontSize = 16;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.color = UiKit.TextPrimary;
                name.style.marginBottom = 2;
                text.Add(name);
                var effect = new Label(vm.Effect);
                effect.style.fontSize = 12;
                effect.style.color = UiKit.TextMuted;
                effect.style.whiteSpace = WhiteSpace.Normal;
                text.Add(effect);
                row.Add(text);

                Button upgrade = UiKit.SmallButton(vm.ActionLabel, () => UpgradeClicked?.Invoke(index), 168f);
                upgrade.style.height = 40;
                upgrade.style.marginLeft = UiKit.SpaceMd;
                UiKit.SetSmallButtonAccent(upgrade, vm.CanUpgrade);
                upgrade.SetEnabled(vm.CanUpgrade);
                row.Add(upgrade);

                _facilities.Add(row);
            }
        }

        private static Label PanelLine(VisualElement parent)
        {
            Label label = UiKit.PanelLine();
            parent.Add(label);
            return label;
        }

        private static Label SectionLabel(string caption) => UiKit.SectionLabel(caption);

        private static Button FooterButton(string text, Action onClick) => UiKit.FooterButton(text, onClick);
    }
}
