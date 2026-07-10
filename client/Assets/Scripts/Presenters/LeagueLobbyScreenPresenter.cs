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
    /// League lobby (task 8.1b): shows the invite code to share, the members, and the generated squads
    /// (read-only browse — clubs are assigned at the 8.2 draft), plus a Leave button. Uses the detail
    /// preloaded by create/join when present, otherwise fetches it by id.
    /// </summary>
    public sealed class LeagueLobbyScreenPresenter : IScreenPresenter
    {
        private static readonly string[] RoleKeys =
        {
            "role.goalkeeper", "role.centreback", "role.fullback", "role.defensivemidfielder",
            "role.centralmidfielder", "role.attackingmidfielder", "role.winger", "role.striker",
        };

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly LeagueLobbyView _view;

        private bool _busy;

        public VisualElement View => _view.Root;

        public LeagueLobbyScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new LeagueLobbyView(loc.Tr);
        }

        public void Enter()
        {
            _view.LeaveClicked += OnLeave;
            _view.BackClicked += OnBack;
            LoadAsync(_selection.TakePreloaded()).Forget();
        }

        public void Exit()
        {
            _view.LeaveClicked -= OnLeave;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => LoadAsync(null).Forget();

        private async UniTaskVoid LoadAsync(LeagueDetailDto preloaded)
        {
            LeagueDetailDto detail = preloaded;
            if (detail == null)
            {
                if (string.IsNullOrEmpty(_selection.LeagueId))
                {
                    _view.ShowStatus(_loc.Tr("leagues.error.not_found"), isError: true);
                    return;
                }

                _view.SetBusy(true);
                _view.ShowStatus(_loc.Tr("leagues.loading"), isError: false);
                var result = await _leagues.GetAsync(_selection.LeagueId);
                _view.SetBusy(false);

                if (!result.Success)
                {
                    _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
                    return;
                }
                detail = result.Value;
            }

            _view.ClearStatus();
            Render(detail);
        }

        private void Render(LeagueDetailDto detail)
        {
            _view.SetHeader(detail.league.name);
            _view.SetInviteCode(detail.league.inviteCode);

            var members = new List<string>(detail.members.Count);
            foreach (var m in detail.members)
            {
                var club = string.IsNullOrEmpty(m.clubName) ? _loc.Tr("lobby.waiting_club") : m.clubName;
                members.Add(m.isCreator
                    ? _loc.Tr("lobby.member_creator", m.displayName, club)
                    : _loc.Tr("lobby.member", m.displayName, club));
            }
            _view.SetMembers(members);

            var clubs = new List<LeagueLobbyView.ClubVm>(detail.clubs.Count);
            foreach (var c in detail.clubs)
            {
                var players = new List<string>(c.players.Count);
                foreach (var p in c.players)
                    players.Add(_loc.Tr("lobby.player_row", p.name, _loc.Tr(RoleKey(p.role)), p.age, p.overall));
                clubs.Add(new LeagueLobbyView.ClubVm(
                    _loc.Tr("lobby.club_row", c.name, c.shortName, c.strength), players));
            }
            _view.SetClubs(clubs);
        }

        private static string RoleKey(int role) =>
            role >= 0 && role < RoleKeys.Length ? RoleKeys[role] : "role.centralmidfielder";

        private void OnBack() => _navigator.Pop();

        private void OnLeave() => LeaveAsync().Forget();

        private async UniTaskVoid LeaveAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("lobby.leaving"), isError: false);

            var result = await _leagues.LeaveAsync(_selection.LeagueId);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
                _navigator.Pop(); // back to the list, which reloads on Reveal
            else
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
        }
    }
}
