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
        private readonly ClubIdentityService _identity;
        private readonly PlayerProfileView _view;

        public VisualElement View => _view.Root;

        public PlayerProfileScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            ILocalizationService loc,
            PlayerProfileTarget target,
            ScoutingService scouting,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _loc = loc;
            _target = target;
            _scouting = scouting;
            _identity = identity;
            _view = new PlayerProfileView(loc.Tr);
        }

        public void Enter()
        {
            _view.BackClicked += OnBack;
            _view.WatchClicked += OnWatch;
            Render();
        }

        public void Exit()
        {
            _view.BackClicked -= OnBack;
            _view.WatchClicked -= OnWatch;
        }

        private void OnBack() => _navigator.Pop();

        /// <summary>
        /// Task 11.3 — put this player under observation, or call the scout off. This is the
        /// roadmap's "direct assignments start from where you actually are": the profile is reached
        /// from a club's squad, from the market and from the world search, so one button serves all
        /// three without any of them having to know what a scouting brief is.
        /// </summary>
        private void OnWatch()
        {
            int playerId = _target.PlayerId;
            if (_scouting.IsWatching(playerId))
                _scouting.Unwatch(playerId);
            else
                _scouting.Watch(playerId);

            Render();
        }

        private void Render()
        {
            // Task 11.3: resolve him ANYWHERE in the world, not only in the divisions the career
            // plays. Since 11.1 a scouting report can name a player from a data-only club in Brazil,
            // and until now opening that report landed on "unknown player".
            Player player = _career.FindPlayer(_target.PlayerId) ?? _career.FindPlayerInWorld(_target.PlayerId);
            if (player == null)
            {
                _view.SetReputation(string.Empty);
                _view.SetWatchAction(string.Empty, false, false, false);
                _view.SetAvatar(null);
                _view.SetIdentity(_loc.Tr("profile.unknown_player"), string.Empty, string.Empty);
                _view.SetConditionVisible(false);
                _view.SetCondition(new ProfileConditionVm());
                _view.SetAttributes(new List<AttrRowVm>());
                _view.SetSeasonGoals(string.Empty);
                return;
            }

            bool owned = IsOwned(player.Id);

            // Portrait placeholder in his club's kit colours (task 6.8).
            int clubId = ClubIdOf(player.Id);
            RenderReputation(player, clubId, owned);
            if (clubId >= 0)
                _view.SetAvatar(Crests.Avatar(_identity.Visual(clubId), 72f, player.FullName));
            else
                _view.SetAvatar(null);

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

        /// <summary>
        /// The id of the club that holds this player, or -1 if he's a free agent / not found. Rides
        /// the world's player index (task 11.1/11.3) rather than walking the playable divisions, so
        /// it answers for a club anywhere in the world and costs a dictionary probe.
        /// </summary>
        private int ClubIdOf(int playerId)
        {
            Club club = _career.World != null ? _career.World.ClubOfPlayer(playerId) : null;
            return club != null ? club.Id : -1;
        }

        /// <summary>
        /// The line under the potential band: where he plays and how publicly known he is (task
        /// 11.3). Your own players do not get it — you do not read your own squad off the news.
        /// </summary>
        private void RenderReputation(Player player, int clubId, bool owned)
        {
            if (owned)
            {
                _view.SetReputation(string.Empty);
                _view.SetWatchAction(string.Empty, false, false, false);
                return;
            }

            Club club = clubId >= 0 ? _career.World.FindClub(clubId) : null;
            League league = clubId >= 0 ? _career.World.LeagueOf(clubId) : null;
            Nation nation = league != null && !string.IsNullOrEmpty(league.NationCode)
                ? _career.World.FindNation(league.NationCode)
                : null;

            string where = club != null && league != null
                ? (nation != null
                    ? _loc.Tr("profile.where", club.Name, nation.Name, league.Name)
                    : _loc.Tr("profile.where_league", club.Name, league.Name))
                : club != null ? club.Name : string.Empty;

            string fame = _loc.Tr("profile.fame",
                _loc.Tr("scouting.fame_tier." + _scouting.FameTierOf(player.Id).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));

            _view.SetReputation(string.IsNullOrEmpty(where) ? fame : where + " · " + fame);

            bool watching = _scouting.IsWatching(player.Id);
            _view.SetWatchAction(
                _loc.Tr(watching ? "profile.stop_watching" : "profile.watch"),
                true,
                watching || _scouting.HasFreeSlot(),
                watching);
        }
    }
}
