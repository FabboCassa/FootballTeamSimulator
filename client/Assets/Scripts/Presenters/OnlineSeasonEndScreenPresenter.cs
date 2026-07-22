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
    /// Private-league season-end screen (task 8.7b): the final table plus the awards the server computed
    /// (champion, top scorer aggregated from the stored replays, best defence, wooden spoon). Once the
    /// season is complete the league creator also gets "new season" — a full server-side reset that
    /// re-equalises the squads and reopens the draft, after which we drop back to the lobby where the
    /// draft card lives. Read-only otherwise. Lives in the App scope (online leagues are not a career
    /// Game scope), so <c>Push&lt;T&gt;</c> resolves it from the App singletons.
    /// </summary>
    public sealed class OnlineSeasonEndScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly OnlineSeasonEndView _view;

        private string _leagueId;
        private bool _isCreator;
        private bool _busy;
        private int? _yourClub;

        public VisualElement View => _view.Root;

        public OnlineSeasonEndScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new OnlineSeasonEndView(loc.Tr);
        }

        public void Enter()
        {
            _view.NewSeasonClicked += OnNewSeason;
            _view.BackClicked += OnBack;

            _leagueId = _selection.LeagueId;
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.NewSeasonClicked -= OnNewSeason;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync().Forget();

        // --- loading -----------------------------------------------------------------------------

        private async UniTaskVoid LoadAsync()
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

            // Your club (to highlight your row in the final table) comes from your membership.
            _yourClub = null;
            foreach (LeagueMemberDto m in detail.Value.members)
            {
                if (m.userId == _leagues.CurrentUserId && m.clubExternalId.HasValue)
                {
                    _yourClub = m.clubExternalId.Value;
                    break;
                }
            }

            var summary = await _leagues.GetSeasonSummaryAsync(_leagueId);
            _view.SetBusy(false);

            if (!summary.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(summary.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            Render(summary.Value);
        }

        // --- render ------------------------------------------------------------------------------

        private void Render(SeasonSummaryDto s)
        {
            _view.SetBanner(s.seasonComplete
                ? _loc.Tr("seasonend.complete")
                : _loc.Tr("seasonend.provisional"));
            _view.SetTotals(_loc.Tr("seasonend.totals", s.matchesPlayed, s.totalGoals));

            var awards = new List<AwardRowVm>(4);
            if (s.champion != null)
            {
                awards.Add(new AwardRowVm
                {
                    Label = _loc.Tr("seasonend.champion"),
                    Winner = s.champion.clubName,
                    Detail = _loc.Tr("seasonend.points", s.champion.value),
                    Highlight = true,
                });
            }
            if (s.topScorer != null)
            {
                awards.Add(new AwardRowVm
                {
                    Label = _loc.Tr("seasonend.top_scorer"),
                    Winner = _loc.Tr("seasonend.scorer_name", s.topScorer.playerName, s.topScorer.clubName),
                    Detail = _loc.Tr("seasonend.goals", s.topScorer.goals),
                });
            }
            if (s.bestDefence != null)
            {
                awards.Add(new AwardRowVm
                {
                    Label = _loc.Tr("seasonend.best_defence"),
                    Winner = s.bestDefence.clubName,
                    Detail = _loc.Tr("seasonend.conceded", s.bestDefence.value),
                });
            }
            if (s.woodenSpoon != null)
            {
                awards.Add(new AwardRowVm
                {
                    Label = _loc.Tr("seasonend.wooden_spoon"),
                    Winner = s.woodenSpoon.clubName,
                    Detail = _loc.Tr("seasonend.points", s.woodenSpoon.value),
                });
            }
            _view.SetAwards(awards);

            var standings = new List<StandingRowVm>(s.finalStandings.Count);
            for (int i = 0; i < s.finalStandings.Count; i++)
            {
                LeagueStandingDto row = s.finalStandings[i];
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

            // Only the creator can start the rematch, and only once the season has actually finished.
            _view.SetNewSeason(visible: _isCreator && s.seasonComplete, enabled: !_busy);
        }

        // --- actions -----------------------------------------------------------------------------

        private void OnNewSeason() => NewSeasonAsync().Forget();

        private async UniTaskVoid NewSeasonAsync()
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("seasonend.status.starting"), isError: false);

            var result = await _leagues.StartNewSeasonAsync(_leagueId);

            _busy = false;
            _view.SetBusy(false);

            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            // The league is back in the draft: this summary and the season screen underneath are both
            // stale, so drop to the lobby (two levels down) where the draft card takes over.
            _navigator.Pop(); // this screen → the season screen
            _navigator.Pop(); // the season screen → the lobby
        }

        private void OnBack() => _navigator.Pop();
    }
}
