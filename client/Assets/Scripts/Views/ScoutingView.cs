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
        public string Label;          // name — club (role · age · OVR range)
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
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("scouting.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 4;
            Root.Add(_header);

            var help = new Label(tr("scouting.help"));
            help.style.color = new Color(1f, 1f, 1f, 0.7f);
            help.style.fontSize = 13;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.maxWidth = 440;
            help.style.marginBottom = 6;
            Root.Add(help);

            var filterBar = new VisualElement();
            filterBar.style.flexDirection = FlexDirection.Row;
            filterBar.style.marginBottom = 4;
            _roleFilterButton = ChipButton(string.Empty, () => RoleFilterClicked?.Invoke());
            filterBar.Add(_roleFilterButton);
            Root.Add(filterBar);

            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            Root.Add(_list);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 6;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetRoleFilter(string text) => _roleFilterButton.text = text;

        public void SetRows(IReadOnlyList<ScoutingRowVm> rows)
        {
            _list.Clear();
            foreach (ScoutingRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 40;
                row.style.marginBottom = 2;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = vm.Watching
                    ? new Color(0.30f, 0.45f, 0.70f, 0.45f)
                    : new Color(1f, 1f, 1f, 0.06f);
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                var name = new Label(vm.Label);
                name.style.flexGrow = 1f;
                name.style.fontSize = 13;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(name);

                row.Add(KnowledgeBar(vm.KnowledgePercent));

                var pct = new Label(vm.KnowledgeText);
                pct.style.width = 92;
                pct.style.fontSize = 12;
                pct.style.unityTextAlign = TextAnchor.MiddleRight;
                pct.style.marginRight = 8;
                pct.style.color = new Color(1f, 1f, 1f, 0.8f);
                row.Add(pct);

                var toggle = new Button(() => WatchToggleClicked?.Invoke(playerId)) { text = vm.ToggleText };
                toggle.style.width = 72;
                toggle.style.height = 30;
                toggle.style.fontSize = 12;
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
            track.style.width = 60;
            track.style.height = 8;
            track.style.marginRight = 8;
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
