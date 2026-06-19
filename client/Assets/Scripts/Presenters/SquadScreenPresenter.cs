using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Squad screen (task 2.4): roster overview + lineup picker.
    /// Works on a copy of the saved plan; Save persists it (used by the sim
    /// from the next match day). Duplicates are impossible by construction:
    /// assigning a player who is already in the XI swaps the two slots.
    /// </summary>
    public sealed class SquadScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly PlayerProfileTarget _profileTarget;
        private readonly SquadView _view;

        private Club _club;
        private LineupPlan _working;
        private int _selectedSlot = -1;

        public VisualElement View => _view.Root;

        public SquadScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc,
            PlayerProfileTarget profileTarget)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _profileTarget = profileTarget;
            _view = new SquadView(loc.Tr);
        }

        public void Enter()
        {
            _view.SlotClicked += OnSlotClicked;
            _view.PlayerClicked += OnPlayerClicked;
            _view.AutoClicked += OnAuto;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

            _club = _career.GetUserClub();
            _working = Clone(_career.UserLineup) ?? LineupPlan.From(LineupSelector.BestEleven(_club, UserFormation()));
            _view.SetHeader(_loc.Tr("squad.header", _club.Name, _club.Squad.Players.Count));
            _view.SetStatus(_career.UserLineup == null
                ? _loc.Tr("squad.status.no_saved")
                : string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.SlotClicked -= OnSlotClicked;
            _view.PlayerClicked -= OnPlayerClicked;
            _view.AutoClicked -= OnAuto;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        private void OnSlotClicked(int index)
        {
            // Tapping the selected slot again deselects it, returning to "browse" mode
            // where a roster tap opens the player's profile.
            _selectedSlot = _selectedSlot == index ? -1 : index;
            _view.SetStatus(string.Empty);
            Refresh();
        }

        private void OnPlayerClicked(int playerId)
        {
            // No slot selected → the tap is a request to view the player's profile
            // (task 4.6). With a slot selected the tap assigns him to the lineup.
            if (_selectedSlot < 0)
            {
                _profileTarget.PlayerId = playerId;
                _navigator.Push<PlayerProfileScreenPresenter>();
                return;
            }

            int currentIndex = SlotIndexOf(playerId);
            if (currentIndex == _selectedSlot)
                return;

            if (currentIndex >= 0)
            {
                // Already in the XI: swap the two slots' players.
                _working.Slots[currentIndex].PlayerId = _working.Slots[_selectedSlot].PlayerId;
            }

            _working.Slots[_selectedSlot].PlayerId = playerId;
            _view.SetStatus(string.Empty);
            Refresh();
        }

        private void OnAuto()
        {
            _working = LineupPlan.From(LineupSelector.BestEleven(_club, UserFormation()));
            _selectedSlot = -1;
            _view.SetStatus(_loc.Tr("squad.status.auto"));
            Refresh();
        }

        private void OnSave()
        {
            _career.UserLineup = Clone(_working);
            _saveRepository.Save(_career);
            _view.SetStatus(_loc.Tr("squad.status.saved"));
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            var slots = new List<LineupSlotVm>(_working.Slots.Count);
            for (int i = 0; i < _working.Slots.Count; i++)
            {
                LineupPlanSlot slot = _working.Slots[i];
                Player player = FindPlayer(slot.PlayerId);
                string name = player != null ? player.FullName : $"Player {slot.PlayerId}";
                int rating = player != null ? PlayerRating.OverallFor(player, slot.Role) : 0;

                var vm = new LineupSlotVm
                {
                    Index = i,
                    Label = $"{RoleAbbr(slot.Role)}  {name}  ({rating})",
                    Selected = i == _selectedSlot
                };
                ApplyCondition(vm, player);
                slots.Add(vm);
            }

            var inLineup = new HashSet<int>();
            foreach (LineupPlanSlot slot in _working.Slots)
                inLineup.Add(slot.PlayerId);

            var roster = new List<Player>(_club.Squad.Players);
            roster.Sort((a, b) =>
            {
                if (a.Role != b.Role) return ((int)a.Role).CompareTo((int)b.Role);
                int byRating = PlayerRating.Overall(b).CompareTo(PlayerRating.Overall(a));
                return byRating != 0 ? byRating : a.Id.CompareTo(b.Id);
            });

            var rows = new List<RosterRowVm>(roster.Count);
            foreach (Player player in roster)
            {
                var vm = new RosterRowVm
                {
                    PlayerId = player.Id,
                    Label = _loc.Tr("squad.row",
                        RoleAbbr(player.Role), player.FullName, player.Age, PlayerRating.Overall(player)),
                    InLineup = inLineup.Contains(player.Id)
                };
                ApplyCondition(vm, player);
                rows.Add(vm);
            }

            _view.SetSlots(slots);
            _view.SetRoster(rows);
        }

        private int SlotIndexOf(int playerId)
        {
            for (int i = 0; i < _working.Slots.Count; i++)
            {
                if (_working.Slots[i].PlayerId == playerId)
                    return i;
            }

            return -1;
        }

        private Player FindPlayer(int playerId)
        {
            foreach (Player player in _club.Squad.Players)
            {
                if (player.Id == playerId)
                    return player;
            }

            return null;
        }

        private void ApplyCondition(LineupSlotVm vm, Player player)
        {
            if (player == null)
                return;

            ConditionDisplay d = ConditionDisplay.Build(player.Condition, _loc.Tr);
            vm.FormArrow = d.FormArrow;
            vm.MoraleFace = d.MoraleFace;
            vm.Fitness = d.Fitness;
            vm.Tooltip = d.Tooltip;
        }

        private void ApplyCondition(RosterRowVm vm, Player player)
        {
            if (player == null)
                return;

            ConditionDisplay d = ConditionDisplay.Build(player.Condition, _loc.Tr);
            vm.FormArrow = d.FormArrow;
            vm.MoraleFace = d.MoraleFace;
            vm.Fitness = d.Fitness;
            vm.Tooltip = d.Tooltip;
        }

        private static LineupPlan Clone(LineupPlan plan)
        {
            if (plan == null)
                return null;

            var copy = new LineupPlan { ClubId = plan.ClubId };
            foreach (LineupPlanSlot slot in plan.Slots)
                copy.Slots.Add(new LineupPlanSlot { Role = slot.Role, PlayerId = slot.PlayerId });
            return copy;
        }

        /// <summary>The user's chosen shape (set on the Tactics screen); 4-3-3 until one is picked.</summary>
        private Formation UserFormation() => _career.UserTactic?.Formation ?? Formation.F433;

        private string RoleAbbr(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
