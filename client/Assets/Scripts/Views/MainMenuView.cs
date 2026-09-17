using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Main menu, redrawn in task 14.4 on the centred page: the game's name in Bebas under a kicker,
    /// then three blocks — CAREER (Continue as the accent CTA, New career), ONLINE (Ranked, private
    /// leagues, account) and the small print (language, the dev bench). Dumb view: exposes events;
    /// <see cref="UpdateTexts"/> re-reads the translate delegate so a language switch refreshes live.
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
        private readonly Label _careerCaption;
        private readonly Label _onlineCaption;
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

            PageParts page = UiKit.CenterPage(string.Empty, string.Empty);
            Root = page.Root;
            Root.AddToClassList("fts-menu");
            _title = page.Title;
            VisualElement col = page.Column;

            _subtitle = new Label(string.Empty);
            _subtitle.AddToClassList("fts-menu__subtitle");
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_subtitle);

            // ---- career
            VisualElement career = UiKit.RaisedCard();
            career.AddToClassList("fts-menu__block");
            _careerCaption = UiKit.SectionLabel(string.Empty);
            _careerCaption.style.marginTop = 0;
            career.Add(_careerCaption);
            _continueButton = UiKit.PrimaryButton(string.Empty, () => ContinueClicked?.Invoke());
            _continueButton.AddToClassList("fts-menu__btn");
            _continueButton.AddToClassList("fts-menu__btn--cta");
            career.Add(_continueButton);
            _newCareerButton = UiKit.GhostButton(string.Empty, () => NewCareerClicked?.Invoke());
            _newCareerButton.AddToClassList("fts-menu__btn");
            career.Add(_newCareerButton);
            col.Add(career);

            // ---- online
            VisualElement online = UiKit.OptionCard();
            online.AddToClassList("fts-menu__block");
            _onlineCaption = UiKit.SectionLabel(string.Empty);
            _onlineCaption.style.marginTop = 0;
            online.Add(_onlineCaption);
            _rankedButton = UiKit.GhostButton(string.Empty, () => RankedClicked?.Invoke());
            _rankedButton.AddToClassList("fts-menu__btn");
            online.Add(_rankedButton);
            _onlineButton = UiKit.GhostButton(string.Empty, () => OnlineLeaguesClicked?.Invoke());
            _onlineButton.AddToClassList("fts-menu__btn");
            online.Add(_onlineButton);
            _accountButton = UiKit.GhostButton(string.Empty, () => AccountClicked?.Invoke());
            _accountButton.AddToClassList("fts-menu__btn");
            online.Add(_accountButton);
            col.Add(online);

            // ---- small print
            var foot = new VisualElement();
            foot.AddToClassList("fts-menu__foot");
            _languageButton = UiKit.GhostButton(string.Empty, () => LanguageClicked?.Invoke());
            _languageButton.AddToClassList("fts-menu__small");
            foot.Add(_languageButton);
            _benchButton = UiKit.GhostButton(string.Empty, () => WorldBenchClicked?.Invoke());
            _benchButton.AddToClassList("fts-menu__small");
            _benchButton.style.display = DisplayStyle.None;
            foot.Add(_benchButton);
            col.Add(foot);

            _errorLabel = new Label(string.Empty);
            _errorLabel.AddToClassList("fts-menu__error");
            _errorLabel.style.whiteSpace = WhiteSpace.Normal;
            _errorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _errorLabel.style.display = DisplayStyle.None;
            col.Add(_errorLabel);

            UpdateTexts();
        }

        /// <summary>Re-reads all static texts (called after a language switch).</summary>
        public void UpdateTexts()
        {
            _title.text = _tr("app.title");
            _subtitle.text = _tr("mainmenu.subtitle");
            _careerCaption.text = _tr("mainmenu.section.career").ToUpperInvariant();
            _onlineCaption.text = _tr("mainmenu.section.online").ToUpperInvariant();
            _continueButton.text = _tr("mainmenu.continue");
            _newCareerButton.text = _tr("mainmenu.new_career");
            _accountButton.text = _tr("mainmenu.account");
            _onlineButton.text = _tr("mainmenu.online_leagues");
            _rankedButton.text = _tr("mainmenu.ranked");
            _benchButton.text = _tr("mainmenu.world_bench");
        }

        public void SetLanguageLabel(string text) => _languageButton.text = text;

        /// <summary>Dev only: the database bench button is hidden in a release build.</summary>
        public void SetWorldBenchVisible(bool visible) =>
            _benchButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>No save → New career becomes the accent action instead of Continue.</summary>
        public void SetContinueVisible(bool visible)
        {
            _continueButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _newCareerButton.EnableInClassList("fts-menu__btn--cta-ghost", !visible);
        }

        public void ShowError(string message)
        {
            _errorLabel.text = message;
            _errorLabel.style.display = DisplayStyle.Flex;
        }

        public void HideError() => _errorLabel.style.display = DisplayStyle.None;
    }
}
