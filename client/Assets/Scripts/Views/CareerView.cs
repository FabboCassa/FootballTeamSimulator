using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Career screen (task 5.6): the user's standing as a coach — the board's objective, his current
    /// league position, the board-confidence meter (the sacking gauge), his reputation, and a scrolling
    /// career history. Dumb view: the presenter formats every label and passes a 0-100 confidence with a
    /// colour band; the view only renders strings/ints and emits Back. No Sim.Core references.
    /// </summary>
    public sealed class CareerView
    {
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Label _objective;
        private readonly Label _position;
        private readonly Label _confidenceLabel;
        private readonly VisualElement _confidenceFill;
        private readonly Label _reputation;
        private readonly ScrollView _history;

        public CareerView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.Background;
            Root.style.paddingTop = UiKit.SpaceSm;
            Root.style.paddingBottom = UiKit.SpaceSm;
            Root.style.paddingLeft = UiKit.SpaceMd;
            Root.style.paddingRight = UiKit.SpaceMd;

            var col = UiKit.CenteredColumn(680f);
            col.style.flexGrow = 1f;
            Root.Add(col);

            _header = UiKit.Header(string.Empty);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            var panel = UiKit.Card();
            _objective = PanelLine(panel);
            _objective.style.fontSize = 16;
            _objective.style.unityFontStyleAndWeight = FontStyle.Bold;
            _objective.style.marginBottom = 2;
            _position = PanelLine(panel);
            _reputation = PanelLine(panel);
            col.Add(panel);

            col.Add(SectionLabel(tr("career.confidence_caption")));
            _confidenceLabel = PanelLine(col);
            var track = new VisualElement();
            track.style.height = 16;
            track.style.marginBottom = UiKit.SpaceSm;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            UiKit.Round(track, UiKit.RadiusSm);
            _confidenceFill = new VisualElement();
            _confidenceFill.style.height = 16;
            _confidenceFill.style.width = Length.Percent(50);
            _confidenceFill.style.backgroundColor = new Color(0.3f, 0.7f, 0.35f);
            UiKit.Round(_confidenceFill, UiKit.RadiusSm);
            track.Add(_confidenceFill);
            col.Add(track);

            col.Add(SectionLabel(tr("career.history_caption")));
            _history = new ScrollView();
            _history.style.flexGrow = 1f;
            _history.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            col.Add(_history);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = UiKit.SpaceSm;
            footer.style.flexShrink = 0f;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 150;
            back.style.height = 44;
            back.style.fontSize = 16;
            footer.Add(back);
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetObjective(string text) => _objective.text = text;
        public void SetPosition(string text) => _position.text = text;
        public void SetReputation(string text) => _reputation.text = text;

        /// <summary>Confidence 0-100 with a colour band: 0 = red (sacking), 1 = amber (warned), 2 = green (safe).</summary>
        public void SetConfidence(string text, int percent, int band)
        {
            _confidenceLabel.text = text;
            _confidenceFill.style.width = Length.Percent(Mathf.Clamp(percent, 0, 100));
            _confidenceFill.style.backgroundColor =
                band <= 0 ? new Color(0.80f, 0.25f, 0.25f) :
                band == 1 ? new Color(0.85f, 0.65f, 0.20f) :
                            new Color(0.30f, 0.70f, 0.35f);
        }

        public void SetHistory(IReadOnlyList<string> lines)
        {
            _history.Clear();
            foreach (string line in lines)
            {
                var label = new Label(line);
                label.style.fontSize = 13;
                label.style.color = Color.white;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 3;
                label.style.paddingTop = 4;
                label.style.paddingBottom = 4;
                label.style.paddingLeft = 8;
                label.style.paddingRight = 8;
                label.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
                _history.Add(label);
            }
        }

        private static Label PanelLine(VisualElement parent)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = 13;
            label.style.color = Color.white;
            parent.Add(label);
            return label;
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
    }
}
