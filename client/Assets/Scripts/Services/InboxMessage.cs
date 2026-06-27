using System.Collections.Generic;

namespace Fts.Services
{
    /// <summary>Inbox message category (task 6.2): drives the row's tag colour and the unread filter.</summary>
    public enum InboxCategory
    {
        System = 0,
        Transfer = 1,
        Board = 2,
        Match = 3,
        Season = 4,
        Market = 5
    }

    /// <summary>
    /// One persisted Inbox notification (task 6.2). Stored translatable: the message keeps a
    /// localization <see cref="Key"/> plus already-stringified <see cref="Args"/> (proper nouns,
    /// numbers, money) so the template re-renders in whichever language is active when read — the
    /// text is never frozen at post time. Stamped with the season year/day it was posted, and a
    /// per-career incrementing <see cref="Id"/> so it can be marked read individually.
    /// </summary>
    public sealed class InboxMessage
    {
        public int Id { get; set; }
        public InboxCategory Category { get; set; }

        /// <summary>Localization key of the message template (e.g. "inbox.transfer_in").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>string.Format arguments for the template, pre-stringified at post time.</summary>
        public List<string> Args { get; set; } = new List<string>();

        public int Year { get; set; }
        public int Day { get; set; }
        public bool Read { get; set; }
    }
}
