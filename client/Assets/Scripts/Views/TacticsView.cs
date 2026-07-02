using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Tactics screen (task 3.3): a formation selector, four instruction toggles,
    /// a familiarity readout and a read-only next-opponent panel. Dumb view — the
    /// presenter owns the tactic state and cycles the enum values.
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
        private readonly Label _opponent;
        private readonly Label _status;
        private readonly PitchFormationView _shapePitch;
        private readonly PitchFormationView _oppPitch;
        private readonly VisualElement _oppPitchWrap;

        public TacticsView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("tactics.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 8;
            Root.Add(_header);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            Root.Add(scroll);

            scroll.Add(SectionLabel(tr("tactics.formation_caption")));
            _formationButton = CycleButton(() => FormationCycleClicked?.Invoke());
            scroll.Add(_formationButton);

            // Live shape preview: the user's best XI in the chosen formation (task 6.7).
            scroll.Add(SectionLabel(tr("tactics.your_shape")));
            _shapePitch = new PitchFormationView(mirror: false);
            scroll.Add(PitchBox(_shapePitch, 240));

            scroll.Add(SectionLabel(tr("tactics.instructions_caption")));
            _mentalityButton = CycleButton(() => MentalityCycleClicked?.Invoke());
            scroll.Add(_mentalityButton);
            _pressingButton = CycleButton(() => PressingCycleClicked?.Invoke());
            scroll.Add(_pressingButton);
            _tempoButton = CycleButton(() => TempoCycleClicked?.Invoke());
            scroll.Add(_tempoButton);
            _widthButton = CycleButton(() => WidthCycleClicked?.Invoke());
            scroll.Add(_widthButton);

            _familiarity = UiKit.Subtitle(string.Empty);
            _familiarity.style.marginTop = 8;
            _familiarity.style.marginBottom = 8;
            _familiarity.style.alignSelf = Align.FlexStart;
            scroll.Add(_familiarity);

            scroll.Add(SectionLabel(tr("tactics.opponent_caption")));
            _opponent = new Label(string.Empty);
            _opponent.style.color = new Color(1f, 1f, 1f, 0.85f);
            _opponent.style.fontSize = 14;
            _opponent.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(_opponent);

            // Opponent shape preview (their likely best XI), hidden when there's no fixture.
            _oppPitch = new PitchFormationView(mirror: true);
            _oppPitchWrap = PitchBox(_oppPitch, 190);
            scroll.Add(_oppPitchWrap);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            footer.Add(FooterButton(tr("tactics.save"), () => SaveClicked?.Invoke()));
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);

            _status = UiKit.Subtitle(string.Empty);
            _status.style.marginTop = 4;
            _status.style.alignSelf = Align.Center;
            Root.Add(_status);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetFormation(string text) => _formationButton.text = text;
        public void SetMentality(string text) => _mentalityButton.text = text;
        public void SetPressing(string text) => _pressingButton.text = text;
        public void SetTempo(string text) => _tempoButton.text = text;
        public void SetWidth(string text) => _widthButton.text = text;
        public void SetFamiliarity(string text) => _familiarity.text = text;
        public void SetOpponent(string text) => _opponent.text = text;
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Updates the live shape preview with the user's XI in the chosen formation.</summary>
        public void SetShape(System.Collections.Generic.IReadOnlyList<PitchTokenVm> tokens) =>
            _shapePitch.SetTokens(tokens);

        /// <summary>Shows/hides + fills the opponent shape preview (task 6.7).</summary>
        public void SetOpponentShape(System.Collections.Generic.IReadOnlyList<PitchTokenVm> tokens, bool known)
        {
            _oppPitchWrap.style.display = known ? DisplayStyle.Flex : DisplayStyle.None;
            if (known)
                _oppPitch.SetTokens(tokens);
        }

        private static VisualElement PitchBox(PitchFormationView pitch, float height)
        {
            var box = new VisualElement();
            box.style.height = height;
            box.style.marginBottom = 6;
            box.Add(pitch);
            return box;
        }

        private static Label SectionLabel(string caption)
        {
            var label = new Label(caption);
            label.style.color = new Color(1f, 1f, 1f, 0.7f);
            label.style.fontSize = 13;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            return label;
        }

        private static Button CycleButton(Action onClick)
        {
            var button = new Button(onClick);
            button.style.height = 40;
            button.style.fontSize = 15;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginBottom = 4;
            button.style.maxWidth = 420;
            return button;
        }

        private static Button FooterButton(string text, Action onClick)
        {
            var button = UiKit.MenuButton(text, onClick);
            button.style.width = 150;
            button.style.height = 44;
            button.style.fontSize = 16;
            button.style.marginLeft = 6;
            button.style.marginRight = 6;
            return button;
        }
    }
}
