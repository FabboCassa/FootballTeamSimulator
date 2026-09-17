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
        /// <summary>Fallback "Name (ROLE)" line, used when <see cref="Name"/> is not set.</summary>
        public string Label;
        public string Name;
        public string RoleAbbr;
        public int RoleGroup = -1;     // 0 GK · 1 def · 2 mid · 3 att; -1 = no role chip
        public string FocusLabel;
        /// <summary>0 = follows the team focus; anything else is a personal plan (lit in accent).</summary>
        public int FocusIndex;
    }

    /// <summary>
    /// Training screen (task 4.3), redrawn in task 14.4 on the standard page.
    ///   • Team focus: the six focuses as a chip grid — all visible, one tap — with a line under
    ///     it saying what the picked one trains. (It used to be a button you cycled blind.)
    ///   • Individual focus: one row per player — role chip, name, and a chip that cycles his
    ///     personal focus; a player with a personal plan lights up so the exceptions stand out.
    /// Save lives in the page head. Dumb view — shared by the career, private-league and ranked
    /// training presenters.
    /// </summary>
    public sealed class TrainingView
    {
        public event Action TeamFocusCycleClicked;
        /// <summary>A team-focus chip was picked (0 Balanced … 5 Tactical).</summary>
        public event Action<int> TeamFocusSelected;
        public event Action<int> IndividualFocusCycleClicked;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private static readonly string[] TeamKeys =
        {
            "training.team.balanced", "training.team.attacking", "training.team.defending",
            "training.team.physical", "training.team.technical", "training.team.tactical"
        };

        private readonly Label _title;
        private readonly Button[] _teamChips = new Button[TeamKeys.Length];
        private readonly Label _teamHint;
        private readonly VisualElement _roster;
        private readonly Label _status;

        public TrainingView(Func<string, string> tr)
        {
            PageParts page = UiKit.StandardPage(tr("training.title"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            _title = page.Title;

            Button save = UiKit.PrimaryButton(tr("training.save"), () => SaveClicked?.Invoke());
            save.AddToClassList("fts-headcta");
            page.Head.Actions.Insert(0, save);

            _status = UiKit.HelpText(string.Empty);
            _status.AddToClassList("fts-status");
            _status.style.display = DisplayStyle.None;
            page.Column.Add(_status);

            // ---- team focus
            VisualElement teamCard = UiKit.OptionCard();
            teamCard.Add(UiKit.BlockHead(tr("training.team_caption")));
            var chips = new VisualElement();
            chips.AddToClassList("fts-train__chips");
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < TeamKeys.Length; i++)
            {
                int index = i;
                _teamChips[i] = UiKit.SegChip(tr(TeamKeys[i]), null, () => TeamFocusSelected?.Invoke(index));
                _teamChips[i].AddToClassList("fts-train__chip");
                _teamChips[i].style.flexBasis = Length.Percent(30);
                _teamChips[i].style.marginRight = UiKit.SpaceSm;
                _teamChips[i].style.marginBottom = UiKit.SpaceSm;
                chips.Add(_teamChips[i]);
            }
            teamCard.Add(chips);
            _teamHint = UiKit.HelpText(string.Empty);
            _teamHint.AddToClassList("fts-train__hint");
            _teamHint.style.marginBottom = 0;
            teamCard.Add(_teamHint);
            page.Column.Add(teamCard);

            // ---- individual focus
            VisualElement rosterCard = UiKit.OptionCard();
            rosterCard.Add(UiKit.BlockHead(tr("training.roster_caption")));
            Label help = UiKit.HelpText(tr("training.roster_help"));
            rosterCard.Add(help);
            _roster = new VisualElement();
            rosterCard.Add(_roster);
            page.Column.Add(rosterCard);
        }

        public void SetHeader(string text) => _title.text = text ?? string.Empty;
        public void SetTeamHint(string text) => _teamHint.text = text ?? string.Empty;

        public void SetStatus(string text)
        {
            _status.text = text ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Lights the picked team-focus chip.</summary>
        public void SetTeamFocusIndex(int index)
        {
            for (int i = 0; i < _teamChips.Length; i++)
                UiKit.SetSegChipState(_teamChips[i], i == index);
        }

        // Kept for compatibility: the chips show the picked focus now.
        public void SetTeamFocus(string text) { }

        public void SetRoster(IReadOnlyList<TrainingRowVm> rows)
        {
            _roster.Clear();
            foreach (TrainingRowVm vm in rows)
            {
                int playerId = vm.PlayerId;
                VisualElement row = PlayerRowKit.Row();
                row.AddToClassList("fts-train__row");

                if (vm.RoleGroup >= 0)
                    row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));

                string name = string.IsNullOrEmpty(vm.Name) ? vm.Label : vm.Name;
                row.Add(PlayerRowKit.TextCell(name, 0, TextAnchor.MiddleLeft, grow: true, bold: true));

                Button focus = UiKit.GhostButton(vm.FocusLabel, () => IndividualFocusCycleClicked?.Invoke(playerId));
                focus.AddToClassList("fts-train__focus");
                focus.EnableInClassList("fts-train__focus--on", vm.FocusIndex > 0);
                row.Add(focus);

                _roster.Add(row);
            }
        }
    }
}
