using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// End-of-season summary (task 2.7), redrawn in task 14.4 on the centred page: the season as the
    /// title, the champion on the raised card (big crest, name in Bebas), promotions and relegations
    /// on two cards, and one accent Continue.
    /// </summary>
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
            PageParts page = UiKit.CenterPage(tr("season_end.kicker"), string.Empty);
            Root = page.Root;
            _title = page.Title;
            VisualElement col = page.Column;

            VisualElement championCard = UiKit.RaisedCard();
            championCard.AddToClassList("fts-end__champion");
            _championCrestSlot = new VisualElement();
            _championCrestSlot.AddToClassList("fts-end__crest");
            _championCrestSlot.style.alignItems = Align.Center;
            _championCrestSlot.style.justifyContent = Justify.Center;
            _championCrestSlot.style.alignSelf = Align.Center;
            championCard.Add(_championCrestSlot);
            _champion = new Label(string.Empty);
            _champion.AddToClassList("fts-end__championname");
            UiKit.UseDisplayFont(_champion);
            _champion.style.unityTextAlign = TextAnchor.MiddleCenter;
            _champion.style.whiteSpace = WhiteSpace.Normal;
            championCard.Add(_champion);
            col.Add(championCard);

            _promoted = Block(col, "fts-end__up");
            _relegated = Block(col, "fts-end__down");

            Button next = UiKit.CtaButton(tr("common.continue"), string.Empty, () => ContinueClicked?.Invoke());
            next.AddToClassList("fts-end__cta");
            col.Add(next);
        }

        public void SetTitle(string text) => _title.text = text ?? string.Empty;
        public void SetChampion(string text) => _champion.text = text ?? string.Empty;

        /// <summary>Shows the champion club's crest (task 6.8).</summary>
        public void SetChampionCrest(VisualElement crest)
        {
            _championCrestSlot.Clear();
            if (crest != null) _championCrestSlot.Add(crest);
        }

        public void SetPromoted(string text) => SetBlock(_promoted, text);
        public void SetRelegated(string text) => SetBlock(_relegated, text);

        private static Label Block(VisualElement col, string cls)
        {
            VisualElement card = UiKit.OptionCard();
            card.AddToClassList(cls);
            var label = new Label(string.Empty);
            label.AddToClassList("fts-end__line");
            label.style.whiteSpace = WhiteSpace.Normal;
            card.Add(label);
            col.Add(card);
            return label;
        }

        private static void SetBlock(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.parent.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
