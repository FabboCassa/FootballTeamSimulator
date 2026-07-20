using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Sim.Core.Development;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Online training screen (task 8.4b): the same team-focus + per-player individual-focus editor as
    /// the single-player Training screen (4.3), but for the user's club in a private online league. It
    /// reads the league detail to find the caller's drafted club + squad, builds a Sim.Core
    /// <see cref="TrainingPlan"/>, and Save POSTs it to /leagues/{id}/training. The server then applies
    /// the plan authoritatively each resolved round (its OnlineSeasonTick development week). Lives in the
    /// App scope; reuses the dumb <see cref="TrainingView"/>. Every other club trains the AI default.
    /// </summary>
    public sealed class OnlineTrainingScreenPresenter : IScreenPresenter
    {
        private const int TeamFocusCount = 6;        // TeamTrainingFocus members
        private const int IndividualFocusCount = 5;  // IndividualTrainingFocus members

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly TrainingView _view;

        private string _leagueId;
        private List<LeaguePlayerDto> _squad;
        private TrainingPlan _working = TrainingPlan.Balanced();
        private bool _busy;
        private bool _loaded;

        public VisualElement View => _view.Root;

        public OnlineTrainingScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new TrainingView(loc.Tr);
        }

        public void Enter()
        {
            _view.TeamFocusCycleClicked += OnTeamFocus;
            _view.IndividualFocusCycleClicked += OnIndividualFocus;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

            _leagueId = _selection.LeagueId;
            _view.SetHeader(_loc.Tr("training.title"));
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.TeamFocusCycleClicked -= OnTeamFocus;
            _view.IndividualFocusCycleClicked -= OnIndividualFocus;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        // --- loading -----------------------------------------------------------------------------

        private async UniTaskVoid LoadAsync()
        {
            if (string.IsNullOrEmpty(_leagueId))
            {
                _view.SetStatus(_loc.Tr("leagues.error.not_found"));
                return;
            }

            _view.SetStatus(_loc.Tr("leagues.loading"));
            var detail = await _leagues.GetAsync(_leagueId);
            if (!detail.Success)
            {
                _view.SetStatus(_loc.Tr(LeagueErrorFormat.Key(detail.Error)));
                return;
            }

            LeagueClubDto club = FindMyClub(detail.Value);
            if (club == null)
            {
                _view.SetStatus(_loc.Tr("leagues.error.not_assigned_club"));
                return;
            }

            _squad = club.players;
            _view.SetHeader(_loc.Tr("training.header", club.name));
            _view.SetStatus(string.Empty);
            _loaded = true;
            Refresh();
        }

        /// <summary>The caller's drafted club in this league (member.clubExternalId → the matching club),
        /// or null if he has no club yet.</summary>
        private LeagueClubDto FindMyClub(LeagueDetailDto detail)
        {
            string myId = _leagues.CurrentUserId;
            int? clubExternalId = null;
            foreach (LeagueMemberDto m in detail.members)
                if (m.userId == myId) { clubExternalId = m.clubExternalId; break; }

            if (!clubExternalId.HasValue) return null;
            foreach (LeagueClubDto c in detail.clubs)
                if (c.externalId == clubExternalId.Value) return c;
            return null;
        }

        // --- editing -----------------------------------------------------------------------------

        private void OnTeamFocus()
        {
            if (!_loaded) return;
            _working.TeamFocus = (TeamTrainingFocus)(((int)_working.TeamFocus + 1) % TeamFocusCount);
            Refresh();
        }

        private void OnIndividualFocus(int playerId)
        {
            if (!_loaded) return;
            IndividualTrainingFocus next =
                (IndividualTrainingFocus)(((int)_working.FocusFor(playerId) + 1) % IndividualFocusCount);

            if (next == IndividualTrainingFocus.None)
                _working.IndividualFocuses.Remove(playerId);
            else
                _working.IndividualFocuses[playerId] = next;

            Refresh();
        }

        private void Refresh()
        {
            _view.SetTeamFocus(_loc.Tr("training.team_focus", TeamFocusName(_working.TeamFocus)));
            _view.SetTeamHint(_loc.Tr("training.team_desc." + _working.TeamFocus.ToString().ToLowerInvariant()));

            var rows = new List<TrainingRowVm>(_squad.Count);
            foreach (LeaguePlayerDto p in _squad)
            {
                rows.Add(new TrainingRowVm
                {
                    PlayerId = p.externalId,
                    Label = $"{p.name} ({RoleName(p.role)})",
                    FocusLabel = IndividualFocusName(_working.FocusFor(p.externalId)),
                });
            }
            _view.SetRoster(rows);
        }

        // --- save --------------------------------------------------------------------------------

        private void OnSave() => SaveAsync().Forget();

        private async UniTaskVoid SaveAsync()
        {
            if (_busy || !_loaded || string.IsNullOrEmpty(_leagueId)) return;
            _busy = true;
            _view.SetStatus(_loc.Tr("training.status.saving"));

            var result = await _leagues.SubmitTrainingAsync(_leagueId, BuildBody());

            _busy = false;
            _view.SetStatus(result.Success
                ? _loc.Tr("training.status.saved")
                : _loc.Tr(LeagueErrorFormat.Key(result.Error)));
        }

        /// <summary>Builds the wire body from the working plan. Enums go over as their integer values
        /// (the server binds the plan with the default numeric-enum JSON) and the individual-focus map
        /// is keyed by the stringified player external id.</summary>
        private SubmitTrainingBody BuildBody()
        {
            var focuses = new Dictionary<string, int>();
            if (_working.IndividualFocuses != null)
                foreach (KeyValuePair<int, IndividualTrainingFocus> kv in _working.IndividualFocuses)
                    focuses[kv.Key.ToString()] = (int)kv.Value;

            return new SubmitTrainingBody
            {
                training = new TrainingPlanBody
                {
                    teamFocus = (int)_working.TeamFocus,
                    individualFocuses = focuses,
                },
            };
        }

        private void OnBack() => _navigator.Pop();

        // --- labels ------------------------------------------------------------------------------

        private string TeamFocusName(TeamTrainingFocus focus) =>
            _loc.Tr("training.team." + focus.ToString().ToLowerInvariant());

        private string IndividualFocusName(IndividualTrainingFocus focus) =>
            _loc.Tr("training.individual." + focus.ToString().ToLowerInvariant());

        private string RoleName(int role) =>
            _loc.Tr("role." + ((PositionRole)role).ToString().ToLowerInvariant());
    }
}
