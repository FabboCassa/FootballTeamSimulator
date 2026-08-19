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
    /// Scouting screen (task 5.4b): assign the club's scouts to players and watch their
    /// knowledge grow. Each row shows a knowledge bar and a watch toggle limited by the number
    /// of scouts; the scouted overall is shown as a range that narrows with knowledge, and
    /// tapping a row opens the player's profile (where every attribute range + the potential
    /// band reflect the same knowledge). Reads everything live from <see cref="ScoutingService"/>.
    /// </summary>
    public sealed class ScoutingScreenPresenter : IScreenPresenter
    {
        private const int MaxRows = 60;
        private const int RoleMax = 7; // PositionRole: goalkeeper(0)..striker(7)

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ScoutingService _scouting;
        private readonly PlayerProfileTarget _profileTarget;
        private readonly ILocalizationService _loc;
        private readonly ScoutingView _view;

        private int _roleFilter = -1; // -1 = all roles

        public VisualElement View => _view.Root;

        public ScoutingScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ScoutingService scouting,
            PlayerProfileTarget profileTarget,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _scouting = scouting;
            _profileTarget = profileTarget;
            _loc = loc;
            _view = new ScoutingView(loc.Tr);
        }

        public void Enter()
        {
            _view.PlayerSelected += OnPlayerSelected;
            _view.WatchToggleClicked += OnWatchToggle;
            _view.RoleFilterSelected += OnRoleFilter;
            _view.BackClicked += OnBack;
            Refresh();
        }

        public void Exit()
        {
            _view.PlayerSelected -= OnPlayerSelected;
            _view.WatchToggleClicked -= OnWatchToggle;
            _view.RoleFilterSelected -= OnRoleFilter;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => Refresh();

        private void OnPlayerSelected(int playerId)
        {
            _profileTarget.PlayerId = playerId;
            _navigator.Push<PlayerProfileScreenPresenter>();
        }

        private void OnWatchToggle(int playerId)
        {
            if (_scouting.IsWatching(playerId))
                _scouting.Unwatch(playerId);
            else
                _scouting.Watch(playerId);
            Refresh();
        }

        /// <summary>A role chip was picked (-1 = every role); the whole row stays visible.</summary>
        private void OnRoleFilter(int role)
        {
            _roleFilter = role < 0 || role > RoleMax ? -1 : role;
            Refresh();
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            _view.SetHeader(_loc.Tr("scouting.header",
                _scouting.ScoutLevel(), _scouting.WatchCount(), _scouting.WatchCapacity()));
            _view.SetRoleFilters(BuildRoleFilters());

            // Every player outside the user's squad, with a scouted read.
            var entries = new List<Entry>();
            foreach (League league in _career.Leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    if (club.Id == _career.UserClubId) continue;
                    foreach (Player p in club.Squad.Players)
                    {
                        if (_roleFilter >= 0 && (int)p.Role != _roleFilter) continue;
                        entries.Add(new Entry
                        {
                            Player = p,
                            ClubName = club.Name,
                            Report = _scouting.Report(p),
                            Knowledge = _scouting.KnowledgeOf(p.Id),
                            Watching = _scouting.IsWatching(p.Id)
                        });
                    }
                }
            }

            // Watched first, then by the scouted (perceived) overall — never the true overall.
            entries.Sort((a, b) =>
            {
                if (a.Watching != b.Watching) return a.Watching ? -1 : 1;
                int byEst = b.Report.Overall.Estimate.CompareTo(a.Report.Overall.Estimate);
                return byEst != 0 ? byEst : a.Player.Id.CompareTo(b.Player.Id);
            });

            bool hasFreeSlot = _scouting.HasFreeSlot();
            var rows = new List<ScoutingRowVm>();
            int shown = 0;
            foreach (Entry e in entries)
            {
                if (shown >= MaxRows) break;
                rows.Add(BuildRow(e, hasFreeSlot));
                shown++;
            }

            _view.SetRows(rows);
        }

        private ScoutingRowVm BuildRow(Entry e, bool hasFreeSlot)
        {
            string ovr = OverallText(e.Report, e.Knowledge);
            int pct = _scouting.MaxKnowledge > 0 ? e.Knowledge * 100 / _scouting.MaxKnowledge : 0;

            string knowledgeText = e.Knowledge <= 0
                ? _loc.Tr("scouting.unscouted")
                : e.Knowledge >= _scouting.MaxKnowledge
                    ? _loc.Tr("scouting.known")
                    : _loc.Tr("scouting.percent", pct);

            return new ScoutingRowVm
            {
                PlayerId = e.Player.Id,
                Name = $"{e.Player.FullName} — {e.ClubName}",
                RoleAbbr = RoleName(e.Player.Role),
                RoleGroup = RoleFormat.Group(e.Player.Role),
                Age = e.Player.Age.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OvrText = ovr,
                KnowledgePercent = pct,
                KnowledgeText = knowledgeText,
                Watching = e.Watching,
                ToggleText = _loc.Tr(e.Watching ? "scouting.stop" : "scouting.scout"),
                ToggleEnabled = e.Watching || hasFreeSlot
            };
        }

        /// <summary>Scouted overall: a single number when fully known, otherwise a narrowing range.</summary>
        private string OverallText(PlayerScoutReport report, int knowledge) =>
            knowledge >= _scouting.MaxKnowledge
                ? _loc.Tr("scouting.ovr_known", report.Overall.Estimate)
                : _loc.Tr("scouting.ovr_range", report.Overall.Min, report.Overall.Max);

        /// <summary>
        /// The filter row: "all" plus every position role, each chip carrying its reparto group so
        /// the selected one lights up in the same colour the role cells use in the list (6.12b).
        /// </summary>
        private List<FilterChipVm> BuildRoleFilters()
        {
            var chips = new List<FilterChipVm>
            {
                new FilterChipVm
                {
                    Value = -1,
                    Label = _loc.Tr("filter.all_roles"),
                    RoleGroup = -1,
                    Selected = _roleFilter < 0
                }
            };
            for (int role = 0; role <= RoleMax; role++)
            {
                chips.Add(new FilterChipVm
                {
                    Value = role,
                    Label = RoleName((PositionRole)role),
                    RoleGroup = RoleFormat.Group((PositionRole)role),
                    Selected = _roleFilter == role
                });
            }
            return chips;
        }

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private sealed class Entry
        {
            public Player Player;
            public string ClubName;
            public PlayerScoutReport Report;
            public int Knowledge;
            public bool Watching;
        }
    }
}
