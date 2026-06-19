using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>End-of-season summary: champion, promotions, relegations.</summary>
    public sealed class SeasonEndView
    {
        public event Action ContinueClicked;

        public VisualElement Root { get; }

        private readonly Label _title;
        private readonly Label _champion;
        private readonly Label _promoted;
        private readonly Label _relegated;

        public SeasonEndView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.MenuGreen);

            _title = UiKit.Title(string.Empty);
            Root.Add(_title);

            _champion = UiKit.Subtitle(string.Empty);
            _champion.style.fontSize = 20;
            _champion.style.color = new Color(1f, 0.85f, 0.4f);
            Root.Add(_champion);

            _promoted = MultilineLabel();
            Root.Add(_promoted);

            _relegated = MultilineLabel();
            Root.Add(_relegated);

            Root.Add(UiKit.MenuButton(tr("common.continue"), () => ContinueClicked?.Invoke()));
        }

        public void SetTitle(string text) => _title.text = text;
        public void SetChampion(string text) => _champion.text = text;
        public void SetPromoted(string text) => _promoted.text = text;
        public void SetRelegated(string text) => _relegated.text = text;

        private static Label MultilineLabel()
        {
            var label = UiKit.Subtitle(string.Empty);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.maxWidth = 480;
            label.style.marginBottom = 8;
            return label;
        }
    }
}
