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
    /// Squad screen (task 2.4, given a visual pitch in 6.7): the XI is shown as tokens on a
    /// top-down pitch and the rest of the squad on a bench list. Drag a player onto a slot
    /// (from the pitch or the bench) to place/swap him, or tap to pick then tap a target
    /// (fallback). Works on a copy of the saved plan; Save persists it. Duplicates are
    /// impossible by construction: <see cref="AssignToSlot"/> always swaps.
    /// </summary>
    public sealed class SquadScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ISaveRepository _saveRepository;
        private readonly ILocalizationService _loc;
        private readonly PlayerProfileTarget _profileTarget;
        private readonly OverlayHost _overlay;
        private readonly ClubIdentityService _identity;
        private readonly SquadView _view;
        // Zone-role bands + tilt config for free positioning (task 6.10); default balance.
        private readonly PositioningBalance _positioning = new BalanceConfig().Positioning;

        private Club _club;
        private LineupPlan _working;
        private int _selectedPlayerId = -1;
        private int _selectedSlot = -1;

        public VisualElement View => _view.Root;

        public SquadScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ISaveRepository saveRepository,
            ILocalizationService loc,
            PlayerProfileTarget profileTarget,
            OverlayHost overlay,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _saveRepository = saveRepository;
            _loc = loc;
            _profileTarget = profileTarget;
            _overlay = overlay;
            _identity = identity;
            _view = new SquadView(loc.Tr);
        }

        public void Enter()
        {
            _view.SlotTapped += OnSlotTapped;
            _view.BenchTapped += OnBenchTapped;
            _view.AssignRequested += OnAssignRequested;
            _view.RepositionRequested += OnReposition;
            _view.ProfileClicked += OnProfile;
            _view.AutoClicked += OnAuto;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;

            _club = _career.GetUserClub();
            _working = Clone(_career.UserLineup) ?? LineupPlan.From(LineupSelector.BestEleven(_club, UserFormation()));
            _selectedPlayerId = -1;
            _selectedSlot = -1;
            _view.SetHeader(_loc.Tr("squad.header", _club.Name, _club.Squad.Players.Count));
            _view.SetStatus(_career.UserLineup == null ? _loc.Tr("squad.status.no_saved") : string.Empty);
            Refresh();
        }

        public void Exit()
        {
            _view.SlotTapped -= OnSlotTapped;
            _view.BenchTapped -= OnBenchTapped;
            _view.AssignRequested -= OnAssignRequested;
            _view.RepositionRequested -= OnReposition;
            _view.ProfileClicked -= OnProfile;
            _view.AutoClicked -= OnAuto;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        // ------------------------------------------------------------- gestures

        private void OnSlotTapped(int index)
        {
            if (_selectedPlayerId < 0)
            {
                _selectedSlot = index;
                _selectedPlayerId = _working.Slots[index].PlayerId;
            }
            else if (_selectedSlot == index)
            {
                ClearSelection();
                return;
            }
            else
            {
                AssignToSlot(index, _selectedPlayerId);
                ClearSelection();
                return;
            }

            _view.SetStatus(string.Empty);
            Refresh();
        }

        private void OnBenchTapped(int playerId)
        {
            if (_selectedPlayerId < 0 || (_selectedSlot < 0 && _selectedPlayerId != playerId))
            {
                // Nothing picked, or a different bench player picked → pick this one.
                _selectedSlot = -1;
                _selectedPlayerId = playerId;
            }
            else if (_selectedPlayerId == playerId)
            {
                ClearSelection();
                return;
            }
            else
            {
                // A slot was picked → drop this bench player into it.
                AssignToSlot(_selectedSlot, playerId);
                ClearSelection();
                return;
            }

            _view.SetStatus(string.Empty);
            Refresh();
        }

        private void OnAssignRequested(int slot, int playerId)
        {
            AssignToSlot(slot, playerId);
            ClearSelection();
        }

        /// <summary>
        /// Free positioning (task 6.10): a player dropped on open turf is moved to that spot,
        /// and the ROLE he plays is re-resolved from the zone (a fullback dragged high on the
        /// flank becomes a winger; the keeper is never moved). To keep one nudge a light tilt,
        /// every other outfield slot is pinned to its formation anchor first, so the team's
        /// shape tilt averages over the whole XI rather than swinging on a single player.
        /// </summary>
        private void OnReposition(int playerId, float x, float y)
        {
            int slot = SlotIndexOf(playerId);
            if (slot < 0)
                return; // only players already in the XI can be repositioned

            LineupPlanSlot target = _working.Slots[slot];
            if (target.Role == PositionRole.Goalkeeper)
                return; // the keeper stays in goal

            EnsureOutfieldAnchors();

            int xp = ToPermille(x);
            int yp = ToPermille(y);
            target.PosXPermille = xp;
            target.PosYPermille = yp;
            target.Role = ZoneRole.Resolve(target.Role, new SlotPosition(xp, yp), _positioning);

            ClearSelection(); // re-renders at the new spot with the new role badge
        }

        /// <summary>
        /// Pins every outfield slot without a custom position to its formation anchor, so a
        /// single repositioned player carries only ~1/10 of the team's shape tilt (matching the
        /// Sim.Core intent that positions are stored for all outfield slots, or none).
        /// </summary>
        private void EnsureOutfieldAnchors()
        {
            var roles = new List<PositionRole>(_working.Slots.Count);
            foreach (LineupPlanSlot s in _working.Slots)
                roles.Add(s.Role);

            for (int i = 0; i < _working.Slots.Count; i++)
            {
                LineupPlanSlot s = _working.Slots[i];
                if (s.Role == PositionRole.Goalkeeper) continue;
                if (s.PosXPermille.HasValue && s.PosYPermille.HasValue) continue;

                (float ax, float ay) = FormationLayout.Normalized(roles, i);
                s.PosXPermille = ToPermille(ax);
                s.PosYPermille = ToPermille(ay);
            }
        }

        private static int ToPermille(float normalized)
        {
            int v = (int)(normalized * 1000f + 0.5f);
            return v < 0 ? 0 : (v > 1000 ? 1000 : v);
        }

        private void OnProfile(int playerId)
        {
            _profileTarget.PlayerId = playerId;
            _navigator.Push<PlayerProfileScreenPresenter>();
        }

        private void OnAuto()
        {
            _working = LineupPlan.From(LineupSelector.BestEleven(_club, UserFormation()));
            ClearSelection();
            _view.SetStatus(_loc.Tr("squad.status.auto"));
            Refresh();
        }

        private void OnSave()
        {
            _career.UserLineup = Clone(_working);
            _saveRepository.Save(_career);
            _view.SetStatus(_loc.Tr("squad.status.saved"));
            Dialogs.Toast(_overlay, _loc, "common.saved");
        }

        private void OnBack() => _navigator.Pop();

        /// <summary>Assigns <paramref name="playerId"/> to <paramref name="slotIndex"/>, swapping
        /// if he is already in the XI so the lineup can never hold a duplicate.</summary>
        private void AssignToSlot(int slotIndex, int playerId)
        {
            if (slotIndex < 0 || slotIndex >= _working.Slots.Count)
                return;

            int currentIndex = SlotIndexOf(playerId);
            if (currentIndex == slotIndex)
                return;

            if (currentIndex >= 0)
                _working.Slots[currentIndex].PlayerId = _working.Slots[slotIndex].PlayerId;

            _working.Slots[slotIndex].PlayerId = playerId;
            _view.SetStatus(string.Empty);
        }

        private void ClearSelection()
        {
            _selectedPlayerId = -1;
            _selectedSlot = -1;
            Refresh();
        }

        // ------------------------------------------------------------- rendering

        private void Refresh()
        {
            ClubVisual visual = _identity.UserVisual();
            var roles = new List<PositionRole>(_working.Slots.Count);
            foreach (LineupPlanSlot s in _working.Slots)
                roles.Add(s.Role);

            var tokens = new List<PitchTokenVm>(_working.Slots.Count);
            for (int i = 0; i < _working.Slots.Count; i++)
            {
                LineupPlanSlot slot = _working.Slots[i];
                Player player = FindPlayer(slot.PlayerId);
                float x, y;
                if (slot.PosXPermille.HasValue && slot.PosYPermille.HasValue)
                {
                    x = slot.PosXPermille.Value / 1000f;
                    y = slot.PosYPermille.Value / 1000f;
                }
                else
                {
                    (x, y) = FormationLayout.Normalized(roles, i);
                }
                var vm = new PitchTokenVm
                {
                    SlotIndex = i,
                    PlayerId = slot.PlayerId,
                    X = x,
                    Y = y,
                    Badge = player != null ? PlayerRating.OverallFor(player, slot.Role).ToString() : "–",
                    Name = player != null ? LastName(player.FullName) : RoleAbbr(slot.Role),
                    Fill = visual.Primary,
                    Text = visual.Emblem,
                    Selected = i == _selectedSlot,
                    Fitness = player != null ? ConditionDisplay.Build(player.Condition, _loc.Tr).Fitness : -1
                };
                tokens.Add(vm);
            }

            var inLineup = new HashSet<int>();
            foreach (LineupPlanSlot slot in _working.Slots)
                inLineup.Add(slot.PlayerId);

            var bench = new List<Player>();
            foreach (Player p in _club.Squad.Players)
                if (!inLineup.Contains(p.Id))
                    bench.Add(p);
            bench.Sort((a, b) =>
            {
                if (a.Role != b.Role) return ((int)a.Role).CompareTo((int)b.Role);
                int byRating = PlayerRating.Overall(b).CompareTo(PlayerRating.Overall(a));
                return byRating != 0 ? byRating : a.Id.CompareTo(b.Id);
            });

            var rows = new List<BenchRowVm>(bench.Count);
            foreach (Player player in bench)
            {
                ConditionDisplay d = ConditionDisplay.Build(player.Condition, _loc.Tr);
                rows.Add(new BenchRowVm
                {
                    PlayerId = player.Id,
                    Role = RoleAbbr(player.Role),
                    RoleGroup = RoleFormat.Group(player.Role),
                    Name = player.FullName,
                    Age = player.Age,
                    Rating = PlayerRating.Overall(player),
                    FormArrow = d.FormArrow,
                    MoraleFace = d.MoraleFace,
                    Fitness = d.Fitness,
                    Tooltip = d.Tooltip,
                    Selected = _selectedSlot < 0 && _selectedPlayerId == player.Id
                });
            }

            _view.SetTokens(tokens);
            _view.SetBench(rows);
            _view.SetSelection(BuildSelection());
        }

        private SelectionVm BuildSelection()
        {
            if (_selectedPlayerId < 0)
                return null;

            Player player = FindPlayer(_selectedPlayerId);
            if (player == null)
                return null;

            PositionRole role = _selectedSlot >= 0 ? _working.Slots[_selectedSlot].Role : player.Role;
            int ovr = PlayerRating.OverallFor(player, role);
            return new SelectionVm
            {
                PlayerId = _selectedPlayerId,
                Text = _loc.Tr("squad.selected", player.FullName, RoleAbbr(role), ovr)
            };
        }

        private int SlotIndexOf(int playerId)
        {
            for (int i = 0; i < _working.Slots.Count; i++)
                if (_working.Slots[i].PlayerId == playerId)
                    return i;
            return -1;
        }

        private Player FindPlayer(int playerId)
        {
            foreach (Player player in _club.Squad.Players)
                if (player.Id == playerId)
                    return player;
            return null;
        }

        private static string LastName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return string.Empty;
            int space = fullName.LastIndexOf(' ');
            return space >= 0 && space < fullName.Length - 1 ? fullName.Substring(space + 1) : fullName;
        }

        private static LineupPlan Clone(LineupPlan plan)
        {
            if (plan == null)
                return null;

            var copy = new LineupPlan { ClubId = plan.ClubId };
            foreach (LineupPlanSlot slot in plan.Slots)
                copy.Slots.Add(new LineupPlanSlot
                {
                    Role = slot.Role,
                    PlayerId = slot.PlayerId,
                    PosXPermille = slot.PosXPermille,
                    PosYPermille = slot.PosYPermille
                });
            return copy;
        }

        private Formation UserFormation() => _career.UserTactic?.Formation ?? Formation.F433;

        private string RoleAbbr(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());
    }
}
