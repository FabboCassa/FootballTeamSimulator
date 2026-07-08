using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Match;
using Sim.Core.Scouting;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Pre-match opponent report (task 6.11): a read-only intel screen opened on purpose from
    /// the Hub next-match card. Shows the upcoming opponent — crest, coarse squad strength,
    /// likely formation/tactic (AI clubs field a default 4-3-3 with a balanced tactic), recent
    /// form, their likely XI on a mirrored visual pitch, and their squad with scouted overall
    /// ranges (the 5.4 knowledge layer: vaguer for the unscouted). Shape/strength are always
    /// visible; per-player attribute detail respects scouting. Pure consultation — it never
    /// writes back and doesn't leak into the user's own Tactics screen. Client-only.
    /// </summary>
    public sealed class OpponentReportScreenPresenter : IScreenPresenter
    {
        private const int RecentFormMatches = 5;

        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly ScoutingService _scouting;
        private readonly ClubIdentityService _identity;
        private readonly PlayerProfileTarget _profileTarget;
        private readonly ILocalizationService _loc;
        private readonly OpponentReportView _view;

        public VisualElement View => _view.Root;

        public OpponentReportScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ScoutingService scouting,
            ClubIdentityService identity,
            PlayerProfileTarget profileTarget,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _career = career;
            _scouting = scouting;
            _identity = identity;
            _profileTarget = profileTarget;
            _loc = loc;
            _view = new OpponentReportView(loc.Tr);
        }

        public void Enter()
        {
            _view.PlayerSelected += OnPlayerSelected;
            _view.BackClicked += OnBack;
            Build();
        }

        public void Exit()
        {
            _view.PlayerSelected -= OnPlayerSelected;
            _view.BackClicked -= OnBack;
        }

        private void OnPlayerSelected(int playerId)
        {
            _profileTarget.PlayerId = playerId;
            _navigator.Push<PlayerProfileScreenPresenter>();
        }

        private void OnBack() => _navigator.Pop();

        private void Build()
        {
            Fixture next = FindNextUserFixture(out bool userHome);
            if (next == null)
            {
                _view.SetHeader(_loc.Tr("opponent.none"));
                _view.SetEmpty(_loc.Tr("opponent.none"));
                return;
            }

            int opponentId = userHome ? next.AwayClubId : next.HomeClubId;
            Club opponent = _career.FindClub(opponentId);
            if (opponent == null)
            {
                _view.SetHeader(_loc.Tr("opponent.none"));
                _view.SetEmpty(_loc.Tr("opponent.none"));
                return;
            }

            // The user hosts when he plays at home, so the opponent's venue is the opposite.
            string venue = _loc.Tr(userHome ? "opponent.away" : "opponent.home");
            _view.SetHeader(_loc.Tr("opponent.title", opponent.Name, venue, next.Round));

            ClubVisual v = _identity.Visual(opponentId);
            _view.SetCrest(new CrestRenderer(
                84f, v.Shape, v.Pattern, v.Primary, v.Secondary, v.Accent, v.Emblem,
                UiKit.Background, opponent.ShortName));

            // Likely XI = the opponent's best eleven in his default 4-3-3 shape.
            Lineup xi = LineupSelector.BestEleven(opponent);

            _view.SetStrength(_loc.Tr("opponent.strength", AverageOverall(xi)));
            _view.SetFormation(_loc.Tr("opponent.formation", _loc.Tr("tactics.formation.f433")));
            _view.SetTactic(_loc.Tr("opponent.tactic", _loc.Tr("opponent.tactic_neutral")));
            _view.SetForm(BuildForm(opponentId), _loc.Tr("opponent.no_form"));
            _view.SetShape(BuildShapeTokens(xi, v));
            _view.SetRoster(BuildRoster(opponent));
        }

        /// <summary>The user's earliest unplayed fixture this season (null once the season is done).</summary>
        private Fixture FindNextUserFixture(out bool userHome)
        {
            userHome = false;
            int userClubId = _career.UserClubId;
            Fixture next = null;
            foreach (Fixture f in _career.Season.Fixtures)
            {
                if (f.Played || !f.Involves(userClubId)) continue;
                if (next == null || f.Day < next.Day)
                    next = f;
            }

            if (next != null)
                userHome = next.HomeClubId == userClubId;
            return next;
        }

        /// <summary>Average overall of the resolved XI (role-aware) — the coarse strength readout.</summary>
        private static int AverageOverall(Lineup xi)
        {
            if (xi.Slots.Count == 0)
                return 0;
            int sum = 0;
            foreach (LineupSlot slot in xi.Slots)
                sum += PlayerRating.OverallFor(slot.Player, slot.Role);
            return sum / xi.Slots.Count;
        }

        /// <summary>The opponent's most recent results (oldest→newest), from their point of view.</summary>
        private List<FormChipVm> BuildForm(int opponentId)
        {
            var played = new List<Fixture>();
            foreach (Fixture f in _career.Season.Fixtures)
                if (f.Played && f.Involves(opponentId))
                    played.Add(f);

            played.Sort((a, b) => a.Day.CompareTo(b.Day));

            int start = played.Count > RecentFormMatches ? played.Count - RecentFormMatches : 0;
            var chips = new List<FormChipVm>();
            for (int i = start; i < played.Count; i++)
            {
                Fixture f = played[i];
                bool home = f.HomeClubId == opponentId;
                int gf = home ? f.HomeGoals : f.AwayGoals;
                int ga = home ? f.AwayGoals : f.HomeGoals;
                chips.Add(new FormChipVm
                {
                    Outcome = gf > ga ? 2 : (gf == ga ? 1 : 0),
                    Text = gf + "-" + ga
                });
            }

            return chips;
        }

        /// <summary>Read-only pitch tokens for the opponent's likely XI, in their kit colours.
        /// The OVR badge shows the scouted estimate, or "?" when the player is unscouted.</summary>
        private List<PitchTokenVm> BuildShapeTokens(Lineup xi, ClubVisual visual)
        {
            var roles = new List<PositionRole>(xi.Slots.Count);
            foreach (LineupSlot s in xi.Slots)
                roles.Add(s.Role);

            var tokens = new List<PitchTokenVm>(xi.Slots.Count);
            for (int i = 0; i < xi.Slots.Count; i++)
            {
                LineupSlot slot = xi.Slots[i];
                (float x, float y) = FormationLayout.Normalized(roles, i);
                tokens.Add(new PitchTokenVm
                {
                    SlotIndex = -1,
                    PlayerId = -1,
                    X = x,
                    Y = y,
                    Badge = ScoutedBadge(slot.Player),
                    Name = LastName(slot.Player.FullName),
                    Fill = visual.Primary,
                    Text = visual.Emblem,
                    Fitness = -1
                });
            }

            return tokens;
        }

        /// <summary>Every opponent squad player with a scouted overall read, best (perceived) first.</summary>
        private List<OpponentRosterRowVm> BuildRoster(Club opponent)
        {
            var entries = new List<(Player Player, PlayerScoutReport Report, int Knowledge)>();
            foreach (Player p in opponent.Squad.Players)
                entries.Add((p, _scouting.Report(p), _scouting.KnowledgeOf(p.Id)));

            entries.Sort((a, b) =>
            {
                int byEst = b.Report.Overall.Estimate.CompareTo(a.Report.Overall.Estimate);
                return byEst != 0 ? byEst : a.Player.Id.CompareTo(b.Player.Id);
            });

            var rows = new List<OpponentRosterRowVm>(entries.Count);
            foreach ((Player player, PlayerScoutReport report, int knowledge) in entries)
            {
                rows.Add(new OpponentRosterRowVm
                {
                    PlayerId = player.Id,
                    Name = player.FullName,
                    RoleAbbr = RoleName(player.Role),
                    RoleGroup = RoleFormat.Group(player.Role),
                    Age = player.Age.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    OvrText = OverallText(report, knowledge)
                });
            }

            return rows;
        }

        /// <summary>Scouted overall: a single number when fully known, otherwise a narrowing range.</summary>
        private string OverallText(PlayerScoutReport report, int knowledge) =>
            knowledge >= _scouting.MaxKnowledge
                ? _loc.Tr("scouting.ovr_known", report.Overall.Estimate)
                : _loc.Tr("scouting.ovr_range", report.Overall.Min, report.Overall.Max);

        /// <summary>The token badge: the scouted OVR estimate, or "?" for an unscouted player.</summary>
        private string ScoutedBadge(Player player) =>
            _scouting.KnowledgeOf(player.Id) > 0
                ? _scouting.Report(player).Overall.Estimate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "?";

        private string RoleName(PositionRole role) =>
            _loc.Tr("role." + role.ToString().ToLowerInvariant());

        private static string LastName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return string.Empty;
            int space = fullName.LastIndexOf(' ');
            return space >= 0 && space < fullName.Length - 1 ? fullName.Substring(space + 1) : fullName;
        }
    }
}
