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
    /// Inbox notifications hub (task 6.2): a scrollable list of the career's messages, newest first,
    /// with a category tag and read/unread styling. A filter cycles all/unread, and the user can mark
    /// everything read or clear the list. Dumb view — the presenter owns the model, translates every
    /// string, decides the row colours and renders the empty state.
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
        private readonly ScrollView _list;
        private readonly Label _empty;

        public InboxView(Func<string, string> tr)
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = UiKit.PanelGray;
            Root.style.paddingTop = 12;
            Root.style.paddingBottom = 12;
            Root.style.paddingLeft = 16;
            Root.style.paddingRight = 16;

            var title = UiKit.Title(tr("inbox.title"));
            title.style.fontSize = 28;
            title.style.marginBottom = 2;
            Root.Add(title);

            _header = UiKit.Subtitle(string.Empty);
            _header.style.marginBottom = 6;
            Root.Add(_header);

            // Action bar: filter chip on the left, mark-all-read + clear on the right.
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 6;

            _filterButton = ChipButton(string.Empty, () => FilterClicked?.Invoke());
            bar.Add(_filterButton);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            bar.Add(spacer);

            bar.Add(ChipButton(tr("inbox.mark_all_read"), () => MarkAllReadClicked?.Invoke()));
            bar.Add(ChipButton(tr("inbox.clear"), () => ClearClicked?.Invoke()));
            Root.Add(bar);

            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            Root.Add(_list);

            _empty = new Label(string.Empty);
            _empty.style.color = new Color(1f, 1f, 1f, 0.7f);
            _empty.style.fontSize = 14;
            _empty.style.whiteSpace = WhiteSpace.Normal;
            _empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            _empty.style.marginTop = 24;
            _empty.style.display = DisplayStyle.None;
            Root.Add(_empty);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.Center;
            footer.style.marginTop = 6;
            footer.Add(FooterButton(tr("common.back"), () => BackClicked?.Invoke()));
            Root.Add(footer);
        }

        public void SetHeader(string text) => _header.text = text;
        public void SetFilter(string text) => _filterButton.text = text;

        public void SetEmpty(string text)
        {
            _empty.text = text;
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
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 44;
                row.style.marginBottom = 3;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = vm.Unread
                    ? new Color(1f, 1f, 1f, 0.10f)
                    : new Color(1f, 1f, 1f, 0.04f);
                UiKit.Round(row, UiKit.RadiusSm);
                row.RegisterCallback<ClickEvent>(_ => MessageClicked?.Invoke(id));

                // Unread dot keeps the eye on what's new.
                var dot = new VisualElement();
                dot.style.width = 8;
                dot.style.height = 8;
                dot.style.marginRight = 8;
                dot.style.backgroundColor = vm.Unread ? UiKit.Accent : new Color(0f, 0f, 0f, 0f);
                UiKit.Round(dot, 4);
                row.Add(dot);

                row.Add(UiKit.Pill(vm.CategoryLabel, vm.CategoryColor, UiKit.Background));

                var text = new Label(vm.Text);
                text.style.flexGrow = 1f;
                text.style.marginLeft = 8;
                text.style.fontSize = 14;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.color = vm.Unread ? UiKit.TextPrimary : UiKit.TextMuted;
                text.style.unityFontStyleAndWeight = vm.Unread ? FontStyle.Bold : FontStyle.Normal;
                row.Add(text);

                var stamp = new Label(vm.Stamp);
                stamp.style.width = 70;
                stamp.style.fontSize = 11;
                stamp.style.unityTextAlign = TextAnchor.MiddleRight;
                stamp.style.color = new Color(1f, 1f, 1f, 0.55f);
                row.Add(stamp);

                _list.Add(row);
            }
        }

        private static Button ChipButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 30;
            button.style.fontSize = 12;
            button.style.marginRight = 6;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
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
