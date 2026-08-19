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
            Root = UiKit.ScreenRoot();

            var col = UiKit.PageColumn(UiKit.WidthWide);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            VisualElement teamPanel = UiKit.Panel();
            Label teamCaption = SectionLabel(tr("training.team_caption"));
            teamCaption.style.marginTop = 0;
            teamPanel.Add(teamCaption);
            _teamFocusButton = CycleButton(() => TeamFocusCycleClicked?.Invoke());
            teamPanel.Add(_teamFocusButton);

            _teamHint = UiKit.HelpText(string.Empty);
            _teamHint.style.marginBottom = 0;
            teamPanel.Add(_teamHint);
            col.Add(teamPanel);

            VisualElement rosterPanel = UiKit.Panel(grow: true);
            Label rosterCaption = SectionLabel(tr("training.roster_caption"));
            rosterCaption.style.marginTop = 0;
            rosterPanel.Add(rosterCaption);
            _rosterList = UiKit.ListScroll();
            rosterPanel.Add(_rosterList);
            col.Add(rosterPanel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(FooterButton(tr("training.save"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceXs;
            _status.style.alignSelf = Align.Center;
            _status.style.flexShrink = 0f;
            col.Add(_status);
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

                VisualElement row = UiKit.RowCard(48f);

                var name = new Label(vm.Label);
                name.style.flexGrow = 1f;
                name.style.flexShrink = 1f;
                name.style.fontSize = 14;
                name.style.color = UiKit.TextPrimary;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                row.Add(name);

                Button focusButton = UiKit.SmallButton(
                    vm.FocusLabel, () => IndividualFocusCycleClicked?.Invoke(playerId), 180f);
                row.Add(focusButton);

                _rosterList.Add(row);
            }
        }

        private static Label SectionLabel(string caption) => UiKit.SectionLabel(caption);

        private static Button CycleButton(Action onClick) => UiKit.CycleButton(onClick);

        private static Button FooterButton(string text, Action onClick) => UiKit.FooterButton(text, onClick);
    }
}
