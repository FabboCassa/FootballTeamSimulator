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
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("club.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 8;
            Root.Add(_header);

            Root.Add(SectionLabel(tr("club.finances_caption")));
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            panel.style.paddingTop = 6;
            panel.style.paddingBottom = 6;
            panel.style.paddingLeft = 10;
            panel.style.paddingRight = 10;
            panel.style.marginBottom = 6;
            _balance = PanelLine(panel);
            _balance.style.fontSize = 16;
            _income = PanelLine(panel);
            _expense = PanelLine(panel);
            _net = PanelLine(panel);
            Root.Add(panel);

            Root.Add(SectionLabel(tr("club.facilities_caption")));
            _facilities = new ScrollView();
            _facilities.style.flexGrow = 1f;
            Root.Add(_facilities);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.marginTop = 4;
            _status.style.alignSelf = Align.Center;
            Root.Add(_status);
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

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 52;
                row.style.marginBottom = 3;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                var name = new Label($"{vm.Name} · {vm.Tier}");
                name.style.fontSize = 14;
                name.style.color = Color.white;
                text.Add(name);
                var effect = new Label(vm.Effect);
                effect.style.fontSize = 12;
                effect.style.color = new Color(1f, 1f, 1f, 0.7f);
                text.Add(effect);
                row.Add(text);

                var upgrade = new Button(() => UpgradeClicked?.Invoke(index)) { text = vm.ActionLabel };
                upgrade.style.width = 150;
                upgrade.style.height = 38;
                upgrade.style.fontSize = 13;
                upgrade.SetEnabled(vm.CanUpgrade);
                row.Add(upgrade);

                _facilities.Add(row);
            }
        }

        private static Label PanelLine(VisualElement parent)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = 13;
            label.style.color = Color.white;
            parent.Add(label);
            return label;
        }

        private static Label SectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            return label;
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
