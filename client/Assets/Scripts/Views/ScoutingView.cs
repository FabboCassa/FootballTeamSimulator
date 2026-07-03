using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>A scoutable player row: identity, scouting progress and a watch toggle.</summary>
    public sealed class ScoutingRowVm
    {
        public int PlayerId;
        public string Name;           // "player — club" (its own cell)
        public string RoleAbbr;       // localized role abbreviation (coloured cell)
        public int RoleGroup;         // 0 GK · 1 def · 2 mid · 3 att (cell colour)
        public string Age;            // its own cell
        public string OvrText;        // scouted OVR / range (its own cell)
        public int KnowledgePercent;  // 0..100
        public string KnowledgeText;  // e.g. "62%" or "fully scouted"
        public bool Watching;
        public string ToggleText;     // "Scout" / "Stop"
        public bool ToggleEnabled;    // false when no free slot and not already watching
    }

    /// <summary>
    /// Scouting screen (task 5.4b): assign the club's scouts to players to narrow what you know
    /// about them. Each row shows how well the player is scouted (a knowledge bar) and a watch
    /// toggle limited by the number of scouts; tapping a row opens his profile, where the
    /// attribute ranges and potential band reflect the current knowledge. Dumb view — the
    /// presenter owns the model, translates every string and decides what's enabled.
    /// </summary>
    public sealed class ScoutingView
    {
        private static readonly Color BarTrackColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color KnowledgeColor = new Color(0.55f, 0.75f, 0.95f);

        public event Action<int> PlayerSelected;
        public event Action<int> WatchToggleClicked;
        public event Action RoleFilterClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Button _roleFilterButton;
        private readonly ScrollView _list;

        public ScoutingView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(760f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceXs;
            col.Add(_header);

            var help = new Label(tr("scouting.help"));
            help.style.color = UiKit.TextMuted;
            help.style.fontSize = 13;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.unityTextAlign = TextAnchor.MiddleCenter;
            help.style.marginBottom = UiKit.SpaceSm;
            col.Add(help);

            var filterBar = new VisualElement();
            filterBar.style.flexDirection = FlexDirection.Row;
            filterBar.style.flexShrink = 0f;
            filterBar.style.marginBottom = UiKit.SpaceXs;
            _roleFilterButton = ChipButton(string.Empty, () => RoleFilterClicked?.Invoke());
            filterBar.Add(_roleFilterButton);
            col.Add(filterBar);

            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_list);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.flexShrink = 0f;
            footer.style.marginTop = UiKit.SpaceSm;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetRoleFilter(string text) => _roleFilterButton.text = text;

        public void SetRows(IReadOnlyList<ScoutingRowVm> rows)
        {
            _list.Clear();
            foreach (ScoutingRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                VisualElement row = PlayerRowKit.Row();
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                // Distinct cell rectangles: name · coloured role · age · OVR · knowledge · action.
                row.Add(PlayerRowKit.TextCell(vm.Name, 0, TextAnchor.MiddleLeft, grow: true, bold: true));
                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));
                row.Add(PlayerRowKit.TextCell(vm.Age, 46f, TextAnchor.MiddleCenter));
                row.Add(PlayerRowKit.TextCell(vm.OvrText, 96f, TextAnchor.MiddleCenter));

                VisualElement knowCell = PlayerRowKit.Cell(168f);
                knowCell.style.justifyContent = Justify.FlexStart;
                VisualElement bar = KnowledgeBar(vm.KnowledgePercent);
                bar.style.marginRight = 8;
                knowCell.Add(bar);
                var pct = new Label(vm.KnowledgeText);
                pct.style.fontSize = 11;
                pct.style.color = UiKit.TextMuted;
                pct.style.whiteSpace = WhiteSpace.NoWrap;
                pct.style.overflow = Overflow.Hidden;
                pct.style.textOverflow = TextOverflow.Ellipsis;
                knowCell.Add(pct);
                row.Add(knowCell);

                var toggle = new Button(() => WatchToggleClicked?.Invoke(playerId)) { text = vm.ToggleText };
                toggle.style.width = 88;
                toggle.style.height = PlayerRowKit.RowHeight;
                toggle.style.fontSize = 13;
                toggle.style.marginLeft = 0;
                toggle.style.marginRight = 0;
                toggle.style.flexShrink = 0f;
                toggle.SetEnabled(vm.ToggleEnabled);
                // Don't let the toggle click bubble up to the row (which opens the profile).
                toggle.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                row.Add(toggle);

                _list.Add(row);
            }
        }

        private static VisualElement KnowledgeBar(int percent)
        {
            int clamped = percent < 0 ? 0 : (percent > 100 ? 100 : percent);

            var track = new VisualElement();
            track.style.width = 54;
            track.style.height = 8;
            track.style.flexShrink = 0f;
            track.style.backgroundColor = BarTrackColor;
            track.style.borderTopLeftRadius = 3;
            track.style.borderTopRightRadius = 3;
            track.style.borderBottomLeftRadius = 3;
            track.style.borderBottomRightRadius = 3;

            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(clamped);
            fill.style.backgroundColor = KnowledgeColor;
            fill.style.borderTopLeftRadius = 3;
            fill.style.borderBottomLeftRadius = 3;
            track.Add(fill);

            return track;
        }

        private static Button ChipButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 30;
            button.style.fontSize = 12;
            button.style.marginRight = 6;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
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
