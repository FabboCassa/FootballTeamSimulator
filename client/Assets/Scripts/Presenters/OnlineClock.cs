using System;
using System.Globalization;

namespace Fts.Presenters
{
    /// <summary>
    /// Reading the server's clock, in one place (task 12.3).
    ///
    /// THE TRAP THIS EXISTS TO CLOSE. Every instant the server sends is UTC — but the STRING does not always
    /// say so: a <c>DateTime</c> loses its Kind in the database and again in JSON, so a kick-off can arrive as
    /// <c>2026-08-26T19:00:00</c> with no trailing <c>Z</c>. Parsed with <c>RoundtripKind</c> that is an
    /// <i>Unspecified</i> time, and <c>ToUniversalTime()</c> then treats it as the DEVICE's local time and
    /// shifts it by the local offset — so an Italian coach's 21:00 match would render at 23:00. Which is
    /// precisely the failure the ladder's whole time-zone design exists to prevent.
    ///
    /// So: parse with <see cref="DateTimeStyles.AssumeUniversal"/>, keep everything in UTC internally, and
    /// convert to local time only at the last moment, for DISPLAY. A world's matches kick off at 21:00 of
    /// ITS zone; every coach reads that same instant on his own clock, which is why the server never sends a
    /// pre-formatted time and the client never renders a raw one.
    /// </summary>
    public static class OnlineClock
    {
        /// <summary>Parse a server instant as UTC. Returns null for a missing or unparseable string.</summary>
        public static DateTime? ParseUtc(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            return DateTime.TryParse(
                iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dt)
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : (DateTime?)null;
        }

        /// <summary>The instant as a short LOCAL date + time ("27/08 21:00"), or the raw string if it will
        /// not parse. This is the only place a kick-off becomes text.</summary>
        public static string LocalDateTime(string iso)
        {
            DateTime? utc = ParseUtc(iso);
            return utc.HasValue ? utc.Value.ToLocalTime().ToString("dd/MM HH:mm") : (iso ?? string.Empty);
        }

        /// <summary>Just the LOCAL time of day ("21:00") — what a fixture row shows next to the teams.</summary>
        public static string LocalTimeOfDay(string iso)
        {
            DateTime? utc = ParseUtc(iso);
            return utc.HasValue ? utc.Value.ToLocalTime().ToString("HH:mm") : string.Empty;
        }

        /// <summary>
        /// How far the DEVICE's clock is behind the server's, from a state that carries both. Everything that
        /// counts down uses it: a countdown is wrong the moment it trusts a phone whose clock is two minutes
        /// fast, and "two minutes" is the whole margin around a kick-off.
        /// </summary>
        public static TimeSpan OffsetFrom(string serverUtcIso)
        {
            DateTime? server = ParseUtc(serverUtcIso);
            return server.HasValue ? server.Value - DateTime.UtcNow : TimeSpan.Zero;
        }

        /// <summary>Now, corrected by <see cref="OffsetFrom"/>.</summary>
        public static DateTime NowUtc(TimeSpan offset) => DateTime.UtcNow + offset;

        /// <summary>A countdown, shortest useful form: "2g 04:11", "04:11:09", "11:09". Never negative —
        /// a passed deadline reads as zero, and the caller says what that means.</summary>
        public static string Countdown(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            if (remaining.TotalDays >= 1)
                return string.Format("{0}g {1:00}:{2:00}", (int)remaining.TotalDays, remaining.Hours, remaining.Minutes);
            if (remaining.TotalHours >= 1)
                return string.Format("{0}:{1:00}:{2:00}", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
            return string.Format("{0}:{1:00}", remaining.Minutes, remaining.Seconds);
        }
    }
}
