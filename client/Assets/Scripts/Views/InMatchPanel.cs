using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A row in the in-match pause panel (a pitch slot or a bench player).</summary>
    public sealed class InMatchRowVm
    {
        public int Id;       // slot index for pitch rows, player id for bench rows
        public string Label;
        public bool Selected;
        public string FormArrow;
        public string MoraleFace;
        public int Fitness;
        public string Tooltip;
    }

    /// <summary>
    /// Pause overlay for the watched match (task 3.4): substitute on the left
    /// (tap a pitch player, then a bench player), tweak the four instruction
    /// axes on the right, then Apply (re-sim the remainder) or Resume (no change).
    /// Dumb view — the presenter owns the lineup/tactic state and the subs budget.
    /// </summary>
    public sealed class InMatchPanel
    {
        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color CardColor = new Color(0.12f, 0.16f, 0.26f);
        private static readonly Color SelectedColor = new Color(0.22f, 0.42f, 0.66f);

        public event Action<int> PitchClicked;   // slot index
        public event Action<int> BenchClicked;   // player id
        public event Action MentalityCycleClicked;
        public event Action PressingCycleClicked;
        public event Action TempoCycleClicked;
        public event Action WidthCycleClicked;
        public event Action ApplyClicked;
        public event Action ResumeClicked;

        public VisualElement Root { get; }

        private readonly Label _title;
        private readonly Label _subsRemaining;
        private readonly Label _familiarity;
        private readonly ScrollView _pitchList;
        private readonly ScrollView _benchList;
        private readonly Button _mentality;
        private readonly Button _pressing;
        private readonly Button _tempo;
        private readonly Button _width;

        public InMatchPanel(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.right = 0;
            Root.style.top = 0;
            Root.style.bottom = 0;
            Root.style.backgroundColor = Backdrop;
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.display = DisplayStyle.None;

            var card = new VisualElement();
            card.style.backgroundColor = CardColor;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.width = new Length(92, LengthUnit.Percent);
            card.style.maxWidth = 640;
            card.style.maxHeight = new Length(92, LengthUnit.Percent);
            Root.Add(card);

            _title = UiKit.Title(tr("inmatch.title"));
            _title.style.fontSize = 24;
            _title.style.marginBottom = 6;
            card.Add(_title);

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1f;
            card.Add(columns);

            columns.Add(BuildColumn(tr("inmatch.on_pitch"), out _pitchList));
            columns.Add(BuildColumn(tr("inmatch.bench"), out _benchList));

            _subsRemaining = UiKit.Subtitle(string.Empty);
            _subsRemaining.style.marginTop = 6;
            _subsRemaining.style.marginBottom = 4;
            _subsRemaining.style.alignSelf = Align.FlexStart;
            card.Add(_subsRemaining);

            var instr = new Label(tr("inmatch.instructions"));
            instr.style.color = new Color(1f, 1f, 1f, 0.7f);
            instr.style.fontSize = 13;
            instr.style.marginBottom = 4;
            card.Add(instr);

            _familiarity = UiKit.Subtitle(string.Empty);
            _familiarity.style.fontSize = 14;
            _familiarity.style.marginTop = 0;
            _familiarity.style.marginBottom = 4;
            _familiarity.style.alignSelf = Align.FlexStart;
            card.Add(_familiarity);

            var instrRow = new VisualElement();
            instrRow.style.flexDirection = FlexDirection.Row;
            instrRow.style.flexWrap = Wrap.Wrap;
            card.Add(instrRow);
            _mentality = InstructionButton(() => MentalityCycleClicked?.Invoke());
            _pressing = InstructionButton(() => PressingCycleClicked?.Invoke());
            _tempo = InstructionButton(() => TempoCycleClicked?.Invoke());
            _width = InstructionButton(() => WidthCycleClicked?.Invoke());
            instrRow.Add(_mentality);
            instrRow.Add(_pressing);
            instrRow.Add(_tempo);
            instrRow.Add(_width);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            footer.Add(FooterButton(tr("inmatch.apply"), () => ApplyClicked?.Invoke()));
            footer.Add(FooterButton(tr("inmatch.resume"), () => ResumeClicked?.Invoke()));
            card.Add(footer);
        }

        public void SetVisible(bool visible) =>
            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void SetTitle(string text) => _title.text = text;
        public void SetSubsRemaining(string text) => _subsRemaining.text = text;
        public void SetFamiliarity(string text) => _familiarity.text = text;
        public void SetMentality(string text) => _mentality.text = text;
        public void SetPressing(string text) => _pressing.text = text;
        public void SetTempo(string text) => _tempo.text = text;
        public void SetWidth(string text) => _width.text = text;

        public void SetPitch(IReadOnlyList<InMatchRowVm> rows) => Fill(_pitchList, rows, slot: true);
        public void SetBench(IReadOnlyList<InMatchRowVm> rows) => Fill(_benchList, rows, slot: false);

        private void Fill(ScrollView list, IReadOnlyList<InMatchRowVm> rows, bool slot)
        {
            list.Clear();
            foreach (InMatchRowVm vm in rows)
            {
                int id = vm.Id;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 30;
                row.style.marginBottom = 2;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                if (vm.Selected)
                    row.style.backgroundColor = SelectedColor;
                row.tooltip = vm.Tooltip ?? string.Empty;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (slot) PitchClicked?.Invoke(id);
                    else BenchClicked?.Invoke(id);
                });

                var name = new Label(vm.Label);
                name.style.flexGrow = 1f;
                name.style.fontSize = 12;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(name);

                ConditionStrip.Append(row, vm.FormArrow, vm.MoraleFace, vm.Fitness);

                list.Add(row);
            }
        }

        private static VisualElement BuildColumn(string caption, out ScrollView list)
        {
            var column = new VisualElement();
            column.style.width = new Length(50, LengthUnit.Percent);
            column.style.paddingRight = 8;

            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginBottom = 4;
            column.Add(label);

            list = new ScrollView();
            list.style.flexGrow = 1f;
            list.style.minHeight = 150;
            column.Add(list);

            return column;
        }

        private static Button InstructionButton(Action onClick)
        {
            var button = new Button(onClick);
            button.style.height = 36;
            button.style.fontSize = 13;
            button.style.marginRight = 6;
            button.style.marginBottom = 4;
            button.style.minWidth = 150;
            return button;
        }

        private static Button FooterButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.width = 160;
            button.style.height = 44;
            button.style.fontSize = 16;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            return button;
        }
    }
}
