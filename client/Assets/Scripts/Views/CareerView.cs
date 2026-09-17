using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One completed season in the coach's history (task 14.4).</summary>
    public sealed class CareerHistoryRowVm
    {
        public string Year;
        public string Club;
        /// <summary>"Finished 3rd · expected 6th · above expectations".</summary>
        public string Detail;
        /// <summary>+1 over, 0 met, -1 under expectations — tints the detail line.</summary>
        public int Outcome;
        public bool Champion;
        public bool Sacked;
    }

    /// <summary>
    /// Career screen (task 5.6), redrawn in task 14.4 on the standard page: the coach's standing in
    /// three blocks, top to bottom, in the order a manager asks the questions.
    ///   1. Where do I stand?   → three stat tiles: the board's objective, the position, reputation.
    ///   2. Is my job safe?     → one card: the confidence number in its colour, a sentence, a meter.
    ///   3. What have I done?   → one row per completed season: year tag, club, a detail line, and a
    ///                            CHAMPION / SACKED badge when it applies.
    /// Dumb view: the presenter formats every string; sizes live in FtsTheme.uss (.fts-career*).
    /// </summary>
    public sealed class CareerView
    {
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Func<string, string> _tr;
        private readonly Label _title;
        private readonly VisualElement _tiles;
        private readonly VisualElement _objectiveTile;
        private readonly VisualElement _positionTile;
        private readonly VisualElement _reputationTile;
        private readonly Label _confidenceValue;
        private readonly Label _confidenceStatus;
        private readonly MeterBar _confidence;
        private readonly Label _confidenceHint;
        private readonly VisualElement _history;

        public CareerView(Func<string, string> tr)
        {
            _tr = tr;

            PageParts page = UiKit.StandardPage(tr("career.kicker"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            _title = page.Title;
            VisualElement col = page.Column;

            // ---- 1. tiles
            _tiles = UiKit.TileRow();
            _objectiveTile = UiKit.StatTile(tr("career.objective_caption"), "—");
            _positionTile = UiKit.StatTile(tr("career.position_caption"), "—");
            _reputationTile = UiKit.StatTile(tr("career.reputation_caption"), "—");
            _tiles.Add(_objectiveTile);
            _tiles.Add(_positionTile);
            _tiles.Add(_reputationTile);
            col.Add(_tiles);

            // ---- 2. board confidence
            VisualElement confidence = UiKit.OptionCard();
            confidence.Add(UiKit.BlockHead(tr("career.confidence_caption")));

            var line = new VisualElement();
            line.AddToClassList("fts-career__confline");
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.FlexEnd;
            _confidenceValue = new Label(string.Empty);
            _confidenceValue.AddToClassList("fts-career__confvalue");
            UiKit.UseDisplayFont(_confidenceValue);
            _confidenceValue.style.flexShrink = 0f;
            line.Add(_confidenceValue);
            _confidenceStatus = new Label(string.Empty);
            _confidenceStatus.AddToClassList("fts-career__confstatus");
            _confidenceStatus.style.whiteSpace = WhiteSpace.Normal;
            _confidenceStatus.style.flexShrink = 1f;
            line.Add(_confidenceStatus);
            confidence.Add(line);

            _confidence = new MeterBar();
            _confidence.Root.style.height = StyleKeyword.Null;
            _confidence.Root.style.borderTopLeftRadius = StyleKeyword.Null;
            _confidence.Root.style.borderTopRightRadius = StyleKeyword.Null;
            _confidence.Root.style.borderBottomLeftRadius = StyleKeyword.Null;
            _confidence.Root.style.borderBottomRightRadius = StyleKeyword.Null;
            _confidence.Root.AddToClassList("fts-meter");
            confidence.Add(_confidence.Root);

            _confidenceHint = UiKit.HelpText(string.Empty);
            _confidenceHint.style.marginTop = UiKit.SpaceSm;
            _confidenceHint.style.marginBottom = 0;
            _confidenceHint.style.display = DisplayStyle.None;
            confidence.Add(_confidenceHint);
            col.Add(confidence);

            // ---- 3. history
            VisualElement history = UiKit.OptionCard();
            history.Add(UiKit.BlockHead(tr("career.history_caption")));
            _history = new VisualElement();
            history.Add(_history);
            col.Add(history);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Responsive.Changed += Layout;
                Layout(Responsive.Current);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Responsive.Changed -= Layout);
            Layout(Responsive.Current);
        }

        // ---------------------------------------------------------------- presenter API

        /// <summary>The page title — the club the coach works for.</summary>
        public void SetHeader(string text) => _title.text = text ?? string.Empty;

        /// <summary>The board's objective, short form ("Upper mid-table · 6th").</summary>
        public void SetObjective(string text) => UiKit.SetStatTileValue(_objectiveTile, text);

        /// <summary>The current league position, short form ("4 of 20").</summary>
        public void SetPosition(string text) => UiKit.SetStatTileValue(_positionTile, text);

        /// <summary>The manager's reputation, short form ("82/100").</summary>
        public void SetReputation(string text) => UiKit.SetStatTileValue(_reputationTile, text);

        /// <summary>
        /// Confidence 0-100 with its band (0 red = sacking, 1 amber = warned, 2 green = safe):
        /// <paramref name="value"/> is the number ("72/100"), <paramref name="status"/> the sentence.
        /// </summary>
        public void SetConfidence(string value, string status, int percent, int band)
        {
            Color color = band <= 0 ? UiKit.Danger : band == 1 ? UiKit.Warning : UiKit.Positive;
            _confidenceValue.text = value ?? string.Empty;
            _confidenceValue.style.color = color;
            _confidenceStatus.text = status ?? string.Empty;
            _confidence.Set(percent, color);
        }

        /// <summary>An optional one-line explanation under the meter.</summary>
        public void SetConfidenceHint(string text)
        {
            _confidenceHint.text = text ?? string.Empty;
            _confidenceHint.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetHistory(IReadOnlyList<CareerHistoryRowVm> rows)
        {
            _history.Clear();
            foreach (CareerHistoryRowVm vm in rows)
            {
                var row = new VisualElement();
                row.AddToClassList("fts-career__season");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                Label year = UiKit.Tag(vm.Year);
                year.AddToClassList("fts-career__year");
                row.Add(year);

                var stack = new VisualElement();
                stack.style.flexGrow = 1f;
                stack.style.flexShrink = 1f;
                stack.style.minWidth = 0f;
                var club = new Label(vm.Club ?? string.Empty);
                club.AddToClassList("fts-selectrow__name");
                club.style.overflow = Overflow.Hidden;
                club.style.textOverflow = TextOverflow.Ellipsis;
                stack.Add(club);
                var detail = new Label(vm.Detail ?? string.Empty);
                detail.AddToClassList("fts-career__detail");
                detail.EnableInClassList("fts-career__detail--over", vm.Outcome > 0);
                detail.EnableInClassList("fts-career__detail--under", vm.Outcome < 0);
                detail.style.whiteSpace = WhiteSpace.Normal;
                stack.Add(detail);
                row.Add(stack);

                if (vm.Champion) row.Add(Badge(_tr("career.badge.champion"), "fts-career__badge--champion"));
                if (vm.Sacked) row.Add(Badge(_tr("career.badge.sacked"), "fts-career__badge--sacked"));

                _history.Add(row);
            }
        }

        /// <summary>Shows the friendly illustration instead of a lone "nothing yet" line (task 6.12).</summary>
        public void SetHistoryEmpty(string message)
        {
            _history.Clear();
            _history.Add(EmptyState.Build("career", message));
        }

        // ---------------------------------------------------------------- layout

        private void Layout(Viewport viewport)
        {
            // Three tiles across everywhere but a phone, where the objective (the longest value)
            // takes the full first line and the two numbers share the second.
            bool mobile = viewport == Viewport.Mobile;
            int i = 0;
            foreach (VisualElement tile in _tiles.Children())
            {
                tile.style.minWidth = 0f;
                tile.style.flexBasis = mobile ? Length.Percent(i == 0 ? 100 : 40) : Length.Percent(25);
                i++;
            }
        }

        private static Label Badge(string text, string cls)
        {
            Label badge = UiKit.Tag(text);
            badge.AddToClassList("fts-career__badge");
            badge.AddToClassList(cls);
            return badge;
        }
    }
}
