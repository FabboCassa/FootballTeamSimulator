using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>One Inbox row: a coloured category tag, the rendered message text, a date stamp and unread state.</summary>
    public sealed class InboxRowVm
    {
        public int Id;
        public string CategoryLabel;
        public Color CategoryColor;
        public string Text;
        public string Stamp;
        public bool Unread;
    }

    /// <summary>
    /// Inbox (task 6.2), redrawn in task 14.4 on the standard page: kicker "Posta" over the unread
    /// summary, the filter and the two bulk actions in one toolbar, and each message as a card —
    /// an accent rail when unread, the category pill, the text at body size, the date on the right.
    /// Dumb view — the presenter owns the model, the strings, the category colours and the empty state.
    /// </summary>
    public sealed class InboxView
    {
        public event Action<int> MessageClicked;
        public event Action FilterClicked;
        public event Action MarkAllReadClicked;
        public event Action ClearClicked;
        public event Action BackClicked;

        public VisualElement Root { get; }

        private readonly Label _header;
        private readonly Button _filterButton;
        private readonly VisualElement _list;
        private readonly VisualElement _empty;
        private readonly Label _emptyText;

        public InboxView(Func<string, string> tr)
        {
            PageParts page = UiKit.StandardPage(tr("inbox.title"), string.Empty, tr("common.back"),
                () => BackClicked?.Invoke(), UiKit.WidthMedium);
            Root = page.Root;
            _header = page.Title;
            VisualElement col = page.Column;

            VisualElement bar = UiKit.Toolbar();
            bar.AddToClassList("fts-inbox__bar");
            _filterButton = UiKit.ChipButton(string.Empty, () => FilterClicked?.Invoke());
            bar.Add(_filterButton);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            bar.Add(spacer);
            bar.Add(UiKit.GhostButton(tr("inbox.mark_all_read"), () => MarkAllReadClicked?.Invoke()));
            Button clear = UiKit.GhostButton(tr("inbox.clear"), () => ClearClicked?.Invoke());
            clear.AddToClassList("fts-inbox__clear");
            bar.Add(clear);
            col.Add(bar);

            _list = new VisualElement();
            col.Add(_list);

            _empty = UiKit.OptionCard();
            _emptyText = UiKit.HelpText(string.Empty);
            _emptyText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _emptyText.style.marginBottom = 0;
            _empty.Add(EmptyState.Build("inbox", string.Empty));
            _empty.Add(_emptyText);
            _empty.style.display = DisplayStyle.None;
            col.Add(_empty);
        }

        public void SetHeader(string text) => _header.text = text ?? string.Empty;
        public void SetFilter(string text) => _filterButton.text = text ?? string.Empty;

        public void SetEmpty(string text)
        {
            _emptyText.text = text ?? string.Empty;
            _empty.style.display = DisplayStyle.Flex;
            _list.style.display = DisplayStyle.None;
        }

        public void SetRows(IReadOnlyList<InboxRowVm> rows)
        {
            _list.Clear();
            _empty.style.display = DisplayStyle.None;
            _list.style.display = DisplayStyle.Flex;

            foreach (InboxRowVm vm in rows)
            {
                int id = vm.Id;

                var row = new VisualElement();
                row.AddToClassList("fts-inbox__row");
                row.EnableInClassList("fts-inbox__row--unread", vm.Unread);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.RegisterCallback<ClickEvent>(_ => MessageClicked?.Invoke(id));

                var rail = new VisualElement();
                rail.AddToClassList("fts-inbox__rail");
                rail.style.flexShrink = 0f;
                rail.pickingMode = PickingMode.Ignore;
                row.Add(rail);

                Label pill = UiKit.Pill(vm.CategoryLabel, vm.CategoryColor, UiKit.Background);
                pill.AddToClassList("fts-inbox__pill");
                pill.style.flexShrink = 0f;
                pill.pickingMode = PickingMode.Ignore;
                row.Add(pill);

                var text = new Label(vm.Text);
                text.AddToClassList("fts-inbox__text");
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.minWidth = 0;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.pickingMode = PickingMode.Ignore;
                row.Add(text);

                var stamp = new Label(vm.Stamp);
                stamp.AddToClassList("fts-inbox__stamp");
                stamp.style.unityTextAlign = TextAnchor.MiddleRight;
                stamp.style.flexShrink = 0f;
                stamp.pickingMode = PickingMode.Ignore;
                row.Add(stamp);

                _list.Add(row);
            }
        }
    }
}
