using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A recent-result chip for the opponent report: outcome drives the colour,
    /// the text is the scoreline from the opponent's point of view (e.g. "3-1").</summary>
    public sealed class FormChipVm
    {
        public int Outcome;        // 0 loss · 1 draw · 2 win (opponent's perspective)
        public string Text = string.Empty; // scoreline, already formatted by the presenter
    }

    /// <summary>A scouted opponent-squad row: identity + scouted overall (a range until fully known).</summary>
    public sealed class OpponentRosterRowVm
    {
        public int PlayerId;
        public string Name = string.Empty;
        public string RoleAbbr = string.Empty;
        public int RoleGroup;      // 0 GK · 1 def · 2 mid · 3 att (cell colour)
        public string Age = string.Empty;
        public string OvrText = string.Empty; // scouted OVR / range
    }

    /// <summary>
    /// Pre-match opponent report (task 6.11): a read-only intel screen the user opens on
    /// purpose from the Hub next-match card. Shows the upcoming opponent's crest, a coarse
    /// strength readout, their likely formation/tactic, recent form, their likely XI on a
    /// mirrored visual pitch (reusing the 6.7 <see cref="PitchFormationView"/>), and their
    /// squad with scouted overall ranges (the 5.4 knowledge layer — vaguer for the unscouted).
    /// Tapping a squad row opens that player's profile. Nothing here writes back — it never
    /// leaks into the user's own Tactics screen, and it can't change the coming match, so
    /// neither side gains an unfair edge. Dumb view: the presenter owns the model and strings.
    /// </summary>
    public sealed class OpponentReportView
    {
        private static readonly Color WinColor = UiKit.Hex(0x43A85F);
        private static readonly Color DrawColor = UiKit.Hex(0xE7B23A);
        private static readonly Color LossColor = UiKit.Hex(0xD25550);

        public event Action<int> PlayerSelected;
        public event Action BackClicked;

        public VisualElement Root { get; }

        /// <summary>Below this root width the two columns stack vertically (portrait / phone).</summary>
        private const float StackBelow = 760f;

        private readonly Label _header;
        private readonly VisualElement _crestSlot;
        private readonly Label _strength;
        private readonly Label _formation;
        private readonly Label _tactic;
        private readonly Label _formCaption;
        private readonly VisualElement _formRow;
        private readonly PitchFormationView _pitch;
        private readonly VisualElement _pitchBox;
        private readonly ScrollView _roster;
        private readonly Label _empty;
        private readonly VisualElement _body;
        private readonly VisualElement _bodyRow;
        private readonly VisualElement _leftCol;
        private readonly VisualElement _rightCol;
        private bool _stacked;

        public OpponentReportView(Func<string, string> tr)
        {
            Root = UiKit.ScreenRoot();

            // Task 6.12: the report is a wide two-column layout, so it uses the same page column
            // as the other list/table screens instead of sprawling edge to edge.
            VisualElement page = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(page);

            // Header: crest + opponent line, kept compact at the top so the body gets the room.
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.style.flexShrink = 0f;
            top.style.marginBottom = UiKit.SpaceSm;

            _crestSlot = new VisualElement();
            _crestSlot.style.alignItems = Align.Center;
            _crestSlot.style.justifyContent = Justify.Center;
            _crestSlot.style.marginRight = UiKit.SpaceSm;
            top.Add(_crestSlot);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.whiteSpace = WhiteSpace.Normal;
            _header.style.marginBottom = 0;
            top.Add(_header);
            page.Add(top);

            // Everything below the header lives in _body so the whole thing can be swapped
            // for a single "no upcoming match" line when the season is complete.
            _body = new VisualElement();
            _body.style.flexGrow = 1f;
            _body.style.flexShrink = 1f;
            page.Add(_body);

            _empty = UiKit.Subtitle(string.Empty);
            _empty.style.display = DisplayStyle.None;
            page.Add(_empty);

            // Intel card (full width): strength, formation, tactic, recent form.
            var card = UiKit.Card();
            card.style.flexShrink = 0f;
            _strength = InfoLine();
            _formation = InfoLine();
            _tactic = InfoLine();
            card.Add(_strength);
            card.Add(_formation);
            card.Add(_tactic);

            _formCaption = UiKit.Caption(tr("opponent.form_caption"));
            _formCaption.style.marginTop = UiKit.SpaceSm;
            card.Add(_formCaption);

            _formRow = new VisualElement();
            _formRow.style.flexDirection = FlexDirection.Row;
            _formRow.style.flexWrap = Wrap.Wrap;
            _formRow.style.marginTop = UiKit.SpaceXs;
            card.Add(_formRow);
            _body.Add(card);

            // Two-column body: a large pitch on the left, the scouted squad list on the right.
            _bodyRow = new VisualElement();
            _bodyRow.style.flexDirection = FlexDirection.Row;
            _bodyRow.style.flexGrow = 1f;
            _bodyRow.style.marginTop = UiKit.SpaceSm;
            _body.Add(_bodyRow);

            _leftCol = new VisualElement();
            _leftCol.style.flexGrow = 1.7f;
            _leftCol.style.flexBasis = 0;
            _leftCol.Add(SectionCaption(tr("opponent.shape_caption")));
            _pitchBox = new VisualElement();
            _pitchBox.style.flexGrow = 1f;
            _pitchBox.style.minHeight = 300;
            _pitch = new PitchFormationView(mirror: true);
            _pitchBox.Add(_pitch);
            _leftCol.Add(_pitchBox);
            _bodyRow.Add(_leftCol);

            _rightCol = new VisualElement();
            _rightCol.style.flexGrow = 1f;
            _rightCol.style.flexBasis = 0;
            _rightCol.style.marginLeft = UiKit.SpaceMd;
            _rightCol.Add(SectionCaption(tr("opponent.roster_caption")));
            _roster = new ScrollView();
            _roster.style.flexGrow = 1f;
            _roster.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _roster.verticalScrollerVisibility = ScrollerVisibility.Hidden; // no ugly scrollbar; wheel/drag still scroll
            _rightCol.Add(_roster);
            _bodyRow.Add(_rightCol);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            page.Add(footer);

            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsive(Root.resolvedStyle.width));
        }

        /// <summary>Side-by-side on wide viewports, stacked (pitch over list) on narrow ones.</summary>
        private void ApplyResponsive(float width)
        {
            bool stack = width > 1f && width < StackBelow;
            if (stack == _stacked)
                return;
            _stacked = stack;

            _bodyRow.style.flexDirection = stack ? FlexDirection.Column : FlexDirection.Row;
            _rightCol.style.marginLeft = stack ? 0 : UiKit.SpaceMd;
            _rightCol.style.marginTop = stack ? UiKit.SpaceSm : 0;
            // Stacked: give each block a real height so neither collapses; side-by-side: let them fill.
            if (stack) _pitchBox.style.height = 320;
            else _pitchBox.style.height = StyleKeyword.Auto;
            _pitchBox.style.flexGrow = stack ? 0f : 1f;
            _rightCol.style.minHeight = stack ? 200 : 0;
        }

        private static Label SectionCaption(string text)
        {
            Label c = UiKit.Caption(text);
            c.style.marginBottom = UiKit.SpaceXs;
            c.style.flexShrink = 0f;
            return c;
        }

        public void SetHeader(string text) => _header.text = text;

        public void SetCrest(VisualElement crest)
        {
            _crestSlot.Clear();
            if (crest != null) _crestSlot.Add(crest);
        }

        public void SetStrength(string text) => _strength.text = text;
        public void SetFormation(string text) => _formation.text = text;
        public void SetTactic(string text) => _tactic.text = text;

        /// <summary>Shows the "no upcoming match" state (season complete): hides the report body.</summary>
        public void SetEmpty(string message)
        {
            _empty.text = message;
            _empty.style.display = DisplayStyle.Flex;
            _body.style.display = DisplayStyle.None;
            _crestSlot.style.display = DisplayStyle.None;
        }

        public void SetForm(IReadOnlyList<FormChipVm> chips, string emptyText)
        {
            _formRow.Clear();
            if (chips == null || chips.Count == 0)
            {
                var none = UiKit.Caption(emptyText);
                none.style.marginTop = 2;
                _formRow.Add(none);
                return;
            }

            foreach (FormChipVm chip in chips)
            {
                Label pill = UiKit.Pill(chip.Text, OutcomeColor(chip.Outcome), Color.white);
                pill.style.marginRight = UiKit.SpaceXs;
                pill.style.marginBottom = UiKit.SpaceXs;
                _formRow.Add(pill);
            }
        }

        public void SetShape(IReadOnlyList<PitchTokenVm> tokens) => _pitch.SetTokens(tokens);

        public void SetRoster(IReadOnlyList<OpponentRosterRowVm> rows)
        {
            _roster.Clear();
            foreach (OpponentRosterRowVm vm in rows)
            {
                int playerId = vm.PlayerId;
                VisualElement row = PlayerRowKit.Row();
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                row.Add(PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true));
                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));
                row.Add(PlayerRowKit.TextCell(vm.Age, 46f, TextAnchor.MiddleCenter));
                row.Add(PlayerRowKit.TextCell(vm.OvrText, 96f, TextAnchor.MiddleCenter));

                _roster.Add(row);
            }
        }

        private Color OutcomeColor(int outcome) =>
            outcome >= 2 ? WinColor : outcome == 1 ? DrawColor : LossColor;

        private static Label InfoLine()
        {
            var label = new Label(string.Empty);
            label.style.fontSize = UiKit.FontBody;
            label.style.color = UiKit.TextPrimary;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 2;
            return label;
        }
    }
}
