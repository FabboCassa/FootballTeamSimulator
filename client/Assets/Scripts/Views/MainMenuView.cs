using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Dumb view: exposes events, renders placeholder UI. No logic.
    /// Receives a translate delegate (views cannot reference Services);
    /// UpdateTexts re-reads it so a language switch refreshes this screen live.
    /// </summary>
    public sealed class MainMenuView
    {
        public event Action ContinueClicked;
        public event Action NewCareerClicked;
        public event Action AccountClicked;
        public event Action OnlineLeaguesClicked;
        public event Action RankedClicked;
        public event Action LanguageClicked;
        /// <summary>Dev only (task 11.3): the database bench.</summary>
        public event Action WorldBenchClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Button _continueButton;
        private readonly Button _newCareerButton;
        private readonly Button _accountButton;
        private readonly Button _onlineButton;
        private readonly Button _rankedButton;
        private readonly Button _benchButton;
        private readonly Button _languageButton;
        private readonly Label _errorLabel;

        public MainMenuView(Func<string, string> tr)
        {
            _tr = tr;

            Root = UiKit.Screen(UiKit.MenuGreen);
            _title = UiKit.Title(string.Empty);
            Root.Add(_title);
            _subtitle = UiKit.Subtitle(string.Empty);
            Root.Add(_subtitle);

            _continueButton = UiKit.MenuButton(string.Empty, () => ContinueClicked?.Invoke());
            Root.Add(_continueButton);
            _newCareerButton = UiKit.MenuButton(string.Empty, () => NewCareerClicked?.Invoke());
            Root.Add(_newCareerButton);
            _accountButton = UiKit.MenuButton(string.Empty, () => AccountClicked?.Invoke());
            Root.Add(_accountButton);
            _onlineButton = UiKit.MenuButton(string.Empty, () => OnlineLeaguesClicked?.Invoke());
            Root.Add(_onlineButton);
            _rankedButton = UiKit.MenuButton(string.Empty, () => RankedClicked?.Invoke());
            Root.Add(_rankedButton);
            _benchButton = UiKit.MenuButton(string.Empty, () => WorldBenchClicked?.Invoke());
            _benchButton.style.display = DisplayStyle.None;
            Root.Add(_benchButton);
            _languageButton = UiKit.MenuButton(string.Empty, () => LanguageClicked?.Invoke());
            Root.Add(_languageButton);

            _errorLabel = UiKit.Subtitle(string.Empty);
            _errorLabel.style.color = new Color(1f, 0.55f, 0.55f);
            _errorLabel.style.display = DisplayStyle.None;
            _errorLabel.style.marginTop = 16;
            Root.Add(_errorLabel);

            UpdateTexts();
        }

        /// <summary>Re-reads all static texts (called after a language switch).</summary>
        public void UpdateTexts()
        {
            _title.text = _tr("app.title");
            _subtitle.text = _tr("mainmenu.subtitle");
            _continueButton.text = _tr("mainmenu.continue");
            _newCareerButton.text = _tr("mainmenu.new_career");
            _accountButton.text = _tr("mainmenu.account");
            _onlineButton.text = _tr("mainmenu.online_leagues");
            _rankedButton.text = _tr("mainmenu.ranked");
            _benchButton.text = _tr("mainmenu.world_bench");
        }

        public void SetLanguageLabel(string text) => _languageButton.text = text;

        /// <summary>Dev only: the database bench button is hidden in a release build.</summary>
        public void SetWorldBenchVisible(bool visible)
        {
            _benchButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetContinueVisible(bool visible)
        {
            _continueButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowError(string message)
        {
            _errorLabel.text = message;
            _errorLabel.style.display = DisplayStyle.Flex;
        }

        public void HideError()
        {
            _errorLabel.style.display = DisplayStyle.None;
        }
    }
}
