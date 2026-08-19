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
    ///
    /// Layout (task 6.12): the wide page scaffold. The standings live in a panel that fills the
    /// page, with a real sticky header row, zebra striping and roomy numeric columns — instead of
    /// 12px figures crushed against the right edge of a 680px column.
    /// </summary>
    public sealed class LeagueView
    {
        private const float PosWidth = 40f;
        private const float CrestWidth = 28f;
        private const float StatWidth = 48f;
        private const float PointsWidth = 58f;
        private const float RowHeight = 34f;

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

            Root = UiKit.ScreenRoot();

            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _leagueLabel = UiKit.ScreenTitle(string.Empty);
            _leagueLabel.style.marginBottom = UiKit.SpaceSm;
            col.Add(_leagueLabel);

            VisualElement tabRow = UiKit.Toolbar();
            string[] tabKeys = { "league.tab.table", "league.tab.fixtures", "league.tab.scorers" };
            _tabs = new Button[tabKeys.Length];
            for (int i = 0; i < tabKeys.Length; i++)
            {
                int index = i;
                _tabs[i] = UiKit.TabButton(tr(tabKeys[i]), () => TabClicked?.Invoke(index));
                tabRow.Add(_tabs[i]);
            }
            _tabs[_tabs.Length - 1].style.marginRight = 0;
            col.Add(tabRow);

            // --- Table section ---
            VisualElement tableSection = UiKit.Panel(grow: true);
            tableSection.style.paddingTop = UiKit.SpaceSm;
            tableSection.style.paddingBottom = UiKit.SpaceSm;
            _tableHeader = new VisualElement();
            _tableHeader.AddToClassList("fts-thead");
            _tableHeader.style.flexDirection = FlexDirection.Row;
            _tableHeader.style.alignItems = Align.Center;
            _tableHeader.style.flexShrink = 0f;
            _tableHeader.style.marginBottom = UiKit.SpaceXs;
            tableSection.Add(_tableHeader);
            _tableList = UiKit.ListScroll();
            tableSection.Add(_tableList);

            // --- Fixtures section ---
            VisualElement fixturesSection = UiKit.Panel(grow: true);
            fixturesSection.style.paddingTop = UiKit.SpaceSm;
            fixturesSection.style.paddingBottom = UiKit.SpaceSm;
            var roundNav = new VisualElement();
            roundNav.style.flexDirection = FlexDirection.Row;
            roundNav.style.justifyContent = Justify.Center;
            roundNav.style.alignItems = Align.Center;
            roundNav.style.flexShrink = 0f;
            roundNav.style.marginBottom = UiKit.SpaceSm;
            Button prev = UiKit.SmallButton("<", () => PrevRoundClicked?.Invoke(), 44f);
            prev.style.marginLeft = 0;
            _roundLabel = new Label(string.Empty);
            _roundLabel.style.color = UiKit.TextPrimary;
            _roundLabel.style.fontSize = 15;
            _roundLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _roundLabel.style.minWidth = 180;
            _roundLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _roundLabel.style.marginLeft = 12;
            _roundLabel.style.marginRight = 12;
            Button next = UiKit.SmallButton(">", () => NextRoundClicked?.Invoke(), 44f);
            next.style.marginLeft = 0;
            roundNav.Add(prev);
            roundNav.Add(_roundLabel);
            roundNav.Add(next);
            fixturesSection.Add(roundNav);
            _fixtureList = UiKit.ListScroll();
            fixturesSection.Add(_fixtureList);

            // --- Scorers section ---
            VisualElement scorersSection = UiKit.Panel(grow: true);
            scorersSection.style.paddingTop = UiKit.SpaceSm;
            scorersSection.style.paddingBottom = UiKit.SpaceSm;
            _scorerList = UiKit.ListScroll();
            scorersSection.Add(_scorerList);

            _sections = new[] { tableSection, fixturesSection, scorersSection };
            foreach (VisualElement section in _sections)
                col.Add(section);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);

            BuildTableHeader();
            ShowTab(0);
        }

        public void SetLeagueName(string name) => _leagueLabel.text = name;

        public void ShowTab(int index)
        {
            for (int i = 0; i < _sections.Length; i++)
            {
                _sections[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                UiKit.SetTabActive(_tabs[i], i == index);
            }
        }

        public void SetTable(IReadOnlyList<TableRowVm> rows)
        {
            _tableList.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                TableRowVm vm = rows[i];
                VisualElement row = TableRow(
                    vm.Position.ToString(), vm.Club,
                    vm.Played.ToString(), vm.Wins.ToString(), vm.Draws.ToString(), vm.Losses.ToString(),
                    vm.GoalsFor.ToString(), vm.GoalsAgainst.ToString(), vm.GoalDifference.ToString(),
                    vm.Points.ToString(),
                    header: false, crest: vm.Crest);

                row.AddToClassList("fts-trow");
                if (vm.IsUser) row.AddToClassList("fts-trow--user");
                else if (i % 2 == 1) row.AddToClassList("fts-trow--zebra");
                if (!UiKit.StylesLoaded)
                {
                    row.style.backgroundColor = vm.IsUser
                        ? UiKit.Hex(0x1F4A34)
                        : (i % 2 == 1 ? new Color(1f, 1f, 1f, 0.035f) : Color.clear);
                    UiKit.Round(row, 6);
                }
                _tableList.Add(row);
            }
        }

        public void SetFixtures(string roundLabel, IReadOnlyList<FixtureRowVm> rows)
        {
            _roundLabel.text = roundLabel;
            _fixtureList.Clear();
            for (int i = 0; i < rows.Count; i++)
                _fixtureList.Add(FixtureRow(rows[i], i));
        }

        public void SetScorers(IReadOnlyList<ScorerRowVm> rows)
        {
            _scorerList.Clear();
            for (int i = 0; i < rows.Count; i++)
                _scorerList.Add(ListRow(rows[i].Label, rows[i].IsUser, rows[i].Crest, i));
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
            VisualElement header = TableRow(
                _tr("league.col.pos"), _tr("league.col.club"),
                _tr("league.col.p"), _tr("league.col.w"), _tr("league.col.d"), _tr("league.col.l"),
                _tr("league.col.gf"), _tr("league.col.ga"), _tr("league.col.gd"), _tr("league.col.pts"),
                header: true, crest: null);
            _tableHeader.Add(header);
        }

        private static VisualElement TableRow(
            string pos, string club, string p, string w, string d, string l,
            string gf, string ga, string gd, string pts, bool header, VisualElement crest)
        {
            Color textColor = header ? UiKit.TextMuted : UiKit.TextPrimary;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = RowHeight;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;

            Label position = Cell(pos, PosWidth, textColor, TextAnchor.MiddleLeft);
            position.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(position);

            // Fixed-width crest slot keeps every column aligned (header passes null → empty slot).
            var crestSlot = new VisualElement();
            crestSlot.style.width = CrestWidth;
            crestSlot.style.height = 22;
            crestSlot.style.flexShrink = 0f;
            crestSlot.style.marginRight = UiKit.SpaceSm;
            crestSlot.style.alignItems = Align.Center;
            crestSlot.style.justifyContent = Justify.Center;
            if (crest != null) crestSlot.Add(crest);
            row.Add(crestSlot);

            Label clubCell = Cell(club, 0, textColor, TextAnchor.MiddleLeft);
            clubCell.style.flexGrow = 1f;
            clubCell.style.flexShrink = 1f;
            clubCell.style.fontSize = header ? 12 : 14;
            if (!header) clubCell.style.unityFontStyleAndWeight = FontStyle.Bold;
            clubCell.style.whiteSpace = WhiteSpace.NoWrap;
            clubCell.style.overflow = Overflow.Hidden;
            clubCell.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(clubCell);

            row.Add(Cell(p, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(w, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(d, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(l, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(gf, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(ga, StatWidth, textColor, TextAnchor.MiddleRight));
            row.Add(Cell(gd, StatWidth, textColor, TextAnchor.MiddleRight));

            Label points = Cell(pts, PointsWidth, header ? UiKit.TextMuted : UiKit.Accent, TextAnchor.MiddleRight);
            points.style.unityFontStyleAndWeight = FontStyle.Bold;
            points.style.fontSize = header ? 12 : 15;
            row.Add(points);

            return row;
        }

        private static Label Cell(string text, float width, Color color, TextAnchor align)
        {
            var label = new Label(text);
            label.style.fontSize = 13;
            label.style.color = color;
            label.style.unityTextAlign = align;
            if (width > 0)
            {
                label.style.width = width;
                label.style.flexShrink = 0f;
            }
            return label;
        }

        /// <summary>A list row with an optional leading crest (scorers).</summary>
        private static VisualElement ListRow(string text, bool isUser, VisualElement crest, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-trow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            Stripe(row, isUser, index);

            var rank = new Label((index + 1).ToString());
            rank.style.width = PosWidth;
            rank.style.flexShrink = 0f;
            rank.style.fontSize = 13;
            rank.style.unityFontStyleAndWeight = FontStyle.Bold;
            rank.style.color = UiKit.TextMuted;
            rank.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(rank);

            var slot = new VisualElement();
            slot.style.width = CrestWidth;
            slot.style.height = 22;
            slot.style.flexShrink = 0f;
            slot.style.marginRight = UiKit.SpaceSm;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            if (crest != null) slot.Add(crest);
            row.Add(slot);

            var label = new Label(text);
            label.style.fontSize = 14;
            label.style.color = UiKit.TextPrimary;
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);
            return row;
        }

        /// <summary>A fixture row: home crest · result/vs text · away crest (task 6.8).</summary>
        private static VisualElement FixtureRow(FixtureRowVm vm, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-trow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = RowHeight + 4;
            row.style.flexShrink = 0f;
            row.style.paddingLeft = UiKit.SpaceSm;
            row.style.paddingRight = UiKit.SpaceSm;
            Stripe(row, vm.IsUser, index);

            row.Add(CrestSlot(vm.HomeCrest));

            var label = new Label(vm.Label);
            label.style.fontSize = 14;
            label.style.color = UiKit.TextPrimary;
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginLeft = UiKit.SpaceSm;
            label.style.marginRight = UiKit.SpaceSm;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            row.Add(CrestSlot(vm.AwayCrest));
            return row;
        }

        /// <summary>Zebra + "this is your club" tinting, class-driven with an inline fallback.</summary>
        private static void Stripe(VisualElement row, bool isUser, int index)
        {
            if (isUser) row.AddToClassList("fts-trow--user");
            else if (index % 2 == 1) row.AddToClassList("fts-trow--zebra");
            if (!UiKit.StylesLoaded)
            {
                row.style.backgroundColor = isUser
                    ? UiKit.Hex(0x1F4A34)
                    : (index % 2 == 1 ? new Color(1f, 1f, 1f, 0.035f) : Color.clear);
                UiKit.Round(row, 6);
            }
        }

        private static VisualElement CrestSlot(VisualElement crest)
        {
            var slot = new VisualElement();
            slot.style.width = CrestWidth;
            slot.style.height = 22;
            slot.style.flexShrink = 0f;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            if (crest != null) slot.Add(crest);
            return slot;
        }
    }
}
