using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Create-league screen (task 8.1b): name + size (mode fixed to "advance when all ready" for now).
    /// On success it stamps the new league into <see cref="LeagueSelection"/> and opens its lobby.
    /// </summary>
    public sealed class CreateLeagueScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly LeagueApiService _leagues;
        private readonly LeagueSelection _selection;
        private readonly CreateLeagueView _view;

        private bool _busy;

        public VisualElement View => _view.Root;

        public CreateLeagueScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, LeagueApiService leagues, LeagueSelection selection)
        {
            _navigator = navigator;
            _loc = loc;
            _leagues = leagues;
            _selection = selection;
            _view = new CreateLeagueView(loc.Tr);
        }

        public void Enter()
        {
            _view.CreateClicked += OnCreate;
            _view.BackClicked += OnBack;
        }

        public void Exit()
        {
            _view.CreateClicked -= OnCreate;
            _view.BackClicked -= OnBack;
        }

        private void OnBack() => _navigator.Pop();

        private void OnCreate() => CreateAsync().Forget();

        private async UniTaskVoid CreateAsync()
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_view.LeagueName))
            {
                _view.ShowStatus(_loc.Tr("create.error.name"), isError: true);
                return;
            }

            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("create.creating"), isError: false);

            var result = await _leagues.CreateAsync(_view.LeagueName, _view.Size, LeagueMode.AllReady);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                _selection.Select(result.Value);
                _navigator.Pop();                              // back to the list
                _navigator.Push<LeagueLobbyScreenPresenter>(); // open the new league's lobby
            }
            else
            {
                _view.ShowStatus(_loc.Tr(LeagueErrorFormat.Key(result.Error)), isError: true);
            }
        }
    }
}
