using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Private-league season screen (task 8.3b): the schedule, the standings and the all-ready controls.
    /// A member marks ready (the server resolves the next round once everyone is); the creator can force
    /// an advance. Played fixtures open the stored replay. Reads the league detail once (for the name +
    /// whether the caller is the creator) then drives everything off the season DTO. Lives in the App
    /// scope (online leagues are not a career Game scope).
    /// </summary>
    public sealed class SeasonScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly SeasonReplayTarget _replayTarget;
        private readonly SeasonView _view;

        private string _leagueId;
        private bool _isCreator;
        private bool _busy;
        private bool _youAreReady;
        private int? _yourClub;
        private LeagueSeasonDto _lastSeason;

        public VisualElement View => _view.Root;

        public SeasonScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues,
            LeagueSelection selection, SeasonReplayTarget replayTarget)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _replayTarget = replayTarget;
            _view = new SeasonView(loc.Tr);
        }

        public void Enter()
        {
            _view.SetEditVisible(false); // the lineup/tactic/plan editor is wired in 8.3b task 10.
            _view.ReadyToggleClicked += OnReadyToggle;
            _view.AdvanceClicked += OnAdvance;
            _view.RefreshClicked += OnRefresh;
            _view.VerifyStateClicked += OnVerifyState;
            _view.FixtureClicked += OnFixture;
            _view.BackClicked += OnBack;

            _leagueId = _selection.LeagueId;
            LoadAllAsync().Forget();
        }

        public void Exit()
        {
            _view.ReadyToggleClicked -= OnReadyToggle;
            _view.AdvanceClicked -= OnAdvance;
            _view.RefreshClicked -= OnRefresh;
            _view.VerifyStateClicked -= OnVerifyState;
            _view.FixtureClicked -= OnFixture;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadSeasonAsync().Forget();

        // --- loading -----------------------------------------------------------------------------

        private async UniTaskVoid LoadAllAsync()
        {
            if (string.IsNullOrEmpty(_leagueId))
            {
                _view.ShowStatus(_loc.Tr("leagues.error.not_found"), isError: true);
                return;
            }

            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("leagues.loading"), isError: false);

            var detail = await _leagues.GetAsync(_leagueId);
            if (!detail.Success)
            {
                _view.SetBusy(false);
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(detail.Error)), isError: true);
                return;
            }

            _isCreator = detail.Value.league.isCreator;
            _view.SetHeader(detail.Value.league.name);

            var season = await _leagues.GetSeasonAsync(_leagueId);
            _view.SetBusy(false);

            if (!season.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(season.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            RenderSeason(season.Value);
        }

        private async UniTaskVoid LoadSeasonAsync()
        {
            if (string.IsNullOrEmpty(_leagueId)) return;

            _view.SetBusy(true);
            var season = await _leagues.GetSeasonAsync(_leagueId);
            _view.SetBusy(false);

            if (season.Success)
            {
                _view.ClearStatus();
                RenderSeason(season.Value);
            }
            else
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(season.Error)), isError: true);
            }
        }

        // --- render ------------------------------------------------------------------------------

        private void RenderSeason(LeagueSeasonDto s)
        {
            _lastSeason = s;
            SeasonStateDto st = s.season;
            _youAreReady = st.youAreReady;
            _yourClub = st.yourClubExternalId;

            _view.SetBanner(st.seasonComplete
                ? _loc.Tr("season.complete")
                : _loc.Tr("season.banner", st.roundsPlayed, st.totalRounds, st.membersReady, st.membersTotal));

            _view.SetReadyButton(
                _youAreReady ? _loc.Tr("season.cancel_ready") : _loc.Tr("season.ready"),
                enabled: !st.seasonComplete && !_busy);
            _view.SetAdvance(visible: _isCreator, enabled: _isCreator && !st.seasonComplete && !_busy);

            var standings = new List<StandingRowVm>(s.standings.Count);
            for (int i = 0; i < s.standings.Count; i++)
            {
                LeagueStandingDto row = s.standings[i];
                standings.Add(new StandingRowVm
                {
                    Pos = i + 1,
                    ClubName = row.clubName,
                    Played = row.played,
                    Won = row.won,
                    Drawn = row.drawn,
                    Lost = row.lost,
                    GoalDifference = row.goalDifference,
                    Points = row.points,
                    IsYours = _yourClub.HasValue && row.clubExternalId == _yourClub.Value,
                });
            }
            _view.SetStandings(standings);

            var groups = new List<FixtureGroupVm>();
            int currentRound = -1;
            List<SeasonFixtureRowVm> rows = null;
            foreach (LeagueFixtureDto f in s.fixtures)
            {
                if (f.round != currentRound)
                {
                    currentRound = f.round;
                    rows = new List<SeasonFixtureRowVm>();
                    groups.Add(new FixtureGroupVm { RoundLabel = _loc.Tr("season.round", f.round), Rows = rows });
                }
                rows.Add(new SeasonFixtureRowVm
                {
                    FixtureId = f.id,
                    HomeName = f.homeClubName,
                    AwayName = f.awayClubName,
                    Played = f.played,
                    HomeGoals = f.homeGoals,
                    AwayGoals = f.awayGoals,
                    IsYours = _yourClub.HasValue
                              && (f.homeClubExternalId == _yourClub.Value || f.awayClubExternalId == _yourClub.Value),
                });
            }
            _view.SetFixtures(groups);

            LoadStateHashAsync().Forget();
        }

        // --- state hash (8.4b) -------------------------------------------------------------------

        private void OnVerifyState() => LoadStateHashAsync().Forget();

        private async UniTaskVoid LoadStateHashAsync()
        {
            if (string.IsNullOrEmpty(_leagueId)) return;

            var result = await _leagues.GetStateHashAsync(_leagueId);
            _view.SetStateHash(result.Success
                ? _loc.Tr("season.state_hash_value",
                    result.Value.hashHex, result.Value.playerCount, result.Value.roundsPlayed)
                : _loc.Tr("season.state_hash_unavailable"));
        }

        // --- actions -----------------------------------------------------------------------------

        private void OnReadyToggle() => ReadyAsync(!_youAreReady).Forget();

        private async UniTaskVoid ReadyAsync(bool ready)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);

            var result = await _leagues.SetReadyAsync(_leagueId, ready);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success) { _view.ClearStatus(); RenderSeason(result.Value); }
            else _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
        }

        private void OnAdvance() => AdvanceAsync().Forget();

        private async UniTaskVoid AdvanceAsync()
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("season.resolving"), isError: false);

            var result = await _leagues.AdvanceAsync(_leagueId);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success) { _view.ClearStatus(); RenderSeason(result.Value); }
            else _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
        }

        private void OnFixture(string fixtureId)
        {
            if (_lastSeason == null || string.IsNullOrEmpty(fixtureId)) return;

            LeagueFixtureDto f = _lastSeason.fixtures.Find(x => x.id == fixtureId);
            if (f == null || !f.played) return;

            _replayTarget.Set(_leagueId, fixtureId, f.homeClubName, f.awayClubName);
            _navigator.Push<OnlineReplayScreenPresenter>();
        }

        private void OnRefresh() => LoadSeasonAsync().Forget();

        private void OnBack() => _navigator.Pop();
    }
}
