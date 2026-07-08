using System;
using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using Fts.Services.Persistence;
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
        /// <summary>Number of onboarding.stepN.title/body pairs defined in the loc tables (task 6.2).</summary>
        private const int OnboardingSteps = 6;

        private readonly ScreenNavigator _navigator;
        private readonly IGameSessionService _session;
        private readonly IGameClock _clock;
        private readonly IMessageBroker _broker;
        private readonly CareerState _career;
        private readonly SeasonService _seasonService;
        private readonly ILocalizationService _loc;
        private readonly ClubIdentityService _identity;
        private readonly OverlayHost _overlay;
        private readonly ISaveRepository _saveRepository;
        private readonly HubView _view;
        private IDisposable _dayAdvancedSubscription;
        private IDisposable _shortcutSubscription;
        private bool _userMatchSeen;

        public VisualElement View => _view.Root;

        public HubPresenter(
            ScreenNavigator navigator,
            IGameSessionService session,
            IGameClock clock,
            IMessageBroker broker,
            CareerState career,
            SeasonService seasonService,
            ILocalizationService loc,
            ClubIdentityService identity,
            OverlayHost overlay,
            ISaveRepository saveRepository)
        {
            _navigator = navigator;
            _session = session;
            _clock = clock;
            _broker = broker;
            _career = career;
            _seasonService = seasonService;
            _loc = loc;
            _identity = identity;
            _overlay = overlay;
            _saveRepository = saveRepository;

            _view = new HubView(loc.Tr);
            var club = career.GetUserClub();
            _view.SetClubName(loc.Tr("hub.career_label", club.Name, career.GetUserLeague().Name));

            // Per-club generated identity (task 6.1): crest + primary-colour tint.
            ClubVisual v = _identity.UserVisual();
            _view.SetCrest(new CrestRenderer(
                96f, v.Shape, v.Pattern, v.Primary, v.Secondary, v.Accent, v.Emblem,
                UiKit.Background, club.ShortName));
            _view.SetAccent(v.Primary);
        }

        public void Enter()
        {
            _view.EndSeasonClicked += OnEndSeason;
            _view.OpponentReportClicked += OnOpponentReport;
            _dayAdvancedSubscription = _broker.Subscribe<DayAdvancedMessage>(OnDayAdvanced);
            // Hub actions arrive as HubShortcutMessage from two publishers that both guarantee
            // the Hub is the top screen first: the 6.5 DesktopController (hotkeys, guarded) and
            // the 6.6 ShellController (sidebar/Continue, pops back to the Hub before publishing).
            _shortcutSubscription = _broker.Subscribe<HubShortcutMessage>(OnShortcut);
            RefreshStatus();
            MaybeShowOnboarding();
        }

        public void Exit()
        {
            _view.EndSeasonClicked -= OnEndSeason;
            _view.OpponentReportClicked -= OnOpponentReport;
            _dayAdvancedSubscription?.Dispose();
            _dayAdvancedSubscription = null;
            _shortcutSubscription?.Dispose();
            _shortcutSubscription = null;
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
            // Confirm first (task 6.2) — ending the season is a one-way step. On confirm, the
            // coach-career decision screen (task 5.6) runs the season-end evaluation, lets the user
            // act on offers/sacking, then commits the rollover and hands off to the standings summary.
            Dialogs.Confirm(_overlay, _loc,
                "dialog.end_season.title", "dialog.end_season.message", "dialog.end_season.confirm",
                () => _navigator.Push<CareerSeasonEndScreenPresenter>());
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
            _view.SetOpponentReportVisible(nextFixture != null);
            _view.SetStatus(_loc.Tr("hub.status.day", _career.Season.Year, _career.Season.CurrentDay) + " " + next + last);
        }

        private void OnOpponentReport() => _navigator.Push<OpponentReportScreenPresenter>();

        /// <summary>Runs the guided first-run tutorial once per career (task 6.2), then marks it done.</summary>
        private void MaybeShowOnboarding()
        {
            if (_career.OnboardingDone)
                return;

            var titles = new List<string>();
            var bodies = new List<string>();
            for (int i = 1; i <= OnboardingSteps; i++)
            {
                titles.Add(_loc.Tr($"onboarding.step{i}.title"));
                bodies.Add(_loc.Tr($"onboarding.step{i}.body"));
            }

            var overlay = new OnboardingOverlay(_loc.Tr, titles, bodies, OnOnboardingComplete);
            _overlay.Show(overlay.Root);
        }

        private void OnOnboardingComplete()
        {
            _career.OnboardingDone = true;
            _saveRepository.Save(_career);
        }

        /// <summary>Routes a desktop hotkey (task 6.5) to the matching Hub action.</summary>
        private void OnShortcut(HubShortcutMessage message)
        {
            switch (message.Action)
            {
                case HubShortcut.AdvanceDay: OnAdvanceDay(); break;
                case HubShortcut.NextMatch: OnNextMatch(); break;
                case HubShortcut.Inbox: OnInbox(); break;
                case HubShortcut.Squad: OnSquad(); break;
                case HubShortcut.Tactics: OnTactics(); break;
                case HubShortcut.Training: OnTraining(); break;
                case HubShortcut.Support: OnSupport(); break;
                case HubShortcut.Market: OnMarket(); break;
                case HubShortcut.Scouting: OnScouting(); break;
                case HubShortcut.Club: OnClub(); break;
                case HubShortcut.Career: OnCareer(); break;
                case HubShortcut.League: OnLeague(); break;
                case HubShortcut.EndSeason: OnEndSeason(); break;
                case HubShortcut.ExitCareer: OnExitCareer(); break;
            }
        }

        private void OnInbox() => _navigator.Push<InboxScreenPresenter>();
        private void OnSquad() => _navigator.Push<SquadScreenPresenter>();
        private void OnTactics() => _navigator.Push<TacticsScreenPresenter>();
        private void OnTraining() => _navigator.Push<TrainingScreenPresenter>();
        private void OnSupport() => _navigator.Push<SupportScreenPresenter>();
        private void OnMarket() => _navigator.Push<MarketScreenPresenter>();
        private void OnScouting() => _navigator.Push<ScoutingScreenPresenter>();
        private void OnClub() => _navigator.Push<ClubScreenPresenter>();
        private void OnCareer() => _navigator.Push<CareerScreenPresenter>();
        private void OnLeague() => _navigator.Push<LeagueScreenPresenter>();

        private void OnExitCareer() =>
            Dialogs.Confirm(_overlay, _loc,
                "dialog.exit_career.title", "dialog.exit_career.message", "dialog.exit_career.confirm",
                () => _session.EndCareer());
    }
}
