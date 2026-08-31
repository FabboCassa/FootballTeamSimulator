using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// "Online Leagues" list (task 8.1b): the account's private leagues, a Create button, and an
    /// inline join-by-code. Reachable from the Main Menu only when signed in. Talks to
    /// <see cref="LeagueApiService"/>; maps outcomes to localized status lines.
    /// </summary>
    public sealed class LeagueListScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly LeagueListView _view;

        private bool _busy;

        public VisualElement View => _view.Root;

        public LeagueListScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new LeagueListView(loc.Tr);
        }

        public void Enter()
        {
            _view.CreateClicked += OnCreate;
            _view.JoinClicked += OnJoin;
            _view.BackClicked += OnBack;
            _view.LeagueSelected += OnLeagueSelected;
            _view.CreateTestLeagueClicked += OnCreateTestLeague;
            _view.SetDevToolsVisible(DevFlags.OnlineTestTools);
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.CreateClicked -= OnCreate;
            _view.JoinClicked -= OnJoin;
            _view.BackClicked -= OnBack;
            _view.LeagueSelected -= OnLeagueSelected;
            _view.CreateTestLeagueClicked -= OnCreateTestLeague;
        }

        public void Reveal() => LoadAsync().Forget();

        private async UniTaskVoid LoadAsync()
        {
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("leagues.loading"), isError: false);

            var result = await _leagues.ListMineAsync();

            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.SetLeagues(null);
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            var rows = new List<LeagueListView.LeagueRow>(result.Value.Count);
            foreach (var l in result.Value)
            {
                // Phase 12.1: the count of transfer negotiations waiting on this coach. It rides the league
                // summary precisely so it can be seen from here, without opening anything.
                string badge = l.offersAwaitingYou > 0
                    ? _loc.Tr("leagues.offers_badge", l.offersAwaitingYou)
                    : null;
                rows.Add(new LeagueListView.LeagueRow(l.id, FormatRow(l), badge));
            }
            _view.SetLeagues(rows);
        }

        private string FormatRow(LeagueSummaryDto l) => _loc.Tr(
            "leagues.row", l.name, l.memberCount, l.size, _loc.Tr(StatusKey(l.status)));

        private static string StatusKey(int status) => status switch
        {
            (int)LeagueStatus.Active => "leagues.status.active",
            (int)LeagueStatus.Completed => "leagues.status.completed",
            _ => "leagues.status.forming",
        };

        private void OnCreate() => _navigator.Push<CreateLeagueScreenPresenter>();

        // Dev-only: one call seeds a ready test league (you as creator + bots, drafted to Active) and
        // drops you into its lobby — so online features can be tested without hand-creating accounts.
        private void OnCreateTestLeague() => SeedTestLeagueAsync().Forget();

        private async UniTaskVoid SeedTestLeagueAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("leagues.dev_seeding"), isError: false);

            var result = await _leagues.SeedTestLeagueAsync(size: 4, bots: 4);

            _busy = false;
            _view.SetBusy(false);
            if (!result.Success)
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
                return;
            }

            _selection.Select(result.Value.leagueId);
            _navigator.Push<LeagueLobbyScreenPresenter>();
        }

        private void OnLeagueSelected(string id)
        {
            _selection.Select(id);
            _navigator.Push<LeagueLobbyScreenPresenter>();
        }

        private void OnJoin() => JoinAsync().Forget();

        private async UniTaskVoid JoinAsync()
        {
            if (_busy) return;
            var code = _view.JoinCode;
            if (string.IsNullOrEmpty(code))
            {
                _view.ShowStatus(_loc.Tr("leagues.error.validation"), isError: true);
                return;
            }

            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("leagues.joining"), isError: false);

            var result = await _leagues.JoinAsync(code);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                _selection.Select(result.Value);
                _navigator.Push<LeagueLobbyScreenPresenter>();
            }
            else
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
            }
        }

        private void OnBack() => _navigator.Pop();
    }
}
