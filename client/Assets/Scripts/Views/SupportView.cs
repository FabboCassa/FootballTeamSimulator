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
        public string Label;
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
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("support.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 4;
            Root.Add(_header);

            _help = new Label(tr("support.help"));
            _help.style.color = new Color(1f, 1f, 1f, 0.7f);
            _help.style.fontSize = 13;
            _help.style.whiteSpace = WhiteSpace.Normal;
            _help.style.maxWidth = 440;
            _help.style.marginBottom = 6;
            Root.Add(_help);

            Root.Add(SectionLabel(tr("support.roster_caption")));
            _rosterList = new ScrollView();
            _rosterList.style.flexGrow = 1f;
            Root.Add(_rosterList);

            _selectedCaption = SectionLabel(string.Empty);
            Root.Add(_selectedCaption);

            _actionRow = new VisualElement();
            _actionRow.style.flexDirection = FlexDirection.Row;
            _actionRow.style.flexWrap = Wrap.Wrap;
            Root.Add(_actionRow);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.marginTop = 6;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.maxWidth = 440;
            _status.style.alignSelf = Align.Center;
            Root.Add(_status);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 6;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);
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

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 36;
                row.style.marginBottom = 2;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = vm.Selected
                    ? new Color(0.30f, 0.45f, 0.70f, 0.55f)
                    : new Color(1f, 1f, 1f, 0.06f);
                row.tooltip = vm.Tooltip ?? string.Empty;
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                var name = new Label(vm.Label);
                name.style.flexGrow = 1f;
                name.style.fontSize = 13;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(name);

                ConditionStrip.Append(row, vm.FormArrow, vm.MoraleFace, vm.Fitness);

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
