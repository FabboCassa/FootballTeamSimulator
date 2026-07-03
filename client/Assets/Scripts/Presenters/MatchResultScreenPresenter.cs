using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using Sim.Core.Match;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Instant result of the user's match (task 2.6), shown right after a
    /// match-day advance. Reads the outcome from UserMatchLog (Game scope).
    /// </summary>
    public sealed class MatchResultScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly CareerState _career;
        private readonly UserMatchLog _matchLog;
        private readonly ILocalizationService _loc;
        private readonly ClubIdentityService _identity;
        private readonly MatchResultView _view;

        public VisualElement View => _view.Root;

        public MatchResultScreenPresenter(
            ScreenNavigator navigator,
            CareerState career,
            UserMatchLog matchLog,
            ILocalizationService loc,
            ClubIdentityService identity)
        {
            _navigator = navigator;
            _career = career;
            _matchLog = matchLog;
            _loc = loc;
            _identity = identity;
            _view = new MatchResultView(loc.Tr);
        }

        public void Enter()
        {
            _view.ContinueClicked += OnContinue;

            var outcome = _matchLog.LastMatch;
            if (outcome == null)
                return;

            string home = ClubName(outcome.Fixture.HomeClubId);
            string away = ClubName(outcome.Fixture.AwayClubId);

            _view.SetScore(_loc.Tr("match.score",
                home, outcome.Fixture.HomeGoals, outcome.Fixture.AwayGoals, away));
            _view.SetSubtitle(_loc.Tr("match.round_day", outcome.Fixture.Round, outcome.Fixture.Day));

            int homeId = outcome.Fixture.HomeClubId;
            int awayId = outcome.Fixture.AwayClubId;
            _view.SetCrests(
                Crests.Badge(_identity.Visual(homeId), 40f, ClubShort(homeId), UiKit.Background),
                Crests.Badge(_identity.Visual(awayId), 40f, ClubShort(awayId), UiKit.Background));

            var rows = new List<MatchEventRowVm>();
            foreach (MatchEvent e in outcome.Report.Events)
            {
                string key;
                switch (e.Type)
                {
                    case MatchEventType.Goal: key = "match.event.goal"; break;
                    case MatchEventType.ChanceSaved: key = "match.event.saved"; break;
                    default: key = "match.event.missed"; break;
                }

                rows.Add(new MatchEventRowVm
                {
                    Label = _loc.Tr(key, e.Minute, PlayerName(e.ClubId, e.PlayerId), ClubName(e.ClubId)),
                    IsGoal = e.Type == MatchEventType.Goal,
                    IsUserClub = e.ClubId == _career.UserClubId
                });
            }

            _view.SetEvents(rows);
        }

        public void Exit()
        {
            _view.ContinueClicked -= OnContinue;
        }

        private void OnContinue() => _navigator.Pop();

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";

        private string ClubShort(int clubId) => _career.FindClub(clubId)?.ShortName ?? "?";

        private string PlayerName(int clubId, int playerId)
        {
            Club club = _career.FindClub(clubId);
            if (club != null)
            {
                foreach (Player p in club.Squad.Players)
                {
                    if (p.Id == playerId)
                        return p.FullName;
                }
            }

            return $"Player {playerId}";
        }
    }
}
