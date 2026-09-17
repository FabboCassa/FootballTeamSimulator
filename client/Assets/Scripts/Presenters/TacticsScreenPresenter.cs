using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Tactics screen (task 3.3): pick a formation + instruction set, see how
    /// familiar the club is with the resulting tactic, and check the next
    /// opponent. Works on a copy of the saved tactic; Save persists it (the sim
    /// uses it from the next match day). Changing the formation re-picks the XI
    /// for the new shape, so the Squad screen and the in-match shape stay in sync.
    /// </summary>
    public sealed class TacticsScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly OverlayHost _overlay;
        private readonly ClubIdentityService _identity;
        private readonly TacticsView _view;
        private readonly TacticsBalance _tacticsConfig = new BalanceConfig().Tactics;

        private Club _club;
        private TacticPlan _working;

        public VisualElement View => _view.Root;

        public TacticsScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc,
            OverlayHost overlay,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _overlay = overlay;
            _identity = identity;
            _view = new TacticsView(loc.Tr);
        }

        public void Enter()
        {
            _view.FormationCycleClicked += OnFormation;
            _view.MentalityCycleClicked += OnMentality;
            _view.PressingCycleClicked += OnPressing;
            _view.TempoCycleClicked += OnTempo;
            _view.WidthCycleClicked += OnWidth;
            _view.FormationSelected += OnFormationSelected;
            _view.InstructionSelected += OnInstructionSelected;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

            var formationLabels = new List<string>(Formations.All.Length);
            for (int i = 0; i < Formations.All.Length; i++)
                formationLabels.Add(FormationName((Formation)i));
            _view.SetFormationOptions(formationLabels);

            _club = _career.GetUserClub();
            _working = Clone(_career.UserTactic) ?? TacticPlan.Neutral();
            _view.SetHeader(_loc.Tr("tactics.header", _club.Name));
            _view.SetStatus(string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.FormationCycleClicked -= OnFormation;
            _view.MentalityCycleClicked -= OnMentality;
            _view.PressingCycleClicked -= OnPressing;
            _view.TempoCycleClicked -= OnTempo;
            _view.WidthCycleClicked -= OnWidth;
            _view.FormationSelected -= OnFormationSelected;
            _view.InstructionSelected -= OnInstructionSelected;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        private void OnFormationSelected(int index)
        {
            if (index < 0 || index >= Formations.All.Length) return;
            _working.Formation = (Formation)index;
            Refresh();
        }

        private void OnInstructionSelected(int axis, int value)
        {
            if (value < 0 || value > 2) return;
            switch (axis)
            {
                case 0: _working.Mentality = (Mentality)value; break;
                case 1: _working.Pressing = (Pressing)value; break;
                case 2: _working.Tempo = (Tempo)value; break;
                case 3: _working.Width = (Width)value; break;
                default: return;
            }
            Refresh();
        }

        private void OnFormation()
        {
            _working.Formation = (Formation)(((int)_working.Formation + 1) % Formations.All.Length);
            Refresh();
        }

        private void OnMentality()
        {
            _working.Mentality = (Mentality)(((int)_working.Mentality + 1) % 3);
            Refresh();
        }

        private void OnPressing()
        {
            _working.Pressing = (Pressing)(((int)_working.Pressing + 1) % 3);
            Refresh();
        }

        private void OnTempo()
        {
            _working.Tempo = (Tempo)(((int)_working.Tempo + 1) % 3);
            Refresh();
        }

        private void OnWidth()
        {
            _working.Width = (Width)(((int)_working.Width + 1) % 3);
            Refresh();
        }

        private void OnSave()
        {
            Formation previous = _career.UserTactic?.Formation ?? Formation.F433;
            bool formationChanged = _working.Formation != previous;

            _career.UserTactic = Clone(_working);

            if (formationChanged)
            {
                // The role layout changed: re-pick the best eleven for the new
                // shape so the saved lineup and the in-match shape match the tactic.
                _career.UserLineup = LineupPlan.From(LineupSelector.BestEleven(_club, _working.Formation));
            }

            _saveRepository.Save(_career);
            _view.SetStatus(_loc.Tr(formationChanged
                ? "tactics.status.formation_changed"
                : "tactics.status.saved"));
            Dialogs.Toast(_overlay, _loc, "common.saved");
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            _view.SetSelection((int)_working.Formation, (int)_working.Mentality, (int)_working.Pressing,
                (int)_working.Tempo, (int)_working.Width);
            int familiarity = FamiliarityPercent(_working);
            _view.SetFamiliarity(_loc.Tr("tactics.familiarity", familiarity), familiarity);

            // Live shape preview: our best XI in the chosen formation (task 6.7).
            Lineup xi = LineupSelector.BestEleven(_club, _working.Formation);
            _view.SetShape(BuildShapeTokens(xi, _identity.UserVisual()));
        }

        private int FamiliarityPercent(TacticPlan tactic)
        {
            int level = _career.TacticFamiliarity.TryGetValue(tactic.Key(), out int v) ? v : 0;
            int max = _tacticsConfig.FamiliarityMax > 0 ? _tacticsConfig.FamiliarityMax : 1;
            return level * 100 / max;
        }

        /// <summary>Read-only pitch tokens for a resolved XI: role/OVR discs in the club's colours.</summary>
        private List<PitchTokenVm> BuildShapeTokens(Lineup lineup, ClubVisual visual)
        {
            var roles = new List<PositionRole>(lineup.Slots.Count);
            foreach (LineupSlot s in lineup.Slots)
                roles.Add(s.Role);

            var tokens = new List<PitchTokenVm>(lineup.Slots.Count);
            for (int i = 0; i < lineup.Slots.Count; i++)
            {
                LineupSlot slot = lineup.Slots[i];
                (float x, float y) = FormationLayout.Normalized(roles, i);
                tokens.Add(new PitchTokenVm
                {
                    SlotIndex = -1,
                    PlayerId = -1,
                    X = x,
                    Y = y,
                    Badge = PlayerRating.OverallFor(slot.Player, slot.Role).ToString(),
                    Name = LastName(slot.Player.FullName),
                    Fill = visual.Primary,
                    Text = visual.Emblem,
                    Fitness = -1
                });
            }

            return tokens;
        }

        private static string LastName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return string.Empty;
            int space = fullName.LastIndexOf(' ');
            return space >= 0 && space < fullName.Length - 1 ? fullName.Substring(space + 1) : fullName;
        }

        private string FormationName(Formation formation) =>
            _loc.Tr("tactics.formation." + formation.ToString().ToLowerInvariant());

        private static TacticPlan Clone(TacticPlan plan)
        {
            if (plan == null)
                return null;

            return new TacticPlan
            {
                Formation = plan.Formation,
                Mentality = plan.Mentality,
                Pressing = plan.Pressing,
                Tempo = plan.Tempo,
                Width = plan.Width
            };
        }
    }
}
