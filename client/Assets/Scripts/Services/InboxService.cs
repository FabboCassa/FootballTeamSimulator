using System.Collections.Generic;
using Fts.Services.Persistence;

namespace Fts.Services
{
    /// <summary>
    /// The Inbox notifications hub (task 6.2): posts persisted messages from career events
    /// (completed transfers, market windows, incoming offers, match results, board outcomes),
    /// tracks read/unread state, and exposes the unread count for the Hub badge. Lives in the
    /// Game scope; other Game-scope services inject it and call <see cref="Post"/>.
    ///
    /// Messages store a localization key + already-stringified args (so they re-render in the
    /// active language) and are stamped with the season year/day. The list is bounded so a long
    /// career can't grow it without limit. Every mutation autosaves, like the rest of the SP flow.
    /// </summary>
    public sealed class InboxService
    {
        /// <summary>Most messages we keep; older ones drop off the front when this is exceeded.</summary>
        private const int MaxMessages = 80;

        private readonly CareerState _career;
        private readonly ISaveRepository _save;

        public InboxService(CareerState career, ISaveRepository save)
        {
            _career = career;
            _save = save;
        }

        /// <summary>All messages, oldest first (the presenter reverses for newest-first display).</summary>
        public IReadOnlyList<InboxMessage> Messages => _career.Inbox;

        public int UnreadCount
        {
            get
            {
                int n = 0;
                foreach (InboxMessage m in _career.Inbox)
                    if (!m.Read)
                        n++;
                return n;
            }
        }

        /// <summary>
        /// Posts a new unread message stamped with the current season year/day and saves. The
        /// caller pre-formats <paramref name="args"/> (names, numbers, money via <see cref="Money"/>)
        /// because only the template — not the proper nouns — is translated.
        /// </summary>
        public void Post(InboxCategory category, string key, params string[] args)
        {
            var message = new InboxMessage
            {
                Id = NextId(),
                Category = category,
                Key = key,
                Args = new List<string>(args ?? System.Array.Empty<string>()),
                Year = _career.Season.Year,
                Day = _career.Season.CurrentDay,
                Read = false
            };

            _career.Inbox.Add(message);
            if (_career.Inbox.Count > MaxMessages)
                _career.Inbox.RemoveRange(0, _career.Inbox.Count - MaxMessages);

            _save.Save(_career);
        }

        public void MarkRead(int id)
        {
            foreach (InboxMessage m in _career.Inbox)
            {
                if (m.Id == id && !m.Read)
                {
                    m.Read = true;
                    _save.Save(_career);
                    return;
                }
            }
        }

        public void MarkAllRead()
        {
            bool changed = false;
            foreach (InboxMessage m in _career.Inbox)
            {
                if (!m.Read)
                {
                    m.Read = true;
                    changed = true;
                }
            }

            if (changed)
                _save.Save(_career);
        }

        public void Clear()
        {
            if (_career.Inbox.Count == 0)
                return;
            _career.Inbox.Clear();
            _save.Save(_career);
        }

        private int NextId()
        {
            int max = 0;
            foreach (InboxMessage m in _career.Inbox)
                if (m.Id > max)
                    max = m.Id;
            return max + 1;
        }
    }
}
