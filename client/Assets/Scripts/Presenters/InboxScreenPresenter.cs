using System.Collections.Generic;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Services.Navigation;
using Fts.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Inbox notifications hub (task 6.2): lists the career's messages newest-first with a category
    /// tag and read/unread styling, a filter (all / unread), and mark-all-read + clear actions.
    /// Tapping a message marks it read. Reads everything live from <see cref="InboxService"/>; the
    /// message text is rendered from each message's stored key + args so it follows the active language.
    /// </summary>
    public sealed class InboxScreenPresenter : IScreenPresenter
    {
        private readonly ScreenNavigator _navigator;
        private readonly InboxService _inbox;
        private readonly ILocalizationService _loc;
        private readonly OverlayHost _overlay;
        private readonly InboxView _view;

        private bool _unreadOnly;

        public VisualElement View => _view.Root;

        public InboxScreenPresenter(ScreenNavigator navigator, InboxService inbox, ILocalizationService loc, OverlayHost overlay)
        {
            _navigator = navigator;
            _inbox = inbox;
            _loc = loc;
            _overlay = overlay;
            _view = new InboxView(loc.Tr);
        }

        public void Enter()
        {
            _view.MessageClicked += OnMessage;
            _view.FilterClicked += OnFilter;
            _view.MarkAllReadClicked += OnMarkAllRead;
            _view.ClearClicked += OnClear;
            _view.BackClicked += OnBack;
            Refresh();
        }

        public void Exit()
        {
            _view.MessageClicked -= OnMessage;
            _view.FilterClicked -= OnFilter;
            _view.MarkAllReadClicked -= OnMarkAllRead;
            _view.ClearClicked -= OnClear;
            _view.BackClicked -= OnBack;
        }

        public void Reveal() => Refresh();

        private void OnMessage(int id)
        {
            _inbox.MarkRead(id);
            Refresh();
        }

        private void OnFilter()
        {
            _unreadOnly = !_unreadOnly;
            Refresh();
        }

        private void OnMarkAllRead()
        {
            _inbox.MarkAllRead();
            Refresh();
        }

        private void OnClear()
        {
            if (_inbox.Messages.Count == 0)
                return;

            // Confirm before wiping the message history (task 6.2).
            Dialogs.Confirm(_overlay, _loc,
                "dialog.clear_inbox.title", "dialog.clear_inbox.message", "dialog.clear_inbox.confirm",
                () => { _inbox.Clear(); Refresh(); });
        }

        private void OnBack() => _navigator.Pop();

        private void Refresh()
        {
            _view.SetHeader(_loc.Tr("inbox.header", _inbox.UnreadCount));
            _view.SetFilter(_loc.Tr(_unreadOnly ? "inbox.filter.unread" : "inbox.filter.all"));

            var rows = new List<InboxRowVm>();
            IReadOnlyList<InboxMessage> messages = _inbox.Messages;
            // Newest first.
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                InboxMessage m = messages[i];
                if (_unreadOnly && m.Read) continue;
                rows.Add(BuildRow(m));
            }

            if (rows.Count == 0)
            {
                _view.SetEmpty(_loc.Tr(_unreadOnly ? "inbox.empty_unread" : "inbox.empty"));
                return;
            }

            _view.SetRows(rows);
        }

        private InboxRowVm BuildRow(InboxMessage m)
        {
            return new InboxRowVm
            {
                Id = m.Id,
                CategoryLabel = CategoryLabel(m.Category),
                CategoryColor = CategoryColor(m.Category),
                Text = _loc.Tr(m.Key, m.Args.ToArray()),
                Stamp = _loc.Tr("inbox.stamp", m.Year, m.Day),
                Unread = !m.Read
            };
        }

        private string CategoryLabel(InboxCategory category)
        {
            switch (category)
            {
                case InboxCategory.Transfer: return _loc.Tr("inbox.cat.transfer");
                case InboxCategory.Board: return _loc.Tr("inbox.cat.board");
                case InboxCategory.Match: return _loc.Tr("inbox.cat.match");
                case InboxCategory.Season: return _loc.Tr("inbox.cat.season");
                case InboxCategory.Market: return _loc.Tr("inbox.cat.market");
                default: return _loc.Tr("inbox.cat.system");
            }
        }

        private static Color CategoryColor(InboxCategory category)
        {
            switch (category)
            {
                case InboxCategory.Transfer: return UiKit.Accent;
                case InboxCategory.Board: return UiKit.Warning;
                case InboxCategory.Match: return UiKit.Hex(0x5B8DEF);
                case InboxCategory.Season: return UiKit.Hex(0xB07CF0);
                case InboxCategory.Market: return UiKit.Hex(0x46C7C7);
                default: return UiKit.Amber;
            }
        }
    }
}
