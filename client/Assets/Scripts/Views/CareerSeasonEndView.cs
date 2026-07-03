using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One job offer on the season-end decision screen: a club to accept, with a description.</summary>
    public sealed class OfferRowVm
    {
        public int ClubId;
        public string Text;        // "Club X · Div 1 · stature 70"
        public string ActionLabel; // "Accept"
        public VisualElement Crest; // offering club's crest (task 6.8)
    }

    /// <summary>
    /// Career season-end decision screen (task 5.6): how the season measured against the board's
    /// objective, the updated confidence/reputation, whether the user was warned or sacked, and the
    /// clubs courting him — accept one to move, or stay. Dumb view: the presenter formats every label
    /// and decides what's offered; the view emits Accept(clubId)/Stay and renders strings. No Sim.Core.
    /// </summary>
    public sealed class CareerSeasonEndView
    {
        public event Action<int> AcceptClicked;
        public event Action StayClicked;

        public VisualElement Root { get; }

        private readonly Label _summary;
        private readonly Label _standing;
        private readonly Label _banner;
        private readonly ScrollView _offers;
        private readonly Button _stayButton;

        public CareerSeasonEndView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("careerend.title"));
            title.style.fontSize = 28;
            Root.Add(title);

            _summary = UiKit.Subtitle(string.Empty);
            _summary.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(_summary);

            _standing = UiKit.Subtitle(string.Empty);
            _standing.style.fontSize = 15;
            Root.Add(_standing);

            _banner = new Label(string.Empty);
            _banner.style.fontSize = 16;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.color = Color.white;
            _banner.style.whiteSpace = WhiteSpace.Normal;
            _banner.style.marginTop = 6;
            _banner.style.marginBottom = 8;
            _banner.style.paddingTop = 6;
            _banner.style.paddingBottom = 6;
            _banner.style.paddingLeft = 10;
            _banner.style.paddingRight = 10;
            Root.Add(_banner);

            var offersCaption = new Label(tr("careerend.offers_caption"));
            offersCaption.style.color = new Color(1f, 1f, 1f, 0.7f);
            offersCaption.style.fontSize = 13;
            offersCaption.style.marginBottom = 4;
            Root.Add(offersCaption);

            _offers = new ScrollView();
            _offers.style.flexGrow = 1f;
            Root.Add(_offers);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            _stayButton = UiKit.MenuButton(tr("careerend.stay"), () => StayClicked?.Invoke());
            _stayButton.style.width = 220;
            _stayButton.style.height = 48;
            _stayButton.style.fontSize = 17;
            footer.Add(_stayButton);
            Root.Add(footer);
        }

        public void SetSummary(string text) => _summary.text = text;
        public void SetStanding(string text) => _standing.text = text;

        /// <summary>band: 0 = sacked (red), 1 = warned (amber), 2 = safe (green).</summary>
        public void SetBanner(string text, int band)
        {
            _banner.text = text;
            _banner.style.backgroundColor =
                band <= 0 ? new Color(0.80f, 0.25f, 0.25f, 0.85f) :
                band == 1 ? new Color(0.85f, 0.65f, 0.20f, 0.85f) :
                            new Color(0.30f, 0.70f, 0.35f, 0.85f);
        }

        public void SetStay(string label, bool visible)
        {
            _stayButton.text = label;
            _stayButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetOffers(IReadOnlyList<OfferRowVm> rows)
        {
            _offers.Clear();
            foreach (OfferRowVm vm in rows)
            {
                int clubId = vm.ClubId;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 50;
                row.style.marginBottom = 3;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);

                if (vm.Crest != null)
                {
                    vm.Crest.style.marginRight = 8;
                    row.Add(vm.Crest);
                }

                var label = new Label(vm.Text);
                label.style.flexGrow = 1f;
                label.style.fontSize = 14;
                label.style.color = Color.white;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                var accept = new Button(() => AcceptClicked?.Invoke(clubId)) { text = vm.ActionLabel };
                accept.style.width = 130;
                accept.style.height = 40;
                accept.style.fontSize = 14;
                row.Add(accept);

                _offers.Add(row);
            }
        }
    }
}
