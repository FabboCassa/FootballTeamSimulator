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
    ///
    /// Layout (task 6.12): the wide page scaffold — a column header, a roster panel that fills
    /// all remaining height, and a pinned action panel at the bottom that names the picked
    /// player, so the screen is full at any window size instead of a narrow strip in the middle.
    /// </summary>
    public sealed class SupportView
    {
        public event Action<int> PlayerSelected;
        public event Action<int> ActionClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly ScrollView _rosterList;
        private readonly Label _rosterCaption;
        private readonly Label _selectedCaption;
        private readonly VisualElement _actionRow;
        private readonly Label _status;
        private readonly string _rosterCaptionText;

        public SupportView(Func<string, string> tr)
        {
            _rosterCaptionText = tr("support.roster_caption");

            Root = UiKit.ScreenRoot();

            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            col.Add(_header);
            col.Add(UiKit.HelpText(tr("support.help")));

            // ---- roster panel: fills every pixel left between the header and the action bar.
            VisualElement listPanel = UiKit.Panel(grow: true);
            listPanel.style.paddingTop = UiKit.SpaceSm;
            listPanel.style.paddingBottom = UiKit.SpaceSm;
            _rosterCaption = UiKit.SectionLabel(_rosterCaptionText);
            _rosterCaption.style.marginTop = 0;
            listPanel.Add(_rosterCaption);
            _rosterList = UiKit.ListScroll();
            listPanel.Add(_rosterList);
            col.Add(listPanel);

            // ---- action panel: who is picked + what you can say to him.
            VisualElement actionPanel = UiKit.Panel();
            _selectedCaption = UiKit.SectionLabel(string.Empty);
            _selectedCaption.style.marginTop = 0;
            actionPanel.Add(_selectedCaption);

            _actionRow = new VisualElement();
            _actionRow.style.flexDirection = FlexDirection.Row;
            _actionRow.style.flexWrap = Wrap.Wrap;
            _actionRow.style.alignItems = Align.Center;
            _actionRow.style.flexShrink = 0f;
            actionPanel.Add(_actionRow);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 0f;
            actionPanel.Add(_status);
            col.Add(actionPanel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;

        public void SetSelectedCaption(string text) => _selectedCaption.text = text ?? string.Empty;

        public void SetStatus(string text) => _status.text = text;

        public void SetRoster(IReadOnlyList<SupportRowVm> rows)
        {
            _rosterList.Clear();
            _rosterCaption.text = $"{_rosterCaptionText} · {rows.Count}";

            foreach (SupportRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                VisualElement row = PlayerRowKit.Row();
                row.tooltip = vm.Tooltip ?? string.Empty;
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                // Coloured role cell first (the reparto colour bands the list), then the name,
                // then a roomy condition cell so the bar/arrow/face never get clipped.
                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));

                VisualElement nameCell = PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true);
                PlayerRowKit.SetSelected(nameCell, vm.Selected);
                row.Add(nameCell);

                VisualElement condCell = PlayerRowKit.Cell(180f);
                PlayerRowKit.SetSelected(condCell, vm.Selected);
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
                Button button = UiKit.SmallButton(vm.Text, () => ActionClicked?.Invoke(actionId), 120f);
                button.style.height = 40;
                button.style.marginLeft = 0;
                button.style.marginRight = 8;
                button.style.marginTop = 4;
                button.style.marginBottom = 4;
                button.SetEnabled(vm.Enabled);
                _actionRow.Add(button);
            }
        }
    }
}
