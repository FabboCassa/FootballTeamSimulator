using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    public sealed class MatchEventRowVm
    {
        public string Label;
        public bool IsGoal;
        public bool IsUserClub;
    }

    /// <summary>
    /// Match result (task 2.6), redrawn in task 14.4 on the centred page: the scoreboard on the raised
    /// card (both crests, the score in Bebas, the line under it), the event timeline on a card with
    /// goals highlighted, and one accent Continue.
    /// </summary>
    public sealed class MatchResultView
    {
        public event Action ContinueClicked;

        public VisualElement Root { get; }

        private readonly Label _score;
        private readonly Label _subtitle;
        private readonly VisualElement _homeCrestSlot;
        private readonly VisualElement _awayCrestSlot;
        private readonly VisualElement _events;

        public MatchResultView(Func<string, string> tr)
        {
            PageParts page = UiKit.CenterPage(tr("match.title"), string.Empty);
            Root = page.Root;
            page.Title.style.display = DisplayStyle.None;
            VisualElement col = page.Column;

            VisualElement board = UiKit.RaisedCard();
            board.AddToClassList("fts-result__board");
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            _homeCrestSlot = CrestSlot();
            row.Add(_homeCrestSlot);
            _score = new Label(string.Empty);
            _score.AddToClassList("fts-result__score");
            UiKit.UseDisplayFont(_score);
            _score.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(_score);
            _awayCrestSlot = CrestSlot();
            row.Add(_awayCrestSlot);
            board.Add(row);
            _subtitle = new Label(string.Empty);
            _subtitle.AddToClassList("fts-result__subtitle");
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            board.Add(_subtitle);
            col.Add(board);

            VisualElement timeline = UiKit.OptionCard();
            timeline.AddToClassList("fts-result__timeline");
            timeline.Add(UiKit.BlockHead(tr("match.timeline")));
            _events = new VisualElement();
            timeline.Add(_events);
            col.Add(timeline);

            Button next = UiKit.CtaButton(tr("match.continue"), string.Empty, () => ContinueClicked?.Invoke());
            next.AddToClassList("fts-end__cta");
            col.Add(next);
        }

        public void SetScore(string score) => _score.text = score ?? string.Empty;

        public void SetSubtitle(string subtitle) => _subtitle.text = subtitle ?? string.Empty;

        /// <summary>Shows the two clubs' crests either side of the scoreline (task 6.8).</summary>
        public void SetCrests(VisualElement home, VisualElement away)
        {
            _homeCrestSlot.Clear();
            if (home != null) _homeCrestSlot.Add(home);
            _awayCrestSlot.Clear();
            if (away != null) _awayCrestSlot.Add(away);
        }

        public void SetEvents(IReadOnlyList<MatchEventRowVm> rows)
        {
            _events.Clear();
            foreach (MatchEventRowVm vm in rows)
            {
                var label = new Label(vm.Label);
                label.AddToClassList("fts-result__event");
                label.EnableInClassList("fts-result__event--goal", vm.IsGoal);
                label.EnableInClassList("fts-result__event--mine", vm.IsGoal && vm.IsUserClub);
                label.style.whiteSpace = WhiteSpace.Normal;
                _events.Add(label);
            }
        }

        private static VisualElement CrestSlot()
        {
            var slot = new VisualElement();
            slot.AddToClassList("fts-result__crest");
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.flexShrink = 0f;
            return slot;
        }
    }
}
