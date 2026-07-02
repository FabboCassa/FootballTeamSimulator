using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Scouting;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Player profile (task 4.6): a pushed detail screen opened from the Squad roster.
    /// Reads the target player id from <see cref="PlayerProfileTarget"/> and renders his
    /// live state — full 10-attribute breakdown, condition with the 4.2 "why" lines,
    /// role/age/overall, market value and season goals. Everything is read fresh on Enter,
    /// so the values track training and matches (the task's ✅). Market value is the stored
    /// figure re-priced on the weekly tick (task 5.1). Potential stays hidden until scouting
    /// (5.4); scouted ranges/contract/appearances fill in with their systems (5.4/5.6).
    /// </summary>
    public sealed class PlayerProfileScreenPresenter : IScreenPresenter
    {
        // Canonical skill order (matches PlayerAttributes' indexer and PlayerRating's weights).
        private static readonly string[] AttrKeys =
        {
            "attr.pace", "attr.strength", "attr.stamina", "attr.technique", "attr.passing",
            "attr.dribbling", "attr.shooting", "attr.defending", "attr.positioning", "attr.goalkeeping"
        };

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ILocalizationService _loc;
        private readonly PlayerProfileTarget _target;
        private readonly ScoutingService _scouting;
        private readonly PlayerProfileView _view;

        public VisualElement View => _view.Root;

        public PlayerProfileScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ILocalizationService loc,
            PlayerProfileTarget target,
            ScoutingService scouting)
        {
            _navigator = navigator;
            _career = career;
            _loc = loc;
            _target = target;
            _scouting = scouting;
            _view = new PlayerProfileView(loc.Tr);
        }

        public void Enter()
        {
            _view.BackClicked += OnBack;
            Render();
        }

        public void Exit()
        {
            _view.BackClicked -= OnBack;
        }

        private void OnBack() => _navigator.Pop();

        private void Render()
        {
            Player player = _career.FindPlayer(_target.PlayerId);
            if (player == null)
            {
                _view.SetIdentity(_loc.Tr("profile.unknown_player"), string.Empty, string.Empty);
                _view.SetConditionVisible(false);
                _view.SetCondition(new ProfileConditionVm());
                _view.SetAttributes(new List<AttrRowVm>());
                _view.SetSeasonGoals(string.Empty);
                return;
            }

            bool owned = IsOwned(player.Id);

            // Money lives only on the Market screen now (task 6.7 feedback) — no value line here.
            _view.SetSeasonGoals(_loc.Tr("profile.season_goals", SeasonGoals(player.Id)));

            if (owned)
            {
                // Own players: exact attributes, live condition, potential still hidden (4.6 design).
                _view.SetIdentity(
                    player.FullName,
                    _loc.Tr("profile.subline", RoleName(player.Role), player.Age, PlayerRating.Overall(player)),
                    _loc.Tr("profile.potential_unknown"));
                _view.SetConditionVisible(true);
                _view.SetCondition(BuildCondition(player.Condition));
                _view.SetAttributes(BuildExactAttributes(player.Attributes));
                return;
            }

            // Scouted players (task 5.4b): attribute ranges + a potential band that narrow with
            // knowledge; condition is hidden (you don't know an opponent's exact form/morale).
            PlayerScoutReport report = _scouting.Report(player);
            int knowledge = _scouting.KnowledgeOf(player.Id);

            _view.SetIdentity(
                player.FullName,
                _loc.Tr("profile.subline_scouted", RoleName(player.Role), player.Age, RangeText(report.Overall, knowledge)),
                _loc.Tr("profile.potential_scouted", RangeText(report.Potential, knowledge)));
            _view.SetConditionVisible(false);
            _view.SetCondition(new ProfileConditionVm());
            _view.SetAttributes(BuildScoutedAttributes(report, knowledge));
        }

        private bool IsOwned(int playerId)
        {
            Club userClub = _career.GetUserClub();
            if (userClub == null)
                return false;
            foreach (Player p in userClub.Squad.Players)
                if (p.Id == playerId)
                    return true;
            return false;
        }

        /// <summary>A range "min–max", or the single estimate once fully scouted.</summary>
        private string RangeText(ScoutedRange r, int knowledge) =>
            knowledge >= _scouting.MaxKnowledge
                ? r.Estimate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : _loc.Tr("profile.range", r.Min, r.Max);

        private ProfileConditionVm BuildCondition(PlayerCondition condition)
        {
            ConditionDisplay d = ConditionDisplay.Build(condition, _loc.Tr);
            // The 4.2 tooltip is exactly the three formatted cause lines (fitness, form, morale).
            string[] lines = d.Tooltip.Split('\n');
            return new ProfileConditionVm
            {
                Fitness = d.Fitness,
                FormArrow = d.FormArrow,
                MoraleFace = d.MoraleFace,
                FitnessLine = lines.Length > 0 ? lines[0] : string.Empty,
                FormLine = lines.Length > 1 ? lines[1] : string.Empty,
                MoraleLine = lines.Length > 2 ? lines[2] : string.Empty
            };
        }

        private List<AttrRowVm> BuildExactAttributes(PlayerAttributes attributes)
        {
            var rows = new List<AttrRowVm>(PlayerAttributes.SkillCount);
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
            {
                int v = attributes[i];
                rows.Add(new AttrRowVm
                {
                    Name = _loc.Tr(AttrKeys[i]),
                    Text = v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    BarValue = v
                });
            }

            return rows;
        }

        private List<AttrRowVm> BuildScoutedAttributes(PlayerScoutReport report, int knowledge)
        {
            var rows = new List<AttrRowVm>(PlayerAttributes.SkillCount);
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
            {
                ScoutedRange r = report.Attributes[i];
                rows.Add(new AttrRowVm
                {
                    Name = _loc.Tr(AttrKeys[i]),
                    Text = RangeText(r, knowledge),
                    BarValue = r.Estimate // band centre drives the bar
                });
            }

            return rows;
        }

        private int SeasonGoals(int playerId)
        {
            int goals = 0;
            foreach (ScorerTally tally in _career.Season.Scorers)
            {
                if (tally.PlayerId == playerId)
                    goals += tally.Goals;
            }

            return goals;
        }

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
