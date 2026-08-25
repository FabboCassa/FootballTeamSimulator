using System.Collections.Generic;
using System.Globalization;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Scouting;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Scouting screen (task 11.2): an ASSIGNMENT BOARD. The manager decides who goes where and
    /// what to look for; the scouts do the searching and file reports. He never browses the world's
    /// player database by hand — which is just as well, since since task 11.1 it can hold tens of
    /// thousands of names.
    ///
    /// Two tabs and a drill-down inside the first:
    ///   • ASSIGNMENTS — one row per scout. Send him to a club, a nation or a continent (Send →
    ///     destination → brief → confirm), or call him home. His row shows how well the club knows
    ///     that area and the ceiling that puts on precision, so the breadth trade is visible rather
    ///     than a hidden rule.
    ///   • REPORTS — the shortlist that came back, plus anyone under direct observation, each with
    ///     the scouted overall as a narrowing range and the brief that produced him. Tapping a row
    ///     opens his profile, where every attribute range and the potential band reflect the same
    ///     knowledge.
    ///   • SEARCH (task 11.3) — the whole database, paged. The manager may look anywhere: at the
    ///     third division of Argentina, at a club he has never heard of. What he READS is still
    ///     knowledge-bound, so most of what he finds is a name, a role and a useless band — unless
    ///     the player is FAMOUS, in which case the world already knows roughly how good he is
    ///     (Sim.Core's PublicKnowledge). Anything he likes he can put under observation from the
    ///     row itself, which is the "start from where you actually are" the roadmap asks for.
    ///
    /// Reads everything live from <see cref="ScoutingService"/>; owns no state but the flow it is
    /// currently in.
    /// </summary>
    public sealed class ScoutingScreenPresenter : IScreenPresenter
    {
        private const int RoleMax = 7; // PositionRole: goalkeeper(0)..striker(7)

        // Brief editor line ids.
        private const int LineRole = 0;
        private const int LineMinAge = 1;
        private const int LineMaxAge = 2;
        private const int LineMinAbility = 3;
        private const int LineMinPotential = 4;
        private const int LineExpiring = 5;
        private const int LineReset = 6;

        // Search option ids (task 11.3). A separate space from the brief lines above: they arrive on
        // a different event, so the two never meet.
        private const int SearchWhere = 0;
        private const int SearchMinAge = 1;
        private const int SearchMaxAge = 2;
        private const int SearchFame = 3;
        private const int SearchSort = 4;
        private const int SearchScouted = 5;
        private const int SearchClear = 6;


        private static readonly int[] MinAgeCycle = { 0, 16, 18, 20, 22, 24, 26, 28, 30 };
        private static readonly int[] MaxAgeCycle = { 0, 20, 22, 24, 26, 28, 30, 34 };
        private static readonly int[] AbilityCycle = { 0, 40, 50, 55, 60, 65, 70, 75, 80 };
        private static readonly int[] PotentialCycle = { 0, 50, 60, 65, 70, 75, 80, 85 };

        /// <summary>Where the screen currently is: a tab, or a step of the "send a scout" flow.</summary>
        private enum Mode
        {
            Assignments,
            Reports,
            Search,
            PickKind,
            PickNation,
            PickDivision,
            PickClub,
            PickPlayer,
            PickContinent,
            EditBrief
        }

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ScoutingService _scouting;
        private readonly PlayerProfileTarget _profileTarget;
        private readonly ILocalizationService _loc;
        private readonly ScoutingView _view;

        private Mode _mode = Mode.Assignments;

        // The flow in progress (all cleared when it ends).
        private int _pendingScoutId;
        private ScoutingAreaKind _pendingKind = ScoutingAreaKind.Club;
        private string _pendingNationCode = string.Empty;
        private int _pendingLeagueId;
        private int _pendingClubId;
        private ScoutingArea _pendingArea;
        private ScoutingFilters _pendingFilters = new ScoutingFilters();

        // The search tab (task 11.3). The query is kept between visits — coming back from a profile
        // must land on the same page of the same search, not on a reset screen.
        private readonly PlayerSearchQuery _search = new PlayerSearchQuery();
        private int _searchScope;   // index into the scope list built by BuildScopes()
        private int _searchFame;    // index into FameCycle

        public VisualElement View => _view.Root;

        public ScoutingScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ScoutingService scouting,
            PlayerProfileTarget profileTarget,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _scouting = scouting;
            _profileTarget = profileTarget;
            _loc = loc;
            _view = new ScoutingView(loc.Tr);
        }

        public void Enter()
        {
            _view.TabSelected += OnTab;
            _view.PlayerSelected += OnPlayerSelected;
            _view.ScoutActionClicked += OnScoutAction;
            _view.RowActionClicked += OnRowAction;
            _view.OptionSelected += OnOption;
            _view.FilterLineClicked += OnFilterLine;
            _view.ConfirmClicked += OnConfirm;
            _view.CancelClicked += OnCancel;
            _view.BackClicked += OnBack;
            _view.SearchTextChanged += OnSearchText;
            _view.SearchOptionClicked += OnSearchOption;
            _view.SearchRoleClicked += OnSearchRole;
            _view.SearchPageClicked += OnSearchPage;
            Refresh();
        }

        public void Exit()
        {
            _view.TabSelected -= OnTab;
            _view.PlayerSelected -= OnPlayerSelected;
            _view.ScoutActionClicked -= OnScoutAction;
            _view.RowActionClicked -= OnRowAction;
            _view.OptionSelected -= OnOption;
            _view.FilterLineClicked -= OnFilterLine;
            _view.ConfirmClicked -= OnConfirm;
            _view.CancelClicked -= OnCancel;
            _view.BackClicked -= OnBack;
            _view.SearchTextChanged -= OnSearchText;
            _view.SearchOptionClicked -= OnSearchOption;
            _view.SearchRoleClicked -= OnSearchRole;
            _view.SearchPageClicked -= OnSearchPage;
        }

        public void Reveal()
        {
            // Coming back from a player's profile should land on the board, not mid-flow.
            if (IsFlow(_mode))
                _mode = Mode.Assignments;
            Refresh();
        }

        // ------------------------------------------------------------------ input

        private void OnTab(int tab)
        {
            _mode = tab == 2 ? Mode.Search : tab == 1 ? Mode.Reports : Mode.Assignments;
            Refresh();
        }

        private void OnPlayerSelected(int playerId)
        {
            _profileTarget.PlayerId = playerId;
            _navigator.Push<PlayerProfileScreenPresenter>();
        }

        /// <summary>The scout's action button: out ⇒ call him home, at home ⇒ start sending him.</summary>
        private void OnScoutAction(int scoutId)
        {
            if (_scouting.BriefOf(scoutId) != null)
            {
                _scouting.RecallScout(scoutId);
                Refresh();
                return;
            }

            _pendingScoutId = scoutId;
            _pendingArea = null;
            _pendingFilters = new ScoutingFilters();
            _pendingNationCode = string.Empty;
            _pendingLeagueId = 0;
            _pendingClubId = 0;
            _mode = Mode.PickKind;
            Refresh();
        }

        /// <summary>
        /// A row's action button. On the Reports tab it stops a direct observation or dismisses a
        /// filed report; in the SEARCH tab (task 11.3) it is the "put him under observation" the
        /// roadmap asks for — the same named-target brief the drill-down creates, started from
        /// wherever the manager happens to be looking.
        /// </summary>
        private void OnRowAction(int playerId)
        {
            if (_scouting.IsWatching(playerId))
                _scouting.Unwatch(playerId);
            else if (_mode == Mode.Search)
                _scouting.Watch(playerId);
            else
                _scouting.DismissReport(playerId);

            Refresh();
        }

        // ------------------------------------------------------------------ the search tab (task 11.3)

        private void OnSearchText(string text)
        {
            _search.Text = text ?? string.Empty;
            _search.Page = 0;
            Refresh();
        }

        private void OnSearchRole(int role)
        {
            _search.Filters.Role = role;
            _search.Page = 0;
            Refresh();
        }

        private void OnSearchPage(int delta)
        {
            _search.Page += delta;
            if (_search.Page < 0)
                _search.Page = 0;
            Refresh();
        }

        private void OnSearchOption(int option)
        {
            switch (option)
            {
                case SearchWhere:
                {
                    List<SearchScope> scopes = BuildScopes();
                    _searchScope = scopes.Count == 0 ? 0 : (_searchScope + 1) % scopes.Count;
                    break;
                }

                case SearchMinAge:
                    _search.Filters.MinAge = Next(MinAgeCycle, _search.Filters.MinAge);
                    break;
                case SearchMaxAge:
                    _search.Filters.MaxAge = Next(MaxAgeCycle, _search.Filters.MaxAge);
                    break;
                case SearchFame:
                    // Four steps (anyone / known / well known / famous), and the fame each one means
                    // is asked of the model — never hardcoded here, or the label and the filter would
                    // drift apart the first time the balance number moves.
                    _searchFame = (_searchFame + 1) % 4;
                    _search.MinFame = _scouting.FameFloorOfTier(_searchFame);
                    break;
                case SearchSort:
                    _search.Sort = (PlayerSearchSort)(((int)_search.Sort + 1) % 6);
                    break;
                case SearchScouted:
                    _search.ScoutedOnly = !_search.ScoutedOnly;
                    break;
                case SearchClear:
                    _search.Filters = new ScoutingFilters();
                    _search.Text = string.Empty;
                    _search.MinFame = 0;
                    _search.ScoutedOnly = false;
                    _search.Sort = PlayerSearchSort.Fame;
                    _searchFame = 0;
                    _searchScope = 0;
                    break;
            }

            _search.Page = 0;
            Refresh();
        }

        private void OnOption(int value)
        {
            switch (_mode)
            {
                case Mode.PickKind:
                    _pendingKind = (ScoutingAreaKind)value;
                    _mode = _pendingKind == ScoutingAreaKind.Continent ? Mode.PickContinent : Mode.PickNation;
                    break;

                case Mode.PickContinent:
                    _pendingArea = ScoutingArea.ForContinent((Continent)value);
                    _mode = Mode.EditBrief;
                    break;

                case Mode.PickNation:
                {
                    Nation nation = NationAt(value);
                    if (nation == null)
                        break;

                    _pendingNationCode = nation.Code;
                    if (_pendingKind == ScoutingAreaKind.Nation)
                    {
                        _pendingArea = ScoutingArea.ForNation(nation.Code);
                        _mode = Mode.EditBrief;
                    }
                    else
                    {
                        _mode = Mode.PickDivision;
                    }

                    break;
                }

                case Mode.PickDivision:
                    _pendingLeagueId = value;
                    _mode = Mode.PickClub;
                    break;

                case Mode.PickClub:
                    _pendingClubId = value;
                    if (_pendingKind == ScoutingAreaKind.Player)
                    {
                        _mode = Mode.PickPlayer;
                    }
                    else
                    {
                        _pendingArea = ScoutingArea.ForClub(value);
                        _mode = Mode.EditBrief;
                    }

                    break;

                case Mode.PickPlayer:
                    // A named target takes no filters — you already chose the man.
                    _scouting.SendScout(_pendingScoutId, ScoutingArea.ForPlayer(value), new ScoutingFilters());
                    _mode = Mode.Assignments;
                    break;
            }

            Refresh();
        }

        private void OnFilterLine(int line)
        {
            switch (line)
            {
                case LineRole:
                    _pendingFilters.Role = _pendingFilters.Role >= RoleMax ? -1 : _pendingFilters.Role + 1;
                    break;
                case LineMinAge:
                    _pendingFilters.MinAge = Next(MinAgeCycle, _pendingFilters.MinAge);
                    break;
                case LineMaxAge:
                    _pendingFilters.MaxAge = Next(MaxAgeCycle, _pendingFilters.MaxAge);
                    break;
                case LineMinAbility:
                    _pendingFilters.MinAbility = Next(AbilityCycle, _pendingFilters.MinAbility);
                    break;
                case LineMinPotential:
                    _pendingFilters.MinPotential = Next(PotentialCycle, _pendingFilters.MinPotential);
                    break;
                case LineExpiring:
                    _pendingFilters.ExpiringContractOnly = !_pendingFilters.ExpiringContractOnly;
                    break;
                case LineReset:
                    _pendingFilters = new ScoutingFilters();
                    break;
            }

            Refresh();
        }

        private void OnConfirm()
        {
            if (_mode == Mode.EditBrief && _pendingArea != null)
                _scouting.SendScout(_pendingScoutId, _pendingArea, _pendingFilters);

            _mode = Mode.Assignments;
            Refresh();
        }

        /// <summary>Steps back one level of the flow rather than leaving the screen.</summary>
        private void OnCancel()
        {
            switch (_mode)
            {
                case Mode.EditBrief:
                    _mode = _pendingKind == ScoutingAreaKind.Continent ? Mode.PickContinent
                        : _pendingKind == ScoutingAreaKind.Nation ? Mode.PickNation
                        : Mode.PickClub;
                    break;
                case Mode.PickPlayer:
                    _mode = Mode.PickClub;
                    break;
                case Mode.PickClub:
                    _mode = Mode.PickDivision;
                    break;
                case Mode.PickDivision:
                    _mode = Mode.PickNation;
                    break;
                case Mode.PickNation:
                case Mode.PickContinent:
                    _mode = Mode.PickKind;
                    break;
                default:
                    _mode = Mode.Assignments;
                    break;
            }

            Refresh();
        }

        private void OnBack() => _navigator.Pop();

        // ------------------------------------------------------------------ rendering

        private void Refresh()
        {
            _view.SetTabs(_mode == Mode.Search ? 2 : _mode == Mode.Reports ? 1 : 0, !IsFlow(_mode));

            switch (_mode)
            {
                case Mode.Search:
                    RefreshSearch();
                    break;

                case Mode.Reports:
                    _view.SetHeader(_loc.Tr("scouting.reports_header", _scouting.Reports().Count));
                    _view.SetHelp(_loc.Tr("scouting.help.reports"));
                    _view.ShowReports(BuildReportRows(), _loc.Tr("scouting.reports.empty"));
                    break;

                case Mode.PickKind:
                    _view.SetHeader(_loc.Tr("scouting.pick.kind_title", ScoutName(_pendingScoutId)));
                    _view.SetHelp(_loc.Tr("scouting.help.kind"));
                    _view.ShowPicker(BuildKindOptions(), string.Empty);
                    break;

                case Mode.PickContinent:
                    _view.SetHeader(_loc.Tr("scouting.pick.continent_title"));
                    _view.SetHelp(_loc.Tr("scouting.help.area"));
                    _view.ShowPicker(BuildContinentOptions(), _loc.Tr("scouting.pick.empty"));
                    break;

                case Mode.PickNation:
                    _view.SetHeader(_loc.Tr("scouting.pick.nation_title"));
                    _view.SetHelp(_loc.Tr("scouting.help.area"));
                    _view.ShowPicker(BuildNationOptions(), _loc.Tr("scouting.pick.empty"));
                    break;

                case Mode.PickDivision:
                    _view.SetHeader(_loc.Tr("scouting.pick.division_title"));
                    _view.SetHelp(_loc.Tr("scouting.help.area"));
                    _view.ShowPicker(BuildDivisionOptions(), _loc.Tr("scouting.pick.empty"));
                    break;

                case Mode.PickClub:
                    _view.SetHeader(_loc.Tr("scouting.pick.club_title"));
                    _view.SetHelp(_loc.Tr("scouting.help.area"));
                    _view.ShowPicker(BuildClubOptions(), _loc.Tr("scouting.pick.empty"));
                    break;

                case Mode.PickPlayer:
                    _view.SetHeader(_loc.Tr("scouting.pick.player_title", ClubName(_pendingClubId)));
                    _view.SetHelp(_loc.Tr("scouting.help.player"));
                    _view.ShowPicker(BuildPlayerOptions(), _loc.Tr("scouting.pick.empty"));
                    break;

                case Mode.EditBrief:
                    _view.SetHeader(_loc.Tr("scouting.brief_title", AreaName(_pendingArea)));
                    _view.SetHelp(_loc.Tr("scouting.help.brief",
                        _scouting.PrecisionCeilingPercent(_pendingArea), _scouting.AreaPlayerCount(_pendingArea)));
                    _view.ShowFilters(BuildBriefLines(), _loc.Tr("scouting.brief.confirm"));
                    break;

                default:
                    _view.SetHeader(_loc.Tr("scouting.header",
                        _scouting.ScoutLevel(), _scouting.WatchCount(), _scouting.WatchCapacity()));
                    _view.SetHelp(_loc.Tr("scouting.help.assignments"));
                    _view.ShowAssignments(BuildScoutRows(), _loc.Tr("scouting.no_scouts"));
                    break;
            }
        }

        private List<ScoutRowVm> BuildScoutRows()
        {
            var rows = new List<ScoutRowVm>();
            foreach (Scout scout in _scouting.Scouts())
            {
                ScoutingAssignment brief = _scouting.BriefOf(scout.Id);
                bool outInTheField = brief != null;

                var vm = new ScoutRowVm
                {
                    ScoutId = scout.Id,
                    Name = scout.Name,
                    LevelText = _loc.Tr("scouting.level", scout.Level),
                    AttributesText = _loc.Tr("scouting.attrs",
                        scout.JudgingAbility, scout.JudgingPotential, scout.Adaptability),
                    Destination = outInTheField ? AreaName(brief.Area) : _loc.Tr("scouting.at_home"),
                    Out = outInTheField,
                    ActionText = _loc.Tr(outInTheField ? "scouting.recall" : "scouting.send")
                };

                if (outInTheField)
                {
                    string areaKey = brief.Area.Key;
                    vm.Detail = _loc.Tr("scouting.brief_detail",
                        brief.WeeksElapsed, _scouting.ReportCountOfArea(areaKey), FilterSummary(brief.Filters));
                    vm.AreaPercent = _scouting.AreaKnowledgePercent(areaKey);
                    vm.AreaText = _loc.Tr("scouting.area_knowledge", vm.AreaPercent);
                    vm.CeilingText = _loc.Tr("scouting.ceiling", _scouting.PrecisionCeilingPercent(brief.Area));
                }

                rows.Add(vm);
            }

            return rows;
        }

        /// <summary>
        /// The Reports tab: everyone the department currently has an eye on — named targets first
        /// (they are the deliberate, expensive choice), then the shortlist, best perceived first.
        /// </summary>
        private List<ScoutingRowVm> BuildReportRows()
        {
            var rows = new List<ScoutingRowVm>();
            var seen = new HashSet<int>();

            foreach (ScoutingAssignment brief in _scouting.Briefs())
            {
                if (brief.Area.Kind != ScoutingAreaKind.Player)
                    continue;

                ScoutingRowVm row = BuildRow(brief.Area.PlayerId, _loc.Tr("scouting.source.direct"), true);
                if (row != null && seen.Add(row.PlayerId))
                    rows.Add(row);
            }

            var filed = new List<ScoutingRowVm>();
            foreach (ScoutReportEntry entry in _scouting.Reports())
            {
                if (seen.Contains(entry.PlayerId))
                    continue;

                ScoutingRowVm row = BuildRow(entry.PlayerId,
                    _loc.Tr("scouting.source.brief", AreaName(ScoutingArea.Parse(entry.AreaKey))), false);
                if (row != null && seen.Add(row.PlayerId))
                    filed.Add(row);
            }

            // Best perceived first — never the true overall. The estimate is embedded in OvrText, so
            // sort on the knowledge-aware value the service hands us rather than re-parsing it.
            filed.Sort((a, b) =>
            {
                int byEstimate = EstimateOf(b.PlayerId).CompareTo(EstimateOf(a.PlayerId));
                return byEstimate != 0 ? byEstimate : a.PlayerId.CompareTo(b.PlayerId);
            });

            rows.AddRange(filed);
            return rows;
        }

        private int EstimateOf(int playerId)
        {
            Player player = _career.FindPlayerInWorld(playerId);
            return player != null ? _scouting.Report(player).Overall.Estimate : 0;
        }

        private ScoutingRowVm BuildRow(int playerId, string source, bool direct)
        {
            Player player = _career.FindPlayerInWorld(playerId);
            if (player == null)
                return null;

            Club club = _career.World.ClubOfPlayer(playerId);
            PlayerScoutReport report = _scouting.Report(player);
            int knowledge = _scouting.KnowledgeOf(playerId);
            int pct = _scouting.KnowledgePercentOf(playerId);

            string knowledgeText = knowledge <= 0
                ? _loc.Tr("scouting.unscouted")
                : knowledge >= _scouting.MaxKnowledge
                    ? _loc.Tr("scouting.known")
                    : _loc.Tr("scouting.percent", pct);

            return new ScoutingRowVm
            {
                PlayerId = playerId,
                Name = club != null ? $"{player.FullName} — {club.Name}" : player.FullName,
                RoleAbbr = RoleName(player.Role),
                RoleGroup = RoleFormat.Group(player.Role),
                Age = player.Age.ToString(CultureInfo.InvariantCulture),
                OvrText = knowledge >= _scouting.MaxKnowledge
                    ? _loc.Tr("scouting.ovr_known", report.Overall.Estimate)
                    : _loc.Tr("scouting.ovr_range", report.Overall.Min, report.Overall.Max),
                KnowledgePercent = pct,
                KnowledgeText = knowledgeText,
                Source = source,
                Watching = direct,
                ToggleText = _loc.Tr(direct ? "scouting.stop" : "scouting.dismiss"),
                ToggleEnabled = true
            };
        }

        // ------------------------------------------------------------------ the search tab (task 11.3)

        /// <summary>Where a search looks: the whole world, your own nation, or one continent.</summary>
        private struct SearchScope
        {
            public ScoutingArea Area;
            public string Label;
        }

        private void RefreshSearch()
        {
            List<SearchScope> scopes = BuildScopes();
            if (_searchScope < 0 || _searchScope >= scopes.Count)
                _searchScope = 0;

            _search.Area = scopes.Count > 0 ? scopes[_searchScope].Area : null;
            _search.PageSize = _scouting.SearchPageSize;

            PlayerSearchPage page = _scouting.Search(_search);
            _search.Page = page.Page; // the search clamps the page; keep the two in step

            _view.SetHeader(_loc.Tr("scouting.search_header", page.Total, _scouting.WorldPlayerCount()));
            _view.SetHelp(_loc.Tr("scouting.help.search"));
            _view.ShowSearch(BuildSearchPanel(page, scopes), BuildSearchRows(page),
                             _loc.Tr("scouting.search.empty"));
        }

        /// <summary>
        /// The scopes the picker cycles through: the whole world first (the point of the feature),
        /// then the nation you actually manage in, then every continent the world loaded. Continents
        /// with no nations are left out — an empty scope would just look broken.
        /// </summary>
        private List<SearchScope> BuildScopes()
        {
            var scopes = new List<SearchScope>
            {
                new SearchScope { Area = null, Label = _loc.Tr("scouting.search.where_world") }
            };

            League userLeague = _career.GetUserLeague();
            if (userLeague != null && !string.IsNullOrEmpty(userLeague.NationCode))
            {
                Nation home = _career.World.FindNation(userLeague.NationCode);
                if (home != null)
                {
                    scopes.Add(new SearchScope
                    {
                        Area = ScoutingArea.ForNation(home.Code),
                        Label = home.Name
                    });
                }
            }

            for (int c = 0; c <= (int)Continent.Oceania; c++)
            {
                var continent = (Continent)c;
                bool any = false;
                foreach (Nation nation in _career.World.Nations)
                {
                    if (nation.Continent == continent)
                    {
                        any = true;
                        break;
                    }
                }

                if (!any)
                    continue;

                scopes.Add(new SearchScope
                {
                    Area = ScoutingArea.ForContinent(continent),
                    Label = ContinentName(continent)
                });
            }

            return scopes;
        }

        private SearchPanelVm BuildSearchPanel(PlayerSearchPage page, List<SearchScope> scopes)
        {
            int pageSize = _scouting.SearchPageSize;
            int from = page.Total == 0 ? 0 : page.Page * pageSize + 1;
            int to = page.Total == 0 ? 0 : from + page.Hits.Count - 1;

            return new SearchPanelVm
            {
                Text = _search.Text,
                Placeholder = _loc.Tr("scouting.search.field"),
                RoleChips = BuildRoleChips(),
                Options = BuildSearchOptions(scopes),
                Summary = page.Total == 0
                    ? _loc.Tr("scouting.search.none")
                    : _loc.Tr("scouting.search.summary", from, to, page.Total, page.Page + 1, page.PageCount),
                HasPrevious = page.Page > 0,
                HasNext = page.Page + 1 < page.PageCount
            };
        }

        private List<FilterChipVm> BuildRoleChips()
        {
            var chips = new List<FilterChipVm>
            {
                new FilterChipVm
                {
                    Value = -1,
                    Label = _loc.Tr("scouting.filter.any"),
                    RoleGroup = -1,
                    Selected = _search.Filters.Role < 0
                }
            };

            for (int role = 0; role <= RoleMax; role++)
            {
                chips.Add(new FilterChipVm
                {
                    Value = role,
                    Label = RoleName((PositionRole)role),
                    RoleGroup = RoleFormat.Group((PositionRole)role),
                    Selected = _search.Filters.Role == role
                });
            }

            return chips;
        }

        private List<FilterLineVm> BuildSearchOptions(List<SearchScope> scopes)
        {
            string any = _loc.Tr("scouting.filter.any");

            return new List<FilterLineVm>
            {
                new FilterLineVm
                {
                    Id = SearchWhere,
                    Label = _loc.Tr("scouting.search.where"),
                    Value = scopes.Count > 0 ? scopes[_searchScope].Label : any
                },
                new FilterLineVm
                {
                    Id = SearchMinAge,
                    Label = _loc.Tr("scouting.filter.min_age"),
                    Value = _search.Filters.MinAge <= 0 ? any : _search.Filters.MinAge.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = SearchMaxAge,
                    Label = _loc.Tr("scouting.filter.max_age"),
                    Value = _search.Filters.MaxAge <= 0 ? any : _search.Filters.MaxAge.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = SearchFame,
                    Label = _loc.Tr("scouting.search.fame"),
                    Value = _loc.Tr("scouting.fame." + _searchFame.ToString(CultureInfo.InvariantCulture))
                },
                new FilterLineVm
                {
                    Id = SearchSort,
                    Label = _loc.Tr("scouting.search.sort"),
                    Value = _loc.Tr("scouting.sort." + ((int)_search.Sort).ToString(CultureInfo.InvariantCulture))
                },
                new FilterLineVm
                {
                    Id = SearchScouted,
                    Label = _loc.Tr("scouting.search.scouted_only"),
                    Value = _loc.Tr(_search.ScoutedOnly ? "scouting.filter.on" : "scouting.filter.off")
                },
                new FilterLineVm
                {
                    Id = SearchClear,
                    Label = _loc.Tr("scouting.search.clear"),
                    Value = _loc.Tr("scouting.search.clear_value")
                }
            };
        }

        /// <summary>
        /// One row per hit. The numbers come from the search itself — they are already built at the
        /// knowledge the club reads him at, fame included — so the row never re-derives them and can
        /// never disagree with what the query filtered on.
        /// </summary>
        private List<ScoutingRowVm> BuildSearchRows(PlayerSearchPage page)
        {
            var rows = new List<ScoutingRowVm>();
            int max = _scouting.MaxKnowledge;

            foreach (PlayerSearchHit hit in page.Hits)
            {
                Player player = hit.Player;
                bool watching = _scouting.IsWatching(hit.PlayerId);
                int percent = max > 0 ? hit.Knowledge * 100 / max : 0;

                rows.Add(new ScoutingRowVm
                {
                    PlayerId = hit.PlayerId,
                    Name = hit.Club != null ? $"{player.FullName} — {hit.Club.Name}" : player.FullName,
                    RoleAbbr = RoleName(player.Role),
                    RoleGroup = RoleFormat.Group(player.Role),
                    Age = player.Age.ToString(CultureInfo.InvariantCulture),
                    OvrText = hit.Knowledge >= max
                        ? _loc.Tr("scouting.ovr_known", hit.Overall.Estimate)
                        : _loc.Tr("scouting.ovr_range", hit.Overall.Min, hit.Overall.Max),
                    KnowledgePercent = percent,
                    KnowledgeText = hit.Knowledge <= 0
                        ? _loc.Tr("scouting.unscouted")
                        : hit.Knowledge >= max
                            ? _loc.Tr("scouting.known")
                            : _loc.Tr("scouting.percent", percent),
                    Source = SearchSource(hit),
                    Watching = watching,
                    ToggleText = _loc.Tr(watching ? "scouting.stop" : "scouting.watch"),
                    ToggleEnabled = watching || _scouting.HasFreeSlot()
                });
            }

            return rows;
        }

        /// <summary>The line under the name: where he plays and how well known he is.</summary>
        private string SearchSource(PlayerSearchHit hit)
        {
            string fame = _loc.Tr("scouting.fame_tier." + hit.FameTier.ToString(CultureInfo.InvariantCulture));

            if (hit.League == null)
                return fame;

            Nation nation = string.IsNullOrEmpty(hit.League.NationCode)
                ? null
                : _career.World.FindNation(hit.League.NationCode);

            return nation != null
                ? _loc.Tr("scouting.search.source", nation.Name, hit.League.Name, fame)
                : _loc.Tr("scouting.search.source_league", hit.League.Name, fame);
        }

        // ------------------------------------------------------------------ the pickers

        private List<PickerOptionVm> BuildKindOptions() => new List<PickerOptionVm>
        {
            new PickerOptionVm
            {
                Value = (int)ScoutingAreaKind.Club,
                Label = _loc.Tr("scouting.pick.club"),
                Detail = _loc.Tr("scouting.pick.club_hint")
            },
            new PickerOptionVm
            {
                Value = (int)ScoutingAreaKind.Nation,
                Label = _loc.Tr("scouting.pick.nation"),
                Detail = _loc.Tr("scouting.pick.nation_hint")
            },
            new PickerOptionVm
            {
                Value = (int)ScoutingAreaKind.Continent,
                Label = _loc.Tr("scouting.pick.continent"),
                Detail = _loc.Tr("scouting.pick.continent_hint")
            },
            new PickerOptionVm
            {
                Value = (int)ScoutingAreaKind.Player,
                Label = _loc.Tr("scouting.pick.player"),
                Detail = _loc.Tr("scouting.pick.player_hint")
            }
        };

        private List<PickerOptionVm> BuildContinentOptions()
        {
            var options = new List<PickerOptionVm>();
            for (int c = 0; c <= (int)Continent.Oceania; c++)
            {
                var continent = (Continent)c;
                int nations = 0;
                foreach (Nation nation in _career.World.Nations)
                {
                    if (nation.Continent == continent)
                        nations++;
                }

                if (nations == 0)
                    continue;

                ScoutingArea area = ScoutingArea.ForContinent(continent);
                options.Add(new PickerOptionVm
                {
                    Value = c,
                    Label = ContinentName(continent),
                    Detail = _loc.Tr("scouting.pick.detail_continent", nations, _scouting.AreaPlayerCount(area))
                });
            }

            return options;
        }

        private List<PickerOptionVm> BuildNationOptions()
        {
            var options = new List<PickerOptionVm>();
            List<Nation> nations = _career.World.Nations;
            for (int i = 0; i < nations.Count; i++)
            {
                Nation nation = nations[i];
                int clubs = 0;
                foreach (League league in nation.Leagues)
                    clubs += league.Clubs.Count;

                options.Add(new PickerOptionVm
                {
                    Value = i,
                    Key = nation.Code,
                    Label = nation.Name,
                    Detail = _loc.Tr("scouting.pick.detail_nation", nation.Leagues.Count, clubs)
                });
            }

            return options;
        }

        private List<PickerOptionVm> BuildDivisionOptions()
        {
            var options = new List<PickerOptionVm>();
            Nation nation = _career.World.FindNation(_pendingNationCode);
            if (nation == null)
                return options;

            foreach (League league in nation.Leagues)
            {
                options.Add(new PickerOptionVm
                {
                    Value = league.Id,
                    Label = league.Name,
                    Detail = _loc.Tr("scouting.pick.detail_division", league.Clubs.Count)
                });
            }

            return options;
        }

        private List<PickerOptionVm> BuildClubOptions()
        {
            var options = new List<PickerOptionVm>();
            League league = _career.World.FindLeague(_pendingLeagueId);
            if (league == null)
                return options;

            foreach (Club club in league.Clubs)
            {
                if (club.Id == _career.UserClubId)
                    continue;

                options.Add(new PickerOptionVm
                {
                    Value = club.Id,
                    Label = club.Name,
                    Detail = _loc.Tr("scouting.pick.detail_club", club.Squad.Players.Count)
                });
            }

            return options;
        }

        /// <summary>
        /// The squad of the club we drilled into, for a NAMED observation — the roadmap's "open a
        /// club, select the players he cares about". Each line shows what we already believe about
        /// him, so the choice is informed by the scouting we have, not by the truth.
        /// </summary>
        private List<PickerOptionVm> BuildPlayerOptions()
        {
            var options = new List<PickerOptionVm>();
            Club club = _career.World.FindClub(_pendingClubId);
            if (club == null)
                return options;

            foreach (Player player in club.Squad.Players)
            {
                if (_scouting.IsWatching(player.Id))
                    continue;

                PlayerScoutReport report = _scouting.Report(player);
                bool known = _scouting.KnowledgeOf(player.Id) >= _scouting.MaxKnowledge;

                options.Add(new PickerOptionVm
                {
                    Value = player.Id,
                    Label = $"{player.FullName} · {RoleName(player.Role)} · {player.Age.ToString(CultureInfo.InvariantCulture)}",
                    Detail = known
                        ? _loc.Tr("scouting.ovr_known", report.Overall.Estimate)
                        : _loc.Tr("scouting.ovr_range", report.Overall.Min, report.Overall.Max)
                });
            }

            return options;
        }

        // ------------------------------------------------------------------ the brief editor

        private List<FilterLineVm> BuildBriefLines()
        {
            ScoutingFilters f = _pendingFilters;
            string any = _loc.Tr("scouting.filter.any");

            return new List<FilterLineVm>
            {
                new FilterLineVm
                {
                    Id = LineRole,
                    Label = _loc.Tr("scouting.filter.role"),
                    Value = f.Role < 0 ? any : RoleName((PositionRole)f.Role)
                },
                new FilterLineVm
                {
                    Id = LineMinAge,
                    Label = _loc.Tr("scouting.filter.min_age"),
                    Value = f.MinAge <= 0 ? any : f.MinAge.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = LineMaxAge,
                    Label = _loc.Tr("scouting.filter.max_age"),
                    Value = f.MaxAge <= 0 ? any : f.MaxAge.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = LineMinAbility,
                    Label = _loc.Tr("scouting.filter.min_ability"),
                    Value = f.MinAbility <= 0 ? any : f.MinAbility.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = LineMinPotential,
                    Label = _loc.Tr("scouting.filter.min_potential"),
                    Value = f.MinPotential <= 0 ? any : f.MinPotential.ToString(CultureInfo.InvariantCulture)
                },
                new FilterLineVm
                {
                    Id = LineExpiring,
                    Label = _loc.Tr("scouting.filter.expiring"),
                    Value = _loc.Tr(f.ExpiringContractOnly ? "scouting.filter.on" : "scouting.filter.off")
                },
                new FilterLineVm
                {
                    Id = LineReset,
                    Label = _loc.Tr("scouting.filter.reset"),
                    Value = FilterSummary(f)
                }
            };
        }

        /// <summary>A one-line human reading of a brief ("any", or "ST · ≤24 · ABI 60+").</summary>
        private string FilterSummary(ScoutingFilters f)
        {
            if (f == null || f.IsEmpty)
                return _loc.Tr("scouting.filter.any");

            var parts = new List<string>();
            if (f.Role >= 0) parts.Add(RoleName((PositionRole)f.Role));
            if (f.MinAge > 0) parts.Add(_loc.Tr("scouting.filter.tag_min_age", f.MinAge));
            if (f.MaxAge > 0) parts.Add(_loc.Tr("scouting.filter.tag_max_age", f.MaxAge));
            if (f.MinAbility > 0) parts.Add(_loc.Tr("scouting.filter.tag_ability", f.MinAbility));
            if (f.MinPotential > 0) parts.Add(_loc.Tr("scouting.filter.tag_potential", f.MinPotential));
            if (f.ExpiringContractOnly) parts.Add(_loc.Tr("scouting.filter.tag_expiring"));

            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------ naming helpers

        private string AreaName(ScoutingArea area)
        {
            if (area == null)
                return string.Empty;

            switch (area.Kind)
            {
                case ScoutingAreaKind.Club:
                {
                    Club club = _career.World.FindClub(area.ClubId);
                    return club != null ? club.Name : _loc.Tr("scouting.area.unknown");
                }

                case ScoutingAreaKind.Nation:
                {
                    Nation nation = _career.World.FindNation(area.NationCode);
                    return nation != null ? nation.Name : area.NationCode;
                }

                case ScoutingAreaKind.Continent:
                    return ContinentName(area.Continent);

                default:
                {
                    Player player = _career.FindPlayerInWorld(area.PlayerId);
                    return player != null ? player.FullName : _loc.Tr("scouting.area.unknown");
                }
            }
        }

        private Nation NationAt(int index)
        {
            List<Nation> nations = _career.World.Nations;
            return index >= 0 && index < nations.Count ? nations[index] : null;
        }

        private string ContinentName(Continent continent)
            => _loc.Tr("continent." + continent.ToString().ToLowerInvariant());

        private string ClubName(int clubId)
        {
            Club club = _career.World.FindClub(clubId);
            return club != null ? club.Name : _loc.Tr("scouting.area.unknown");
        }

        private string ScoutName(int scoutId)
        {
            foreach (Scout scout in _scouting.Scouts())
            {
                if (scout.Id == scoutId)
                    return scout.Name;
            }

            return string.Empty;
        }

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private static bool IsFlow(Mode mode)
            => mode != Mode.Assignments && mode != Mode.Reports && mode != Mode.Search;

        /// <summary>Next value in a cycle, wrapping; an unknown current value restarts the cycle.</summary>
        private static int Next(int[] cycle, int current)
        {
            for (int i = 0; i < cycle.Length; i++)
            {
                if (cycle[i] == current)
                    return cycle[(i + 1) % cycle.Length];
            }

            return cycle.Length > 0 ? cycle[0] : 0;
        }
    }
}
