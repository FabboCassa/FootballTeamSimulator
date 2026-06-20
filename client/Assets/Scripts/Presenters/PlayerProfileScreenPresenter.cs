using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
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
        private readonly PlayerProfileView _view;

        public VisualElement View => _view.Root;

        public PlayerProfileScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ILocalizationService loc,
            PlayerProfileTarget target)
        {
            _navigator = navigator;
            _career = career;
            _loc = loc;
            _target = target;
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
                _view.SetValue(string.Empty);
                _view.SetCondition(new ProfileConditionVm());
                _view.SetAttributes(new List<AttrRowVm>());
                _view.SetSeasonGoals(string.Empty);
                return;
            }

            _view.SetIdentity(
                player.FullName,
                _loc.Tr("profile.subline", RoleName(player.Role), player.Age, PlayerRating.Overall(player)),
                _loc.Tr("profile.potential_unknown"));

            // Market value is the stored figure, re-priced on the weekly tick (task 5.1).
            _view.SetValue(_loc.Tr("profile.market_value", MoneyFormat.Short(player.MarketValue)));

            _view.SetCondition(BuildCondition(player.Condition));
            _view.SetAttributes(BuildAttributes(player.Attributes));
            _view.SetSeasonGoals(_loc.Tr("profile.season_goals", SeasonGoals(player.Id)));
        }

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

        private List<AttrRowVm> BuildAttributes(PlayerAttributes attributes)
        {
            var rows = new List<AttrRowVm>(PlayerAttributes.SkillCount);
            for (int i = 0; i < PlayerAttributes.SkillCount; i++)
                rows.Add(new AttrRowVm { Name = _loc.Tr(AttrKeys[i]), Value = attributes[i] });

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
