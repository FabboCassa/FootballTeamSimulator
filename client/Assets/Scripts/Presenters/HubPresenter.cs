using System;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using Fts.Views;
using Sim.Core.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Career hub: calendar control + navigation. Lives in the Game scope.
    /// Hardware back ends the career instead of a raw pop, keeping the
    /// Game scope lifecycle consistent.
    /// </summary>
    public sealed class HubPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly IGameSessionService _session;
        private readonly IGameClock _clock;
        private readonly IMessageBroker _broker;
        private readonly CareerState _career;
        private readonly SeasonService _seasonService;
        private readonly ILocalizationService _loc;
        private readonly HubView _view;
        private IDisposable _dayAdvancedSubscription;
        private bool _userMatchSeen;

        public VisualElement View => _view.Root;

        public HubPresenter(
            ScreenNavigator navigator,
            IGameSessionService session,
            IGameClock clock,
            IMessageBroker broker,
            CareerState career,
            SeasonService seasonService,
            ILocalizationService loc)
        {
            _navigator = navigator;
            _session = session;
            _clock = clock;
            _broker = broker;
            _career = career;
            _seasonService = seasonService;
            _loc = loc;

            _view = new HubView(loc.Tr);
            var club = career.GetUserClub();
            _view.SetClubName(loc.Tr("hub.career_label", club.Name, career.GetUserLeague().Name));
        }

        public void Enter()
        {
            _view.AdvanceDayClicked += OnAdvanceDay;
            _view.NextMatchClicked += OnNextMatch;
            _view.EndSeasonClicked += OnEndSeason;
            _view.SquadClicked += OnSquad;
            _view.TacticsClicked += OnTactics;
            _view.TrainingClicked += OnTraining;
            _view.SupportClicked += OnSupport;
            _view.LeagueClicked += OnLeague;
            _view.ExitCareerClicked += OnExitCareer;
            _dayAdvancedSubscription = _broker.Subscribe<DayAdvancedMessage>(OnDayAdvanced);
            RefreshStatus();
        }

        public void Exit()
        {
            _view.AdvanceDayClicked -= OnAdvanceDay;
            _view.NextMatchClicked -= OnNextMatch;
            _view.EndSeasonClicked -= OnEndSeason;
            _view.SquadClicked -= OnSquad;
            _view.TacticsClicked -= OnTactics;
            _view.TrainingClicked -= OnTraining;
            _view.SupportClicked -= OnSupport;
            _view.LeagueClicked -= OnLeague;
            _view.ExitCareerClicked -= OnExitCareer;
            _dayAdvancedSubscription?.Dispose();
            _dayAdvancedSubscription = null;
        }

        public void Reveal() => RefreshStatus();

        public bool HandleBack()
        {
            _session.EndCareer();
            return true;
        }

        private void OnAdvanceDay() => _clock.AdvanceDay();

        /// <summary>Advances until the user's match has been played (or the season ends).</summary>
        private void OnNextMatch()
        {
            _userMatchSeen = false;
            int guard = 0;
            while (!_userMatchSeen && !_seasonService.IsSeasonComplete && guard++ < 14)
                _clock.AdvanceDay();
        }

        private void OnEndSeason()
        {
            _seasonService.EndSeason();
            RefreshStatus();
            _navigator.Push<SeasonEndScreenPresenter>();
        }

        private void OnDayAdvanced(DayAdvancedMessage message)
        {
            RefreshStatus();
            if (message.MatchesPlayed > 0)
                Debug.Log(LeagueTableFormatter.Format(_career.GetUserLeague(), _career.Season));
            if (message.UserMatchPlayed)
            {
                _userMatchSeen = true;
                _navigator.Push<MatchWatchScreenPresenter>();
            }
        }

        private void RefreshStatus()
        {
            int userClubId = _career.UserClubId;
            string next = _loc.Tr("hub.status.season_complete");
            string last = string.Empty;

            Fixture nextFixture = null;
            Fixture lastFixture = null;
            foreach (Fixture f in _career.Season.Fixtures)
            {
                if (!f.Involves(userClubId)) continue;
                if (f.Played) lastFixture = f;
                else if (nextFixture == null || f.Day < nextFixture.Day) nextFixture = f;
            }

            if (nextFixture != null)
            {
                bool home = nextFixture.HomeClubId == userClubId;
                int opponentId = home ? nextFixture.AwayClubId : nextFixture.HomeClubId;
                string opponent = _career.FindClub(opponentId)?.Name ?? $"Club {opponentId}";
                string venue = _loc.Tr(home ? "hub.home_short" : "hub.away_short");
                next = _loc.Tr("hub.status.next", opponent, venue, nextFixture.Day, nextFixture.Round);
            }

            if (lastFixture != null)
            {
                string homeName = _career.FindClub(lastFixture.HomeClubId)?.Name ?? "?";
                string awayName = _career.FindClub(lastFixture.AwayClubId)?.Name ?? "?";
                last = "\n" + _loc.Tr("hub.status.last", homeName, lastFixture.HomeGoals, lastFixture.AwayGoals, awayName);
            }

            _view.SetSeasonComplete(_seasonService.IsSeasonComplete);
            _view.SetStatus(_loc.Tr("hub.status.day", _career.Season.Year, _career.Season.CurrentDay) + " " + next + last);
        }

        private void OnSquad() => _navigator.Push<SquadScreenPresenter>();
        private void OnTactics() => _navigator.Push<TacticsScreenPresenter>();
        private void OnTraining() => _navigator.Push<TrainingScreenPresenter>();
        private void OnSupport() => _navigator.Push<SupportScreenPresenter>();
        private void OnLeague() => _navigator.Push<LeagueScreenPresenter>();
        private void OnExitCareer() => _session.EndCareer();
    }
}
