using Cysharp.Threading.Tasks;
using Fts.Services;
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
        private readonly OverlayHost _overlay;
        private readonly LoginView _view;

        private bool _busy;

        public VisualElement View => _view.Root;

        public LoginScreenPresenter(
            ScreenNavigator navigator, ILocalizationService loc, ApiClient api, OverlayHost overlay)
        {
            _navigator = navigator;
            _loc = loc;
            _api = api;
            _overlay = overlay;
            _view = new LoginView(loc.Tr);
        }

        public void Enter()
        {
            _view.SubmitClicked += OnSubmit;
            _view.ToggleModeClicked += OnToggleMode;
            _view.LogoutClicked += OnLogout;
            _view.BackClicked += OnBack;
            _view.DeleteRequested += OnDeleteRequested;
            _view.DeleteConfirmClicked += OnDeleteConfirm;
            _view.DeleteCancelClicked += OnDeleteCancel;

            _view.SetServerUrl(_api.BaseUrl);
            RefreshAuthState();
        }

        public void Exit()
        {
            _view.SubmitClicked -= OnSubmit;
            _view.ToggleModeClicked -= OnToggleMode;
            _view.LogoutClicked -= OnLogout;
            _view.BackClicked -= OnBack;
            _view.DeleteRequested -= OnDeleteRequested;
            _view.DeleteConfirmClicked -= OnDeleteConfirm;
            _view.DeleteCancelClicked -= OnDeleteCancel;
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

        // ---- delete account (Roadmap 10.2a) ----
        // Two gates before anything irreversible happens: the password panel, then a modal confirm.
        // Both mobile stores require this route to exist inside the app.

        private void OnDeleteRequested() => _view.ShowDeletePanel();

        private void OnDeleteCancel() => _view.HideDeletePanel();

        private void OnDeleteConfirm()
        {
            if (_busy) return;

            if (string.IsNullOrEmpty(_view.DeletePassword))
            {
                _view.ShowDeleteStatus(_loc.Tr("login.error.missing_fields"), isError: true);
                return;
            }

            Dialogs.Confirm(
                _overlay, _loc,
                "login.delete_dialog_title", "login.delete_dialog_message", "login.delete_confirm",
                () => DeleteAsync().Forget());
        }

        private async UniTaskVoid DeleteAsync()
        {
            if (_busy) return;
            _busy = true;
            _view.SetBusy(true);
            _view.ShowDeleteStatus(_loc.Tr("login.deleting"), isError: false);

            var result = await _api.DeleteAccountAsync(_view.DeletePassword);

            _busy = false;
            _view.SetBusy(false);

            if (result.Success)
            {
                // The session is already gone: the screen drops back to the signed-out form.
                _view.HideDeletePanel();
                RefreshAuthState();
                _view.ShowStatus(_loc.Tr("login.delete_done"), isError: false);
                Dialogs.Toast(_overlay, _loc, "login.delete_done");
                return;
            }

            _view.ShowDeleteStatus(_loc.Tr(DeleteErrorKey(result.Error)), isError: true);
        }

        private static string DeleteErrorKey(ApiError error) => error switch
        {
            // The server re-checks the password, so a 401 here means "wrong password", not "signed out".
            ApiError.InvalidCredentials => "login.error.wrong_password",
            ApiError.Network => "login.error.network",
            _ => "login.error.server"
        };

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
