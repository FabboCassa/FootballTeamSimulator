using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Tactics screen (task 3.3), redrawn in task 14.4 on the standard page.
    ///
    /// The old screen was a column of "Modulo: 4-3-3" buttons you tapped to CYCLE through values
    /// you could not see, with a small pitch squeezed between them. Now every choice is visible at
    /// once and one tap picks it:
    ///   • left  — a large pitch with the shape of your best eleven in the picked module;
    ///   • right — the six modules as a chip grid, the four instructions as three-way segmented
    ///             rows, a familiarity meter, and the Save CTA.
    /// A phone stacks the two columns (pitch first). Every size lives in FtsTheme.uss (.fts-tac*).
    ///
    /// Dumb view: labels come from the loc tables through <c>tr</c>, the presenter tells it which
    /// option is picked and receives indices back. The cycle events are kept for compatibility.
    /// </summary>
    public sealed class TacticsView
    {
        public event Action FormationCycleClicked;
        public event Action MentalityCycleClicked;
        public event Action PressingCycleClicked;
        public event Action TempoCycleClicked;
        public event Action WidthCycleClicked;
        /// <summary>A module chip was picked (index into the list given to <see cref="SetFormationOptions"/>).</summary>
        public event Action<int> FormationSelected;
        /// <summary>An instruction was picked: axis 0 mentality · 1 pressing · 2 tempo · 3 width; value 0..2.</summary>
        public event Action<int, int> InstructionSelected;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private static readonly string[][] AxisKeys =
        {
            new[] { "tactics.mentality.defensive", "tactics.mentality.balanced", "tactics.mentality.attacking" },
            new[] { "tactics.pressing.low", "tactics.pressing.medium", "tactics.pressing.high" },
            new[] { "tactics.tempo.slow", "tactics.tempo.normal", "tactics.tempo.fast" },
            new[] { "tactics.width.narrow", "tactics.width.normal", "tactics.width.wide" },
        };

        private static readonly string[] AxisCaptionKeys =
            { "tactics.axis.mentality", "tactics.axis.pressing", "tactics.axis.tempo", "tactics.axis.width" };

        private readonly Func<string, string> _tr;
        private readonly Label _title;
        private readonly VisualElement _grid;
        private readonly VisualElement _colPitch;
        private readonly VisualElement _colSettings;
        private readonly PitchFormationView _shapePitch;
        private readonly VisualElement _formationGrid;
        private readonly List<Button> _formationChips = new List<Button>();
        private readonly Button[][] _axisChips = new Button[4][];
        private readonly Label _familiarityValue;
        private readonly MeterBar _familiarity;
        private readonly Label _status;

        public TacticsView(Func<string, string> tr)
        {
            _tr = tr;

            PageParts page = UiKit.StandardPage(tr("tactics.title"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke());
            Root = page.Root;
            Root.AddToClassList("fts-tac");
            _title = page.Title;

            Button save = UiKit.PrimaryButton(tr("tactics.save"), () => SaveClicked?.Invoke());
            save.AddToClassList("fts-headcta");
            page.Head.Actions.Insert(0, save);

            _status = UiKit.HelpText(string.Empty);
            _status.AddToClassList("fts-status");
            _status.style.display = DisplayStyle.None;
            page.Column.Add(_status);

            _grid = new VisualElement();
            _grid.AddToClassList("fts-tac__grid");
            page.Column.Add(_grid);

            // ---- left: the shape
            _colPitch = new VisualElement();
            _colPitch.AddToClassList("fts-tac__pitchcol");
            VisualElement pitchCard = UiKit.OptionCard();
            pitchCard.Add(UiKit.BlockHead(tr("tactics.your_shape")));
            var pitchBox = new VisualElement();
            pitchBox.AddToClassList("fts-tac__pitch");
            pitchBox.style.overflow = Overflow.Hidden;
            _shapePitch = new PitchFormationView(mirror: false);
            pitchBox.Add(_shapePitch);
            pitchCard.Add(pitchBox);
            _colPitch.Add(pitchCard);
            _grid.Add(_colPitch);

            // ---- right: choices
            _colSettings = new VisualElement();
            _colSettings.AddToClassList("fts-tac__settings");
            _grid.Add(_colSettings);

            VisualElement moduleCard = UiKit.OptionCard();
            moduleCard.Add(UiKit.BlockHead(tr("tactics.formation_caption")));
            _formationGrid = new VisualElement();
            _formationGrid.AddToClassList("fts-tac__modules");
            _formationGrid.style.flexDirection = FlexDirection.Row;
            _formationGrid.style.flexWrap = Wrap.Wrap;
            moduleCard.Add(_formationGrid);
            _colSettings.Add(moduleCard);

            VisualElement instrCard = UiKit.OptionCard();
            instrCard.Add(UiKit.BlockHead(tr("tactics.instructions_caption")));
            for (int axis = 0; axis < 4; axis++)
            {
                Label caption = UiKit.Caption(tr(AxisCaptionKeys[axis]));
                caption.AddToClassList("fts-tac__axis");
                instrCard.Add(caption);

                _axisChips[axis] = new Button[3];
                for (int v = 0; v < 3; v++)
                {
                    int a = axis, value = v;
                    _axisChips[axis][v] = UiKit.SegChip(tr(AxisKeys[axis][v]), null, () => InstructionSelected?.Invoke(a, value));
                }
                VisualElement seg = UiKit.SegRow(_axisChips[axis][0], _axisChips[axis][1], _axisChips[axis][2]);
                seg.AddToClassList("fts-tac__seg");
                instrCard.Add(seg);
            }
            _colSettings.Add(instrCard);

            VisualElement famCard = UiKit.OptionCard();
            famCard.Add(UiKit.BlockHead(tr("tactics.familiarity_caption")));
            _familiarityValue = new Label(string.Empty);
            _familiarityValue.AddToClassList("fts-tac__famvalue");
            _familiarityValue.style.whiteSpace = WhiteSpace.Normal;
            famCard.Add(_familiarityValue);
            _familiarity = new MeterBar();
            _familiarity.Root.style.height = StyleKeyword.Null;
            _familiarity.Root.AddToClassList("fts-meter");
            famCard.Add(_familiarity.Root);
            _colSettings.Add(famCard);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        // ---------------------------------------------------------------- presenter API

        public void SetHeader(string text) => _title.text = text ?? string.Empty;

        public void SetStatus(string text)
        {
            _status.text = text ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The module chips, in the order the presenter will index them.</summary>
        public void SetFormationOptions(IReadOnlyList<string> labels)
        {
            _formationGrid.Clear();
            _formationChips.Clear();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button chip = UiKit.SegChip(labels[i], null, () => FormationSelected?.Invoke(index));
                chip.AddToClassList("fts-tac__module");
                chip.style.flexGrow = 1f;
                chip.style.flexBasis = Length.Percent(30);
                chip.style.marginRight = UiKit.SpaceSm;
                chip.style.marginBottom = UiKit.SpaceSm;
                _formationGrid.Add(chip);
                _formationChips.Add(chip);
            }
        }

        /// <summary>Lights the picked module and the picked value on each instruction axis.</summary>
        public void SetSelection(int formation, int mentality, int pressing, int tempo, int width)
        {
            for (int i = 0; i < _formationChips.Count; i++)
                UiKit.SetSegChipState(_formationChips[i], i == formation);

            int[] values = { mentality, pressing, tempo, width };
            for (int axis = 0; axis < 4; axis++)
                for (int v = 0; v < 3; v++)
                    UiKit.SetSegChipState(_axisChips[axis][v], v == values[axis]);
        }

        /// <summary>Familiarity with the picked tactic, 0-100.</summary>
        public void SetFamiliarity(string text, int percent)
        {
            _familiarityValue.text = text ?? string.Empty;
            Color color = percent >= 80 ? UiKit.Positive : percent >= 40 ? UiKit.Warning : UiKit.Danger;
            _familiarity.Set(percent, color);
        }

        // Kept for compatibility: the texts are now shown by the chips themselves.
        public void SetFamiliarity(string text) => _familiarityValue.text = text ?? string.Empty;
        public void SetFormation(string text) { }
        public void SetMentality(string text) { }
        public void SetPressing(string text) { }
        public void SetTempo(string text) { }
        public void SetWidth(string text) { }

        /// <summary>Updates the live shape preview with the user's XI in the chosen formation.</summary>
        public void SetShape(IReadOnlyList<PitchTokenVm> tokens) => _shapePitch.SetTokens(tokens);

        // ---------------------------------------------------------------- layout

        private void Layout(Viewport viewport)
        {
            bool twoColumns = viewport != Viewport.Mobile;
            _grid.style.flexDirection = twoColumns ? FlexDirection.Row : FlexDirection.Column;
            _grid.style.alignItems = twoColumns ? Align.FlexStart : Align.Stretch;
            _colPitch.style.flexGrow = twoColumns ? 3f : 0f;
            _colPitch.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colSettings.style.flexGrow = twoColumns ? 2f : 0f;
            _colSettings.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colSettings.style.marginLeft = twoColumns ? UiKit.SpaceMd : 0f;
        }
    }
}
