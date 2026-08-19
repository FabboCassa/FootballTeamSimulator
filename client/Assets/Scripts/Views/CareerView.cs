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
    ///
    /// Layout (task 6.12): the three headline facts are stat tiles across the top of the wide column,
    /// the confidence gauge is a full-width meter under them and the history fills the rest of the
    /// page — the screen no longer bottoms out into a huge empty area on a desktop window.
    /// </summary>
    public sealed class CareerView
    {
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly VisualElement _objectiveTile;
        private readonly VisualElement _positionTile;
        private readonly VisualElement _reputationTile;
        private readonly Label _confidenceLabel;
        private readonly MeterBar _confidence;
        private readonly Label _confidenceHint;
        private readonly ScrollView _history;

        public CareerView(Func<string, string> tr)
        {
            Root = UiKit.ScreenRoot();

            VisualElement col = UiKit.PageColumn(UiKit.WidthWide);
            Root.Add(col);

            _header = UiKit.ScreenTitle(string.Empty);
            _header.style.marginBottom = UiKit.SpaceSm;
            col.Add(_header);

            // ---- headline tiles
            VisualElement tiles = UiKit.TileRow();
            _objectiveTile = UiKit.StatTile(tr("career.objective_caption"), string.Empty, null, 220f);
            _positionTile = UiKit.StatTile(tr("career.position_caption"), string.Empty, null, 180f);
            _reputationTile = UiKit.StatTile(tr("career.reputation_caption"), string.Empty, null, 180f);
            tiles.Add(_objectiveTile);
            tiles.Add(_positionTile);
            tiles.Add(_reputationTile);
            col.Add(tiles);

            // ---- board confidence gauge
            VisualElement confidencePanel = UiKit.Panel();
            Label confidenceCaption = UiKit.SectionLabel(tr("career.confidence_caption"));
            confidenceCaption.style.marginTop = 0;
            confidencePanel.Add(confidenceCaption);

            _confidenceLabel = UiKit.PanelLine(string.Empty);
            _confidenceLabel.style.fontSize = 18;
            _confidenceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _confidenceLabel.style.marginBottom = UiKit.SpaceSm;
            confidencePanel.Add(_confidenceLabel);

            _confidence = new MeterBar(18f);
            confidencePanel.Add(_confidence.Root);

            _confidenceHint = UiKit.Caption(string.Empty);
            _confidenceHint.style.marginTop = UiKit.SpaceXs;
            _confidenceHint.style.whiteSpace = WhiteSpace.Normal;
            confidencePanel.Add(_confidenceHint);
            col.Add(confidencePanel);

            // ---- history fills whatever is left
            VisualElement historyPanel = UiKit.Panel(grow: true);
            Label historyCaption = UiKit.SectionLabel(tr("career.history_caption"));
            historyCaption.style.marginTop = 0;
            historyPanel.Add(historyCaption);
            _history = UiKit.ListScroll();
            historyPanel.Add(_history);
            col.Add(historyPanel);

            VisualElement footer = UiKit.FooterBar();
            footer.Add(UiKit.FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            col.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;

        /// <summary>The board's objective, short form (e.g. "win the title (position 1)").</summary>
        public void SetObjective(string text) => UiKit.SetStatTileValue(_objectiveTile, text);

        /// <summary>The current league position, short form (e.g. "4 of 20").</summary>
        public void SetPosition(string text) => UiKit.SetStatTileValue(_positionTile, text);

        /// <summary>The manager's reputation, short form (e.g. "82/100").</summary>
        public void SetReputation(string text) => UiKit.SetStatTileValue(_reputationTile, text);

        /// <summary>Confidence 0-100 with a colour band: 0 = red (sacking), 1 = amber (warned), 2 = green (safe).</summary>
        public void SetConfidence(string text, int percent, int band)
        {
            Color color =
                band <= 0 ? UiKit.Danger :
                band == 1 ? UiKit.Warning :
                            UiKit.Positive;

            _confidenceLabel.text = text;
            _confidenceLabel.style.color = color;
            _confidence.Set(percent, color);
        }

        /// <summary>An optional one-line explanation shown under the gauge.</summary>
        public void SetConfidenceHint(string text) => _confidenceHint.text = text ?? string.Empty;

        public void SetHistory(IReadOnlyList<string> lines)
        {
            _history.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                var label = new Label(lines[i]);
                label.style.fontSize = 14;
                label.style.color = UiKit.TextPrimary;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 4;
                label.style.paddingTop = 10;
                label.style.paddingBottom = 10;
                label.style.paddingLeft = 12;
                label.style.paddingRight = 12;
                label.style.backgroundColor = (i % 2 == 0)
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : new Color(1f, 1f, 1f, 0.03f);
                UiKit.Round(label, UiKit.RadiusSm);
                _history.Add(label);
            }
        }

        /// <summary>Shows the friendly illustration instead of a lone "nothing yet" line (task 6.12).</summary>
        public void SetHistoryEmpty(string message)
        {
            _history.Clear();
            _history.Add(EmptyState.Build("career", message));
        }
    }
}
