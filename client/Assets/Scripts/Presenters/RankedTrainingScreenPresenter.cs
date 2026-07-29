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
    /// Ranked training screen (Phase 9.4b): the same team-focus + per-player individual-focus editor as the
    /// single-player Training screen (4.3) and the private-league one (8.4b), but for the caller's ladder
    /// club. It reads the caller's ladder state for their club, that club's squad, and the plan already
    /// stored server-side (the server seeds a balanced one when the season starts, so this screen always
    /// opens on a real plan), then Save POSTs it to /ranked/training. The server applies it every resolved
    /// matchday in its development week; a club without a plan trains the AI default.
    ///
    /// App scope, resolved via <c>Push&lt;T&gt;</c> (every dependency is already an App singleton); reuses
    /// the dumb <see cref="TrainingView"/> so there is no new UI to maintain.
    /// </summary>
    public sealed class RankedTrainingScreenPresenter : IScreenPresenter
    {
        private const int TeamFocusCount = 6;        // TeamTrainingFocus members
        private const int IndividualFocusCount = 5;  // IndividualTrainingFocus members

        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly TrainingView _view;

        private List<RankedPlayerDto> _squad;
        private TrainingPlan _working = TrainingPlan.Balanced();
        private bool _busy;
        private bool _loaded;

        public VisualElement View => _view.Root;

        public RankedTrainingScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new TrainingView(loc.Tr);
        }

        public void Enter()
        {
            _view.TeamFocusCycleClicked += OnTeamFocus;
            _view.IndividualFocusCycleClicked += OnIndividualFocus;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

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
            _view.SetStatus(_loc.Tr("ranked.loading"));

            var state = await _ranked.GetMineAsync();
            if (!state.Success)
            {
                _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(state.Error)));
                return;
            }
            if (state.Value == null || !state.Value.enrolled || state.Value.clubExternalId == null)
            {
                _view.SetStatus(_loc.Tr("ranked.not_enrolled"));
                return;
            }

            var squad = await _ranked.GetClubSquadAsync(state.Value.clubExternalId.Value);
            if (!squad.Success)
            {
                _view.SetStatus(_loc.Tr(RankedErrorFormat.Key(squad.Error)));
                return;
            }

            // Open on the plan the server already holds (seeded balanced at season start) rather than on a
            // fresh default, so re-opening the screen shows what the club is actually training.
            var stored = await _ranked.GetMyTrainingAsync();
            if (stored.Success && stored.Value != null) _working = stored.Value;
            if (_working.IndividualFocuses == null) _working = TrainingPlan.Balanced();

            _squad = squad.Value.players;
            _view.SetHeader(_loc.Tr("training.header", squad.Value.clubName));
            _view.SetStatus(string.Empty);
            _loaded = true;
            Refresh();
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
            foreach (RankedPlayerDto p in _squad)
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
            if (_busy || !_loaded) return;
            _busy = true;
            _view.SetStatus(_loc.Tr("training.status.saving"));

            var result = await _ranked.SubmitTrainingAsync(BuildBody());

            _busy = false;
            _view.SetStatus(result.Success
                ? _loc.Tr("training.status.saved")
                : _loc.Tr(RankedErrorFormat.Key(result.Error)));
        }

        /// <summary>Builds the wire body from the working plan. Enums go over as their integer values (the
        /// server binds the plan with the default numeric-enum JSON) and the individual-focus map is keyed by
        /// the stringified player external id — the same shape the private-league screen posts.</summary>
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
