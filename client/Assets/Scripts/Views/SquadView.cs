using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    public sealed class LineupSlotVm
    {
        public int Index;
        public string Label;
        public bool Selected;
        public string FormArrow;
        public string MoraleFace;
        public int Fitness;
        public string Tooltip;
    }

    public sealed class RosterRowVm
    {
        public int PlayerId;
        public string Label;
        public bool InLineup;
        public string FormArrow;
        public string MoraleFace;
        public int Fitness;
        public string Tooltip;
    }

    /// <summary>
    /// Squad screen: lineup slots on the left, roster on the right. Each row carries a
    /// condition strip — fitness bar, form arrow, morale face — with a tooltip that
    /// explains every value (task 4.2). Dumb view: thresholds/wording are computed by
    /// the presenter; the view only paints the bar colour from the fitness value.
    /// </summary>
    public sealed class SquadView
    {
        private static readonly Color SelectedSlotColor = new Color(0.22f, 0.42f, 0.66f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color InLineupTextColor = new Color(0.55f, 0.85f, 0.55f);

        public event Action<int> SlotClicked;
        public event Action<int> PlayerClicked;
        public event Action AutoClicked;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly ScrollView _slotList;
        private readonly ScrollView _rosterList;
        private readonly Label _status;

        public SquadView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("squad.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 8;
            Root.Add(_header);

            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.flexGrow = 1f;
            Root.Add(content);

            content.Add(BuildColumn(tr("squad.lineup_caption"), out _slotList, 48));
            content.Add(BuildColumn(tr("squad.roster_caption"), out _rosterList, 52));

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            footer.Add(FooterButton(tr("squad.auto_pick"), () => AutoClicked?.Invoke()));
            footer.Add(FooterButton(tr("squad.save_lineup"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.marginTop = 4;
            _status.style.alignSelf = Align.Center;
            Root.Add(_status);
        }

        public void SetHeader(string text) => _header.text = text;

        public void SetStatus(string text) => _status.text = text;

        public void SetSlots(IReadOnlyList<LineupSlotVm> slots)
        {
            _slotList.Clear();
            foreach (LineupSlotVm vm in slots)
            {
                int index = vm.Index;
                _slotList.Add(ConditionRow(
                    vm.Label, null, vm.Selected, vm.FormArrow, vm.MoraleFace, vm.Fitness, vm.Tooltip,
                    () => SlotClicked?.Invoke(index)));
            }
        }

        public void SetRoster(IReadOnlyList<RosterRowVm> rows)
        {
            _rosterList.Clear();
            foreach (RosterRowVm vm in rows)
            {
                int playerId = vm.PlayerId;
                Color? textColor = vm.InLineup ? InLineupTextColor : (Color?)null;
                string label = vm.InLineup ? $"● {vm.Label}" : vm.Label;
                _rosterList.Add(ConditionRow(
                    label, textColor, false, vm.FormArrow, vm.MoraleFace, vm.Fitness, vm.Tooltip,
                    () => PlayerClicked?.Invoke(playerId)));
            }
        }

        /// <summary>A clickable row: name on the left, then the condition strip (bar + arrow + face).</summary>
        private static VisualElement ConditionRow(
            string text, Color? textColor, bool selected,
            string formArrow, string moraleFace, int fitness, string tooltip, Action onClick)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 32;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.backgroundColor = selected ? SelectedSlotColor : RowColor;
            row.tooltip = tooltip ?? string.Empty;
            row.RegisterCallback<ClickEvent>(_ => onClick());

            var name = new Label(text);
            name.style.flexGrow = 1f;
            name.style.fontSize = 13;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (textColor.HasValue)
                name.style.color = textColor.Value;
            row.Add(name);

            ConditionStrip.Append(row, formArrow, moraleFace, fitness);

            return row;
        }

        private static VisualElement BuildColumn(string caption, out ScrollView list, float widthPercent)
        {
            var column = new VisualElement();
            column.style.width = new Length(widthPercent, LengthUnit.Percent);
            column.style.paddingRight = 8;

            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginBottom = 4;
            column.Add(label);

            list = new ScrollView();
            list.style.flexGrow = 1f;
            column.Add(list);

            return column;
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
