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
    /// League screen (task 2.5), redrawn in task 14.4 on the standard page: kicker over the league
    /// name, the three tabs as a segmented row, and each tab in one card. The table is now sized by
    /// the sheet (.fts-lt*): 19-point figures in roomy columns instead of 13-point numbers pressed
    /// against the right edge, your club tinted, points in the accent. On a phone the goals-for /
    /// goals-against columns are hidden (the goal difference carries the same story) so the club
    /// name keeps its room. Dumb view; the presenter computes every row.
    /// </summary>
    public sealed class LeagueView
    {
        public event Action<int> TabClicked;
        public event Action PrevRoundClicked;
        public event Action NextRoundClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _leagueLabel;
        private readonly Button[] _tabs;
        private readonly VisualElement[] _sections;
        private readonly VisualElement _table;
        private readonly Label _roundLabel;
        private readonly VisualElement _fixtureList;
        private readonly VisualElement _scorerList;
        private readonly Func<string, string> _tr;

        public LeagueView(Func<string, string> tr)
        {
            _tr = tr;

            PageParts page = UiKit.StandardPage(tr("league.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            Root.AddToClassList("fts-league");
            _leagueLabel = page.Title;
            VisualElement col = page.Column;

            string[] tabKeys = { "league.tab.table", "league.tab.fixtures", "league.tab.scorers" };
            _tabs = new Button[tabKeys.Length];
            for (int i = 0; i < tabKeys.Length; i++)
            {
                int index = i;
                _tabs[i] = UiKit.SegChip(tr(tabKeys[i]), null, () => TabClicked?.Invoke(index));
            }
            VisualElement tabRow = UiKit.SegRow(_tabs[0], _tabs[1], _tabs[2]);
            tabRow.AddToClassList("fts-league__tabs");
            col.Add(tabRow);

            // --- table
            VisualElement tableSection = UiKit.OptionCard();
            _table = new VisualElement();
            tableSection.Add(_table);

            // --- fixtures
            VisualElement fixturesSection = UiKit.OptionCard();
            var roundNav = new VisualElement();
            roundNav.AddToClassList("fts-league__roundnav");
            roundNav.style.flexDirection = FlexDirection.Row;
            roundNav.style.justifyContent = Justify.SpaceBetween;
            roundNav.style.alignItems = Align.Center;
            Button prev = UiKit.GhostButton("\u25C0", () => PrevRoundClicked?.Invoke());
            prev.AddToClassList("fts-league__roundbtn");
            _roundLabel = new Label(string.Empty);
            _roundLabel.AddToClassList("fts-league__round");
            UiKit.UseDisplayFont(_roundLabel);
            _roundLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _roundLabel.style.flexGrow = 1f;
            Button next = UiKit.GhostButton("\u25B6", () => NextRoundClicked?.Invoke());
            next.AddToClassList("fts-league__roundbtn");
            roundNav.Add(prev);
            roundNav.Add(_roundLabel);
            roundNav.Add(next);
            fixturesSection.Add(roundNav);
            _fixtureList = new VisualElement();
            fixturesSection.Add(_fixtureList);

            // --- scorers
            VisualElement scorersSection = UiKit.OptionCard();
            _scorerList = new VisualElement();
            scorersSection.Add(_scorerList);

            _sections = new[] { tableSection, fixturesSection, scorersSection };
            foreach (VisualElement section in _sections)
                col.Add(section);

            ShowTab(0);
        }

        public void SetLeagueName(string name) => _leagueLabel.text = name ?? string.Empty;

        public void ShowTab(int index)
        {
            for (int i = 0; i < _sections.Length; i++)
            {
                _sections[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                UiKit.SetSegChipState(_tabs[i], i == index);
            }
        }

        public void SetTable(IReadOnlyList<TableRowVm> rows)
        {
            _table.Clear();
            _table.Add(TableRow(null, 0, true));
            for (int i = 0; i < rows.Count; i++)
                _table.Add(TableRow(rows[i], i, false));
        }

        public void SetFixtures(string roundLabel, IReadOnlyList<FixtureRowVm> rows)
        {
            _roundLabel.text = (roundLabel ?? string.Empty).ToUpperInvariant();
            _fixtureList.Clear();
            for (int i = 0; i < rows.Count; i++)
                _fixtureList.Add(FixtureRow(rows[i], i));
        }

        public void SetScorers(IReadOnlyList<ScorerRowVm> rows)
        {
            _scorerList.Clear();
            for (int i = 0; i < rows.Count; i++)
                _scorerList.Add(ScorerRow(rows[i], i));
        }

        /// <summary>Empty-state illustration for the scorers tab early in the season (task 6.8).</summary>
        public void SetScorersEmpty(VisualElement emptyState)
        {
            _scorerList.Clear();
            _scorerList.Add(emptyState);
        }

        // ---------------------------------------------------------------- rows

        private VisualElement TableRow(TableRowVm vm, int index, bool header)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-lt__row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (header) row.AddToClassList("fts-lt__row--head");
            else Stripe(row, vm.IsUser, index);

            row.Add(Cell(header ? _tr("league.col.pos") : vm.Position.ToString(), "fts-lt__pos"));
            row.Add(CrestSlot(header ? null : vm.Crest, "fts-lt__crest"));

            Label club = Cell(header ? _tr("league.col.club") : vm.Club, "fts-lt__club");
            club.style.flexGrow = 1f;
            club.style.flexShrink = 1f;
            club.style.minWidth = 0f;
            club.style.overflow = Overflow.Hidden;
            club.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(club);

            row.Add(Cell(header ? _tr("league.col.p") : vm.Played.ToString(), "fts-lt__num"));
            row.Add(Cell(header ? _tr("league.col.w") : vm.Wins.ToString(), "fts-lt__num"));
            row.Add(Cell(header ? _tr("league.col.d") : vm.Draws.ToString(), "fts-lt__num"));
            row.Add(Cell(header ? _tr("league.col.l") : vm.Losses.ToString(), "fts-lt__num"));
            row.Add(Cell(header ? _tr("league.col.gf") : vm.GoalsFor.ToString(), "fts-lt__num", "fts-lt__wide"));
            row.Add(Cell(header ? _tr("league.col.ga") : vm.GoalsAgainst.ToString(), "fts-lt__num", "fts-lt__wide"));
            row.Add(Cell(header ? _tr("league.col.gd") : Signed(vm.GoalDifference), "fts-lt__num"));
            row.Add(Cell(header ? _tr("league.col.pts") : vm.Points.ToString(), "fts-lt__num", "fts-lt__pts"));
            return row;
        }

        private static VisualElement FixtureRow(FixtureRowVm vm, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-lt__row");
            row.AddToClassList("fts-lt__fixture");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            Stripe(row, vm.IsUser, index);

            row.Add(CrestSlot(vm.HomeCrest, "fts-lt__crest"));
            Label label = Cell(vm.Label, "fts-lt__fixturetext");
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.minWidth = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);
            row.Add(CrestSlot(vm.AwayCrest, "fts-lt__crest"));
            return row;
        }

        private static VisualElement ScorerRow(ScorerRowVm vm, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-lt__row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            Stripe(row, vm.IsUser, index);

            row.Add(Cell((index + 1).ToString(), "fts-lt__pos"));
            row.Add(CrestSlot(vm.Crest, "fts-lt__crest"));
            Label label = Cell(vm.Label, "fts-lt__club");
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.minWidth = 0f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);
            return row;
        }

        private static Label Cell(string text, string cls, string cls2 = null)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("fts-lt__cell");
            label.AddToClassList(cls);
            if (cls2 != null) label.AddToClassList(cls2);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.flexShrink = 0f;
            return label;
        }

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        /// <summary>Zebra + "this is your club" tinting.</summary>
        private static void Stripe(VisualElement row, bool isUser, int index)
        {
            if (isUser) row.AddToClassList("fts-lt__row--user");
            else if (index % 2 == 1) row.AddToClassList("fts-lt__row--zebra");
        }

        private static VisualElement CrestSlot(VisualElement crest, string cls)
        {
            var slot = new VisualElement();
            slot.AddToClassList(cls);
            slot.style.flexShrink = 0f;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            if (crest != null) slot.Add(crest);
            return slot;
        }
    }
}
