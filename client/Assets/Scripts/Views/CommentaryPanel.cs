using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// The commentary side panel of the watched match (spec watchable-match-engine R15): a scrolling
    /// list, newest line on top, each row a minute, an icon and a sentence; goals, cards and
    /// substitutions highlighted. Sizes and colours live in FtsTheme.uss (.fts-commentary*), with a
    /// .fts--mobile override that puts the panel under the pitch on a phone.
    ///
    /// POOLED. Every row is built up front and recycled: the presenter (driven by a
    /// Sim.Core CommentaryFeed) names the row a line goes into, and showing it only sets text and
    /// classes and moves the row to the top — no element is created during playback.
    ///
    /// Dumb: it takes finished strings and a style class for the icon.
    /// </summary>
    public sealed class CommentaryPanel
    {
        /// <summary>Lines kept on show; older ones give their row to the newest.</summary>
        public const int DefaultCapacity = 60;

        private const string HighlightClass = "fts-commentary__row--highlight";

        private readonly ScrollView _scroll;
        private readonly VisualElement[] _rows;
        private readonly Label[] _minutes;
        private readonly VisualElement[] _icons;
        private readonly Label[] _texts;
        private readonly string[] _iconClasses;

        public VisualElement Root { get; }

        public int Capacity => _rows.Length;

        public CommentaryPanel(string title, int capacity = DefaultCapacity)
        {
            Root = UiKit.Panel(grow: false);
            Root.AddToClassList("fts-commentary");
            Root.Add(UiKit.BlockHead(title));

            _scroll = UiKit.ListScroll();
            Root.Add(_scroll);

            int count = capacity > 0 ? capacity : DefaultCapacity;
            _rows = new VisualElement[count];
            _minutes = new Label[count];
            _icons = new VisualElement[count];
            _texts = new Label[count];
            _iconClasses = new string[count];

            for (int i = 0; i < count; i++)
                BuildRow(i);

            Clear();
        }

        private void BuildRow(int i)
        {
            VisualElement row = UiKit.Row();
            row.AddToClassList("fts-commentary__row");
            row.style.alignItems = Align.FlexStart;

            var minute = new Label(string.Empty);
            minute.AddToClassList("fts-commentary__minute");
            row.Add(minute);

            var icon = new VisualElement();
            icon.AddToClassList("fts-commentary__icon");
            row.Add(icon);

            var text = new Label(string.Empty);
            text.AddToClassList("fts-commentary__text");
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            row.Add(text);

            _rows[i] = row;
            _minutes[i] = minute;
            _icons[i] = icon;
            _texts[i] = text;
            _scroll.Add(row);
        }

        /// <summary>Writes a line into pooled row <paramref name="row"/> and puts it on top.</summary>
        public void Show(int row, string minute, string iconClass, string sentence, bool highlight)
        {
            if (row < 0 || row >= _rows.Length)
                return;

            _minutes[row].text = minute;
            _texts[row].text = sentence;

            if (_iconClasses[row] != iconClass)
            {
                if (_iconClasses[row] != null) _icons[row].RemoveFromClassList(_iconClasses[row]);
                if (iconClass != null) _icons[row].AddToClassList(iconClass);
                _iconClasses[row] = iconClass;
            }

            VisualElement element = _rows[row];
            element.EnableInClassList(HighlightClass, highlight);
            element.style.display = DisplayStyle.Flex;
            element.SendToBack(); // newest first: the latest line reads without scrolling
        }

        /// <summary>Hides every row (a re-simulated remainder starts a fresh commentary).</summary>
        public void Clear()
        {
            foreach (VisualElement row in _rows)
                row.style.display = DisplayStyle.None;
            _scroll.scrollOffset = UnityEngine.Vector2.zero;
        }
    }
}
