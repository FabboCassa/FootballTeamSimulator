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
        private readonly VisualElement _championCrestSlot;

        public SeasonEndView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.MenuGreen);

            _title = UiKit.Title(string.Empty);
            Root.Add(_title);

            // Champion line: crest + name (task 6.8).
            var championRow = new VisualElement();
            championRow.style.flexDirection = FlexDirection.Row;
            championRow.style.alignItems = Align.Center;
            championRow.style.justifyContent = Justify.Center;
            championRow.style.marginBottom = UiKit.SpaceMd;

            _championCrestSlot = new VisualElement();
            _championCrestSlot.style.width = 40;
            _championCrestSlot.style.height = 40;
            _championCrestSlot.style.marginRight = UiKit.SpaceSm;
            _championCrestSlot.style.alignItems = Align.Center;
            _championCrestSlot.style.justifyContent = Justify.Center;
            championRow.Add(_championCrestSlot);

            _champion = UiKit.Subtitle(string.Empty);
            _champion.style.fontSize = 20;
            _champion.style.marginBottom = 0;
            _champion.style.color = new Color(1f, 0.85f, 0.4f);
            championRow.Add(_champion);
            Root.Add(championRow);

            _promoted = MultilineLabel();
            Root.Add(_promoted);

            _relegated = MultilineLabel();
            Root.Add(_relegated);

            Root.Add(UiKit.MenuButton(tr("common.continue"), () => ContinueClicked?.Invoke()));
        }

        public void SetTitle(string text) => _title.text = text;
        public void SetChampion(string text) => _champion.text = text;

        /// <summary>Shows the champion club's crest next to its name (task 6.8).</summary>
        public void SetChampionCrest(VisualElement crest)
        {
            _championCrestSlot.Clear();
            if (crest != null) _championCrestSlot.Add(crest);
        }
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
