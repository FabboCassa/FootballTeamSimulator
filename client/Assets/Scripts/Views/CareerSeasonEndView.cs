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
    /// Career season-end decision (task 5.6), redrawn in task 14.4 on the centred page: the verdict
    /// first — a coloured card that says safe / warned / sacked — then how the season went on the
    /// raised card, then the clubs courting you as rows (crest, offer, Accept) and Stay as a ghost.
    /// Dumb view: the presenter formats every label and decides what's offered.
    /// </summary>
    public sealed class CareerSeasonEndView
    {
        public event Action<int> AcceptClicked;
        public event Action StayClicked;

        public VisualElement Root { get; }

        private readonly Label _summary;
        private readonly Label _standing;
        private readonly Label _banner;
        private readonly VisualElement _offersCard;
        private readonly VisualElement _offers;
        private readonly Button _stayButton;

        public CareerSeasonEndView(Func<string, string> tr)
        {
            PageParts page = UiKit.CenterPage(tr("careerend.kicker"), tr("careerend.title"), UiKit.WidthMedium);
            Root = page.Root;
            VisualElement col = page.Column;

            _banner = new Label(string.Empty);
            _banner.AddToClassList("fts-verdict");
            _banner.style.whiteSpace = WhiteSpace.Normal;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(_banner);

            VisualElement summary = UiKit.RaisedCard();
            summary.AddToClassList("fts-careerend__summary");
            _summary = new Label(string.Empty);
            _summary.AddToClassList("fts-careerend__text");
            _summary.style.whiteSpace = WhiteSpace.Normal;
            summary.Add(_summary);
            _standing = new Label(string.Empty);
            _standing.AddToClassList("fts-careerend__meta");
            _standing.style.whiteSpace = WhiteSpace.Normal;
            summary.Add(_standing);
            col.Add(summary);

            _offersCard = UiKit.OptionCard();
            _offersCard.Add(UiKit.BlockHead(tr("careerend.offers_caption")));
            _offers = new VisualElement();
            _offersCard.Add(_offers);
            col.Add(_offersCard);

            _stayButton = UiKit.GhostButton(tr("careerend.stay"), () => StayClicked?.Invoke());
            _stayButton.AddToClassList("fts-careerend__stay");
            col.Add(_stayButton);
        }

        public void SetSummary(string text) => _summary.text = text ?? string.Empty;
        public void SetStanding(string text) => _standing.text = text ?? string.Empty;

        /// <summary>band: 0 = sacked (red), 1 = warned (amber), 2 = safe (green).</summary>
        public void SetBanner(string text, int band)
        {
            _banner.text = text ?? string.Empty;
            _banner.EnableInClassList("fts-verdict--bad", band <= 0);
            _banner.EnableInClassList("fts-verdict--warn", band == 1);
            _banner.EnableInClassList("fts-verdict--good", band >= 2);
        }

        public void SetStay(string label, bool visible)
        {
            _stayButton.text = label ?? string.Empty;
            _stayButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetOffers(IReadOnlyList<OfferRowVm> rows)
        {
            _offers.Clear();
            _offersCard.style.display = rows.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (OfferRowVm vm in rows)
            {
                int clubId = vm.ClubId;

                var row = new VisualElement();
                row.AddToClassList("fts-careerend__offer");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                if (vm.Crest != null)
                {
                    vm.Crest.AddToClassList("fts-careerend__crest");
                    vm.Crest.style.flexShrink = 0f;
                    row.Add(vm.Crest);
                }

                var label = new Label(vm.Text);
                label.AddToClassList("fts-careerend__text");
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                Button accept = UiKit.PrimaryButton(vm.ActionLabel, () => AcceptClicked?.Invoke(clubId));
                accept.AddToClassList("fts-careerend__accept");
                row.Add(accept);

                _offers.Add(row);
            }
        }
    }
}
