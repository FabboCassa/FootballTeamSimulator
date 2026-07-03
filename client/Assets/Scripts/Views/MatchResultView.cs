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
    /// Instant match-day result: final score and the event timeline.
    /// The watchable 2D match arrives with the renderer in 3.1.
    /// </summary>
    public sealed class MatchResultView
    {
        private static readonly Color GoalColor = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color MutedColor = new Color(1f, 1f, 1f, 0.65f);

        public event Action ContinueClicked;

        public VisualElement Root { get; }

        private readonly Label _score;
        private readonly Label _subtitle;
        private readonly VisualElement _homeCrestSlot;
        private readonly VisualElement _awayCrestSlot;
        private readonly ScrollView _events;

        public MatchResultView(Func<string, string> tr)
        {
            Root = UiKit.Screen(UiKit.HubBlue);

            var title = UiKit.Subtitle(tr("match.title"));
            Root.Add(title);

            // Scoreboard: home crest · score · away crest (task 6.8).
            var scoreboard = new VisualElement();
            scoreboard.style.flexDirection = FlexDirection.Row;
            scoreboard.style.alignItems = Align.Center;
            scoreboard.style.justifyContent = Justify.Center;
            scoreboard.style.marginBottom = UiKit.SpaceSm;

            _homeCrestSlot = CrestSlot();
            _homeCrestSlot.style.marginRight = UiKit.SpaceMd;
            scoreboard.Add(_homeCrestSlot);

            _score = UiKit.Title(string.Empty);
            _score.style.fontSize = 32;
            _score.style.marginBottom = 0;
            scoreboard.Add(_score);

            _awayCrestSlot = CrestSlot();
            _awayCrestSlot.style.marginLeft = UiKit.SpaceMd;
            scoreboard.Add(_awayCrestSlot);
            Root.Add(scoreboard);

            _subtitle = UiKit.Subtitle(string.Empty);
            Root.Add(_subtitle);

            _events = new ScrollView();
            _events.style.maxHeight = new Length(45f, LengthUnit.Percent);
            _events.style.width = 460;
            _events.style.maxWidth = new Length(92f, LengthUnit.Percent);
            _events.style.marginTop = 8;
            _events.style.marginBottom = 8;
            Root.Add(_events);

            Root.Add(UiKit.MenuButton(tr("match.continue"), () => ContinueClicked?.Invoke()));
        }

        public void SetScore(string score) => _score.text = score;

        public void SetSubtitle(string subtitle) => _subtitle.text = subtitle;

        /// <summary>Shows the two clubs' crests either side of the scoreline (task 6.8).</summary>
        public void SetCrests(VisualElement home, VisualElement away)
        {
            _homeCrestSlot.Clear();
            if (home != null) _homeCrestSlot.Add(home);
            _awayCrestSlot.Clear();
            if (away != null) _awayCrestSlot.Add(away);
        }

        private static VisualElement CrestSlot()
        {
            var slot = new VisualElement();
            slot.style.width = 44;
            slot.style.height = 44;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            return slot;
        }

        public void SetEvents(IReadOnlyList<MatchEventRowVm> rows)
        {
            _events.Clear();
            foreach (MatchEventRowVm vm in rows)
            {
                var label = new Label(vm.Label);
                label.style.fontSize = 14;
                label.style.height = 24;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.color = vm.IsGoal ? GoalColor : MutedColor;
                if (vm.IsGoal)
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                _events.Add(label);
            }
        }
    }
}
