using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Development;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Training screen (task 4.3): pick the squad-wide team focus and, per player, an
    /// individual focus. Works on a copy of the saved plan; Save persists it and the
    /// weekly world-development tick (LocalClock) applies it from the next training week.
    /// The user shapes only his own club — every other club trains the balanced default.
    /// </summary>
    public sealed class TrainingScreenPresenter : IScreenPresenter
    {
        private const int TeamFocusCount = 6;        // TeamTrainingFocus members
        private const int IndividualFocusCount = 5;  // IndividualTrainingFocus members

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly TrainingView _view;

        private Club _club;
        private TrainingPlan _working;

        public VisualElement View => _view.Root;

        public TrainingScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _view = new TrainingView(loc.Tr);
        }

        public void Enter()
        {
            _view.TeamFocusCycleClicked += OnTeamFocus;
            _view.IndividualFocusCycleClicked += OnIndividualFocus;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

            _club = _career.GetUserClub();
            _working = Clone(_career.UserTraining) ?? TrainingPlan.Balanced();
            _view.SetHeader(_loc.Tr("training.header", _club.Name));
            _view.SetStatus(string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.TeamFocusCycleClicked -= OnTeamFocus;
            _view.IndividualFocusCycleClicked -= OnIndividualFocus;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        private void OnTeamFocus()
        {
            _working.TeamFocus = (TeamTrainingFocus)(((int)_working.TeamFocus + 1) % TeamFocusCount);
            Refresh();
        }

        private void OnIndividualFocus(int playerId)
        {
            IndividualTrainingFocus next =
                (IndividualTrainingFocus)(((int)_working.FocusFor(playerId) + 1) % IndividualFocusCount);

            if (next == IndividualTrainingFocus.None)
                _working.IndividualFocuses.Remove(playerId);
            else
                _working.IndividualFocuses[playerId] = next;

            Refresh();
        }

        private void OnSave()
        {
            _career.UserTraining = Clone(_working);
            _saveRepository.Save(_career);
            _view.SetStatus(_loc.Tr("training.status.saved"));
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            _view.SetTeamFocus(_loc.Tr("training.team_focus", TeamFocusName(_working.TeamFocus)));
            _view.SetTeamHint(_loc.Tr("training.team_desc." + _working.TeamFocus.ToString().ToLowerInvariant()));

            var rows = new List<TrainingRowVm>();
            foreach (Player p in _club.Squad.Players)
            {
                rows.Add(new TrainingRowVm
                {
                    PlayerId = p.Id,
                    Label = $"{p.FullName} ({RoleName(p.Role)})",
                    FocusLabel = IndividualFocusName(_working.FocusFor(p.Id))
                });
            }

            _view.SetRoster(rows);
        }

        private string TeamFocusName(TeamTrainingFocus focus) =>
            _loc.Tr("training.team." + focus.ToString().ToLowerInvariant());

        private string IndividualFocusName(IndividualTrainingFocus focus) =>
            _loc.Tr("training.individual." + focus.ToString().ToLowerInvariant());

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private static TrainingPlan Clone(TrainingPlan plan)
        {
            if (plan == null)
                return null;

            return new TrainingPlan
            {
                TeamFocus = plan.TeamFocus,
                IndividualFocuses = plan.IndividualFocuses != null
                    ? new Dictionary<int, IndividualTrainingFocus>(plan.IndividualFocuses)
                    : new Dictionary<int, IndividualTrainingFocus>()
            };
        }
    }
}
