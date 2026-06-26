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
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("career.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 8;
            Root.Add(_header);

            var panel = Panel();
            _objective = PanelLine(panel);
            _objective.style.fontSize = 15;
            _position = PanelLine(panel);
            _reputation = PanelLine(panel);
            Root.Add(panel);

            Root.Add(SectionLabel(tr("career.confidence_caption")));
            _confidenceLabel = PanelLine(Root);
            var track = new VisualElement();
            track.style.height = 14;
            track.style.marginBottom = 8;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            _confidenceFill = new VisualElement();
            _confidenceFill.style.height = 14;
            _confidenceFill.style.width = Length.Percent(50);
            _confidenceFill.style.backgroundColor = new Color(0.3f, 0.7f, 0.35f);
            track.Add(_confidenceFill);
            Root.Add(track);

            Root.Add(SectionLabel(tr("career.history_caption")));
            _history = new ScrollView();
            _history.style.flexGrow = 1f;
            Root.Add(_history);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 10;
            var back = UiKit.MenuButton(tr("common.back"), () => BackClicked?.Invoke());
            back.style.width = 150;
            back.style.height = 44;
            back.style.fontSize = 16;
            footer.Add(back);
            Root.Add(footer);
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

        private static VisualElement Panel()
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            panel.style.paddingTop = 6;
            panel.style.paddingBottom = 6;
            panel.style.paddingLeft = 10;
            panel.style.paddingRight = 10;
            panel.style.marginBottom = 6;
            return panel;
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
