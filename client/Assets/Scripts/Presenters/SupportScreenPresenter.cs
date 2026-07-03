using System.Collections.Generic;
using System.Globalization;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Domain;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Support screen (task 4.5): the coach's lightweight morale levers. Pick a player and
    /// apply praise / encourage / motivate / criticize / rest; each moves his condition by a
    /// capped, context-sensitive amount (Sim.Core <see cref="SupportActionModel"/>) and then
    /// goes on a per-player cooldown so spamming is ineffective. Cooldowns persist on the
    /// career (rehydrated into a <see cref="SupportActionLog"/> here); Rest also flags the
    /// player so LocalClock skips his next training week (its opportunity cost).
    /// </summary>
    public sealed class SupportScreenPresenter : IScreenPresenter
    {
        private const int ActionCount = 5; // SupportAction members

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly SupportView _view;
        private readonly SupportBalance _cfg = new BalanceConfig().Support;

        private readonly SupportActionLog _log = new SupportActionLog();
        private Club _club;
        private int _selectedPlayerId;

        public VisualElement View => _view.Root;

        public SupportScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _view = new SupportView(loc.Tr);
        }

        public void Enter()
        {
            _view.PlayerSelected += OnPlayerSelected;
            _view.ActionClicked += OnAction;
            _view.BackClicked += OnBack;

            _club = _career.GetUserClub();
            LoadCooldowns();

            if (_club.Squad.Players.Count > 0)
                _selectedPlayerId = _club.Squad.Players[0].Id;

            _view.SetHeader(_loc.Tr("support.header", _club.Name));
            _view.SetStatus(string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.PlayerSelected -= OnPlayerSelected;
            _view.ActionClicked -= OnAction;
            _view.BackClicked -= OnBack;
        }

        private void OnPlayerSelected(int playerId)
        {
            _selectedPlayerId = playerId;
            Refresh();
        }

        private void OnAction(int actionId)
        {
            Player player = FindSquadPlayer(_selectedPlayerId);
            if (player == null)
                return;

            var action = (SupportAction)actionId;
            SupportOutcome outcome = SupportActionModel.TryApply(
                player.Condition, action, _log, player.Id, CurrentDay, _cfg);

            if (!outcome.Applied)
            {
                _view.SetStatus(_loc.Tr("support.status.on_cooldown"));
                Refresh();
                return;
            }

            // Rest costs the player his next training week (LocalClock honours this flag).
            if (action == SupportAction.Rest && !_career.RestedSinceTraining.Contains(player.Id))
                _career.RestedSinceTraining.Add(player.Id);

            SaveCooldowns();
            _saveRepository.Save(_career);

            _view.SetStatus(_loc.Tr("support.applied", player.FullName, DescribeOutcome(outcome)));
            Refresh();
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            var rows = new List<SupportRowVm>();
            foreach (Player p in _club.Squad.Players)
            {
                ConditionDisplay cd = ConditionDisplay.Build(p.Condition, _loc.Tr);
                rows.Add(new SupportRowVm
                {
                    PlayerId = p.Id,
                    Name = p.FullName,
                    RoleAbbr = RoleName(p.Role),
                    RoleGroup = RoleFormat.Group(p.Role),
                    FormArrow = cd.FormArrow,
                    MoraleFace = cd.MoraleFace,
                    Fitness = cd.Fitness,
                    Tooltip = cd.Tooltip,
                    Selected = p.Id == _selectedPlayerId
                });
            }
            _view.SetRoster(rows);

            Player selected = FindSquadPlayer(_selectedPlayerId);
            _view.SetSelectedCaption(selected != null
                ? _loc.Tr("support.selected", selected.FullName)
                : string.Empty);

            _view.SetActions(BuildActions());
        }

        private List<SupportButtonVm> BuildActions()
        {
            var actions = new List<SupportButtonVm>();
            for (int i = 0; i < ActionCount; i++)
            {
                var action = (SupportAction)i;
                string name = _loc.Tr("support.action." + action.ToString().ToLowerInvariant());

                bool onCooldown = _log.IsOnCooldown(_selectedPlayerId, action, CurrentDay, _cfg);
                string text = name;
                if (onCooldown)
                {
                    int days = _log.DaysUntilAvailable(_selectedPlayerId, action, CurrentDay, _cfg);
                    text = _loc.Tr("support.action_cooldown", name, days);
                }

                actions.Add(new SupportButtonVm { ActionId = i, Text = text, Enabled = !onCooldown });
            }

            return actions;
        }

        /// <summary>Builds a short signed summary of what the action moved (skips zero deltas).</summary>
        private string DescribeOutcome(SupportOutcome outcome)
        {
            var parts = new List<string>();
            if (outcome.MoraleDelta != 0)
                parts.Add(_loc.Tr("support.delta.morale", Signed(outcome.MoraleDelta)));
            if (outcome.FormDelta != 0)
                parts.Add(_loc.Tr("support.delta.form", Signed(outcome.FormDelta)));
            if (outcome.FitnessDelta != 0)
                parts.Add(_loc.Tr("support.delta.fitness", Signed(outcome.FitnessDelta)));

            return parts.Count > 0 ? string.Join(", ", parts) : _loc.Tr("support.delta.none");
        }

        private static string Signed(int value) =>
            value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture)
                      : value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Rehydrates the cooldown log from the saved "{playerId}:{action}" -> day map.</summary>
        private void LoadCooldowns()
        {
            if (_career.SupportCooldowns == null)
                return;

            foreach (var kv in _career.SupportCooldowns)
            {
                string[] parts = kv.Key.Split(':');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int playerId)
                    && int.TryParse(parts[1], out int action))
                {
                    _log.Import(playerId, action, kv.Value);
                }
            }
        }

        /// <summary>Writes the cooldown log back to the saved map (called after each action).</summary>
        private void SaveCooldowns()
        {
            _career.SupportCooldowns.Clear();
            foreach ((int playerId, int action, int day) in _log.Export())
                _career.SupportCooldowns[playerId.ToString(CultureInfo.InvariantCulture) + ":" + action.ToString(CultureInfo.InvariantCulture)] = day;
        }

        private Player FindSquadPlayer(int playerId)
        {
            foreach (Player p in _club.Squad.Players)
            {
                if (p.Id == playerId)
                    return p;
            }

            return null;
        }

        private int CurrentDay => _career.Season.CurrentDay;

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
