using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One scout in the department: who he is, where he is and what he has sent back.</summary>
    public sealed class ScoutRowVm
    {
        public int ScoutId;
        public string Name;             // "Chief Scout"
        public string LevelText;        // "lvl 3"
        public string AttributesText;   // "JA 62 · JP 48 · AD 55"
        public string Destination;      // "Brazil" / "Juventus" / "at home"
        public string Detail;           // "12 weeks · 8 reports"
        public string CeilingText;      // "max precision 41%"
        public int AreaPercent;         // 0..100, the area knowledge meter
        public string AreaText;         // "area knowledge 24%"
        public bool Out;                // in the field ⇒ the action recalls him
        public string ActionText;       // "Send" / "Recall"
    }

    /// <summary>A name a scout brought back (or a player under direct observation).</summary>
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
        public string Source;         // which brief found him
        public bool Watching;         // true = a direct, named observation
        public string ToggleText;     // "Stop" / "Dismiss"
        public bool ToggleEnabled;
    }

    /// <summary>One choice in a destination picker (a continent, a nation, a division, a club).</summary>
    public sealed class PickerOptionVm
    {
        public int Value;
        public string Key;      // free-form payload (a nation code); the presenter decides
        public string Label;
        public string Detail;   // "412 players"
    }

    /// <summary>One line of the brief editor: a label and the value it currently cycles to.</summary>
    public sealed class FilterLineVm
    {
        public int Id;
        public string Label;
        public string Value;
    }

    /// <summary>
    /// The state of the search tab's controls (task 11.3). Rebuilt on every refresh EXCEPT the text
    /// field, which is only synced when it differs — recreating it under the player's fingers would
    /// steal focus on every keystroke.
    /// </summary>
    public sealed class SearchPanelVm
    {
        public string Text;                               // what is currently typed
        public string Placeholder;                        // caption above the field
        public IReadOnlyList<FilterChipVm> RoleChips;     // role filter, "all" included
        public IReadOnlyList<FilterLineVm> Options;       // where / age / fame / sort / scouted-only / clear
        public string Summary;                            // "21–40 of 3,412 · page 2/171"
        public bool HasPrevious;
        public bool HasNext;
    }

    /// <summary>
    /// The Scouting screen (task 11.2) — an assignment board, not a list of every player in the
    /// world. Two tabs:
    ///
    ///   ASSIGNMENTS — one row per scout: his level and his three attributes, where he is, how long
    ///   he has been there, how well the club knows that area and therefore how precisely anything
    ///   there can be read. Send him somewhere or call him home.
    ///
    ///   REPORTS — the shortlist the scouts have actually produced, with the brief that found each
    ///   name. Tapping a row opens the profile, where the attribute ranges and the potential band
    ///   reflect the same knowledge.
    ///
    /// Sending a scout runs inside the Assignments tab as a drill-down (destination → brief), which
    /// is the same one-list-in-several-modes pattern the career setup club picker uses.
    ///
    /// Dumb view — the presenter owns the model, translates every string and decides what is enabled.
    /// </summary>
    public sealed class ScoutingView
    {
        private static readonly Color BarTrackColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color KnowledgeColor = new Color(0.55f, 0.75f, 0.95f);
        private static readonly Color AreaColor = new Color(0.95f, 0.78f, 0.42f);

        public event Action<int> TabSelected;
        /// <summary>A row was tapped — open this player's profile.</summary>
        public event Action<int> PlayerSelected;
        /// <summary>The scout's action button: send him out, or call him home.</summary>
        public event Action<int> ScoutActionClicked;
        /// <summary>The report row's action: stop a direct observation, or dismiss a report.</summary>
        public event Action<int> RowActionClicked;
        /// <summary>A destination option was picked.</summary>
        public event Action<int> OptionSelected;
        /// <summary>A brief line was tapped — cycle it to its next value.</summary>
        public event Action<int> FilterLineClicked;
        /// <summary>Confirm the brief and send the scout.</summary>
        public event Action ConfirmClicked;
        /// <summary>Step back out of a picker / the brief editor.</summary>
        public event Action CancelClicked;
        public event Action BackClicked;

        /// <summary>The search box changed (task 11.3). Fired per keystroke; the presenter re-queries.</summary>
        public event Action<string> SearchTextChanged;
        /// <summary>A search option row was tapped — cycle it.</summary>
        public event Action<int> SearchOptionClicked;
        /// <summary>A role chip was picked in the search tab (-1 = every role).</summary>
        public event Action<int> SearchRoleClicked;
        /// <summary>Page the results: -1 back, +1 forward.</summary>
        public event Action<int> SearchPageClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;
        private readonly Label _header;
        private readonly Label _help;
        private readonly VisualElement _tabs;
        private readonly Button _tabAssignments;
        private readonly Button _tabReports;
        private readonly Button _tabSearch;
        private readonly ScrollView _list;
        private readonly VisualElement _actionBar;

        // Task 11.3 — the search tab's controls. Built once and kept: the text field must survive a
        // refresh with its focus and caret intact.
        private readonly VisualElement _searchPanel;
        private readonly TextField _searchField;
        private readonly VisualElement _searchChips;
        private readonly VisualElement _searchOptions;

        public ScoutingView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.ScreenRoot();

            var col = UiKit.PageColumn(UiKit.WidthWide);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceXs;
            _header.style.flexShrink = 0f;
            col.Add(_header);

            _help = UiKit.HelpText(string.Empty);
            _help.style.flexShrink = 0f;
            col.Add(_help);

            _tabs = UiKit.Row();
            _tabs.style.flexShrink = 0f;
            _tabs.style.marginBottom = UiKit.SpaceSm;
            _tabAssignments = UiKit.TabButton(tr("scouting.tab.assignments"), () => TabSelected?.Invoke(0));
            _tabReports = UiKit.TabButton(tr("scouting.tab.reports"), () => TabSelected?.Invoke(1));
            _tabSearch = UiKit.TabButton(tr("scouting.tab.search"), () => TabSelected?.Invoke(2));
            _tabSearch.style.marginRight = 0;
            _tabs.Add(_tabAssignments);
            _tabs.Add(_tabReports);
            _tabs.Add(_tabSearch);
            col.Add(_tabs);

            // --- the search controls (task 11.3), hidden unless the search tab is open ---------
            _searchPanel = new VisualElement();
            _searchPanel.style.flexShrink = 0f;
            _searchPanel.style.marginBottom = UiKit.SpaceSm;
            _searchPanel.style.display = DisplayStyle.None;
            col.Add(_searchPanel);

            _searchField = new TextField { maxLength = 40 };
            _searchField.style.minHeight = 36;
            _searchField.style.marginBottom = UiKit.SpaceXs;
            _searchField.RegisterValueChangedCallback(e => SearchTextChanged?.Invoke(e.newValue ?? string.Empty));
            _searchPanel.Add(_searchField);

            _searchChips = UiKit.Row();
            _searchChips.style.flexWrap = Wrap.Wrap;
            _searchChips.style.marginBottom = UiKit.SpaceXs;
            _searchPanel.Add(_searchChips);

            _searchOptions = UiKit.Row();
            _searchOptions.style.flexWrap = Wrap.Wrap;
            _searchPanel.Add(_searchOptions);

            VisualElement listPanel = UiKit.Panel(grow: true);
            listPanel.style.paddingTop = UiKit.SpaceSm;
            listPanel.style.paddingBottom = UiKit.SpaceSm;
            listPanel.style.minHeight = 0f;
            _list = UiKit.ListScroll();
            _list.style.minHeight = 0f;
            listPanel.Add(_list);
            col.Add(listPanel);

            // Sits between the list and the footer: the "confirm the brief" bar, hidden most of the time.
            _actionBar = UiKit.Row();
            _actionBar.style.flexShrink = 0f;
            _actionBar.style.marginTop = UiKit.SpaceSm;
            _actionBar.style.display = DisplayStyle.None;
            col.Add(_actionBar);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;

        public void SetHelp(string text) => _help.text = text;

        /// <summary>Lights up the active tab; the pickers hide the tab row entirely (you are mid-flow).</summary>
        public void SetTabs(int active, bool visible)
        {
            _tabs.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            UiKit.SetTabActive(_tabAssignments, active == 0);
            UiKit.SetTabActive(_tabReports, active == 1);
            UiKit.SetTabActive(_tabSearch, active == 2);
        }

        // ------------------------------------------------------------------ the assignment board

        public void ShowAssignments(IReadOnlyList<ScoutRowVm> rows, string emptyMessage)
        {
            _list.Clear();
            HideSearchPanel();
            HideActionBar();

            if (rows == null || rows.Count == 0)
            {
                _list.Add(EmptyState.Build("scouting", emptyMessage));
                return;
            }

            foreach (ScoutRowVm vm in rows)
            {
                int scoutId = vm.ScoutId;

                VisualElement card = UiKit.RowCard(72f);
                card.style.flexDirection = FlexDirection.Row;
                card.style.alignItems = Align.Center;

                var text = new VisualElement();
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.overflow = Overflow.Hidden;

                var title = new Label($"{vm.Name} · {vm.LevelText}");
                title.style.fontSize = 14;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = UiKit.TextPrimary;
                text.Add(title);

                var attrs = new Label(vm.AttributesText);
                attrs.style.fontSize = 11;
                attrs.style.color = UiKit.TextMuted;
                text.Add(attrs);

                var where = new Label(vm.Destination);
                where.style.fontSize = 13;
                where.style.color = vm.Out ? UiKit.Accent : UiKit.TextMuted;
                where.style.whiteSpace = WhiteSpace.NoWrap;
                where.style.overflow = Overflow.Hidden;
                where.style.textOverflow = TextOverflow.Ellipsis;
                text.Add(where);

                if (vm.Out)
                {
                    var detail = new Label(vm.Detail);
                    detail.style.fontSize = 11;
                    detail.style.color = UiKit.TextMuted;
                    text.Add(detail);
                }

                card.Add(text);

                if (vm.Out)
                {
                    VisualElement meters = new VisualElement();
                    meters.style.width = 168;
                    meters.style.flexShrink = 0f;
                    meters.style.justifyContent = Justify.Center;

                    meters.Add(MeterLine(vm.AreaText, vm.AreaPercent, AreaColor));

                    var ceiling = new Label(vm.CeilingText);
                    ceiling.style.fontSize = 11;
                    ceiling.style.color = UiKit.TextMuted;
                    ceiling.style.marginTop = 2;
                    meters.Add(ceiling);

                    card.Add(meters);
                }

                Button action = UiKit.SmallButton(vm.ActionText, () => ScoutActionClicked?.Invoke(scoutId), 104f);
                UiKit.SetSmallButtonOn(action, vm.Out);
                card.Add(action);

                _list.Add(card);
            }
        }

        // ------------------------------------------------------------------ the reports

        public void ShowReports(IReadOnlyList<ScoutingRowVm> rows, string emptyMessage)
        {
            _list.Clear();
            HideSearchPanel();
            HideActionBar();

            if (rows == null || rows.Count == 0)
            {
                _list.Add(EmptyState.Build("scouting", emptyMessage));
                return;
            }

            FillPlayerRows(rows);
        }

        /// <summary>
        /// One player row per name, shared by the Reports tab and the search tab (task 11.3) — the
        /// two lists say the same things about a player, so they are rendered by the same code.
        /// </summary>
        private void FillPlayerRows(IReadOnlyList<ScoutingRowVm> rows)
        {
            foreach (ScoutingRowVm vm in rows)
            {
                int playerId = vm.PlayerId;

                VisualElement row = PlayerRowKit.Row();
                row.RegisterCallback<ClickEvent>(_ => PlayerSelected?.Invoke(playerId));

                VisualElement nameCell = PlayerRowKit.Cell(0f, grow: true);
                nameCell.style.flexDirection = FlexDirection.Column;
                nameCell.style.alignItems = Align.FlexStart;
                nameCell.style.justifyContent = Justify.Center;

                var name = new Label(vm.Name);
                name.style.fontSize = 13;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.color = UiKit.TextPrimary;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                nameCell.Add(name);

                var source = new Label(vm.Source);
                source.style.fontSize = 10;
                source.style.color = UiKit.TextMuted;
                source.style.whiteSpace = WhiteSpace.NoWrap;
                source.style.overflow = Overflow.Hidden;
                source.style.textOverflow = TextOverflow.Ellipsis;
                nameCell.Add(source);

                row.Add(nameCell);
                row.Add(PlayerRowKit.RoleChip(vm.RoleAbbr, vm.RoleGroup));
                row.Add(PlayerRowKit.TextCell(vm.Age, 46f, TextAnchor.MiddleCenter));
                row.Add(PlayerRowKit.TextCell(vm.OvrText, 96f, TextAnchor.MiddleCenter));

                VisualElement knowCell = PlayerRowKit.Cell(148f);
                knowCell.style.justifyContent = Justify.FlexStart;
                VisualElement bar = Meter(vm.KnowledgePercent, KnowledgeColor);
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

                Button toggle = UiKit.SmallButton(vm.ToggleText, () => RowActionClicked?.Invoke(playerId), 96f);
                toggle.style.height = PlayerRowKit.RowHeight;
                UiKit.SetSmallButtonOn(toggle, vm.Watching);
                toggle.SetEnabled(vm.ToggleEnabled);
                // Don't let the toggle click bubble up to the row (which opens the profile).
                toggle.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                row.Add(toggle);

                _list.Add(row);
            }
        }

        // ------------------------------------------------------------------ destination picker

        public void ShowPicker(IReadOnlyList<PickerOptionVm> options, string emptyMessage)
        {
            _list.Clear();
            HideSearchPanel();
            ShowActionBar(null, _tr("common.back"));

            if (options == null || options.Count == 0)
            {
                _list.Add(EmptyState.Build("scouting", emptyMessage));
                return;
            }

            foreach (PickerOptionVm option in options)
            {
                int value = option.Value;

                VisualElement row = UiKit.RowCard();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.RegisterCallback<ClickEvent>(_ => OptionSelected?.Invoke(value));

                var label = new Label(option.Label);
                label.style.flexGrow = 1f;
                label.style.fontSize = 14;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = UiKit.TextPrimary;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                row.Add(label);

                if (!string.IsNullOrEmpty(option.Detail))
                {
                    var detail = new Label(option.Detail);
                    detail.style.fontSize = 12;
                    detail.style.color = UiKit.TextMuted;
                    detail.style.flexShrink = 0f;
                    row.Add(detail);
                }

                _list.Add(row);
            }
        }

        // ------------------------------------------------------------------ the brief editor

        public void ShowFilters(IReadOnlyList<FilterLineVm> lines, string confirmText)
        {
            _list.Clear();
            HideSearchPanel();
            ShowActionBar(confirmText, _tr("common.back"));

            if (lines == null)
                return;

            foreach (FilterLineVm line in lines)
            {
                int id = line.Id;

                VisualElement row = UiKit.RowCard();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.RegisterCallback<ClickEvent>(_ => FilterLineClicked?.Invoke(id));

                var label = new Label(line.Label);
                label.style.flexGrow = 1f;
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                row.Add(label);

                var value = new Label(line.Value);
                value.style.fontSize = 14;
                value.style.unityFontStyleAndWeight = FontStyle.Bold;
                value.style.color = UiKit.Accent;
                value.style.flexShrink = 0f;
                row.Add(value);

                _list.Add(row);
            }
        }

        // ------------------------------------------------------------------ the world search (task 11.3)

        /// <summary>
        /// The search tab: the controls on top, one page of players in the middle, the pager below.
        /// The list holds AT MOST one page — a world of 26,000 players is never turned into 26,000
        /// rows, which is the whole reason paging exists here rather than a scroll and a prayer.
        /// </summary>
        public void ShowSearch(SearchPanelVm panel, IReadOnlyList<ScoutingRowVm> rows, string emptyMessage)
        {
            _list.Clear();
            _searchPanel.style.display = DisplayStyle.Flex;

            if (panel != null)
            {
                // Only ever push text INTO the field when it really differs: assigning while the
                // player is typing would move the caret to the end on every keystroke.
                string text = panel.Text ?? string.Empty;
                if (_searchField.value != text)
                    _searchField.SetValueWithoutNotify(text);

                if (!string.IsNullOrEmpty(panel.Placeholder))
                    _searchField.label = panel.Placeholder;

                if (panel.RoleChips != null)
                    UiKit.FillFilterChips(_searchChips, panel.RoleChips, role => SearchRoleClicked?.Invoke(role));

                _searchOptions.Clear();
                if (panel.Options != null)
                {
                    foreach (FilterLineVm option in panel.Options)
                    {
                        int id = option.Id;
                        Button button = UiKit.SmallButton(
                            $"{option.Label}: {option.Value}", () => SearchOptionClicked?.Invoke(id), 132f);
                        button.style.marginRight = 6;
                        button.style.marginBottom = 4;
                        _searchOptions.Add(button);
                    }
                }

                ShowPager(panel);
            }

            if (rows == null || rows.Count == 0)
            {
                _list.Add(EmptyState.Build("scouting", emptyMessage));
                return;
            }

            FillPlayerRows(rows);
        }

        /// <summary>The bar under the list: back a page, where you are, forward a page.</summary>
        private void ShowPager(SearchPanelVm panel)
        {
            _actionBar.Clear();
            _actionBar.style.display = DisplayStyle.Flex;

            Button previous = UiKit.SmallButton("\u25C0", () => SearchPageClicked?.Invoke(-1), 72f);
            previous.SetEnabled(panel.HasPrevious);
            _actionBar.Add(previous);

            var summary = new Label(panel.Summary ?? string.Empty);
            summary.style.flexGrow = 1f;
            summary.style.fontSize = 12;
            summary.style.color = UiKit.TextMuted;
            summary.style.unityTextAlign = TextAnchor.MiddleCenter;
            _actionBar.Add(summary);

            Button next = UiKit.SmallButton("\u25B6", () => SearchPageClicked?.Invoke(+1), 72f);
            next.SetEnabled(panel.HasNext);
            _actionBar.Add(next);
        }

        private void HideSearchPanel() => _searchPanel.style.display = DisplayStyle.None;

        // ------------------------------------------------------------------ chrome

        private void ShowActionBar(string confirmText, string cancelText)
        {
            _actionBar.Clear();
            _actionBar.style.display = DisplayStyle.Flex;

            Button cancel = UiKit.SmallButton(cancelText, () => CancelClicked?.Invoke(), 120f);
            cancel.style.flexGrow = 1f;
            _actionBar.Add(cancel);

            if (!string.IsNullOrEmpty(confirmText))
            {
                Button confirm = UiKit.SmallButton(confirmText, () => ConfirmClicked?.Invoke(), 160f);
                confirm.style.flexGrow = 1f;
                UiKit.SetSmallButtonAccent(confirm, true);
                _actionBar.Add(confirm);
            }
        }

        private void HideActionBar()
        {
            _actionBar.Clear();
            _actionBar.style.display = DisplayStyle.None;
        }

        private static VisualElement MeterLine(string caption, int percent, Color fill)
        {
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;

            VisualElement bar = Meter(percent, fill);
            bar.style.marginRight = 8;
            line.Add(bar);

            var label = new Label(caption);
            label.style.fontSize = 11;
            label.style.color = UiKit.TextMuted;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            line.Add(label);

            return line;
        }

        private static VisualElement Meter(int percent, Color fill)
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

            var bar = new VisualElement();
            bar.style.height = Length.Percent(100);
            bar.style.width = Length.Percent(clamped);
            bar.style.backgroundColor = fill;
            bar.style.borderTopLeftRadius = 3;
            bar.style.borderBottomLeftRadius = 3;
            track.Add(bar);

            return track;
        }
    }
}
