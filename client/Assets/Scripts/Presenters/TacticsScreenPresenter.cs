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
    /// Tactics screen (task 3.3, R21): pick a style, a formation and any instruction tweak, see
    /// how familiar the club is with the resulting tactic. The edits are <see cref="TacticEditor"/>'s:
    /// a style loads its preset, a tweak keeps the style and marks it custom. Works on a copy of
    /// the saved tactic; Save persists it (the sim uses it from the next match day). Changing the
    /// formation re-picks the XI for the new shape, so the Squad screen and the in-match shape
    /// stay in sync.
    ///
    /// The match plan card (issue #44) edits the user's <see cref="PrematchPlan"/>: a rule
    /// "from minute M, if the score is S, shout X". Adding or removing a rule saves at once — it
    /// is kept apart from the tactic's Save so editing the plan never commits a tactic the coach
    /// did not pick.
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

        private static readonly int[] PlanMinutes = { 30, 45, 60, 70, 80 };
        private static readonly ScoreSituation[] PlanSituations =
        {
            ScoreSituation.Always, ScoreSituation.Losing, ScoreSituation.Drawing,
            ScoreSituation.Winning, ScoreSituation.NotWinning, ScoreSituation.NotLosing
        };

        private Club _club;
        private TacticPlan _working;
        private int _draftMinute = 2;    // index into PlanMinutes: 60'
        private int _draftSituation = 1; // losing
        private int _draftShout = 2;     // all forward

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
            _view.StyleSelected += OnStyleSelected;
            _view.SuggestedFormationSelected += OnSuggestedFormationSelected;
            _view.FormationSelected += OnFormationSelected;
            _view.InstructionSelected += OnInstructionSelected;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;
            _view.PlanOptionSelected += OnPlanOption;
            _view.PlanAddClicked += OnPlanAdd;
            _view.PlanRemoveClicked += OnPlanRemove;

            SetTacticOptions();
            SetPlanOptions();

            _club = _career.GetUserClub();
            _working = Clone(_career.UserTactic) ?? TacticPlan.Neutral();
            _view.SetHeader(_loc.Tr("tactics.header", _club.Name));
            _view.SetStatus(string.Empty);
            Refresh();
            RefreshPlan();
        }

        public void Exit()
        {
            _view.StyleSelected -= OnStyleSelected;
            _view.SuggestedFormationSelected -= OnSuggestedFormationSelected;
            _view.FormationSelected -= OnFormationSelected;
            _view.InstructionSelected -= OnInstructionSelected;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
            _view.PlanOptionSelected -= OnPlanOption;
            _view.PlanAddClicked -= OnPlanAdd;
            _view.PlanRemoveClicked -= OnPlanRemove;
        }

        private void OnStyleSelected(int index)
        {
            if (index < 0 || index >= PlayingStyles.All.Count) return;
            _working = TacticEditor.WithStyle(_working, PlayingStyles.All[index].Style);
            Refresh();
        }

        private void OnSuggestedFormationSelected(int index)
        {
            IReadOnlyList<Formation> suggested = PlayingStyles.Get(_working.Style).SuggestedFormations;
            if (index < 0 || index >= suggested.Count) return;
            _working = TacticEditor.WithFormation(_working, suggested[index]);
            Refresh();
        }

        private void OnFormationSelected(int index)
        {
            if (index < 0 || index >= Formations.Catalogue.Length) return;
            _working = TacticEditor.WithFormation(_working, Formations.Catalogue[index]);
            Refresh();
        }

        private void OnInstructionSelected(int axis, int level)
        {
            if (axis < 0 || axis >= TacticEditor.Axes.Count) return;
            InstructionAxis picked = TacticEditor.Axes[axis];
            if (level < 0 || level >= TacticEditor.LevelCount(picked)) return;
            _working = TacticEditor.WithInstruction(_working, picked, level);
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

        // ---------------------------------------------------------------- match plan

        private void OnPlanOption(int part, int index)
        {
            switch (part)
            {
                case 0 when index >= 0 && index < PlanMinutes.Length: _draftMinute = index; break;
                case 1 when index >= 0 && index < PlanSituations.Length: _draftSituation = index; break;
                case 2 when index >= 0 && index < ShoutBoard.Choices.Count: _draftShout = index; break;
                default: return;
            }
            RefreshPlan();
        }

        private void OnPlanAdd()
        {
            PrematchPlan plan = _career.UserPlan ?? PrematchPlan.Empty();
            PrematchRule rule = DraftRule();
            if (!plan.CanAdd(rule))
            {
                _view.SetStatus(_loc.Tr("tactics.plan.full", PrematchPlan.MaxRules));
                return;
            }

            SavePlan(plan.WithRule(rule));
        }

        private void OnPlanRemove(int index)
        {
            if (_career.UserPlan == null) return;
            SavePlan(_career.UserPlan.WithoutRule(index));
        }

        private void SavePlan(PrematchPlan plan)
        {
            _career.UserPlan = plan;
            _saveRepository.Save(_career);
            _view.SetStatus(string.Empty);
            Dialogs.Toast(_overlay, _loc, "common.saved");
            RefreshPlan();
        }

        private PrematchRule DraftRule() =>
            PrematchRule.ForShout(PlanMinutes[_draftMinute], PlanSituations[_draftSituation], ShoutBoard.Choices[_draftShout]);

        private void SetPlanOptions()
        {
            var minutes = new List<string>(PlanMinutes.Length);
            foreach (int m in PlanMinutes) minutes.Add(_loc.Tr("tactics.plan.minute", m));
            var situations = new List<string>(PlanSituations.Length);
            foreach (ScoreSituation w in PlanSituations) situations.Add(_loc.Tr(SituationKey(w)));
            var shouts = new List<string>(ShoutBoard.Choices.Count);
            foreach (TouchlineShout sh in ShoutBoard.Choices) shouts.Add(_loc.Tr(ShoutPicker.NameKey(sh)));
            _view.SetPlanOptions(minutes, situations, shouts);
        }

        private void RefreshPlan()
        {
            PrematchPlan plan = _career.UserPlan ?? PrematchPlan.Empty();
            var lines = new List<string>(plan.Rules.Count);
            foreach (PrematchRule rule in plan.Rules)
                lines.Add(_loc.Tr("tactics.plan.rule", rule.FromMinute, _loc.Tr(SituationKey(rule.When)), ActionText(rule)));
            _view.SetPlanRules(lines, _loc.Tr("tactics.plan.remove"), _loc.Tr("tactics.plan.empty"));
            _view.SetPlanDraft(_draftMinute, _draftSituation, _draftShout, plan.CanAdd(DraftRule()));
        }

        /// <summary>What a rule does, in words: its shout, and any instruction change or substitution it carries.</summary>
        private string ActionText(PrematchRule rule)
        {
            var parts = new List<string>(3);
            if (rule.Shout != TouchlineShout.None)
                parts.Add(_loc.Tr("tactics.plan.action.shout", _loc.Tr(ShoutPicker.NameKey(rule.Shout))));
            if (rule.ChangeInstructions) parts.Add(_loc.Tr("tactics.plan.action.tactic"));
            if (rule.SubOutPlayerId != 0 && rule.SubInPlayerId != 0) parts.Add(_loc.Tr("tactics.plan.action.sub"));

            string text = parts.Count > 0 ? parts[0] : string.Empty;
            for (int i = 1; i < parts.Count; i++) text = _loc.Tr("tactics.plan.action.join", text, parts[i]);
            return text;
        }

        private static string SituationKey(ScoreSituation when)
        {
            switch (when)
            {
                case ScoreSituation.Losing: return "tactics.plan.when.losing";
                case ScoreSituation.Drawing: return "tactics.plan.when.drawing";
                case ScoreSituation.Winning: return "tactics.plan.when.winning";
                case ScoreSituation.NotWinning: return "tactics.plan.when.not_winning";
                case ScoreSituation.NotLosing: return "tactics.plan.when.not_losing";
                default: return "tactics.plan.when.always";
            }
        }

        /// <summary>The fixed option lists: styles, the formation catalogue and the instruction rows.</summary>
        private void SetTacticOptions()
        {
            var styles = new List<string>(PlayingStyles.All.Count);
            foreach (StyleDefinition def in PlayingStyles.All) styles.Add(_loc.Tr(TacticsLocKeys.StyleName(def.Style)));
            _view.SetStyleOptions(styles);

            var formations = new List<string>(Formations.Catalogue.Length);
            foreach (Formation f in Formations.Catalogue) formations.Add(_loc.Tr(TacticsLocKeys.Formation(f)));
            _view.SetFormationOptions(formations);

            var axes = new List<TacticAxisVm>(TacticEditor.Axes.Count);
            foreach (InstructionAxis axis in TacticEditor.Axes)
            {
                var levels = new List<string>(TacticEditor.LevelCount(axis));
                for (int level = 0; level < TacticEditor.LevelCount(axis); level++)
                    levels.Add(_loc.Tr(TacticsLocKeys.Level(axis, level)));
                axes.Add(new TacticAxisVm
                {
                    Phase = _loc.Tr(TacticsLocKeys.Phase(TacticEditor.PhaseOf(axis))),
                    Caption = _loc.Tr(TacticsLocKeys.AxisName(axis)),
                    Description = _loc.Tr(TacticsLocKeys.AxisDescription(axis)),
                    Levels = levels
                });
            }
            _view.SetInstructionAxes(axes);
        }

        private void Refresh()
        {
            StyleDefinition style = PlayingStyles.Get(_working.Style);
            string styleName = _loc.Tr(TacticsLocKeys.StyleName(style.Style));
            var suggested = new List<string>(style.SuggestedFormations.Count);
            foreach (Formation f in style.SuggestedFormations) suggested.Add(_loc.Tr(TacticsLocKeys.Formation(f)));
            _view.SetStyle(IndexOfStyle(style.Style),
                TacticEditor.IsCustom(_working) ? _loc.Tr("tactics.style.custom", styleName) : styleName,
                _loc.Tr(TacticsLocKeys.StyleDescription(style.Style)),
                suggested, IndexOf(style.SuggestedFormations, _working.Formation));

            _view.SetFormation(IndexOf(Formations.Catalogue, _working.Formation),
                _loc.Tr("tactics.formation_shape", Formations.InPossession(_working.Formation),
                    Formations.OutOfPossession(_working.Formation)));

            var levels = new List<int>(TacticEditor.Axes.Count);
            foreach (InstructionAxis axis in TacticEditor.Axes) levels.Add(TacticEditor.LevelOf(_working, axis));
            _view.SetInstructionLevels(levels);

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

        private static int IndexOfStyle(PlayingStyle style)
        {
            for (int i = 0; i < PlayingStyles.All.Count; i++)
                if (PlayingStyles.All[i].Style == style) return i;
            return -1;
        }

        private static int IndexOf(IReadOnlyList<Formation> formations, Formation formation)
        {
            for (int i = 0; i < formations.Count; i++)
                if (formations[i] == formation) return i;
            return -1;
        }

        private static TacticPlan Clone(TacticPlan plan) => plan?.Copy();
    }
}
