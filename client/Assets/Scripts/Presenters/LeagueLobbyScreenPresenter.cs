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
        private string _leagueId;

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
            _view.StartDraftClicked += OnStartDraft;
            _view.RefreshClicked += OnRefresh;
            _view.PickClicked += OnPick;
            LoadAsync(_selection.TakePreloaded()).Forget();
        }

        public void Exit()
        {
            _view.LeaveClicked -= OnLeave;
            _view.BackClicked -= OnBack;
            _view.StartDraftClicked -= OnStartDraft;
            _view.RefreshClicked -= OnRefresh;
            _view.PickClicked -= OnPick;
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
            _leagueId = detail.league.id;
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

            RenderDraft(detail);

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

        /// <summary>Draws the draft card based on the league phase: a "Start draft" button for the owner
        /// while forming, a live turn banner + a pick list on your turn while drafting, hidden once the
        /// season is active (8.2b).</summary>
        private void RenderDraft(LeagueDetailDto detail)
        {
            var status = (LeagueStatus)detail.league.status;

            if (status == LeagueStatus.Forming)
            {
                _view.SetDraftVisible(true);
                _view.SetDraftBanner(string.Empty, false);
                _view.SetPickList(null);
                _view.SetRefreshVisible(true);

                bool enoughMembers = detail.members.Count >= 2;
                if (detail.league.isCreator)
                {
                    _view.SetStartButton(visible: true, enabled: enoughMembers && !_busy);
                    _view.SetStartHint(_loc.Tr("lobby.start_hint"), visible: !enoughMembers);
                }
                else
                {
                    _view.SetStartButton(false, false);
                    _view.SetStartHint(_loc.Tr("lobby.waiting_owner"), true);
                }
                return;
            }

            if (status == LeagueStatus.Drafting)
            {
                _view.SetDraftVisible(true);
                _view.SetStartButton(false, false);
                _view.SetStartHint(string.Empty, false);
                _view.SetRefreshVisible(true);

                var draft = detail.draft;
                int made = draft?.picksMade ?? 0;
                int total = draft?.totalPicks ?? detail.members.Count;
                string progress = _loc.Tr("lobby.draft_progress", made, total);

                bool myTurn = draft != null && !string.IsNullOrEmpty(draft.currentPickUserId)
                              && draft.currentPickUserId == _leagues.CurrentUserId;

                if (myTurn)
                {
                    _view.SetDraftBanner(_loc.Tr("lobby.draft_your_turn") + "  " + progress, true);
                    _view.SetPickList(BuildPickList(detail));
                }
                else
                {
                    string who = MemberName(detail, draft?.currentPickUserId);
                    _view.SetDraftBanner(_loc.Tr("lobby.draft_turn", who) + "  " + progress, true);
                    _view.SetPickList(null);
                }
                return;
            }

            // Active / Completed — the season has started; the draft card is no longer needed.
            _view.SetDraftVisible(false);
        }

        private List<LeagueLobbyView.PickVm> BuildPickList(LeagueDetailDto detail)
        {
            var taken = new HashSet<int>();
            foreach (var m in detail.members)
                if (m.clubExternalId.HasValue) taken.Add(m.clubExternalId.Value);

            var picks = new List<LeagueLobbyView.PickVm>();
            foreach (var c in detail.clubs)
            {
                if (taken.Contains(c.externalId)) continue;
                var label = _loc.Tr("lobby.pick_row", c.name, c.strength, MoneyFormat.Short(c.transferBudget));
                picks.Add(new LeagueLobbyView.PickVm(c.externalId, label));
            }
            return picks;
        }

        private static string MemberName(LeagueDetailDto detail, string userId)
        {
            if (!string.IsNullOrEmpty(userId))
                foreach (var m in detail.members)
                    if (m.userId == userId) return m.displayName;
            return "…";
        }

        private void OnStartDraft() => StartDraftAsync().Forget();

        private async UniTaskVoid StartDraftAsync()
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("lobby.starting_draft"), isError: false);

            var result = await _leagues.StartDraftAsync(_leagueId);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                _view.ClearStatus();
                Render(result.Value);
            }
            else
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
            }
        }

        private void OnPick(int clubExternalId) => PickAsync(clubExternalId).Forget();

        private async UniTaskVoid PickAsync(int clubExternalId)
        {
            if (_busy || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("lobby.picking"), isError: false);

            var result = await _leagues.PickClubAsync(_leagueId, clubExternalId);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                _view.ClearStatus();
                Render(result.Value);
            }
            else
            {
                // A stale turn/club (someone else moved) → resync from the server, then show why.
                var fresh = await _leagues.GetAsync(_leagueId);
                if (fresh.Success) Render(fresh.Value);
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
            }
        }

        private void OnRefresh() => LoadAsync(null).Forget();

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
