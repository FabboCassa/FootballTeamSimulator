using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A roster row on the Training screen: a player and his individual focus.</summary>
    public sealed class TrainingRowVm
    {
        public int PlayerId;
        public string Label;
        public string FocusLabel;
    }

    /// <summary>
    /// Training screen (task 4.3): a squad-wide team-focus selector, a note about what
    /// the current focus does, and a roster where each player's individual focus can be
    /// cycled. Dumb view — the presenter owns the plan and translates every label; the
    /// view only emits cycle/save/back events and renders the strings it's handed.
    /// </summary>
    public sealed class TrainingView
    {
        public event Action TeamFocusCycleClicked;
        public event Action<int> IndividualFocusCycleClicked;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Button _teamFocusButton;
        private readonly Label _teamHint;
        private readonly ScrollView _rosterList;
        private readonly Label _status;

        public TrainingView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("training.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 8;
            Root.Add(_header);

            Root.Add(SectionLabel(tr("training.team_caption")));
            _teamFocusButton = CycleButton(() => TeamFocusCycleClicked?.Invoke());
            Root.Add(_teamFocusButton);

            _teamHint = new Label(string.Empty);
            _teamHint.style.color = new Color(1f, 1f, 1f, 0.7f);
            _teamHint.style.fontSize = 13;
            _teamHint.style.whiteSpace = WhiteSpace.Normal;
            _teamHint.style.maxWidth = 420;
            _teamHint.style.marginBottom = 6;
            Root.Add(_teamHint);

            Root.Add(SectionLabel(tr("training.roster_caption")));
            _rosterList = new ScrollView();
            _rosterList.style.flexGrow = 1f;
            Root.Add(_rosterList);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            footer.Add(FooterButton(tr("training.save"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.marginTop = 4;
            _status.style.alignSelf = Align.Center;
            Root.Add(_status);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetTeamFocus(string text) => _teamFocusButton.text = text;
        public void SetTeamHint(string text) => _teamHint.text = text;
        public void SetStatus(string text) => _status.text = text;

        public void SetRoster(IReadOnlyList<TrainingRowVm> rows)
        {
            _rosterList.Clear();
            foreach (TrainingRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 36;
                row.style.marginBottom = 2;
                row.style.paddingLeft = 8;
                row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);

                var name = new Label(vm.Label);
                name.style.flexGrow = 1f;
                name.style.fontSize = 13;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(name);

                var focusButton = new Button(() => IndividualFocusCycleClicked?.Invoke(playerId)) { text = vm.FocusLabel };
                focusButton.style.width = 150;
                focusButton.style.height = 30;
                focusButton.style.fontSize = 13;
                focusButton.style.marginRight = 6;
                row.Add(focusButton);

                _rosterList.Add(row);
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

        private static Button CycleButton(Action onClick)
        {
            var button = new Button(onClick);
            button.style.height = 40;
            button.style.fontSize = 15;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginBottom = 4;
            button.style.maxWidth = 420;
            return button;
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
