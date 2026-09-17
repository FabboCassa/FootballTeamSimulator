using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One line of the Hub's mini standings (task 14.4).</summary>
    public sealed class HubTableRowVm
    {
        public int Position;
        public string Club;
        public int Played;
        public int Points;
        public bool IsUser;
        public VisualElement Crest;
    }

    /// <summary>The next-fixture block of the Hub (task 14.4).</summary>
    public sealed class HubFixtureVm
    {
        /// <summary>The caption over the block ("GIORNATA 15 · IN CASA").</summary>
        public string Kicker;
        public string HomeName;
        public string AwayName;
        public VisualElement HomeCrest;
        public VisualElement AwayCrest;
        /// <summary>The CTA's quiet second line ("Tra 2 giorni").</summary>
        public string When;
    }

    /// <summary>The user's last played fixture (task 14.4).</summary>
    public sealed class HubResultVm
    {
        public string HomeName;
        public string AwayName;
        public int HomeGoals;
        public int AwayGoals;
        public VisualElement HomeCrest;
        public VisualElement AwayCrest;
        /// <summary>+1 win, 0 draw, -1 loss — from the user's side.</summary>
        public int Outcome;
    }

    /// <summary>
    /// The career overview — the "Panoramica" dashboard — redrawn in task 14.4 on the Nuova Carriera
    /// grammar. The brief for this screen is the user's: SIMPLE, READABLE, CLEAN. So it answers four
    /// questions and nothing else, one block each:
    ///   1. How am I doing?     → four stat tiles: position, points, form, board confidence.
    ///   2. What's next?        → the next fixture on the page's one RAISED card, with the accent CTA.
    ///   3. What just happened? → the last result: two crests, a big score, a W/D/L pip.
    ///   4. What needs me?      → mini standings around the club + three "to do" rows.
    ///
    /// Desktop: head, tiles, then two columns (match blocks left, table + to-do right).
    /// Tablet/phone: the same blocks stacked in the one scroller; on a phone the tiles go two by two.
    /// As in CareerSetupView the blocks are built ONCE and the breakpoint only flips the grid's
    /// direction and the tile basis — no second tree to keep in sync. Every size lives in
    /// FtsTheme.uss under <c>.fts-hub*</c> with its <c>.fts--mobile</c> overrides; nothing here sets a
    /// font size inline (the 14.1 corollary: an inline size freezes an element at desktop value).
    ///
    /// Dumb view: every string is formatted by <c>HubPresenter</c>; crests arrive as built elements.
    /// </summary>
    public sealed class HubView
    {
        public event Action EndSeasonClicked;
        public event Action NextMatchClicked;
        public event Action OpponentReportClicked;
        /// <summary>A "to do" row or the "see all" link: "inbox", "squad", "market", "league".</summary>
        public event Action<string> SectionClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly Label _kicker;
        private readonly VisualElement _tiles;
        private readonly VisualElement _positionTile;
        private readonly VisualElement _pointsTile;
        private readonly VisualElement _formTile;
        private readonly VisualElement _confidenceTile;
        private readonly VisualElement _formPips;

        private readonly VisualElement _grid;
        private readonly VisualElement _colMain;
        private readonly VisualElement _colSide;

        // next match
        private readonly Label _nextCaption;
        private readonly VisualElement _fixtureRow;
        private readonly VisualElement _homeCrest;
        private readonly VisualElement _awayCrest;
        private readonly Label _homeName;
        private readonly Label _awayName;
        private readonly Label _seasonOver;
        private readonly Button _cta;
        private readonly Label _ctaLabel;
        private readonly Button _reportButton;

        // last result
        private readonly VisualElement _resultRow;
        private readonly VisualElement _resHomeCrest;
        private readonly VisualElement _resAwayCrest;
        private readonly Label _resHome;
        private readonly Label _resAway;
        private readonly Label _resScore;
        private readonly Label _resPip;
        private readonly Label _resEmpty;

        // table + to do
        private readonly VisualElement _table;
        private readonly SelectRowParts _inboxRow;
        private readonly SelectRowParts _squadRow;
        private readonly SelectRowParts _marketRow;

        private bool _seasonComplete;

        public HubView(Func<string, string> tr)
        {
            _tr = tr;

            // The overview is the root of the career: no Back on it.
            PageParts page = UiKit.StandardPage(string.Empty, tr("hub.overview"), null, null);
            Root = page.Root;
            Root.AddToClassList("fts-hub");
            _kicker = page.Kicker;
            VisualElement col = page.Column;

            // ---------------------------------------------------------------- tiles
            _tiles = UiKit.TileRow();
            _tiles.AddToClassList("fts-hub__tiles");
            _positionTile = UiKit.StatTile(tr("hub.tile.position"), "—");
            _pointsTile = UiKit.StatTile(tr("hub.tile.points"), "—");
            _formTile = UiKit.StatTile(tr("hub.tile.form"), string.Empty);
            _confidenceTile = UiKit.StatTile(tr("hub.tile.confidence"), "—");

            // The form tile shows pips instead of a text value.
            if (_formTile.userData is Label formValue)
                formValue.style.display = DisplayStyle.None;
            _formPips = new VisualElement();
            _formPips.AddToClassList("fts-hub__pips");
            _formPips.style.flexDirection = FlexDirection.Row;
            _formPips.style.alignItems = Align.Center;
            _formTile.Add(_formPips);

            _tiles.Add(_positionTile);
            _tiles.Add(_pointsTile);
            _tiles.Add(_formTile);
            _tiles.Add(_confidenceTile);
            col.Add(_tiles);

            // ---------------------------------------------------------------- grid
            _grid = new VisualElement();
            _grid.AddToClassList("fts-hub__grid");
            col.Add(_grid);

            _colMain = new VisualElement();
            _colMain.AddToClassList("fts-hub__main");
            _colSide = new VisualElement();
            _colSide.AddToClassList("fts-hub__side");
            _grid.Add(_colMain);
            _grid.Add(_colSide);

            // ---- next match: the one raised card on the page
            VisualElement next = UiKit.RaisedCard();
            next.AddToClassList("fts-hub__block");
            _nextCaption = UiKit.SectionLabel(string.Empty);
            _nextCaption.style.marginTop = 0;
            next.Add(_nextCaption);

            _homeCrest = CrestSlot(false);
            _awayCrest = CrestSlot(true);
            _homeName = TeamName(TextAnchor.MiddleLeft);
            _awayName = TeamName(TextAnchor.MiddleRight);
            var vs = new Label(tr("hub.vs"));
            vs.AddToClassList("fts-hub__vs");
            UiKit.UseDisplayFont(vs);
            vs.style.flexShrink = 0f;
            _fixtureRow = FixtureRow(Side(_homeCrest, _homeName, false), vs, Side(_awayCrest, _awayName, true));
            next.Add(_fixtureRow);

            _seasonOver = new Label(tr("hub.season_over.body"));
            _seasonOver.AddToClassList("fts-hub__note");
            _seasonOver.style.whiteSpace = WhiteSpace.Normal;
            _seasonOver.style.display = DisplayStyle.None;
            next.Add(_seasonOver);

            var actions = new VisualElement();
            actions.AddToClassList("fts-hub__actions");
            _cta = UiKit.CtaButton(tr("hub.next_match"), string.Empty, OnCta);
            _cta.AddToClassList("fts-hub__cta");
            _ctaLabel = _cta.childCount > 0 ? _cta[0] as Label : null;
            _reportButton = UiKit.GhostButton(tr("hub.opponent_report"), () => OpponentReportClicked?.Invoke());
            _reportButton.AddToClassList("fts-hub__report");
            actions.Add(_cta);
            actions.Add(_reportButton);
            next.Add(actions);
            _colMain.Add(next);

            // ---- last result
            VisualElement last = UiKit.OptionCard();
            last.AddToClassList("fts-hub__block");
            Label lastCaption = UiKit.SectionLabel(tr("hub.last.caption"));
            lastCaption.style.marginTop = 0;
            last.Add(lastCaption);

            _resHomeCrest = CrestSlot(false);
            _resAwayCrest = CrestSlot(true);
            _resHome = TeamName(TextAnchor.MiddleLeft);
            _resAway = TeamName(TextAnchor.MiddleRight);
            _resScore = new Label(string.Empty);
            _resScore.AddToClassList("fts-hub__score");
            UiKit.UseDisplayFont(_resScore);
            _resScore.style.flexShrink = 0f;
            _resultRow = FixtureRow(Side(_resHomeCrest, _resHome, false), _resScore, Side(_resAwayCrest, _resAway, true));
            last.Add(_resultRow);

            _resPip = new Label(string.Empty);
            _resPip.AddToClassList("fts-hub__pip");
            _resPip.AddToClassList("fts-hub__pip--wide");
            _resPip.style.alignSelf = Align.Center;
            last.Add(_resPip);

            _resEmpty = UiKit.HelpText(tr("hub.last.none"));
            _resEmpty.style.marginBottom = 0;
            last.Add(_resEmpty);
            _colMain.Add(last);

            // ---- mini standings
            VisualElement standings = UiKit.OptionCard();
            standings.AddToClassList("fts-hub__block");
            standings.Add(UiKit.BlockHead(tr("hub.table.caption"), tr("hub.table.all"), () => SectionClicked?.Invoke("league")));

            _table = new VisualElement();
            standings.Add(_table);
            _colSide.Add(standings);

            // ---- to do
            VisualElement todo = UiKit.OptionCard();
            todo.AddToClassList("fts-hub__block");
            Label todoCaption = UiKit.SectionLabel(tr("hub.todo.caption"));
            todoCaption.style.marginTop = 0;
            todo.Add(todoCaption);
            _inboxRow = TodoRow(todo, tr("hub.inbox"), "inbox");
            _squadRow = TodoRow(todo, tr("hub.squad"), "squad");
            _marketRow = TodoRow(todo, tr("hub.market"), "market");
            _colSide.Add(todo);

            // The breakpoint can change while the screen is up (resized window, rotated tablet).
            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        // ---------------------------------------------------------------- presenter API

        /// <summary>The kicker over the title ("STAGIONE 2026 · GIORNO 14").</summary>
        public void SetKicker(string kicker) => _kicker.text = (kicker ?? string.Empty).ToUpperInvariant();

        public void SetPosition(string value) => UiKit.SetStatTileValue(_positionTile, value);

        public void SetPoints(string value) => UiKit.SetStatTileValue(_pointsTile, value);

        /// <summary>Board confidence, with its band (0 risk, 1 warned, 2 safe) picking the colour.</summary>
        public void SetConfidence(string value, int band) =>
            UiKit.SetStatTileValue(_confidenceTile, value,
                band <= 0 ? UiKit.Danger : band == 1 ? UiKit.Warning : UiKit.Positive);

        /// <summary>The last results, oldest first: +1 win, 0 draw, -1 loss.</summary>
        public void SetForm(IReadOnlyList<int> outcomes)
        {
            _formPips.Clear();
            if (outcomes == null || outcomes.Count == 0)
            {
                var none = new Label("—");
                none.AddToClassList("fts-stat__v");
                _formPips.Add(none);
                return;
            }

            foreach (int outcome in outcomes)
            {
                var pip = new Label(string.Empty);
                pip.AddToClassList("fts-hub__pip");
                PaintPip(pip, outcome, false);
                _formPips.Add(pip);
            }
        }

        /// <summary>
        /// The next fixture. With <paramref name="seasonComplete"/> the card turns into the
        /// "season over" block and the CTA ends the season instead.
        /// </summary>
        public void SetNextFixture(HubFixtureVm fixture, bool seasonComplete)
        {
            _seasonComplete = seasonComplete;
            bool has = fixture != null && !seasonComplete;

            _nextCaption.text = (has ? fixture.Kicker ?? string.Empty : _tr("hub.season_over.title")).ToUpperInvariant();
            _fixtureRow.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _seasonOver.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            _reportButton.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;

            if (_ctaLabel != null)
                _ctaLabel.text = _tr(has ? "hub.next_match" : "hub.end_season");
            UiKit.SetCtaSub(_cta, has ? fixture.When : string.Empty);
            _cta.SetEnabled(has || seasonComplete);

            if (!has)
                return;

            _homeName.text = fixture.HomeName ?? string.Empty;
            _awayName.text = fixture.AwayName ?? string.Empty;
            Fill(_homeCrest, fixture.HomeCrest);
            Fill(_awayCrest, fixture.AwayCrest);
        }

        /// <summary>The last result; null shows the "no match yet" line.</summary>
        public void SetLastResult(HubResultVm result)
        {
            bool has = result != null;
            _resultRow.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _resPip.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _resEmpty.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            if (!has)
                return;

            _resHome.text = result.HomeName ?? string.Empty;
            _resAway.text = result.AwayName ?? string.Empty;
            _resScore.text = result.HomeGoals + " – " + result.AwayGoals;
            Fill(_resHomeCrest, result.HomeCrest);
            Fill(_resAwayCrest, result.AwayCrest);
            PaintPip(_resPip, result.Outcome, true);
        }

        /// <summary>A handful of standings rows around the user's club.</summary>
        public void SetTable(IReadOnlyList<HubTableRowVm> rows)
        {
            _table.Clear();

            var head = new VisualElement();
            head.AddToClassList("fts-hub__trow");
            head.AddToClassList("fts-hub__trow--head");
            head.Add(Cell(_tr("league.col.pos"), "fts-hub__tpos"));
            head.Add(Cell(string.Empty, "fts-hub__tcrest"));
            head.Add(Cell(_tr("league.col.club"), "fts-hub__tname"));
            head.Add(Cell(_tr("league.col.p"), "fts-hub__tnum"));
            head.Add(Cell(_tr("league.col.pts"), "fts-hub__tnum"));
            _table.Add(head);

            if (rows == null)
                return;

            foreach (HubTableRowVm r in rows)
            {
                var row = new VisualElement();
                row.AddToClassList("fts-hub__trow");
                if (r.IsUser) row.AddToClassList("fts-hub__trow--user");

                row.Add(Cell(r.Position.ToString(), "fts-hub__tpos"));
                var crest = new VisualElement();
                crest.AddToClassList("fts-hub__tcrest");
                crest.style.alignItems = Align.Center;
                crest.style.justifyContent = Justify.Center;
                if (r.Crest != null) crest.Add(r.Crest);
                row.Add(crest);
                row.Add(Cell(r.Club, "fts-hub__tname"));
                row.Add(Cell(r.Played.ToString(), "fts-hub__tnum"));
                Label pts = Cell(r.Points.ToString(), "fts-hub__tnum");
                pts.AddToClassList("fts-hub__tpts");
                row.Add(pts);
                _table.Add(row);
            }
        }

        /// <summary>The three "to do" lines: a short status under each, amber when it wants attention.</summary>
        public void SetTodo(string inbox, bool inboxAlert, string squad, bool squadAlert, string market)
        {
            SetTodoRow(_inboxRow, inbox, inboxAlert);
            SetTodoRow(_squadRow, squad, squadAlert);
            SetTodoRow(_marketRow, market, false);
        }

        // ---------------------------------------------------------------- layout

        private void Layout(Viewport viewport)
        {
            bool mobile = viewport == Viewport.Mobile;
            // Task 14.9: two columns wherever there is room (desktop, landscape tablet); an upright
            // tablet is one column like the phone, but keeps four tiles across.
            bool twoColumns = !mobile && Responsive.HasWideColumns;

            _grid.style.flexDirection = twoColumns ? FlexDirection.Row : FlexDirection.Column;
            _grid.style.alignItems = twoColumns ? Align.FlexStart : Align.Stretch;
            _colMain.style.flexGrow = twoColumns ? 3f : 0f;
            _colMain.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colSide.style.flexGrow = twoColumns ? 2f : 0f;
            _colSide.style.flexBasis = twoColumns ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            _colSide.style.marginLeft = twoColumns ? UiKit.SpaceMd : 0f;

            // Four tiles across on a desktop/tablet, two by two on a phone.
            foreach (VisualElement tile in _tiles.Children())
            {
                tile.style.flexBasis = mobile ? Length.Percent(40) : Length.Percent(20);
                tile.style.minWidth = 0f;
            }
        }

        // ---------------------------------------------------------------- building blocks

        private void OnCta()
        {
            if (_seasonComplete) EndSeasonClicked?.Invoke();
            else NextMatchClicked?.Invoke();
        }

        private SelectRowParts TodoRow(VisualElement parent, string name, string key)
        {
            SelectRowParts row = UiKit.SelectRow(() => SectionClicked?.Invoke(key));
            row.Root.AddToClassList("fts-hub__todo");
            row.Name.text = name;
            row.Meta.style.display = DisplayStyle.Flex;
            row.Mark.style.display = DisplayStyle.Flex;
            row.Mark.text = _tr("hub.todo.open");
            parent.Add(row.Root);
            return row;
        }

        private static void SetTodoRow(SelectRowParts row, string meta, bool alert)
        {
            row.Meta.text = meta ?? string.Empty;
            row.Meta.EnableInClassList("fts-hub__alert", alert);
            if (!UiKit.StylesLoaded)
                row.Meta.style.color = alert ? UiKit.Amber : UiKit.TextLabel;
        }

        private static VisualElement FixtureRow(VisualElement home, VisualElement middle, VisualElement away)
        {
            var row = new VisualElement();
            row.AddToClassList("fts-hub__fixture");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.Add(home);
            row.Add(middle);
            row.Add(away);
            return row;
        }

        private static VisualElement CrestSlot(bool away)
        {
            var e = new VisualElement();
            e.AddToClassList("fts-hub__crest");
            if (away) e.AddToClassList("fts-hub__crest--away");
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            e.style.flexShrink = 0f;
            return e;
        }

        private static Label TeamName(TextAnchor align)
        {
            var l = new Label(string.Empty);
            l.AddToClassList("fts-hub__team");
            l.style.unityTextAlign = align;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1f;
            l.style.minWidth = 0f;
            return l;
        }

        /// <summary>Crest + name, mirrored on the away side so both names sit next to the centre.</summary>
        private static VisualElement Side(VisualElement crest, Label name, bool away)
        {
            var e = new VisualElement();
            e.style.flexDirection = away ? FlexDirection.RowReverse : FlexDirection.Row;
            e.style.alignItems = Align.Center;
            e.style.flexGrow = 1f;
            e.style.flexBasis = 0f;
            e.style.minWidth = 0f;
            e.Add(crest);
            e.Add(name);
            return e;
        }

        private static void Fill(VisualElement slot, VisualElement content)
        {
            slot.Clear();
            if (content != null) slot.Add(content);
        }

        private void PaintPip(Label pip, int outcome, bool longText)
        {
            string key = outcome > 0 ? "hub.result.win" : outcome < 0 ? "hub.result.loss" : "hub.result.draw";
            pip.text = _tr(longText ? key + "_long" : key).ToUpperInvariant();
            pip.EnableInClassList("fts-hub__pip--win", outcome > 0);
            pip.EnableInClassList("fts-hub__pip--draw", outcome == 0);
            pip.EnableInClassList("fts-hub__pip--loss", outcome < 0);
            pip.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (!UiKit.StylesLoaded)
            {
                pip.style.backgroundColor = outcome > 0 ? UiKit.Accent : outcome < 0 ? UiKit.Danger : UiKit.TagSurface;
                pip.style.color = outcome > 0 ? UiKit.TextOnAccent : UiKit.TextPrimary;
            }
        }

        private static Label Cell(string text, string cls)
        {
            var l = new Label(text ?? string.Empty);
            l.AddToClassList("fts-hub__tcell");
            l.AddToClassList(cls);
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            return l;
        }
    }
}
