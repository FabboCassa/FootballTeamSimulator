using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
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
        private readonly MainMenuView _view;

        public VisualElement View => _view.Root;

        public MainMenuPresenter(
            IGameSessionService session,
            ISaveRepository saveRepository,
            ScreenNavigator navigator,
            ILocalizationService loc)
        {
            _session = session;
            _saveRepository = saveRepository;
            _navigator = navigator;
            _loc = loc;
            _view = new MainMenuView(loc.Tr);
        }

        public void Enter()
        {
            _view.ContinueClicked += OnContinue;
            _view.NewCareerClicked += OnNewCareer;
            _view.LanguageClicked += OnLanguage;
            Refresh();
        }

        public void Exit()
        {
            _view.ContinueClicked -= OnContinue;
            _view.NewCareerClicked -= OnNewCareer;
            _view.LanguageClicked -= OnLanguage;
        }

        public void Reveal() => Refresh();

        private void Refresh()
        {
            _view.SetContinueVisible(_saveRepository.HasSave);
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
