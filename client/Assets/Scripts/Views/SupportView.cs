using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A selectable roster row on the Support screen: a player + his condition.</summary>
    public sealed class SupportRowVm
    {
        public int PlayerId;
        public string Name;        // player name (its own cell)
        public string RoleAbbr;    // localized role abbreviation (coloured cell)
        public int RoleGroup;      // 0 GK · 1 def · 2 mid · 3 att (cell colour)
        public string FormArrow;
        public string MoraleFace;
        public int Fitness;
        public string Tooltip;
        public bool Selected;
    }

    /// <summary>One support-action button for the selected player (greyed out on cooldown).</summary>
    public sealed class SupportButtonVm
    {
        public int ActionId;
        public string Text;
        public bool Enabled;
    }

    /// <summary>
    /// Support screen (task 4.5): pick a player from the roster, then apply a lightweight
    /// conversation (praise / encourage / motivate / criticize / rest). Each row shows the
    /// shared condition strip (so the effect is visible), and the action buttons grey out
    /// while on cooldown. Dumb view — the presenter owns the model, translates every label,
    /// and decides which actions are available; the view only emits events and renders.
    /// </summary>
    public sealed class SupportView
    {
        public event Action<int> PlayerSelected;
        public event Action<int> ActionClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Label _help;
        private readonly ScrollView _rosterList;
        private readonly Label _selectedCaption;
        private readonly VisualElement _actionRow;
        private readonly Label _status;

        public SupportView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(680f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceXs;
            col.Add(_header);

            _help = new Label(tr("support.help"));
            _help.style.color = UiKit.TextMuted;
            _help.style.fontSize = 13;
            _help.style.whiteSpace = WhiteSpace.Normal;
            _help.style.unityTextAlign = TextAnchor.MiddleCenter;
            _help.style.marginBottom = UiKit.SpaceSm;
            col.Add(_help);

            col.Add(SectionLabel(tr("support.roster_caption")));
            _rosterList = new ScrollView();
            _rosterList.style.flexGrow = 1f;
            _rosterList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_rosterList);

            _selectedCaption = SectionLabel(string.Empty);
            _selectedCaption.style.flexShrink = 0f;
            col.Add(_selectedCaption);

            _actionRow = new VisualElement();
            _actionRow.style.flexDirection = FlexDirection.Row;
            _actionRow.style.flexWrap = Wrap.Wrap;
            _actionRow.style.justifyContent = Justify.Center;
            _actionRow.style.flexShrink = 0f;
            col.Add(_actionRow);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.alignSelf = Align.Center;
            _status.style.flexShrink = 0f;
            col.Add(_status);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = UiKit.SpaceSm;
            footer.style.flexShrink = 0f;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetSelectedCaption(string text) => _selectedCaption.text = text;
        public void SetStatus(string text) => _status.text = text;

        public void SetRoster(IReadOnlyList<SupportRowVm> rows)
        {
            _rosterList.Clear();
            foreach (SupportRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                VisualElement row = PlayerRowKit.Row();
                row.tooltip = vm.Tooltip ?? string.Empty;
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                // Name (its own cell, tinted when selected) · coloured role cell · condition cell.
                VisualElement nameCell = PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true);
                PlayerRowKit.SetSelected(nameCell, vm.Selected);
                row.Add(nameCell);

                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));

                VisualElement condCell = PlayerRowKit.Cell(128f);
                ConditionStrip.Append(condCell, vm.FormArrow, vm.MoraleFace, vm.Fitness);
                row.Add(condCell);

                _rosterList.Add(row);
            }
        }

        public void SetActions(IReadOnlyList<SupportButtonVm> actions)
        {
            _actionRow.Clear();
            foreach (SupportButtonVm vm in actions)
            {
                int actionId = vm.ActionId;
                var button = new Button(() => ActionClicked?.Invoke(actionId)) { text = vm.Text };
                button.style.height = 40;
                button.style.fontSize = 13;
                button.style.marginRight = 6;
                button.style.marginTop = 4;
                button.style.minWidth = 96;
                button.SetEnabled(vm.Enabled);
                _actionRow.Add(button);
            }
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
