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
        private readonly CareerService _careerService;
        private readonly InboxService _inbox;
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
            ISaveRepository saveRepository,
            CareerService careerService,
            InboxService inbox)
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
            _careerService = careerService;
            _inbox = inbox;

            // The club's name and crest live in the shell's top bar (task 14.3), so the overview
            // no longer repeats them: it is a dashboard, not an identity card (task 14.4).
            _view = new HubView(loc.Tr);
        }

        public void Enter()
        {
            _view.EndSeasonClicked += OnEndSeason;
            _view.NextMatchClicked += OnNextMatch;
            _view.OpponentReportClicked += OnOpponentReport;
            _view.SectionClicked += OnSection;
            Responsive.Changed += OnViewportChanged;
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
            _view.NextMatchClicked -= OnNextMatch;
            _view.OpponentReportClicked -= OnOpponentReport;
            _view.SectionClicked -= OnSection;
            Responsive.Changed -= OnViewportChanged;
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

        // Crests are painter2D elements sized in C#, so they cannot restyle themselves when the
        // breakpoint changes: rebuild the dashboard at the new size instead (same trick as the
        // shell's crest in 14.3).
        private void OnViewportChanged(Viewport _) => RefreshStatus();

        private float CrestSize(bool big) =>
            Responsive.IsMobile ? (big ? 120f : 44f) : (big ? 64f : 24f);

        /// <summary>Fills every block of the overview dashboard (task 14.4).</summary>
        private void RefreshStatus()
        {
            int userClubId = _career.UserClubId;
            Season season = _career.Season;
            bool complete = _seasonService.IsSeasonComplete;

            _view.SetKicker(_loc.Tr("shell.day", season.Year, season.CurrentDay));

            // ---- the user's fixtures, in calendar order
            var played = new List<Fixture>();
            Fixture nextFixture = null;
            foreach (Fixture f in season.Fixtures)
            {
                if (!f.Involves(userClubId)) continue;
                if (f.Played) played.Add(f);
                else if (nextFixture == null || f.Day < nextFixture.Day) nextFixture = f;
            }
            played.Sort((a, b) => a.Day.CompareTo(b.Day));

            // ---- tiles
            League league = _career.GetUserLeague();
            List<LeagueTableRow> table = LeagueTable.Compute(league, season);
            int userIndex = table.FindIndex(r => r.ClubId == userClubId);

            if (userIndex >= 0)
            {
                LeagueTableRow me = table[userIndex];
                _view.SetPosition(_loc.Tr("career.position_value", userIndex + 1, table.Count));
                _view.SetPoints(_loc.Tr("hub.points_value", me.Points, me.Played));
            }
            else
            {
                _view.SetPosition("—");
                _view.SetPoints("—");
            }

            var form = new List<int>();
            for (int i = Math.Max(0, played.Count - 5); i < played.Count; i++)
                form.Add(Outcome(played[i], userClubId));
            _view.SetForm(form);

            _view.SetConfidence(_loc.Tr("hub.confidence_value", _careerService.Confidence), _careerService.ConfidenceBand);

            // ---- next match
            HubFixtureVm next = null;
            if (nextFixture != null)
            {
                bool home = nextFixture.HomeClubId == userClubId;
                string venue = _loc.Tr(home ? "hub.venue.home" : "hub.venue.away");
                int inDays = nextFixture.Day - season.CurrentDay;
                string when = inDays <= 0 ? _loc.Tr("hub.when.today")
                    : inDays == 1 ? _loc.Tr("hub.when.tomorrow")
                    : _loc.Tr("hub.when.days", inDays);

                next = new HubFixtureVm
                {
                    Kicker = _loc.Tr("hub.next.kicker", nextFixture.Round, venue),
                    HomeName = ClubName(nextFixture.HomeClubId),
                    AwayName = ClubName(nextFixture.AwayClubId),
                    HomeCrest = Crest(nextFixture.HomeClubId, CrestSize(true), UiKit.SurfaceRaised),
                    AwayCrest = Crest(nextFixture.AwayClubId, CrestSize(true), UiKit.SurfaceRaised),
                    When = when
                };
            }
            _view.SetNextFixture(next, complete);

            // ---- last result
            HubResultVm last = null;
            if (played.Count > 0)
            {
                Fixture f = played[played.Count - 1];
                last = new HubResultVm
                {
                    HomeName = ClubName(f.HomeClubId),
                    AwayName = ClubName(f.AwayClubId),
                    HomeGoals = f.HomeGoals,
                    AwayGoals = f.AwayGoals,
                    HomeCrest = Crest(f.HomeClubId, CrestSize(true), UiKit.Surface),
                    AwayCrest = Crest(f.AwayClubId, CrestSize(true), UiKit.Surface),
                    Outcome = Outcome(f, userClubId)
                };
            }
            _view.SetLastResult(last);

            // ---- mini standings: five rows with the user's club in them
            const int window = 5;
            int from = Math.Max(0, Math.Min((userIndex < 0 ? 0 : userIndex) - window / 2, table.Count - window));
            var rows = new List<HubTableRowVm>(window);
            for (int i = from; i < Math.Min(table.Count, from + window); i++)
            {
                LeagueTableRow r = table[i];
                rows.Add(new HubTableRowVm
                {
                    Position = i + 1,
                    Club = ClubName(r.ClubId),
                    Played = r.Played,
                    Points = r.Points,
                    IsUser = r.ClubId == userClubId,
                    Crest = Crest(r.ClubId, CrestSize(false), r.ClubId == userClubId ? UserRowGround : UiKit.Surface)
                });
            }
            _view.SetTable(rows);

            // ---- to do
            int unread = _inbox.UnreadCount;
            int tired = 0;
            Club club = _career.GetUserClub();
            if (club?.Squad?.Players != null)
                foreach (Player p in club.Squad.Players)
                    if (p.Condition.Fitness < TiredFitness) tired++;

            _view.SetTodo(
                unread > 0 ? _loc.Tr("hub.todo.inbox_unread", unread) : _loc.Tr("hub.todo.inbox_clear"),
                unread > 0,
                tired > 0 ? _loc.Tr("hub.todo.squad_tired", tired) : _loc.Tr("hub.todo.squad_ok"),
                tired > 0,
                _loc.Tr("hub.todo.market_budget", MoneyFormat.Short(club?.TransferBudget ?? 0)));
        }

        /// <summary>
        /// The ground under the user's standings row: the card surface with the 12% accent tint of
        /// <c>.fts-hub__trow--user</c> composited on it. A crest's frame mask must match what is
        /// behind it or it shows a square halo.
        /// </summary>
        private static readonly Color UserRowGround = UiKit.Hex(0x143037);

        /// <summary>Below this a player reads "tired" (the same band ConditionDisplay calls tired).</summary>
        private const int TiredFitness = 70;

        private static int Outcome(Fixture f, int userClubId)
        {
            int mine = f.HomeClubId == userClubId ? f.HomeGoals : f.AwayGoals;
            int theirs = f.HomeClubId == userClubId ? f.AwayGoals : f.HomeGoals;
            return mine > theirs ? 1 : mine < theirs ? -1 : 0;
        }

        private string ClubName(int clubId) => _career.FindClub(clubId)?.Name ?? $"Club {clubId}";

        private VisualElement Crest(int clubId, float size, Color mask)
        {
            Club club = _career.FindClub(clubId);
            return Crests.Badge(_identity.Visual(clubId), size, club?.ShortName ?? "?", mask);
        }

        private void OnSection(string key)
        {
            switch (key)
            {
                case "inbox": OnInbox(); break;
                case "squad": OnSquad(); break;
                case "market": OnMarket(); break;
                case "league": OnLeague(); break;
            }
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
