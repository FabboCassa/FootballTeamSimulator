using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Services.Persistence;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>Root screen: continue a saved career, start a new one, switch language.</summary>
    public sealed class MainMenuPresenter : IScreenPresenter
    {
        private readonly IGameSessionService _session;
        private readonly ISaveRepository _saveRepository;
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly ApiClient _api;
        private readonly MainMenuView _view;

        public VisualElement View => _view.Root;

        public MainMenuPresenter(
            IGameSessionService session,
            ISaveRepository saveRepository,
            ScreenNavigator navigator,
            ILocalizationService loc,
            ApiClient api)
        {
            _session = session;
            _saveRepository = saveRepository;
            _navigator = navigator;
            _loc = loc;
            _api = api;
            _view = new MainMenuView(loc.Tr);
        }

        public void Enter()
        {
            _view.ContinueClicked += OnContinue;
            _view.NewCareerClicked += OnNewCareer;
            _view.AccountClicked += OnAccount;
            _view.OnlineLeaguesClicked += OnOnlineLeagues;
            _view.RankedClicked += OnRanked;
            _view.WorldBenchClicked += OnWorldBench;
            _view.LanguageClicked += OnLanguage;
            Refresh();
        }

        public void Exit()
        {
            _view.ContinueClicked -= OnContinue;
            _view.NewCareerClicked -= OnNewCareer;
            _view.AccountClicked -= OnAccount;
            _view.OnlineLeaguesClicked -= OnOnlineLeagues;
            _view.RankedClicked -= OnRanked;
            _view.WorldBenchClicked -= OnWorldBench;
            _view.LanguageClicked -= OnLanguage;
        }

        public void Reveal() => Refresh();

        private void Refresh()
        {
            _view.SetContinueVisible(_saveRepository.HasSave);
            _view.SetWorldBenchVisible(DevFlags.WorldBench);
            _view.SetLanguageLabel(_loc.Tr("mainmenu.language", _loc.CurrentLanguage.ToUpperInvariant()));
            _view.HideError();
        }

        private void OnContinue()
        {
            switch (_session.ContinueCareer())
            {
                case SaveLoadStatus.Success:
                    break;
                case SaveLoadStatus.NotFound:
                    _view.SetContinueVisible(false);
                    break;
                case SaveLoadStatus.IncompatibleVersion:
                    _view.ShowError(_loc.Tr("mainmenu.error.newer_version"));
                    break;
                default:
                    _view.ShowError(_loc.Tr("mainmenu.error.corrupted"));
                    break;
            }
        }

        private void OnNewCareer() => _navigator.Push<CareerSetupPresenter>();

        private void OnAccount() => _navigator.Push<LoginScreenPresenter>();

        // Online leagues need an account: signed in → the leagues list, otherwise the Account screen
        // (sign in there, come back, and tap again).
        private void OnOnlineLeagues()
        {
            if (_api.IsSignedIn)
                _navigator.Push<LeagueListScreenPresenter>();
            else
                _navigator.Push<LoginScreenPresenter>();
        }

        // The ranked ladder needs an account, like online leagues: signed in → ranked home, else Account.
        private void OnRanked()
        {
            if (_api.IsSignedIn)
                _navigator.Push<RankedHomeScreenPresenter>();
            else
                _navigator.Push<LoginScreenPresenter>();
        }

        // Task 11.3: the database bench, which is how Small / Medium / Large get measured on the
        // targets that decide the shipped default (WebGL, mid-range Android) instead of on a desktop.
        private void OnWorldBench() => _navigator.Push<WorldBenchScreenPresenter>();

        private void OnLanguage()
        {
            // Cycle through the available languages.
            var languages = _loc.AvailableLanguages;
            int index = 0;
            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] == _loc.CurrentLanguage)
                {
                    index = i;
                    break;
                }
            }

            _loc.SetLanguage(languages[(index + 1) % languages.Count]);
            _view.UpdateTexts();
            Refresh();
        }
    }
}
