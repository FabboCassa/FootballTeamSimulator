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
        public string Name;
        public string RoleAbbr;
        public int RoleGroup;      // 0 GK · 1 def · 2 mid · 3 att
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
    /// Support screen (task 4.5), redrawn in task 14.4 on the standard page.
    /// Two blocks side by side on a desktop, stacked on a phone:
    ///   • the squad — one tappable row per player: role chip, name, condition (bar + F / M pills);
    ///     the picked row is tinted in the accent;
    ///   • the conversation — the picked player's name, then the five actions as big buttons each
    ///     carrying one line that says what it is for, so nobody has to guess what "Sprona" does.
    /// Dumb view: the presenter owns the model and cooldowns.
    /// </summary>
    public sealed class SupportView
    {
        public event Action<int> PlayerSelected;
        public event Action<int> ActionClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private static readonly string[] DescKeys =
        {
            "support.desc.praise", "support.desc.encourage", "support.desc.motivate",
            "support.desc.criticize", "support.desc.rest"
        };

        private readonly Func<string, string> _tr;
        private readonly Label _title;
        private readonly VisualElement _grid;
        private readonly VisualElement _colRoster;
        private readonly VisualElement _colActions;
        private readonly Label _rosterCount;
        private readonly VisualElement _roster;
        private readonly Label _selected;
        private readonly VisualElement _actions;
        private readonly Label _status;
        private readonly string _rosterCaptionText;

        public SupportView(Func<string, string> tr)
        {
            _tr = tr;
            _rosterCaptionText = tr("support.roster_caption");

            PageParts page = UiKit.StandardPage(tr("support.title"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke());
            Root = page.Root;
            _title = page.Title;

            Label help = UiKit.HelpText(tr("support.help"));
            help.AddToClassList("fts-support__help");
            page.Column.Add(help);

            _grid = new VisualElement();
            _grid.AddToClassList("fts-support__grid");
            page.Column.Add(_grid);

            // ---- squad
            _colRoster = UiKit.OptionCard();
            _colRoster.AddToClassList("fts-support__roster");
            var head = UiKit.BlockHead(_rosterCaptionText);
            _rosterCount = head.childCount > 0 ? head[0] as Label : null;
            _colRoster.Add(head);
            _roster = new VisualElement();
            _colRoster.Add(_roster);
            _grid.Add(_colRoster);

            // ---- conversation
            _colActions = UiKit.RaisedCard();
            _colActions.AddToClassList("fts-support__actions");
            _selected = new Label(string.Empty);
            _selected.AddToClassList("fts-support__selected");
            UiKit.UseDisplayFont(_selected);
            _selected.style.whiteSpace = WhiteSpace.Normal;
            _colActions.Add(_selected);
            _actions = new VisualElement();
            _colActions.Add(_actions);
            _status = new Label(string.Empty);
            _status.AddToClassList("fts-support__status");
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            _colActions.Add(_status);
            _grid.Add(_colActions);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        public void SetHeader(string text) => _title.text = text ?? string.Empty;

        public void SetSelectedCaption(string text) => _selected.text = text ?? string.Empty;

        public void SetStatus(string text)
        {
            _status.text = text ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetRoster(IReadOnlyList<SupportRowVm> rows)
        {
            _roster.Clear();
            if (_rosterCount != null)
                _rosterCount.text = (_rosterCaptionText + " · " + rows.Count).ToUpperInvariant();

            foreach (SupportRowVm vm in rows)
            {
                int playerId = vm.PlayerId;
                VisualElement row = PlayerRowKit.Row();
                row.AddToClassList("fts-support__row");
                row.EnableInClassList("fts-support__row--selected", vm.Selected);
                row.tooltip = vm.Tooltip ?? string.Empty;
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));
                VisualElement nameCell = PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true);
                PlayerRowKit.SetSelected(nameCell, vm.Selected);
                row.Add(nameCell);

                VisualElement condCell = PlayerRowKit.Cell(0);
                condCell.AddToClassList("fts-support__cond");
                condCell.style.width = StyleKeyword.Null;
                PlayerRowKit.SetSelected(condCell, vm.Selected);
                ConditionStrip.Append(condCell, vm.FormArrow, vm.MoraleFace, vm.Fitness);
                row.Add(condCell);

                _roster.Add(row);
            }
        }

        public void SetActions(IReadOnlyList<SupportButtonVm> actions)
        {
            _actions.Clear();
            foreach (SupportButtonVm vm in actions)
            {
                int actionId = vm.ActionId;
                var button = new Button(() => ActionClicked?.Invoke(actionId)) { text = string.Empty };
                button.AddToClassList("fts-support__action");
                button.style.flexDirection = FlexDirection.Column;
                button.style.alignItems = Align.FlexStart;

                var name = new Label(vm.Text ?? string.Empty);
                name.AddToClassList("fts-support__actionname");
                button.Add(name);

                if (actionId >= 0 && actionId < DescKeys.Length)
                {
                    var desc = new Label(_tr(DescKeys[actionId]));
                    desc.AddToClassList("fts-support__actiondesc");
                    desc.style.whiteSpace = WhiteSpace.Normal;
                    button.Add(desc);
                }

                button.SetEnabled(vm.Enabled);
                _actions.Add(button);
            }
        }

        private void Layout(Viewport viewport)
        {
            bool twoColumns = viewport != Viewport.Mobile;
            // On a phone the conversation comes first: the list below is long, the buttons are the point.
            _grid.style.flexDirection = twoColumns ? FlexDirection.Row : FlexDirection.ColumnReverse;
            _grid.style.alignItems = twoColumns ? Align.FlexStart : Align.Stretch;
            _colRoster.style.flexGrow = twoColumns ? 3f : 0f;
            _colRoster.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colActions.style.flexGrow = twoColumns ? 2f : 0f;
            _colActions.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colActions.style.marginLeft = twoColumns ? UiKit.SpaceMd : 0f;
            _colActions.style.marginBottom = twoColumns ? 0f : UiKit.SpaceMd;
        }
    }
}
