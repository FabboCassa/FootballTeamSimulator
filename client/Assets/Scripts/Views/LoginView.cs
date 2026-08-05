using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view for the account screen (task 7.2b): email/password (+ display name in register
    /// mode) with a login/register toggle, plus a signed-in panel with logout. An advanced
    /// "server URL" field lets a device point at the PC's LAN address for testing. No logic —
    /// exposes field getters + button events; the presenter drives the ApiClient.
    /// </summary>
    public sealed class LoginView
    {
        public event Action SubmitClicked;
        public event Action ToggleModeClicked;
        public event Action LogoutClicked;
        public event Action BackClicked;

        /// <summary>"Delete account" tapped — opens the password panel (Roadmap 10.2a).</summary>
        public event Action DeleteRequested;
        public event Action DeleteConfirmClicked;
        public event Action DeleteCancelClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;

        private readonly VisualElement _formCard;
        private readonly VisualElement _signedInCard;

        private readonly Label _title;
        private readonly Label _nameCaption;
        private readonly TextField _nameField;
        private readonly Label _emailCaption;
        private readonly TextField _emailField;
        private readonly Label _passwordCaption;
        private readonly TextField _passwordField;
        private readonly Button _submitButton;
        private readonly Button _toggleButton;
        private readonly Label _status;

        private readonly Label _serverCaption;
        private readonly TextField _serverField;

        private readonly Label _signedInLabel;
        private readonly Button _logoutButton;

        private readonly Button _deleteButton;
        private readonly VisualElement _deleteCard;
        private readonly Label _deleteWarning;
        private readonly Label _deletePasswordCaption;
        private readonly TextField _deletePasswordField;
        private readonly Button _deleteConfirmButton;
        private readonly Button _deleteCancelButton;
        private readonly Label _deleteStatus;

        private readonly Button _backButton;

        private bool _registerMode;

        public LoginView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.Background);
            var col = UiKit.CenteredColumn(420f);
            Root.Add(col);

            _title = UiKit.Header(string.Empty);
            col.Add(_title);

            // --- sign-in / register form ---
            _formCard = UiKit.Card();
            col.Add(_formCard);

            _nameCaption = UiKit.Caption(string.Empty);
            _formCard.Add(_nameCaption);
            _nameField = Field(isPassword: false);
            _formCard.Add(_nameField);

            _emailCaption = UiKit.Caption(string.Empty);
            _formCard.Add(_emailCaption);
            _emailField = Field(isPassword: false);
            _formCard.Add(_emailField);

            _passwordCaption = UiKit.Caption(string.Empty);
            _formCard.Add(_passwordCaption);
            _passwordField = Field(isPassword: true);
            _formCard.Add(_passwordField);

            _submitButton = UiKit.PrimaryButton(string.Empty, () => SubmitClicked?.Invoke());
            _submitButton.style.marginTop = UiKit.SpaceMd;
            _formCard.Add(_submitButton);

            _toggleButton = UiKit.MenuButton(string.Empty, () => ToggleModeClicked?.Invoke());
            _toggleButton.style.marginTop = UiKit.SpaceSm;
            _toggleButton.style.width = StyleKeyword.Auto;
            _formCard.Add(_toggleButton);

            _status = UiKit.Caption(string.Empty);
            _status.style.marginTop = UiKit.SpaceSm;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.display = DisplayStyle.None;
            _formCard.Add(_status);

            // --- signed-in panel ---
            _signedInCard = UiKit.Card();
            _signedInCard.style.display = DisplayStyle.None;
            col.Add(_signedInCard);

            _signedInLabel = UiKit.Subtitle(string.Empty);
            _signedInLabel.style.whiteSpace = WhiteSpace.Normal;
            _signedInCard.Add(_signedInLabel);

            _logoutButton = UiKit.PrimaryButton(string.Empty, () => LogoutClicked?.Invoke());
            _signedInCard.Add(_logoutButton);

            // Deleting the account is deliberately the quietest control on the screen: an outline
            // button, not a filled one, so it never competes with logout for a mis-tap.
            _deleteButton = UiKit.MenuButton(string.Empty, () => DeleteRequested?.Invoke());
            _deleteButton.style.marginTop = UiKit.SpaceSm;
            _deleteButton.style.color = UiKit.Danger;
            _signedInCard.Add(_deleteButton);

            // --- delete-account panel (hidden until asked for) ---
            _deleteCard = UiKit.Card();
            _deleteCard.style.display = DisplayStyle.None;
            col.Add(_deleteCard);

            _deleteWarning = UiKit.Caption(string.Empty);
            _deleteWarning.style.whiteSpace = WhiteSpace.Normal;
            _deleteWarning.style.color = UiKit.Danger;
            _deleteWarning.style.marginBottom = UiKit.SpaceSm;
            _deleteCard.Add(_deleteWarning);

            _deletePasswordCaption = UiKit.Caption(string.Empty);
            _deleteCard.Add(_deletePasswordCaption);
            _deletePasswordField = Field(isPassword: true);
            _deleteCard.Add(_deletePasswordField);

            _deleteConfirmButton = UiKit.PrimaryButton(string.Empty, () => DeleteConfirmClicked?.Invoke());
            _deleteConfirmButton.style.marginTop = UiKit.SpaceSm;
            _deleteConfirmButton.style.backgroundColor = UiKit.Danger;
            _deleteCard.Add(_deleteConfirmButton);

            _deleteCancelButton = UiKit.MenuButton(string.Empty, () => DeleteCancelClicked?.Invoke());
            _deleteCancelButton.style.marginTop = UiKit.SpaceSm;
            _deleteCard.Add(_deleteCancelButton);

            // The form card's status label is hidden while signed in, so this panel carries its own.
            _deleteStatus = UiKit.Caption(string.Empty);
            _deleteStatus.style.marginTop = UiKit.SpaceSm;
            _deleteStatus.style.whiteSpace = WhiteSpace.Normal;
            _deleteStatus.style.display = DisplayStyle.None;
            _deleteCard.Add(_deleteStatus);

            // --- advanced: server URL ---
            _serverCaption = UiKit.Caption(string.Empty);
            _serverCaption.style.marginTop = UiKit.SpaceMd;
            col.Add(_serverCaption);
            _serverField = Field(isPassword: false);
            col.Add(_serverField);

            _backButton = UiKit.MenuButton(string.Empty, () => BackClicked?.Invoke());
            _backButton.style.marginTop = UiKit.SpaceMd;
            col.Add(_backButton);

            SetMode(false);
            UpdateTexts();
        }

        // ---- getters the presenter reads on submit ----
        public string Email => (_emailField.value ?? string.Empty).Trim();
        public string Password => _passwordField.value ?? string.Empty;
        public string DisplayName => (_nameField.value ?? string.Empty).Trim();
        public string ServerUrl => (_serverField.value ?? string.Empty).Trim();
        public bool IsRegisterMode => _registerMode;

        public void SetServerUrl(string url) => _serverField.SetValueWithoutNotify(url);

        /// <summary>Switches between login and register (shows/hides the display-name field and
        /// relabels the buttons).</summary>
        public void SetMode(bool register)
        {
            _registerMode = register;
            _nameCaption.style.display = register ? DisplayStyle.Flex : DisplayStyle.None;
            _nameField.style.display = register ? DisplayStyle.Flex : DisplayStyle.None;
            _submitButton.text = _tr(register ? "login.register" : "login.login");
            _toggleButton.text = _tr(register ? "login.switch_to_login" : "login.switch_to_register");
            ClearStatus();
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message;
            _status.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _status.style.display = DisplayStyle.Flex;
        }

        public void ClearStatus() => _status.style.display = DisplayStyle.None;

        public void SetBusy(bool busy)
        {
            _submitButton.SetEnabled(!busy);
            _toggleButton.SetEnabled(!busy);
            _logoutButton.SetEnabled(!busy);
            _deleteButton.SetEnabled(!busy);
            _deleteConfirmButton.SetEnabled(!busy);
            _deleteCancelButton.SetEnabled(!busy);
        }

        public void ShowSignedIn(string displayName, string email)
        {
            _formCard.style.display = DisplayStyle.None;
            _signedInCard.style.display = DisplayStyle.Flex;
            _signedInLabel.text = string.Format(_tr("login.signed_in_as"), displayName, email);
        }

        public void ShowSignedOut()
        {
            _signedInCard.style.display = DisplayStyle.None;
            _formCard.style.display = DisplayStyle.Flex;
            HideDeletePanel();
        }

        // ---- delete account (Roadmap 10.2a) ----

        public string DeletePassword => _deletePasswordField.value ?? string.Empty;

        public void ShowDeletePanel()
        {
            _deletePasswordField.SetValueWithoutNotify(string.Empty);
            _deleteStatus.style.display = DisplayStyle.None;
            _deleteCard.style.display = DisplayStyle.Flex;
            _deleteButton.SetEnabled(false);
            _deletePasswordField.Focus();
        }

        public void HideDeletePanel()
        {
            _deleteCard.style.display = DisplayStyle.None;
            _deletePasswordField.SetValueWithoutNotify(string.Empty);
            _deleteStatus.style.display = DisplayStyle.None;
            _deleteButton.SetEnabled(true);
        }

        public void ShowDeleteStatus(string message, bool isError)
        {
            _deleteStatus.text = message;
            _deleteStatus.style.color = isError ? UiKit.Danger : UiKit.Positive;
            _deleteStatus.style.display = DisplayStyle.Flex;
        }

        public void UpdateTexts()
        {
            _title.text = _tr("login.title");
            _nameCaption.text = _tr("login.display_name");
            _emailCaption.text = _tr("login.email");
            _passwordCaption.text = _tr("login.password");
            _serverCaption.text = _tr("login.server_url");
            _logoutButton.text = _tr("login.logout");
            _deleteButton.text = _tr("login.delete_account");
            _deleteWarning.text = _tr("login.delete_warning");
            _deletePasswordCaption.text = _tr("login.delete_password");
            _deleteConfirmButton.text = _tr("login.delete_confirm");
            _deleteCancelButton.text = _tr("common.cancel");
            _backButton.text = _tr("common.back");
            SetMode(_registerMode);
        }

        private static TextField Field(bool isPassword)
        {
            var field = new TextField { isPasswordField = isPassword };
            field.style.marginBottom = UiKit.SpaceSm;
            field.style.minHeight = 40;
            return field;
        }
    }
}
