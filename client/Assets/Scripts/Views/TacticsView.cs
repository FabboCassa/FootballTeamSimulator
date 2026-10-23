using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One instruction row of the Tactics screen: its caption, one-line description and level labels.</summary>
    public sealed class TacticAxisVm
    {
        /// <summary>The phase block the row belongs to (rows arrive grouped by it, in order).</summary>
        public string Phase;
        public string Caption;
        public string Description;
        public IReadOnlyList<string> Levels;
    }

    /// <summary>
    /// Tactics screen (task 3.3), redrawn in task 14.4 on the standard page and extended for the
    /// R21 styles (issue #99).
    ///
    /// Every choice is visible at once and one tap picks it:
    ///   • left  — a large pitch with the shape of your best eleven in the picked module;
    ///   • right — the style card (style chips, the picked style's one-line description, "custom"
    ///             once an instruction is tweaked, and its suggested modules as chips), the module
    ///             card (the formation catalogue and its in/out-of-possession shape), one card per
    ///             instruction phase (a segmented row per instruction with its one-line
    ///             description), a familiarity meter, the Save CTA, and the match plan: conditional
    ///             rules that call a touchline shout during the match (issue #44).
    /// A phone stacks the two columns (pitch first). Every size lives in FtsTheme.uss (.fts-tac*).
    ///
    /// Dumb view: labels come from the loc tables through <c>tr</c> or from the presenter, the
    /// presenter tells it which option is picked and receives indices back.
    /// </summary>
    public sealed class TacticsView
    {
        /// <summary>A style chip was picked (index into the list given to <see cref="SetStyleOptions"/>).</summary>
        public event Action<int> StyleSelected;
        /// <summary>A suggested module chip was picked (index into the list given to <see cref="SetStyle"/>).</summary>
        public event Action<int> SuggestedFormationSelected;
        /// <summary>A module chip was picked (index into the list given to <see cref="SetFormationOptions"/>).</summary>
        public event Action<int> FormationSelected;
        /// <summary>An instruction was picked: axis = index into the rows given to <see cref="SetInstructionAxes"/>; level 0-based.</summary>
        public event Action<int, int> InstructionSelected;
        public event Action SaveClicked;
        /// <summary>A chip of the new-rule form was picked: part 0 minute · 1 score · 2 shout; value = option index.</summary>
        public event Action<int, int> PlanOptionSelected;
        public event Action PlanAddClicked;
        /// <summary>A rule's remove button was pressed (index into the list given to <see cref="SetPlanRules"/>).</summary>
        public event Action<int> PlanRemoveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _title;
        private readonly VisualElement _grid;
        private readonly VisualElement _colPitch;
        private readonly VisualElement _colSettings;
        private readonly PitchFormationView _shapePitch;
        private readonly VisualElement _styleGrid;
        private readonly List<Button> _styleChips = new List<Button>();
        private readonly Label _styleName;
        private readonly Label _styleDescription;
        private readonly VisualElement _suggestedRow;
        private readonly VisualElement _formationGrid;
        private readonly List<Button> _formationChips = new List<Button>();
        private readonly Label _formationShape;
        private readonly VisualElement _instructionCards;
        private readonly List<Button[]> _axisChips = new List<Button[]>();
        private readonly Label _familiarityValue;
        private readonly MeterBar _familiarity;
        private readonly Label _status;
        private readonly VisualElement _planRules;
        private readonly VisualElement[] _planChipRows = new VisualElement[3];
        private readonly List<Button>[] _planChips = { new List<Button>(), new List<Button>(), new List<Button>() };
        private readonly Button _planAdd;

        public TacticsView(Func<string, string> tr)
        {
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

            VisualElement styleCard = UiKit.OptionCard();
            styleCard.Add(UiKit.BlockHead(tr("tactics.style_caption")));
            _styleGrid = ChipGrid("fts-tac__styles");
            styleCard.Add(_styleGrid);
            _styleName = new Label(string.Empty);
            _styleName.AddToClassList("fts-tac__stylename");
            _styleName.style.whiteSpace = WhiteSpace.Normal;
            styleCard.Add(_styleName);
            _styleDescription = UiKit.HelpText(string.Empty);
            styleCard.Add(_styleDescription);
            Label suggestedCaption = UiKit.Caption(tr("tactics.style.suggested"));
            suggestedCaption.AddToClassList("fts-tac__axis");
            styleCard.Add(suggestedCaption);
            _suggestedRow = new VisualElement();
            _suggestedRow.AddToClassList("fts-plan__chips");
            styleCard.Add(_suggestedRow);
            _colSettings.Add(styleCard);

            VisualElement moduleCard = UiKit.OptionCard();
            moduleCard.Add(UiKit.BlockHead(tr("tactics.formation_caption")));
            _formationGrid = ChipGrid("fts-tac__modules");
            moduleCard.Add(_formationGrid);
            _formationShape = UiKit.HelpText(string.Empty);
            moduleCard.Add(_formationShape);
            _colSettings.Add(moduleCard);

            // One card per instruction phase, built by SetInstructionAxes.
            _instructionCards = new VisualElement();
            _colSettings.Add(_instructionCards);

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

            VisualElement planCard = UiKit.OptionCard();
            planCard.Add(UiKit.BlockHead(tr("tactics.plan_caption")));
            _planRules = new VisualElement();
            planCard.Add(_planRules);
            string[] partCaptions = { "tactics.plan.from", "tactics.plan.if", "tactics.plan.shout" };
            for (int part = 0; part < 3; part++)
            {
                Label caption = UiKit.Caption(tr(partCaptions[part]));
                caption.AddToClassList("fts-tac__axis");
                planCard.Add(caption);
                _planChipRows[part] = new VisualElement();
                _planChipRows[part].AddToClassList("fts-plan__chips");
                planCard.Add(_planChipRows[part]);
            }
            _planAdd = UiKit.PrimaryButton(tr("tactics.plan.add"), () => PlanAddClicked?.Invoke());
            _planAdd.AddToClassList("fts-plan__add");
            planCard.Add(_planAdd);
            _colSettings.Add(planCard);

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

        /// <summary>The style chips, in the order the presenter will index them.</summary>
        public void SetStyleOptions(IReadOnlyList<string> labels) =>
            FillChipGrid(_styleGrid, _styleChips, labels, "fts-tac__style", 45f, i => StyleSelected?.Invoke(i));

        /// <summary>
        /// Lights the picked style and shows its name (marked custom by the presenter once
        /// tweaked), its one-line description and its suggested modules, lighting the one in use.
        /// </summary>
        public void SetStyle(int selected, string name, string description, IReadOnlyList<string> suggested, int suggestedInUse)
        {
            for (int i = 0; i < _styleChips.Count; i++)
                UiKit.SetSegChipState(_styleChips[i], i == selected);
            _styleName.text = name ?? string.Empty;
            _styleDescription.text = description ?? string.Empty;

            _suggestedRow.Clear();
            for (int i = 0; i < suggested.Count; i++)
            {
                int index = i;
                Button chip = UiKit.SegChip(suggested[i], null, () => SuggestedFormationSelected?.Invoke(index));
                chip.AddToClassList("fts-plan__chip");
                chip.style.flexBasis = new Length(30, LengthUnit.Percent);
                chip.style.flexGrow = 0f;
                UiKit.SetSegChipState(chip, i == suggestedInUse);
                _suggestedRow.Add(chip);
            }
        }

        /// <summary>The module chips, in the order the presenter will index them.</summary>
        public void SetFormationOptions(IReadOnlyList<string> labels) =>
            FillChipGrid(_formationGrid, _formationChips, labels, "fts-tac__module", 30f, i => FormationSelected?.Invoke(i));

        /// <summary>Lights the picked module and shows its in/out-of-possession shape line.</summary>
        public void SetFormation(int selected, string shape)
        {
            for (int i = 0; i < _formationChips.Count; i++)
                UiKit.SetSegChipState(_formationChips[i], i == selected);
            _formationShape.text = shape ?? string.Empty;
        }

        /// <summary>Builds one card per phase and a segmented row per instruction, in the order given.</summary>
        public void SetInstructionAxes(IReadOnlyList<TacticAxisVm> axes)
        {
            _instructionCards.Clear();
            _axisChips.Clear();
            VisualElement card = null;
            string phase = null;
            for (int axis = 0; axis < axes.Count; axis++)
            {
                TacticAxisVm vm = axes[axis];
                if (card == null || vm.Phase != phase)
                {
                    phase = vm.Phase;
                    card = UiKit.OptionCard();
                    card.Add(UiKit.BlockHead(phase));
                    _instructionCards.Add(card);
                }

                Label caption = UiKit.Caption(vm.Caption);
                caption.AddToClassList("fts-tac__axis");
                card.Add(caption);
                card.Add(UiKit.HelpText(vm.Description));

                var chips = new Button[vm.Levels.Count];
                for (int v = 0; v < chips.Length; v++)
                {
                    int a = axis, level = v;
                    chips[v] = UiKit.SegChip(vm.Levels[v], null, () => InstructionSelected?.Invoke(a, level));
                }
                VisualElement seg = UiKit.SegRow(chips);
                seg.AddToClassList("fts-tac__seg");
                if (chips.Length > 3) seg.AddToClassList("fts-tac__seg--wide");
                card.Add(seg);
                _axisChips.Add(chips);
            }
        }

        /// <summary>Lights the picked level of each instruction row.</summary>
        public void SetInstructionLevels(IReadOnlyList<int> levels)
        {
            for (int axis = 0; axis < _axisChips.Count && axis < levels.Count; axis++)
                for (int v = 0; v < _axisChips[axis].Length; v++)
                    UiKit.SetSegChipState(_axisChips[axis][v], v == levels[axis]);
        }

        /// <summary>Familiarity with the picked tactic, 0-100.</summary>
        public void SetFamiliarity(string text, int percent)
        {
            _familiarityValue.text = text ?? string.Empty;
            Color color = percent >= 80 ? UiKit.Positive : percent >= 40 ? UiKit.Warning : UiKit.Danger;
            _familiarity.Set(percent, color);
        }

        /// <summary>The options of the new-rule form: minutes, score situations and shouts, as labels.</summary>
        public void SetPlanOptions(IReadOnlyList<string> minutes, IReadOnlyList<string> situations, IReadOnlyList<string> shouts)
        {
            IReadOnlyList<string>[] parts = { minutes, situations, shouts };
            for (int part = 0; part < 3; part++)
            {
                _planChipRows[part].Clear();
                _planChips[part].Clear();
                for (int i = 0; i < parts[part].Count; i++)
                {
                    int p = part, value = i;
                    Button chip = UiKit.SegChip(parts[part][i], null, () => PlanOptionSelected?.Invoke(p, value));
                    chip.AddToClassList("fts-plan__chip");
                    chip.style.flexBasis = new Length(part == 0 ? 15 : 30, LengthUnit.Percent);
                    _planChipRows[part].Add(chip);
                    _planChips[part].Add(chip);
                }
            }
        }

        /// <summary>Lights the picked option of each part of the new-rule form; Add is off when the rule cannot be added.</summary>
        public void SetPlanDraft(int minute, int situation, int shout, bool canAdd)
        {
            int[] picked = { minute, situation, shout };
            for (int part = 0; part < 3; part++)
                for (int i = 0; i < _planChips[part].Count; i++)
                    UiKit.SetSegChipState(_planChips[part][i], i == picked[part]);
            _planAdd.SetEnabled(canAdd);
        }

        /// <summary>The plan's rules, one line each with a remove button; <paramref name="emptyText"/> when there are none.</summary>
        public void SetPlanRules(IReadOnlyList<string> rules, string removeText, string emptyText)
        {
            _planRules.Clear();
            if (rules.Count == 0)
            {
                var empty = new Label(emptyText ?? string.Empty);
                empty.AddToClassList("fts-plan__empty");
                _planRules.Add(empty);
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("fts-plan__rule");
                var text = new Label(rules[i]);
                text.AddToClassList("fts-plan__ruletext");
                row.Add(text);
                row.Add(UiKit.SmallButton(removeText, () => PlanRemoveClicked?.Invoke(index)));
                _planRules.Add(row);
            }
        }

        /// <summary>Updates the live shape preview with the user's XI in the chosen formation.</summary>
        public void SetShape(IReadOnlyList<PitchTokenVm> tokens) => _shapePitch.SetTokens(tokens);

        // ---------------------------------------------------------------- building

        private static VisualElement ChipGrid(string className)
        {
            var grid = new VisualElement();
            grid.AddToClassList(className);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            return grid;
        }

        private static void FillChipGrid(VisualElement grid, List<Button> chips, IReadOnlyList<string> labels,
            string chipClass, float basisPercent, Action<int> onPick)
        {
            grid.Clear();
            chips.Clear();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button chip = UiKit.SegChip(labels[i], null, () => onPick(index));
                chip.AddToClassList(chipClass);
                chip.style.flexGrow = 1f;
                chip.style.flexBasis = Length.Percent(basisPercent);
                chip.style.marginRight = UiKit.SpaceSm;
                chip.style.marginBottom = UiKit.SpaceSm;
                grid.Add(chip);
                chips.Add(chip);
            }
        }

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
