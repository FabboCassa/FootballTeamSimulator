using Cysharp.Threading.Tasks;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Services.Online;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Account screen (task 7.2b): register or log in against the backend, or show the signed-in
    /// account with a logout. Offline-first — reachable from the Main Menu but never required to
    /// play. Talks to the <see cref="ApiClient"/> (which owns the rotating token store); this
    /// presenter only maps outcomes to localized status messages.
    /// </summary>
    public sealed class LoginScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly ILocalizationService _loc;
        private readonly ApiClient _api;
        private readonly LoginView _view;

        private bool _busy;

        public VisualElement View => _view.Root;

        public LoginScreenPresenter(ScreenNavigator navigator, ILocalizationService loc, ApiClient api)
        {
            _navigator = navigator;
            _loc = loc;
            _api = api;
            _view = new LoginView(loc.Tr);
        }

        public void Enter()
        {
            _view.SubmitClicked += OnSubmit;
            _view.ToggleModeClicked += OnToggleMode;
            _view.LogoutClicked += OnLogout;
            _view.BackClicked += OnBack;

            _view.SetServerUrl(_api.BaseUrl);
            RefreshAuthState();
        }

        public void Exit()
        {
            _view.SubmitClicked -= OnSubmit;
            _view.ToggleModeClicked -= OnToggleMode;
            _view.LogoutClicked -= OnLogout;
            _view.BackClicked -= OnBack;
        }

        private void RefreshAuthState()
        {
            if (_api.IsSignedIn && _api.Profile.HasValue)
                _view.ShowSignedIn(_api.Profile.Value.DisplayName, _api.Profile.Value.Email);
            else
                _view.ShowSignedOut();
        }

        private void OnToggleMode() => _view.SetMode(!_view.IsRegisterMode);

        private void OnBack() => _navigator.Pop();

        private void OnSubmit() => SubmitAsync().Forget();

        private async UniTaskVoid SubmitAsync()
        {
            if (_busy) return;

            // Basic client-side guard before hitting the network.
            if (string.IsNullOrEmpty(_view.Email) || string.IsNullOrEmpty(_view.Password)
                || (_view.IsRegisterMode && string.IsNullOrEmpty(_view.DisplayName)))
            {
                _view.ShowStatus(_loc.Tr("login.error.missing_fields"), isError: true);
                return;
            }

            _api.SetBaseUrl(_view.ServerUrl);

            _busy = true;
            _view.SetBusy(true);
            _view.ShowStatus(_loc.Tr("login.submitting"), isError: false);

            var result = _view.IsRegisterMode
                ? await _api.RegisterAsync(_view.Email, _view.Password, _view.DisplayName)
                : await _api.LoginAsync(_view.Email, _view.Password);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                _view.ShowStatus(_loc.Tr(_view.IsRegisterMode ? "login.success.registered" : "login.success.logged_in"), isError: false);
                RefreshAuthState();
            }
            else
            {
                _view.ShowStatus(_loc.Tr(ErrorKey(result.Error)), isError: true);
            }
        }

        private void OnLogout() => LogoutAsync().Forget();

        private async UniTaskVoid LogoutAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);

            await _api.LogoutAsync();

            _busy = false;
            _view.SetBusy(false);
            RefreshAuthState();
        }

        private static string ErrorKey(ApiError error) => error switch
        {
            ApiError.EmailInUse => "login.error.email_in_use",
            ApiError.WeakPassword => "login.error.weak_password",
            ApiError.Validation => "login.error.validation",
            ApiError.InvalidCredentials => "login.error.invalid_credentials",
            ApiError.Network => "login.error.network",
            _ => "login.error.server"
        };
    }
}
