using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// New-career setup, rebuilt in task 14.2 on the "Nuova Carriera" mockup
    /// (docs/Nuova Carriera.html) — the screen that defines the app's new visual language.
    ///
    /// WHAT IT LOOKS LIKE NOW.
    ///   • Desktop: a header (kicker + "{nation} / {division}" in Bebas + two quiet actions) over
    ///     three columns — the world/database/difficulty option cards on the left, the ONE list in
    ///     the middle, and a sticky summary + note + accent CTA on the right.
    ///   • Phone: the same blocks in one column under a lifted header, with a fixed bottom bar
    ///     carrying the summary line and the CTA, and the nation picker in a modal bottom sheet.
    ///
    /// THE TRICK THAT KEEPS THE TWO IN SYNC. Every block is built ONCE in the constructor and then
    /// REPARENTED by <see cref="Layout"/> when <see cref="Responsive"/> crosses a breakpoint. There
    /// is exactly one nation row, one list, one CTA — so there is no second copy of the state to
    /// forget to update, which is what a "build a desktop tree and a mobile tree" version always
    /// ends up doing wrong.
    ///
    /// WHAT DID NOT CHANGE. The presenter contract: same events, same Set* methods, same argument
    /// shapes. CareerSetupPresenter was not touched. Two consequences are visible in the code and
    /// deliberate, both written up as follow-ups in Roadmap 14.2b:
    ///   • "playable divisions" is still a CYCLE (the presenter only exposes TiersCycled), so it is
    ///     a tappable row here rather than the mockup's 1 / 2 / 3 segmented control.
    ///   • the nation rows carry a three-letter INITIALISM taken from the nation's name, not its
    ///     real federation code, because the presenter sends a formatted string and not a profile.
    ///
    /// The layout rules task 11.1b learned still hold and are still the reason this screen behaves:
    /// ONE scroller per shape, everything else flexShrink = 0, and the list carries minHeight = 0
    /// so it can actually give height back instead of pushing the footer off the bottom.
    /// </summary>
    public sealed class CareerSetupView
    {
        public event Action<int> ClubSelected;
        public event Action RerollClicked;
        public event Action BackClicked;
        /// <summary>Raised when the user taps a difficulty button (0 = Easy, 1 = Normal, 2 = Hard).</summary>
        public event Action<int> DifficultySelected;
        /// <summary>Raised when the user taps the nation row — the presenter answers with SetNations.</summary>
        public event Action NationPickerRequested;
        /// <summary>Raised when the user picks a nation from the list (its index in the offered list).</summary>
        public event Action<int> NationSelected;
        /// <summary>Raised when the user taps the "playable divisions" row, which cycles its value.</summary>
        public event Action TiersCycled;
        /// <summary>Raised when the user taps a database-size chip (0 = Small, 1 = Medium, 2 = Large).</summary>
        public event Action<int> DatabaseSizeSelected;
        /// <summary>Raised when the user picks a division to browse (its league id).</summary>
        public event Action<int> LeagueSelected;
        /// <summary>Raised by the "back" row at the top of the club or nation list.</summary>
        public event Action BackToLeaguesClicked;

        public VisualElement Root { get; }

        private enum Mode { Leagues, Clubs, Nations }

        private readonly Func<string, string> _tr;

        // --- header ------------------------------------------------------------------------
        private readonly VisualElement _headStack;
        private readonly Label _headTitle;
        private readonly Label _headMeta;
        private readonly VisualElement _actions;

        // --- blocks that get reparented per breakpoint --------------------------------------
        private readonly VisualElement _worldCard;
        private readonly VisualElement _dbCard;
        private readonly VisualElement _diffCard;
        private readonly VisualElement _pane;
        private readonly VisualElement _summaryCard;
        private readonly Label _note;
        private readonly Button _cta;

        // --- the one list, and the header above it ------------------------------------------
        private readonly Label _paneTitle;
        private readonly VisualElement _paneHead;
        private readonly VisualElement _searchRow;
        private readonly TextField _search;
        private readonly ScrollView _list;

        // --- controls -----------------------------------------------------------------------
        private readonly SelectRowParts _nationRow;
        private readonly SelectRowParts _tiersRow;
        private readonly Button[] _databaseChips = new Button[3];
        private readonly Label _databaseDesc;
        private readonly Button[] _difficultyButtons = new Button[3];
        private readonly Label _difficultyDesc;

        // --- summary ------------------------------------------------------------------------
        private readonly Label _sumNation;
        private readonly Label _sumTiers;
        private readonly Label _sumPick;
        private readonly Label _sumDatabase;
        private readonly Label _sumDifficulty;
        private readonly Label _bottomSummary;

        // --- mobile scaffolding --------------------------------------------------------------
        private readonly VisualElement _mobileHeader;
        private readonly ScrollView _mobileBody;
        private readonly VisualElement _bottomBar;
        private readonly SheetParts _sheet;
        private readonly VisualElement _desktopBody;
        private readonly VisualElement _desktopHead;
        private readonly VisualElement _colLeft;
        private readonly VisualElement _colCenter;
        private readonly VisualElement _colRight;

        private static readonly string[] DifficultyKeys = { "difficulty.easy", "difficulty.normal", "difficulty.hard" };
        private static readonly string[] DifficultyDescKeys =
            { "difficulty.desc.easy", "difficulty.desc.normal", "difficulty.desc.hard" };
        private static readonly string[] DatabaseKeys =
            { "career_setup.db.small", "career_setup.db.medium", "career_setup.db.large" };

        private Mode _mode = Mode.Leagues;
        private Viewport _builtFor = (Viewport)(-1);
        private IReadOnlyList<string> _nations = Array.Empty<string>();
        private string _nationName = string.Empty;
        private string _paneName = string.Empty;
        private int _selectedClubId = -1;
        private string _selectedClubName = string.Empty;
        private readonly List<SelectRowParts> _rows = new List<SelectRowParts>();
        private readonly List<int> _rowClubIds = new List<int>();

        public CareerSetupView(Func<string, string> tr)
        {
            _tr = tr;

            Root = new VisualElement();
            Root.AddToClassList("fts-screen");
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;

            // ---------------------------------------------------------------- header block
            _headStack = new VisualElement();
            _headStack.style.flexShrink = 1f;
            _headStack.style.minWidth = 0f;
            _headStack.Add(UiKit.Eyebrow(tr("career_setup.eyebrow")));

            _headTitle = new Label(string.Empty);
            _headTitle.AddToClassList("fts-title");
            _headTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
            _headTitle.style.marginBottom = 0;
            _headTitle.style.overflow = Overflow.Hidden;
            _headTitle.style.textOverflow = TextOverflow.Ellipsis;
            UiKit.UseDisplayFont(_headTitle);
            _headStack.Add(_headTitle);

            _headMeta = UiKit.Caption(string.Empty);
            _headMeta.style.whiteSpace = WhiteSpace.NoWrap;
            _headMeta.style.overflow = Overflow.Hidden;
            _headMeta.style.textOverflow = TextOverflow.Ellipsis;
            _headStack.Add(_headMeta);

            _actions = new VisualElement();
            _actions.style.flexDirection = FlexDirection.Row;
            _actions.style.alignItems = Align.Center;
            _actions.style.flexShrink = 0f;
            _actions.Add(UiKit.GhostButton(tr("career_setup.reroll"), () => RerollClicked?.Invoke()));
            _actions.Add(UiKit.GhostButton(tr("common.back"), () => BackClicked?.Invoke()));

            // ---------------------------------------------------------------- world card
            _worldCard = UiKit.OptionCard();
            _worldCard.Add(UiKit.SectionLabel(tr("career_setup.world")));

            _nationRow = UiKit.SelectRow(() => NationPickerRequested?.Invoke());
            UiKit.SetSelectRowState(_nationRow, true);
            _nationRow.Meta.style.display = DisplayStyle.Flex;
            _nationRow.Meta.text = tr("career_setup.nation_label").ToUpperInvariant();
            _nationRow.Mark.style.display = DisplayStyle.Flex;
            _nationRow.Mark.text = tr("career_setup.change");
            Reorder(_nationRow);
            MetaFirst(_nationRow);
            _worldCard.Add(_nationRow.Root);

            _tiersRow = UiKit.SelectRow(() => TiersCycled?.Invoke());
            _tiersRow.Meta.style.display = DisplayStyle.Flex;
            _tiersRow.Meta.text = tr("career_setup.tiers_label").ToUpperInvariant();
            _tiersRow.Mark.style.display = DisplayStyle.Flex;
            _tiersRow.Mark.text = tr("career_setup.change");
            Reorder(_tiersRow);
            MetaFirst(_tiersRow);
            _worldCard.Add(_tiersRow.Root);

            Label worldHint = UiKit.HelpText(tr("career_setup.tap_hint"));
            worldHint.style.marginBottom = 0;
            _worldCard.Add(worldHint);

            // ---------------------------------------------------------------- database card
            _dbCard = UiKit.OptionCard();
            _dbCard.Add(UiKit.SectionLabel(tr("career_setup.database")));
            for (int i = 0; i < _databaseChips.Length; i++)
            {
                int size = i;
                _databaseChips[i] = UiKit.SegChip(tr(DatabaseKeys[i]), null, () => DatabaseSizeSelected?.Invoke(size));
            }
            _dbCard.Add(UiKit.SegRow(_databaseChips[0], _databaseChips[1], _databaseChips[2]));
            _databaseDesc = UiKit.HelpText(string.Empty);
            _databaseDesc.style.marginTop = UiKit.SpaceSm;
            _databaseDesc.style.marginBottom = 0;
            _dbCard.Add(_databaseDesc);

            // ---------------------------------------------------------------- difficulty card
            _diffCard = UiKit.OptionCard();
            _diffCard.Add(UiKit.SectionLabel(tr("career_setup.difficulty_label")));
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                int level = i;
                _difficultyButtons[i] = UiKit.SegChip(tr(DifficultyKeys[i]), null, () => DifficultySelected?.Invoke(level));
            }
            _diffCard.Add(UiKit.SegRow(_difficultyButtons[0], _difficultyButtons[1], _difficultyButtons[2]));
            _difficultyDesc = UiKit.HelpText(string.Empty);
            _difficultyDesc.style.marginTop = UiKit.SpaceSm;
            _difficultyDesc.style.marginBottom = 0;
            _diffCard.Add(_difficultyDesc);

            // ---------------------------------------------------------------- the list pane
            _pane = UiKit.Panel(true);
            _pane.style.minHeight = 0f;

            _paneHead = new VisualElement();
            _paneHead.style.flexDirection = FlexDirection.Row;
            _paneHead.style.alignItems = Align.Center;
            _paneHead.style.justifyContent = Justify.SpaceBetween;
            _paneHead.style.flexShrink = 0f;
            _paneHead.style.marginBottom = UiKit.SpaceSm;

            _paneTitle = UiKit.Header(string.Empty);
            _paneTitle.style.marginBottom = 0;
            _paneTitle.style.flexShrink = 1f;
            _paneTitle.style.overflow = Overflow.Hidden;
            _paneTitle.style.textOverflow = TextOverflow.Ellipsis;
            _paneHead.Add(_paneTitle);

            _searchRow = new VisualElement();
            _searchRow.style.flexDirection = FlexDirection.Row;
            _searchRow.style.alignItems = Align.Center;
            _searchRow.style.flexGrow = 1f;
            _searchRow.style.flexShrink = 1f;
            _searchRow.style.minWidth = 0f;
            _searchRow.style.marginLeft = UiKit.SpaceMd;
            _searchRow.style.display = DisplayStyle.None;

            _search = UiKit.SearchField(tr("career_setup.search_nation"));
            _search.RegisterValueChangedCallback(_ => FillNationRows());
            _searchRow.Add(_search);

            Button cancelSearch = UiKit.GhostButton(tr("career_setup.cancel"), () => BackToLeaguesClicked?.Invoke());
            _searchRow.Add(cancelSearch);
            _paneHead.Add(_searchRow);

            _pane.Add(_paneHead);

            _list = UiKit.ListScroll();
            _list.style.minHeight = 0f;
            _pane.Add(_list);

            // ---------------------------------------------------------------- summary + CTA
            _summaryCard = UiKit.RaisedCard();
            _summaryCard.Add(UiKit.SectionLabel(tr("career_setup.summary")));
            _sumNation = UiKit.SummaryRow(_summaryCard, tr("career_setup.nation_label"), "—");
            _sumTiers = UiKit.SummaryRow(_summaryCard, tr("career_setup.tiers_label"), "—");
            _sumPick = UiKit.SummaryRow(_summaryCard, tr("career_setup.sum_pick"), "—");
            _sumDatabase = UiKit.SummaryRow(_summaryCard, tr("career_setup.database"), "—");
            _sumDifficulty = UiKit.SummaryRow(_summaryCard, tr("career_setup.difficulty_label"), "—");

            _note = UiKit.NoteCard(tr("career_setup.scope_fixed_long"));
            _note.style.marginTop = UiKit.SpaceSm + UiKit.SpaceXs;
            _note.style.marginBottom = UiKit.SpaceSm + UiKit.SpaceXs;

            _cta = UiKit.CtaButton(tr("career_setup.start"), string.Empty, ConfirmClub);
            _cta.SetEnabled(false);

            _bottomSummary = new Label(string.Empty);
            _bottomSummary.AddToClassList("fts-caption");
            _bottomSummary.style.whiteSpace = WhiteSpace.NoWrap;
            _bottomSummary.style.overflow = Overflow.Hidden;
            _bottomSummary.style.textOverflow = TextOverflow.Ellipsis;

            // ---------------------------------------------------------------- desktop scaffold
            _desktopHead = new VisualElement();
            _desktopHead.AddToClassList("fts-pagehead");
            _desktopHead.style.flexDirection = FlexDirection.Row;
            _desktopHead.style.alignItems = Align.FlexEnd;
            _desktopHead.style.justifyContent = Justify.SpaceBetween;
            _desktopHead.style.flexShrink = 0f;

            _desktopBody = new VisualElement();
            _desktopBody.style.flexDirection = FlexDirection.Row;
            _desktopBody.style.flexGrow = 1f;
            _desktopBody.style.minHeight = 0f;

            _colLeft = Column(1f, 400f);
            _colCenter = Column(2f, 0f);
            _colCenter.style.marginLeft = UiKit.SpaceMd;
            _colCenter.style.marginRight = UiKit.SpaceMd;
            _colRight = Column(1f, 380f);

            _desktopBody.Add(_colLeft);
            _desktopBody.Add(_colCenter);
            _desktopBody.Add(_colRight);

            // ---------------------------------------------------------------- mobile scaffold
            _mobileHeader = UiKit.MobileHeader();
            _mobileBody = UiKit.ListScroll();
            _mobileBody.style.minHeight = 0f;
            _mobileBody.contentContainer.style.paddingLeft = UiKit.SpaceMd;
            _mobileBody.contentContainer.style.paddingRight = UiKit.SpaceMd;
            _mobileBody.contentContainer.style.paddingTop = UiKit.SpaceMd;
            _mobileBody.contentContainer.style.paddingBottom = UiKit.SpaceMd;

            _bottomBar = UiKit.BottomBar();
            _sheet = UiKit.Sheet();

            // The breakpoint drives the assembly, and it can change while the screen is up
            // (a resized desktop window, a rotated tablet).
            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += OnViewportChanged;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= OnViewportChanged);

            Layout(Responsive.Current);
        }

        // ---------------------------------------------------------------- presenter API

        /// <summary>Highlights the chosen difficulty button (0/1/2) and shows its description.</summary>
        public void SetDifficulty(int level)
        {
            for (int i = 0; i < _difficultyButtons.Length; i++)
                UiKit.SetSegChipState(_difficultyButtons[i], i == level);

            if (level >= 0 && level < DifficultyDescKeys.Length)
            {
                _difficultyDesc.text = _tr(DifficultyDescKeys[level]);
                _sumDifficulty.text = _tr(DifficultyKeys[level]);
            }

            RefreshBottomSummary();
        }

        /// <summary>The nation row's caption, already formatted by the presenter.</summary>
        public void SetNation(string caption)
        {
            _nationName = caption ?? string.Empty;
            _nationRow.Name.text = _nationName;
            _sumNation.text = _nationName;
            RefreshTitle();
        }

        /// <summary>The playable-divisions row's caption, already formatted by the presenter.</summary>
        public void SetTiers(string caption)
        {
            _tiersRow.Name.text = caption ?? string.Empty;
            _sumTiers.text = caption ?? string.Empty;
            RefreshBottomSummary();
        }

        /// <summary>The line under the title: which divisions this world will run.</summary>
        public void SetSummary(string summary)
        {
            _headMeta.text = summary ?? string.Empty;
        }

        /// <summary>Lights the chosen database-size chip (0/1/2) and shows what it means.</summary>
        public void SetDatabaseSize(int size)
        {
            for (int i = 0; i < _databaseChips.Length; i++)
                UiKit.SetSegChipState(_databaseChips[i], i == size);

            if (size >= 0 && size < DatabaseKeys.Length)
            {
                _databaseDesc.text = _tr(DatabaseKeys[size] + ".desc");
                _sumDatabase.text = _tr(DatabaseKeys[size]);
            }

            RefreshBottomSummary();
        }

        /// <summary>Division-picking mode: one row per playable division.</summary>
        public void SetLeagues(IReadOnlyList<KeyValuePair<int, string>> leagues)
        {
            EnterMode(Mode.Leagues, _tr("career_setup.pick_league"));
            ClearSelection();

            for (int i = 0; i < leagues.Count; i++)
            {
                int leagueId = leagues[i].Key;
                SelectRowParts row = NewRow(() => LeagueSelected?.Invoke(leagueId));
                Split(leagues[i].Value, out string name, out string meta);
                row.Name.text = name;
                SetMeta(row, meta);
                row.Lead.Insert(0, UiKit.TierBadge((i + 1).ToString()));
                SetMark(row, _tr("career_setup.choose"), false);
                _list.Add(row.Root);
            }

            _list.scrollOffset = Vector2.zero;
        }

        /// <summary>Club-picking mode: the clubs of ONE division, with a way back up.</summary>
        public void SetClubs(string leagueName, IReadOnlyList<KeyValuePair<int, string>> clubs, bool canGoBack)
        {
            EnterMode(Mode.Clubs, leagueName);
            ClearSelection();

            if (canGoBack)
            {
                Button back = UiKit.GhostButton(_tr("career_setup.back_to_leagues"), () => BackToLeaguesClicked?.Invoke());
                back.style.marginLeft = 0;
                back.style.marginBottom = UiKit.SpaceSm;
                back.style.width = Length.Percent(100);
                _list.Add(back);
            }

            foreach (KeyValuePair<int, string> club in clubs)
            {
                int clubId = club.Key;
                SelectRowParts row = NewRow(() => PickClub(clubId));
                Split(club.Value, out string name, out string meta);
                row.Name.text = name;
                SetMeta(row, meta);
                SetMark(row, _tr("career_setup.choose"), false);
                _rowClubIds.Add(clubId);
                _rows.Add(row);
                _list.Add(row.Root);
            }

            _list.scrollOffset = Vector2.zero;
        }

        /// <summary>Nation-picking mode: the same list, filled with nations instead.</summary>
        public void SetNations(IReadOnlyList<string> nations)
        {
            _nations = nations ?? Array.Empty<string>();
            EnterMode(Mode.Nations, _tr("career_setup.pick_nation"));
            _search.SetValueWithoutNotify(string.Empty);
            FillNationRows();
        }

        // ---------------------------------------------------------------- list building

        private void FillNationRows()
        {
            if (_mode != Mode.Nations)
                return;

            _list.Clear();
            _rows.Clear();
            _rowClubIds.Clear();

            string query = (_search.value ?? string.Empty).Trim();
            for (int i = 0; i < _nations.Count; i++)
            {
                Split(_nations[i], out string name, out string meta);
                if (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int index = i;
                SelectRowParts row = NewRow(() =>
                {
                    UiKit.ShowSheet(_sheet, false);
                    NationSelected?.Invoke(index);
                });
                row.Name.text = name;
                SetMeta(row, meta);
                row.Lead.Insert(0, UiKit.Tag(Initialism(name)));
                bool active = string.Equals(name, _nationName, StringComparison.OrdinalIgnoreCase);
                UiKit.SetSelectRowState(row, active);
                if (row.Lead.childCount > 0 && row.Lead[0] is Label tag)
                    UiKit.SetBadgeActive(tag, active);
                _list.Add(row.Root);
            }

            _list.scrollOffset = Vector2.zero;
        }

        private SelectRowParts NewRow(Action onClick)
        {
            SelectRowParts row = UiKit.SelectRow(onClick);
            Reorder(row);
            return row;
        }

        /// <summary>
        /// Puts the marker after the lead group. <see cref="UiKit.SelectRow"/> already adds them in
        /// that order; this exists so a caller that inserts a badge into the lead cannot end up with
        /// the marker in the middle of the row.
        /// </summary>
        private static void Reorder(SelectRowParts row)
        {
            row.Mark.RemoveFromHierarchy();
            row.Root.Add(row.Mark);
        }

        /// <summary>
        /// Flips a row's stack so the small caption sits ABOVE the value — the mockup's setting row
        /// ("NAZIONE" over "Italia"), as opposed to a list row where the meta belongs underneath.
        /// </summary>
        private static void MetaFirst(SelectRowParts row)
        {
            VisualElement stack = row.Name.parent;
            row.Meta.RemoveFromHierarchy();
            stack.Insert(0, row.Meta);
        }

        private static void SetMeta(SelectRowParts row, string meta)
        {
            row.Meta.text = meta ?? string.Empty;
            row.Meta.style.display = string.IsNullOrEmpty(meta) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void SetMark(SelectRowParts row, string text, bool on)
        {
            row.Mark.text = text ?? string.Empty;
            row.Mark.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            row.Mark.EnableInClassList("fts-selectrow__mark--off", !on);
            if (!UiKit.StylesLoaded)
                row.Mark.style.color = on ? UiKit.Accent : UiKit.TextHint;
        }

        /// <summary>
        /// Splits "Serie A · 20 club" into a headline and a quiet meta line. The separator is ours —
        /// the localization tables were rewritten in 14.2 to emit it — so this never has to guess.
        /// </summary>
        private static void Split(string value, out string name, out string meta)
        {
            string text = value ?? string.Empty;
            int at = text.IndexOf('·');
            if (at < 0)
            {
                name = text.Trim();
                meta = string.Empty;
                return;
            }

            name = text.Substring(0, at).Trim();
            meta = text.Substring(at + 1).Trim();
        }

        /// <summary>Three letters for the code tag. Not a federation code — see the class remarks.</summary>
        private static string Initialism(string name)
        {
            string text = (name ?? string.Empty).Trim();
            return text.Length <= 3 ? text : text.Substring(0, 3);
        }

        private void EnterMode(Mode mode, string title)
        {
            _mode = mode;
            _paneName = title ?? string.Empty;
            _paneTitle.text = _paneName;

            _list.Clear();
            _rows.Clear();
            _rowClubIds.Clear();

            bool picking = mode == Mode.Nations;
            _searchRow.style.display = picking ? DisplayStyle.Flex : DisplayStyle.None;

            // On a phone the nation list is a modal sheet; everywhere else it is the middle column.
            if (Responsive.IsMobile)
            {
                UiKit.ShowSheet(_sheet, picking);
                MoveListTo(picking ? _sheet.Panel : _pane);
            }
            else
            {
                UiKit.ShowSheet(_sheet, false);
                MoveListTo(_pane);
            }

            RefreshCta();
        }

        private void MoveListTo(VisualElement host)
        {
            if (ReferenceEquals(_paneHead.parent, host) && ReferenceEquals(_list.parent, host))
                return;

            _paneHead.RemoveFromHierarchy();
            _list.RemoveFromHierarchy();
            host.Add(_paneHead);
            host.Add(_list);
        }

        // ---------------------------------------------------------------- selection & CTA

        private void PickClub(int clubId)
        {
            _selectedClubId = clubId;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool active = _rowClubIds[i] == clubId;
                UiKit.SetSelectRowState(_rows[i], active);
                SetMark(_rows[i], active ? _tr("career_setup.selected") : _tr("career_setup.choose"), active);
                if (active) _selectedClubName = _rows[i].Name.text;
            }

            RefreshCta();
        }

        /// <summary>
        /// The CTA. Picking a club no longer starts the career on the spot: the row selects, the
        /// summary fills in, and THIS is the commit — which is what gives the mockup's accent block
        /// something to do, and gives a mis-tap on a phone somewhere to be undone.
        /// </summary>
        private void ConfirmClub()
        {
            if (_selectedClubId >= 0)
                ClubSelected?.Invoke(_selectedClubId);
        }

        private void ClearSelection()
        {
            _selectedClubId = -1;
            _selectedClubName = string.Empty;
            RefreshCta();
        }

        private void RefreshCta()
        {
            bool ready = _mode == Mode.Clubs && _selectedClubId >= 0;
            _cta.SetEnabled(ready);
            UiKit.SetCtaSub(_cta, ready
                ? _selectedClubName + "  ·  " + _paneName
                : _tr(_mode == Mode.Clubs ? "career_setup.start_hint_club" : "career_setup.start_hint_league"));

            _sumPick.text = ready ? _selectedClubName : "—";
            RefreshBottomSummary();
        }

        private void RefreshTitle()
        {
            _headTitle.text = _nationName;
        }

        private void RefreshBottomSummary()
        {
            _bottomSummary.text = string.Join("  ·  ", new[]
            {
                _sumNation.text, _sumTiers.text, _sumDatabase.text, _sumDifficulty.text
            });
        }

        // ---------------------------------------------------------------- assembly

        private void OnViewportChanged(Viewport viewport) => Layout(viewport);

        /// <summary>
        /// Re-parents the blocks into the scaffold this shape needs. Cheap enough to run on every
        /// breakpoint crossing, and it is the ONLY place either scaffold is assembled.
        /// </summary>
        private void Layout(Viewport viewport)
        {
            if (viewport == _builtFor)
                return;

            _builtFor = viewport;
            Root.Clear();
            DetachAll();

            if (viewport == Viewport.Mobile)
            {
                Root.style.paddingLeft = 0;
                Root.style.paddingRight = 0;
                Root.style.paddingTop = 0;
                Root.style.paddingBottom = 0;

                _mobileHeader.Clear();
                _mobileHeader.Add(_headStack);
                Root.Add(_mobileHeader);

                _mobileBody.Add(_worldCard);
                _mobileBody.Add(_pane);
                _mobileBody.Add(_dbCard);
                _mobileBody.Add(_diffCard);
                _actions.style.flexDirection = FlexDirection.Row;
                foreach (VisualElement action in _actions.Children())
                {
                    action.style.flexGrow = 1f;
                    action.style.flexBasis = 0f;
                }
                _mobileBody.Add(_actions);
                Root.Add(_mobileBody);

                // A phone gives the list a floor rather than the leftover height: the page scrolls,
                // not the list, so a nested scroller here would be the 6.4 double-scrollbar bug back.
                _pane.style.flexGrow = 0f;
                _pane.style.minHeight = 900f;
                _list.style.flexGrow = 1f;

                _bottomBar.Clear();
                var line = new VisualElement();
                line.style.flexShrink = 1f;
                line.style.minWidth = 0f;
                line.style.marginRight = UiKit.SpaceMd;
                line.Add(UiKit.Eyebrow(_tr("career_setup.summary")));
                line.Add(_bottomSummary);
                _bottomBar.Add(line);
                _cta.style.flexShrink = 0f;
                _cta.style.minWidth = 300f;
                _bottomBar.Add(_cta);
                Root.Add(_bottomBar);

                Root.Add(_sheet.Root);
            }
            else
            {
                Root.style.paddingLeft = StyleKeyword.Null;
                Root.style.paddingRight = StyleKeyword.Null;
                Root.style.paddingTop = StyleKeyword.Null;
                Root.style.paddingBottom = StyleKeyword.Null;

                _desktopHead.Clear();
                _desktopHead.Add(_headStack);
                _desktopHead.Add(_actions);
                foreach (VisualElement action in _actions.Children())
                {
                    action.style.flexGrow = 0f;
                    action.style.flexBasis = StyleKeyword.Null;
                }
                Root.Add(_desktopHead);

                _colLeft.Clear();
                _colLeft.Add(_worldCard);
                _colLeft.Add(_dbCard);
                _colLeft.Add(_diffCard);

                _colCenter.Clear();
                _pane.style.flexGrow = 1f;
                _pane.style.minHeight = 0f;
                _colCenter.Add(_pane);

                _colRight.Clear();
                _colRight.Add(_summaryCard);
                _colRight.Add(_note);
                _cta.style.minWidth = StyleKeyword.Null;
                _colRight.Add(_cta);

                // A tablet drops the third column: the summary rides under the options instead.
                bool tablet = viewport == Viewport.Tablet;
                _colRight.style.display = tablet ? DisplayStyle.None : DisplayStyle.Flex;
                if (tablet)
                {
                    _colLeft.Add(_summaryCard);
                    _colLeft.Add(_note);
                    _colLeft.Add(_cta);
                }

                Root.Add(_desktopBody);
                Root.Add(_sheet.Root);
            }

            // The list follows whichever host is live now.
            MoveListTo(Responsive.IsMobile && _mode == Mode.Nations ? _sheet.Panel : _pane);
            if (Responsive.IsMobile && _mode == Mode.Nations)
                UiKit.ShowSheet(_sheet, true);
        }

        private void DetachAll()
        {
            _headStack.RemoveFromHierarchy();
            _actions.RemoveFromHierarchy();
            _worldCard.RemoveFromHierarchy();
            _dbCard.RemoveFromHierarchy();
            _diffCard.RemoveFromHierarchy();
            _pane.RemoveFromHierarchy();
            _summaryCard.RemoveFromHierarchy();
            _note.RemoveFromHierarchy();
            _cta.RemoveFromHierarchy();
            _bottomSummary.RemoveFromHierarchy();
            _mobileHeader.RemoveFromHierarchy();
            _mobileBody.RemoveFromHierarchy();
            _bottomBar.RemoveFromHierarchy();
            _sheet.Root.RemoveFromHierarchy();
            _desktopHead.RemoveFromHierarchy();
            _desktopBody.RemoveFromHierarchy();
        }

        private static VisualElement Column(float grow, float maxWidth)
        {
            var e = new VisualElement();
            e.style.flexGrow = grow;
            e.style.flexShrink = 1f;
            e.style.flexBasis = 0f;
            e.style.minWidth = 0f;
            e.style.minHeight = 0f;
            if (maxWidth > 0f) e.style.maxWidth = maxWidth;
            return e;
        }
    }
}
