using System;
using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Messaging;
using Fts.Services.Navigation;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Owns the persistent FM-style app chrome while a career is active (task 6.6).
    /// On <see cref="Attach"/> it inserts the <see cref="AppShell"/> into the UI root and
    /// re-roots the <see cref="ScreenNavigator"/> onto the shell's content host, so every
    /// screen renders inside the chrome; on dispose (career end) it restores the original
    /// root. All actions are routed by popping back to the Hub and publishing the matching
    /// <see cref="HubShortcutMessage"/> — HubPresenter stays the single owner of the
    /// calendar/navigation logic (no duplication, and the pop keeps the stack sane).
    /// Lives in the Game scope; registered by GameSessionService.
    /// </summary>
    public sealed class ShellController : IDisposable
    {
        private readonly ScreenNavigator _navigator;
        private readonly IMessageBroker _broker;
        private readonly CareerState _career;
        private readonly SeasonService _seasonService;
        private readonly ILocalizationService _loc;
        private readonly ClubIdentityService _identity;
        private readonly InboxService _inbox;

        private AppShell _shell;
        private VisualElement _originalRoot;
        private int _hubDepth;
        private IDisposable _daySubscription;
        private IDisposable _screenSubscription;

        public ShellController(
            ScreenNavigator navigator,
            IMessageBroker broker,
            CareerState career,
            SeasonService seasonService,
            ILocalizationService loc,
            ClubIdentityService identity,
            InboxService inbox)
        {
            _navigator = navigator;
            _broker = broker;
            _career = career;
            _seasonService = seasonService;
            _loc = loc;
            _identity = identity;
            _inbox = inbox;
        }

        /// <summary>Builds the chrome and re-roots the navigator. Call BEFORE pushing the Hub.</summary>
        public void Attach()
        {
            if (_shell != null)
                return;

            _shell = new AppShell();
            _originalRoot = _navigator.Root;
            _hubDepth = _navigator.StackDepth + 1; // the Hub is pushed right after Attach()
            _originalRoot.Add(_shell.Root);
            _navigator.SetRoot(_shell.ContentHost);

            var club = _career.GetUserClub();
            ClubVisual v = _identity.UserVisual();
            _shell.SetCrest(new CrestRenderer(
                44f, v.Shape, v.Pattern, v.Primary, v.Secondary, v.Accent, v.Emblem,
                UiKit.Surface, club.ShortName));
            _shell.SetClub(club.Name);
            _shell.SetAccent(v.Primary);
            _shell.SetSections(BuildSections());
            _shell.SetActiveSection("overview");
            _shell.SetBackVisible(false);

            _shell.SectionClicked += OnSection;
            _shell.ContinueClicked += OnContinue;
            _shell.AdvanceDayClicked += OnAdvanceDay;
            _shell.BackClicked += OnBack;

            _daySubscription = _broker.Subscribe<DayAdvancedMessage>(_ => Refresh());
            _screenSubscription = _broker.Subscribe<ScreenChangedMessage>(OnScreenChanged);
            Refresh();
        }

        public void Dispose()
        {
            _daySubscription?.Dispose();
            _daySubscription = null;
            _screenSubscription?.Dispose();
            _screenSubscription = null;

            if (_shell == null)
                return;

            _shell.SectionClicked -= OnSection;
            _shell.ContinueClicked -= OnContinue;
            _shell.AdvanceDayClicked -= OnAdvanceDay;
            _shell.BackClicked -= OnBack;

            _navigator.SetRoot(_originalRoot);
            _originalRoot.Remove(_shell.Root);
            _shell = null;
            _originalRoot = null;
        }

        // ---------------------------------------------------------------- actions

        private void OnSection(string id)
        {
            // Return to the Hub first: HubPresenter is then the top screen, so acting on the
            // shortcut is always safe (same guarantee the 6.5 DesktopController relies on).
            _navigator.PopAbove(_hubDepth);
            if (id == "overview")
                return;
            if (SectionShortcuts.TryGetValue(id, out HubShortcut shortcut))
                _broker.Publish(new HubShortcutMessage(shortcut));
        }

        private void OnContinue()
        {
            _navigator.PopAbove(_hubDepth);
            _broker.Publish(new HubShortcutMessage(
                _seasonService.IsSeasonComplete ? HubShortcut.EndSeason : HubShortcut.NextMatch));
        }

        private void OnAdvanceDay()
        {
            _navigator.PopAbove(_hubDepth);
            _broker.Publish(new HubShortcutMessage(HubShortcut.AdvanceDay));
        }

        private void OnBack() => _navigator.Back();

        // ---------------------------------------------------------------- refresh

        private void OnScreenChanged(ScreenChangedMessage message)
        {
            if (_shell == null)
                return;

            _shell.SetBackVisible(message.StackDepth > _hubDepth);
            _shell.SetChromeVisible(message.ScreenName != nameof(MatchWatchScreenPresenter));
            _shell.SetActiveSection(ActiveSectionFor(message.ScreenName));
            Refresh();
        }

        private void Refresh()
        {
            if (_shell == null)
                return;

            string day = _loc.Tr("shell.day", _career.Season.Year, _career.Season.CurrentDay);
            string budget = _loc.Tr("market.budget", MoneyFormat.Short(_career.GetUserClub().TransferBudget));
            _shell.SetSubline(day + "  ·  " + budget);

            bool complete = _seasonService.IsSeasonComplete;
            _shell.SetContinueLabel(_loc.Tr(complete ? "hub.end_season" : "hub.next_match"));
            _shell.SetAdvanceVisible(!complete);
            _shell.SetBadge("inbox", _inbox.UnreadCount);
        }

        // ---------------------------------------------------------------- section catalogue

        private static readonly Dictionary<string, HubShortcut> SectionShortcuts =
            new Dictionary<string, HubShortcut>
            {
                { "inbox", HubShortcut.Inbox },
                { "squad", HubShortcut.Squad },
                { "tactics", HubShortcut.Tactics },
                { "training", HubShortcut.Training },
                { "support", HubShortcut.Support },
                { "market", HubShortcut.Market },
                { "scouting", HubShortcut.Scouting },
                { "club", HubShortcut.Club },
                { "career", HubShortcut.Career },
                { "league", HubShortcut.League },
                { "exit", HubShortcut.ExitCareer }
            };

        private List<ShellSectionVm> BuildSections() => new List<ShellSectionVm>
        {
            new ShellSectionVm { Id = "overview", Label = _loc.Tr("hub.overview"), IconId = "overview", InBottomBar = true },
            new ShellSectionVm { Id = "inbox", Label = _loc.Tr("hub.inbox"), IconId = "inbox", InBottomBar = true },
            new ShellSectionVm { Id = "squad", Label = _loc.Tr("hub.squad"), IconId = "squad", InBottomBar = true },
            new ShellSectionVm { Id = "tactics", Label = _loc.Tr("hub.tactics"), IconId = "tactics", InBottomBar = true },
            new ShellSectionVm { Id = "training", Label = _loc.Tr("hub.training"), IconId = "training" },
            new ShellSectionVm { Id = "support", Label = _loc.Tr("hub.support"), IconId = "support" },
            new ShellSectionVm { Id = "market", Label = _loc.Tr("hub.market"), IconId = "market", InBottomBar = true },
            new ShellSectionVm { Id = "scouting", Label = _loc.Tr("hub.scouting"), IconId = "scouting" },
            new ShellSectionVm { Id = "club", Label = _loc.Tr("hub.club"), IconId = "club" },
            new ShellSectionVm { Id = "career", Label = _loc.Tr("hub.career"), IconId = "career" },
            new ShellSectionVm { Id = "league", Label = _loc.Tr("hub.league"), IconId = "league", InBottomBar = true },
            new ShellSectionVm { Id = "exit", Label = _loc.Tr("hub.exit_career"), IconId = "exit" }
        };

        private static string ActiveSectionFor(string screenName)
        {
            switch (screenName)
            {
                case nameof(HubPresenter): return "overview";
                case nameof(InboxScreenPresenter): return "inbox";
                case nameof(SquadScreenPresenter): return "squad";
                case nameof(PlayerProfileScreenPresenter): return "squad";
                case nameof(TacticsScreenPresenter): return "tactics";
                case nameof(TrainingScreenPresenter): return "training";
                case nameof(SupportScreenPresenter): return "support";
                case nameof(MarketScreenPresenter): return "market";
                case nameof(NegotiationScreenPresenter): return "market";
                case nameof(ScoutingScreenPresenter): return "scouting";
                case nameof(ClubScreenPresenter): return "club";
                case nameof(CareerScreenPresenter): return "career";
                case nameof(CareerSeasonEndScreenPresenter): return "career";
                case nameof(LeagueScreenPresenter): return "league";
                default: return null;
            }
        }
    }
}
