using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    public sealed class TableRowVm
    {
        public int Position;
        public string Club;
        public int Played, Wins, Draws, Losses, GoalsFor, GoalsAgainst, GoalDifference, Points;
        public bool IsUser;
        public VisualElement Crest;   // small club crest (task 6.8); built by the presenter
    }

    public sealed class FixtureRowVm
    {
        public string Label;
        public bool IsUser;
        public VisualElement HomeCrest; // task 6.8 (optional)
        public VisualElement AwayCrest;
    }

    public sealed class ScorerRowVm
    {
        public string Label;
        public bool IsUser;
        public VisualElement Crest;   // scorer's club crest (task 6.8)
    }

    /// <summary>
    /// League screen: three tabs (table, fixtures by round, top scorers).
    /// Dumb view; the presenter computes every row.
    /// </summary>
    public sealed class LeagueView
    {
        private static readonly Color UserRowColor = new Color(0.20f, 0.38f, 0.24f);
        private static readonly Color ActiveTabColor = new Color(0.22f, 0.42f, 0.66f);
        private static readonly Color HeaderColor = new Color(1f, 1f, 1f, 0.6f);

        public event Action<int> TabClicked;
        public event Action PrevRoundClicked;
        public event Action NextRoundClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _leagueLabel;
        private readonly Button[] _tabs;
        private readonly VisualElement[] _sections;
        private readonly VisualElement _tableHeader;
        private readonly ScrollView _tableList;
        private readonly Label _roundLabel;
        private readonly ScrollView _fixtureList;
        private readonly ScrollView _scorerList;
        private readonly Func<string, string> _tr;

        public LeagueView(Func<string, string> tr)
        {
            _tr = tr;

            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            // Centred, capped-width column so the table doesn't sprawl and its stat columns stop
            // hugging the far-right edge on wide viewports (6.9).
            var col = UiKit.CenteredColumn(680f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _leagueLabel = UiKit.Header(string.Empty);
            _leagueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _leagueLabel.style.marginBottom = UiKit.SpaceSm;
            col.Add(_leagueLabel);

            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.justifyContent = Justify.Center;
            tabRow.style.flexShrink = 0f;
            tabRow.style.marginBottom = 8;
            string[] tabKeys = { "league.tab.table", "league.tab.fixtures", "league.tab.scorers" };
            _tabs = new Button[tabKeys.Length];
            for (int i = 0; i < tabKeys.Length; i++)
            {
                int index = i;
                _tabs[i] = new Button(() => TabClicked?.Invoke(index)) { text = tr(tabKeys[i]) };
                _tabs[i].style.height = 36;
                _tabs[i].style.fontSize = 14;
                _tabs[i].style.flexGrow = 1f;
                tabRow.Add(_tabs[i]);
            }
            col.Add(tabRow);

            // --- Table section ---
            var tableSection = new VisualElement();
            tableSection.style.flexGrow = 1f;
            _tableHeader = new VisualElement();
            _tableHeader.style.flexDirection = FlexDirection.Row;
            tableSection.Add(_tableHeader);
            _tableList = new ScrollView();
            _tableList.style.flexGrow = 1f;
            tableSection.Add(_tableList);

            // --- Fixtures section ---
            var fixturesSection = new VisualElement();
            fixturesSection.style.flexGrow = 1f;
            var roundNav = new VisualElement();
            roundNav.style.flexDirection = FlexDirection.Row;
            roundNav.style.justifyContent = Justify.Center;
            roundNav.style.alignItems = Align.Center;
            roundNav.style.marginBottom = 4;
            var prev = new Button(() => PrevRoundClicked?.Invoke()) { text = "<" };
            prev.style.width = 40;
            prev.style.height = 32;
            _roundLabel = new Label(string.Empty);
            _roundLabel.style.color = Color.white;
            _roundLabel.style.fontSize = 14;
            _roundLabel.style.marginLeft = 10;
            _roundLabel.style.marginRight = 10;
            var next = new Button(() => NextRoundClicked?.Invoke()) { text = ">" };
            next.style.width = 40;
            next.style.height = 32;
            roundNav.Add(prev);
            roundNav.Add(_roundLabel);
            roundNav.Add(next);
            fixturesSection.Add(roundNav);
            _fixtureList = new ScrollView();
            _fixtureList.style.flexGrow = 1f;
            fixturesSection.Add(_fixtureList);

            // --- Scorers section ---
            var scorersSection = new VisualElement();
            scorersSection.style.flexGrow = 1f;
            _scorerList = new ScrollView();
            _scorerList.style.flexGrow = 1f;
            scorersSection.Add(_scorerList);

            _sections = new[] { tableSection, fixturesSection, scorersSection };
            foreach (VisualElement section in _sections)
                col.Add(section);

            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.alignSelf = Align.Center;
            back.style.height = 44;
            back.style.width = 200;
            back.style.marginTop = 8;
            back.style.flexShrink = 0f;
            col.Add(back);

            BuildTableHeader();
            ShowTab(0);
        }

        public void SetLeagueName(string name) => _leagueLabel.text = name;

        public void ShowTab(int index)
        {
            for (int i = 0; i < _sections.Length; i++)
            {
                _sections[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                _tabs[i].style.backgroundColor = i == index ? ActiveTabColor : StyleKeyword.Null;
            }
        }

        public void SetTable(IReadOnlyList<TableRowVm> rows)
        {
            _tableList.Clear();
            foreach (TableRowVm vm in rows)
            {
                var row = TableRow(
                    vm.Position.ToString(), vm.Club,
                    vm.Played.ToString(), vm.Wins.ToString(), vm.Draws.ToString(), vm.Losses.ToString(),
                    vm.GoalsFor.ToString(), vm.GoalsAgainst.ToString(), vm.GoalDifference.ToString(),
                    vm.Points.ToString(),
                    Color.white, vm.Crest);
                if (vm.IsUser)
                    row.style.backgroundColor = UserRowColor;
                _tableList.Add(row);
            }
        }

        public void SetFixtures(string roundLabel, IReadOnlyList<FixtureRowVm> rows)
        {
            _roundLabel.text = roundLabel;
            _fixtureList.Clear();
            foreach (FixtureRowVm vm in rows)
                _fixtureList.Add(FixtureRow(vm));
        }

        public void SetScorers(IReadOnlyList<ScorerRowVm> rows)
        {
            _scorerList.Clear();
            foreach (ScorerRowVm vm in rows)
                _scorerList.Add(ListRow(vm.Label, vm.IsUser, vm.Crest));
        }

        /// <summary>Empty-state illustration for the scorers tab early in the season (task 6.8).</summary>
        public void SetScorersEmpty(VisualElement emptyState)
        {
            _scorerList.Clear();
            _scorerList.Add(emptyState);
        }

        private void BuildTableHeader()
        {
            _tableHeader.Clear();
            _tableHeader.Add(TableRow(
                _tr("league.col.pos"), _tr("league.col.club"),
                _tr("league.col.p"), _tr("league.col.w"), _tr("league.col.d"), _tr("league.col.l"),
                _tr("league.col.gf"), _tr("league.col.ga"), _tr("league.col.gd"), _tr("league.col.pts"),
                HeaderColor, null));
        }

        private static VisualElement TableRow(
            string pos, string club, string p, string w, string d, string l,
            string gf, string ga, string gd, string pts, Color textColor, VisualElement crest)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 26;
            row.style.alignItems = Align.Center;

            row.Add(Cell(pos, 30, textColor));

            // Fixed-width crest slot keeps every column aligned (header passes null → empty slot).
            var crestSlot = new VisualElement();
            crestSlot.style.width = 24;
            crestSlot.style.height = 20;
            crestSlot.style.marginRight = 4;
            crestSlot.style.alignItems = Align.Center;
            crestSlot.style.justifyContent = Justify.Center;
            if (crest != null) crestSlot.Add(crest);
            row.Add(crestSlot);

            var clubCell = Cell(club, 0, textColor);
            clubCell.style.flexGrow = 1f;
            clubCell.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(clubCell);
            row.Add(Cell(p, 26, textColor));
            row.Add(Cell(w, 26, textColor));
            row.Add(Cell(d, 26, textColor));
            row.Add(Cell(l, 26, textColor));
            row.Add(Cell(gf, 32, textColor));
            row.Add(Cell(ga, 32, textColor));
            row.Add(Cell(gd, 34, textColor));
            row.Add(Cell(pts, 36, textColor));
            return row;
        }

        private static Label Cell(string text, float width, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = color;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            if (width > 0)
                label.style.width = width;
            return label;
        }

        /// <summary>A list row with an optional leading crest (scorers).</summary>
        private static VisualElement ListRow(string text, bool isUser, VisualElement crest)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 26;
            if (isUser)
                row.style.backgroundColor = UserRowColor;

            var slot = new VisualElement();
            slot.style.width = 24;
            slot.style.height = 20;
            slot.style.marginRight = 6;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            if (crest != null) slot.Add(crest);
            row.Add(slot);

            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.color = Color.white;
            label.style.flexGrow = 1f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(label);
            return row;
        }

        /// <summary>A fixture row: home crest · result/vs text · away crest (task 6.8).</summary>
        private static VisualElement FixtureRow(FixtureRowVm vm)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 26;
            if (vm.IsUser)
                row.style.backgroundColor = UserRowColor;

            row.Add(CrestSlot(vm.HomeCrest));

            var label = new Label(vm.Label);
            label.style.fontSize = 13;
            label.style.color = Color.white;
            label.style.flexGrow = 1f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginLeft = 2;
            label.style.marginRight = 2;
            row.Add(label);

            row.Add(CrestSlot(vm.AwayCrest));
            return row;
        }

        private static VisualElement CrestSlot(VisualElement crest)
        {
            var slot = new VisualElement();
            slot.style.width = 22;
            slot.style.height = 20;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            if (crest != null) slot.Add(crest);
            return slot;
        }
    }
}
