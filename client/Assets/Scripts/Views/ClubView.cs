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
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(760f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            col.Add(SectionLabel(tr("club.finances_caption")));
            var panel = UiKit.Card();
            _balance = PanelLine(panel);
            _balance.style.fontSize = 18;
            _balance.style.unityFontStyleAndWeight = FontStyle.Bold;
            _balance.style.marginBottom = 2;
            _income = PanelLine(panel);
            _expense = PanelLine(panel);
            _net = PanelLine(panel);
            col.Add(panel);

            col.Add(SectionLabel(tr("club.facilities_caption")));
            _facilities = new ScrollView();
            _facilities.style.flexGrow = 1f;
            _facilities.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_facilities);

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
                row.style.minHeight = 62;
                row.style.marginBottom = 6;
                row.style.paddingLeft = 12;
                row.style.paddingRight = 12;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;
                row.style.backgroundColor = UiKit.Surface;
                UiKit.Round(row, UiKit.RadiusSm);

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                var name = new Label($"{vm.Name} · {vm.Tier}");
                name.style.fontSize = 15;
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

                var upgrade = new Button(() => UpgradeClicked?.Invoke(index)) { text = vm.ActionLabel };
                upgrade.style.width = 150;
                upgrade.style.height = 40;
                upgrade.style.fontSize = 13;
                upgrade.style.flexShrink = 0f;
                upgrade.style.marginLeft = UiKit.SpaceSm;
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
