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
            // Task 6.9: aligned with the shell look — themed navy background, a centred capped-width
            // column, a section Header instead of the old giant Title, and a Card for the summary block.
            Root = UiKit.ScreenRoot();

            var col = UiKit.PageColumn(UiKit.WidthMedium);
            col.style.flexGrow = 1f;
            Root.Add(col);

            var header = UiKit.Header(tr("careerend.title"));
            header.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.style.marginBottom = UiKit.SpaceSm;
            col.Add(header);

            var panel = UiKit.Card();
            _summary = new Label(string.Empty);
            _summary.style.fontSize = 14;
            _summary.style.color = Color.white;
            _summary.style.whiteSpace = WhiteSpace.Normal;
            _summary.style.marginBottom = 4;
            panel.Add(_summary);

            _standing = new Label(string.Empty);
            _standing.style.fontSize = 13;
            _standing.style.color = new Color(1f, 1f, 1f, 0.85f);
            _standing.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_standing);
            col.Add(panel);

            _banner = new Label(string.Empty);
            _banner.style.fontSize = 15;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.color = Color.white;
            _banner.style.whiteSpace = WhiteSpace.Normal;
            _banner.style.marginTop = 6;
            _banner.style.marginBottom = 8;
            _banner.style.paddingTop = 8;
            _banner.style.paddingBottom = 8;
            _banner.style.paddingLeft = 12;
            _banner.style.paddingRight = 12;
            UiKit.Round(_banner, UiKit.RadiusSm);
            col.Add(_banner);

            col.Add(SectionLabel(tr("careerend.offers_caption")));

            _offers = new ScrollView();
            _offers.style.flexGrow = 1f;
            _offers.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_offers);

            VisualElement footer = UiKit.FooterBar();
            _stayButton = UiKit.MenuButton(tr("careerend.stay"), () => StayClicked?.Invoke());
            _stayButton.style.width = 220;
            _stayButton.style.height = 48;
            _stayButton.style.fontSize = 17;
            footer.Add(_stayButton);
            col.Add(footer);
        }

        private static Label SectionLabel(string caption) => UiKit.SectionLabel(caption);

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
                row.style.minHeight = 52;
                row.style.marginBottom = 4;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.backgroundColor = UiKit.Surface;
                UiKit.Round(row, UiKit.RadiusSm);

                if (vm.Crest != null)
                {
                    vm.Crest.style.marginRight = 8;
                    row.Add(vm.Crest);
                }

                var label = new Label(vm.Text);
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0;
                label.style.fontSize = 14;
                label.style.color = Color.white;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);

                var accept = new Button(() => AcceptClicked?.Invoke(clubId)) { text = vm.ActionLabel };
                accept.style.width = 130;
                accept.style.height = 40;
                accept.style.flexShrink = 0f;
                accept.style.fontSize = 14;
                row.Add(accept);

                _offers.Add(row);
            }
        }
    }
}
