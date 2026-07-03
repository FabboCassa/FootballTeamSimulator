using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Tactics screen (task 3.3, relaid out in 6.9): a formation selector, a large hero pitch
    /// showing the chosen shape, four instruction toggles and a familiarity readout — all in a
    /// centred, capped-width column so it reads like the other screens and the pitch is big and
    /// clear. The next-opponent preview was removed (6.9): scouting the opponent before a match is
    /// its own screen (roadmap 6.11), not shown inside the user's own tactics. Dumb view.
    /// </summary>
    public sealed class TacticsView
    {
        public event Action FormationCycleClicked;
        public event Action MentalityCycleClicked;
        public event Action PressingCycleClicked;
        public event Action TempoCycleClicked;
        public event Action WidthCycleClicked;
        public event Action SaveClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Button _formationButton;
        private readonly Button _mentalityButton;
        private readonly Button _pressingButton;
        private readonly Button _tempoButton;
        private readonly Button _widthButton;
        private readonly Label _familiarity;
        private readonly Label _status;
        private readonly PitchFormationView _shapePitch;

        public TacticsView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Root.Add(scroll);

            var col = UiKit.CenteredColumn(640f);
            scroll.Add(col);

            _header = UiKit.Caption(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            col.Add(SectionLabel(tr("tactics.formation_caption")));
            _formationButton = CycleButton(() => FormationCycleClicked?.Invoke());
            col.Add(_formationButton);

            // Live shape preview — the hero of the screen (task 6.9: big + clear). A tall box lets
            // the pitch fill the column width so the eleven tokens spread out (no name/disc overlap).
            _shapePitch = new PitchFormationView(mirror: false);
            col.Add(PitchBox(_shapePitch, 380));

            col.Add(SectionLabel(tr("tactics.instructions_caption")));
            _mentalityButton = CycleButton(() => MentalityCycleClicked?.Invoke());
            col.Add(_mentalityButton);
            _pressingButton = CycleButton(() => PressingCycleClicked?.Invoke());
            col.Add(_pressingButton);
            _tempoButton = CycleButton(() => TempoCycleClicked?.Invoke());
            col.Add(_tempoButton);
            _widthButton = CycleButton(() => WidthCycleClicked?.Invoke());
            col.Add(_widthButton);

            _familiarity = UiKit.Caption(string.Empty);
            _familiarity.style.marginTop = UiKit.SpaceSm;
            _familiarity.style.marginBottom = UiKit.SpaceSm;
            col.Add(_familiarity);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = UiKit.SpaceSm;
            footer.style.marginBottom = UiKit.SpaceXs;
            footer.style.flexShrink = 0f;
            footer.Add(FooterButton(tr("tactics.save"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Caption(string.Empty);
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _status.style.flexShrink = 0f;
            Root.Add(_status);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetFormation(string text) => _formationButton.text = text;
        public void SetMentality(string text) => _mentalityButton.text = text;
        public void SetPressing(string text) => _pressingButton.text = text;
        public void SetTempo(string text) => _tempoButton.text = text;
        public void SetWidth(string text) => _widthButton.text = text;
        public void SetFamiliarity(string text) => _familiarity.text = text;
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Updates the live shape preview with the user's XI in the chosen formation.</summary>
        public void SetShape(System.Collections.Generic.IReadOnlyList<PitchTokenVm> tokens) =>
            _shapePitch.SetTokens(tokens);

        private static VisualElement PitchBox(PitchFormationView pitch, float height)
        {
            var box = new VisualElement();
            box.style.height = height;
            box.style.width = Length.Percent(100);
            box.style.marginBottom = UiKit.SpaceSm;
            box.style.flexShrink = 0f;
            UiKit.Round(box, UiKit.RadiusMd);
            box.style.overflow = Overflow.Hidden;
            box.Add(pitch);
            return box;
        }

        private static Label SectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = UiKit.TextMuted;
            label.style.fontSize = 13;
            label.style.marginTop = UiKit.SpaceSm;
            label.style.marginBottom = UiKit.SpaceXs;
            return label;
        }

        private static Button CycleButton(Action onClick)
        {
            var button = new Button(onClick);
            button.style.height = 42;
            button.style.fontSize = 15;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginBottom = UiKit.SpaceXs;
            button.style.width = Length.Percent(100);
            return button;
        }

        private static Button FooterButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.width = 160;
            button.style.height = 44;
            button.style.fontSize = 16;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            return button;
        }
    }
}
