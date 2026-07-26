using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Tactics;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// The ranked lineup editor (Phase 9.2): the coach fetches their own club's squad, picks a formation, and
    /// arranges the XI (tap a slot, then a bench player). Save builds a Sim.Core <see cref="LineupPlan"/> from
    /// the chosen player external ids + formation roles and POSTs it to /ranked/lineup — the server reuses it
    /// each matchday (falling back to BestEleven only when nothing is submitted). No pitch drawing yet (a
    /// slot/bench list, like the SP 2.4 squad screen); tactic/pre-match plan are left null for this increment.
    /// </summary>
    public sealed class RankedLineupScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly RankedApiService _ranked;
        private readonly RankedLineupView _view;

        private List<RankedPlayerDto> _squad = new List<RankedPlayerDto>();
        private int _myClubExt;
        private Formation _formation = Formation.F433;
        private readonly int[] _assigned = new int[11]; // slot -> player externalId (-1 = empty)
        private int _selectedSlot = -1;
        private bool _busy;

        public VisualElement View => _view.Root;

        public RankedLineupScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, RankedApiService ranked)
        {
            _navigator = navigator;
            _loc = loc;
            _ranked = ranked;
            _view = new RankedLineupView(loc.Tr);
        }

        public void Enter()
        {
            _view.FormationClicked += OnFormation;
            _view.SlotClicked += OnSlot;
            _view.PlayerClicked += OnPlayer;
            _view.SaveClicked += OnSave;
            _view.BackClicked += OnBack;
            LoadAsync().Forget();
        }

        public void Exit()
        {
            _view.FormationClicked -= OnFormation;
            _view.SlotClicked -= OnSlot;
            _view.PlayerClicked -= OnPlayer;
            _view.SaveClicked -= OnSave;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() { }

        private async UniTaskVoid LoadAsync()
        {
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.loading"), isError: false);

            var mine = await _ranked.GetMineAsync();
            if (!mine.Success || mine.Value == null || mine.Value.clubExternalId == null)
            {
                _view.SetBusy(false);
                _view.ShowStatus(_loc.Tr("ranked.error.wrong_phase"), isError: true);
                return;
            }
            _myClubExt = mine.Value.clubExternalId.Value;

            var squad = await _ranked.GetClubSquadAsync(_myClubExt);
            _view.SetBusy(false);
            if (!squad.Success || squad.Value?.players == null)
            {
                _view.ShowStatus(_loc.Tr(RankedErrorFormat.Key(squad.Error)), isError: true);
                return;
            }

            _view.ClearStatus();
            _squad = squad.Value.players;

            // Re-open on the SAVED lineup when there is one (so an edit survives leaving the screen);
            // otherwise start from the auto best XI.
            var saved = await _ranked.GetMyLineupAsync();
            bool restored = saved.Success && saved.Value != null && ApplySavedPlan(saved.Value);
            if (!restored) AutoFill(_formation);

            Render();
        }

        /// <summary>Rebuilds the editor state from a stored plan: infers the formation from its role list
        /// (falling back to the current one) and restores each slot's player. Returns false when the plan
        /// doesn't fit the current squad (e.g. a sold player), so the caller falls back to the best XI.</summary>
        private bool ApplySavedPlan(LineupPlan plan)
        {
            if (plan.Slots == null || plan.Slots.Count != 11) return false;

            var savedRoles = plan.Slots.Select(s => s.Role).ToArray();
            Formation? match = null;
            foreach (Formation f in Formations.All)
                if (Formations.Roles(f).SequenceEqual(savedRoles)) { match = f; break; }

            var squadIds = new HashSet<int>(_squad.Select(p => p.externalId));
            for (int i = 0; i < plan.Slots.Count; i++)
                if (!squadIds.Contains(plan.Slots[i].PlayerId)) return false;

            _formation = match ?? _formation;
            _selectedSlot = -1;
            for (int i = 0; i < _assigned.Length; i++) _assigned[i] = plan.Slots[i].PlayerId;
            return true;
        }

        /// <summary>Best XI for the formation: the best goalkeeper in the GK slot, then the best remaining
        /// players by overall in the outfield slots.</summary>
        private void AutoFill(Formation formation)
        {
            _formation = formation;
            _selectedSlot = -1;
            for (int i = 0; i < _assigned.Length; i++) _assigned[i] = -1;

            var roles = Formations.Roles(formation);
            var byOverall = _squad.OrderByDescending(p => p.overall).ThenBy(p => p.externalId).ToList();
            var used = new HashSet<int>();

            // GK slot(s) first (only slot 0 in every shape).
            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i] != PositionRole.Goalkeeper) continue;
                var gk = byOverall.FirstOrDefault(p => p.role == (int)PositionRole.Goalkeeper && !used.Contains(p.externalId))
                         ?? byOverall.FirstOrDefault(p => !used.Contains(p.externalId));
                if (gk != null) { _assigned[i] = gk.externalId; used.Add(gk.externalId); }
            }
            // Outfield slots: best remaining.
            for (int i = 0; i < roles.Length; i++)
            {
                if (_assigned[i] != -1) continue;
                var pick = byOverall.FirstOrDefault(p => !used.Contains(p.externalId));
                if (pick != null) { _assigned[i] = pick.externalId; used.Add(pick.externalId); }
            }
        }

        private void Render()
        {
            _view.SetFormation(_loc.Tr("ranked.lineup.formation", FormationName(_formation)));

            var roles = Formations.Roles(_formation);
            var slots = new List<RankedLineupView.SlotVm>(11);
            for (int i = 0; i < roles.Length; i++)
                slots.Add(new RankedLineupView.SlotVm(
                    _loc.Tr(RoleKey(roles[i])), PlayerLabel(_assigned[i]), i == _selectedSlot));
            _view.SetSlots(slots);

            var onPitch = new HashSet<int>(_assigned);
            var bench = new List<RankedLineupView.BenchVm>();
            foreach (var p in _squad.OrderByDescending(p => p.overall).ThenBy(p => p.externalId))
                if (!onPitch.Contains(p.externalId))
                    bench.Add(new RankedLineupView.BenchVm(p.externalId,
                        $"{_loc.Tr(RoleKey((PositionRole)p.role))}  {p.name}  {p.overall}"));
            _view.SetBench(bench);
        }

        private void OnFormation()
        {
            int idx = System.Array.IndexOf(Formations.All, _formation);
            _formation = Formations.All[(idx + 1) % Formations.All.Length];
            AutoFill(_formation);
            Render();
        }

        private void OnSlot(int slot)
        {
            _selectedSlot = _selectedSlot == slot ? -1 : slot;
            Render();
        }

        private void OnPlayer(int externalId)
        {
            if (_selectedSlot < 0)
            {
                _view.ShowStatus(_loc.Tr("ranked.lineup.select_slot"), isError: true);
                return;
            }
            _view.ClearStatus();

            // If the picked player already occupies another slot, swap the two.
            int other = System.Array.IndexOf(_assigned, externalId);
            if (other >= 0) _assigned[other] = _assigned[_selectedSlot];
            _assigned[_selectedSlot] = externalId;
            _selectedSlot = -1;
            Render();
        }

        private void OnSave() => SaveAsync().Forget();

        private async UniTaskVoid SaveAsync()
        {
            if (_busy) return;
            foreach (int id in _assigned)
                if (id < 0) { _view.ShowStatus(_loc.Tr("ranked.lineup.incomplete"), isError: true); return; }

            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("ranked.lineup.saving"), isError: false);

            var roles = Formations.Roles(_formation);
            var lineupPlan = new LineupPlan { ClubId = _myClubExt };
            for (int i = 0; i < roles.Length; i++)
                lineupPlan.Slots.Add(new LineupPlanSlot { Role = roles[i], PlayerId = _assigned[i] });

            var body = new { lineup = lineupPlan, tactic = (object)null, plan = (object)null };
            var result = await _ranked.SubmitLineupAsync(body);

            _busy = false;
            _view.SetBusy(false);
            _view.ShowStatus(
                result.Success ? _loc.Tr("ranked.lineup.saved") : _loc.Tr(RankedErrorFormat.Key(result.Error)),
                isError: !result.Success);
        }

        private string PlayerLabel(int externalId)
        {
            if (externalId < 0) return "—";
            var p = _squad.FirstOrDefault(x => x.externalId == externalId);
            return p == null ? "—" : $"{p.name}  {p.overall}";
        }

        private static string RoleKey(PositionRole role) => "role." + role.ToString().ToLowerInvariant();

        private static string FormationName(Formation f) => f switch
        {
            Formation.F433 => "4-3-3",
            Formation.F442 => "4-4-2",
            Formation.F352 => "3-5-2",
            Formation.F4231 => "4-2-3-1",
            Formation.F532 => "5-3-2",
            Formation.F343 => "3-4-3",
            _ => f.ToString(),
        };

        private void OnBack() => _navigator.Pop();
    }
}
