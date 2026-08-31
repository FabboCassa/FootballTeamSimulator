using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The running commentary beside the pitch (task 13.1): a short stack of lines
    /// naming what is happening — who played it to whom, who won it back, who shot.
    /// The goal toast stays what it is; this is the second channel, the one that makes
    /// the movement legible instead of decorative.
    ///
    /// Dumb: it takes strings. It sits as an absolute overlay inside the pitch
    /// container, so it costs the layout nothing and behaves the same in the career,
    /// live and replay screens.
    /// </summary>
    public sealed class ActionFeed
    {
        private const int MaxLines = 4;
        private const long IdleHideMs = 7000;

        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);

        private readonly VisualElement _root;
        private IVisualElementScheduledItem _hide;

        public VisualElement Root => _root;

        public ActionFeed()
        {
            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.left = 10;
            _root.style.bottom = 10;
            _root.style.maxWidth = Length.Percent(60);
            _root.style.flexDirection = FlexDirection.Column;
            _root.pickingMode = PickingMode.Ignore;
        }

        /// <summary>Adds a line, dropping the oldest once the stack is full.</summary>
        public void Push(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            while (_root.childCount >= MaxLines)
                _root.RemoveAt(0);

            var line = new Label(text);
            line.style.color = Color.white;
            line.style.fontSize = 13;
            line.style.backgroundColor = Backdrop;
            line.style.paddingLeft = 8;
            line.style.paddingRight = 8;
            line.style.paddingTop = 3;
            line.style.paddingBottom = 3;
            line.style.marginTop = 2;
            line.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(line);

            // Fade the older lines back so the newest one reads first.
            for (int i = 0; i < _root.childCount; i++)
            {
                float weight = (i + 1f) / _root.childCount;
                _root[i].style.opacity = 0.35f + 0.65f * weight;
            }

            _root.style.display = DisplayStyle.Flex;
            _hide?.Pause();
            _hide = _root.schedule.Execute(Clear).StartingIn(IdleHideMs);
        }

        /// <summary>Empties the feed (a re-simulated remainder starts a fresh commentary).</summary>
        public void Clear()
        {
            _root.Clear();
            _root.style.display = DisplayStyle.None;
        }
    }
}
