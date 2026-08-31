namespace Fts.Infrastructure.Ranked;

/// <summary>
/// The ranked season's real-time calendar (Phase 9.2) — pure, deterministic clock math, no I/O. Given the
/// instant a group's season started plus the interval knobs, it says when each matchday kicks off and when
/// the two market windows are open. Both the season job (to decide what is due) and the read model (to show
/// the next kickoff / current window) go through here, so the schedule is defined in exactly one place.
///
/// Layout — one matchday per interval, with a market window at the start and around the midpoint:
/// <code>
///   matchday 1 ─── matchday 2 ─── … ─── matchday M(mid) ─── … ─── matchday N
///   ^seasonStart   ^+interval                 ^window 1 opens here
///   [── window 0 ──]                          [── window 1 ──]
/// </code>
/// Round R kicks off at <c>seasonStart + (R-1)*interval</c> (matchday 1 fires at season start). The two
/// windows are decoupled from the matchday cadence — window 0 opens at season start and window 1 at the
/// midpoint matchday, each lasting one window duration — so a market window and a matchday can coexist and a
/// test can compress the matchday interval to 0 without collapsing the windows.
///
/// TASK 12.3 — A VISIBLE KICK-OFF TIME. A ladder match is now something you can attend, so "season start +
/// N × 24h" is no longer good enough: a season that happened to start at 04:00 would ask its coaches to turn
/// up at 04:00 every day. A <see cref="KickoffClock"/> therefore carries the world's OWN time zone and the
/// local hour matches kick off at (21:00), and a round's kickoff is the round-th such hour on or after the
/// season start. The zone is stored rather than a fixed UTC offset precisely so a season that runs across a
/// DST change keeps kicking off at 21:00 LOCAL — which is why every kickoff is re-normalised to the local
/// hour instead of being a plain addition of seconds.
///
/// The anchoring engages only when the interval is a whole number of DAYS (the shipped 86,400s), because
/// "the same hour every day" is meaningful only then: a compressed calendar (a test's 0s or 3600s) keeps the
/// original season-start-relative behaviour, which is what keeps every pre-12.3 ranked test true by
/// construction.
/// </summary>
public static class RankedCalendar
{
    /// <summary>A day, in seconds — the cadence at which anchoring a kickoff to a local hour is meaningful.</summary>
    public const int SecondsPerDay = 86_400;

    /// <summary>
    /// How a group's matchdays are spaced (task 12.3). <paramref name="IntervalSeconds"/> is the gap between
    /// matchdays; <paramref name="Zone"/> and <paramref name="HourLocal"/> are the world's own time zone and
    /// the local hour it kicks off at. When <see cref="AnchorsToLocalHour"/> is false the clock behaves
    /// exactly as it did before 12.3 (season start + N × interval).
    /// </summary>
    public readonly record struct KickoffClock(int IntervalSeconds, TimeZoneInfo? Zone, int HourLocal)
    {
        /// <summary>The pre-12.3 clock: no zone, kickoffs relative to the season start.</summary>
        public static KickoffClock Relative(int intervalSeconds) => new(intervalSeconds, null, -1);

        /// <summary>True when kickoffs snap to <see cref="HourLocal"/> in <see cref="Zone"/>. Requires a
        /// whole-day cadence — at any other interval "the same hour every day" has no meaning, and a
        /// compressed test calendar must keep resolving matchday after matchday without waiting for
        /// tomorrow evening.</summary>
        public bool AnchorsToLocalHour =>
            Zone is not null
            && HourLocal is >= 0 and <= 23
            && IntervalSeconds >= SecondsPerDay
            && IntervalSeconds % SecondsPerDay == 0;
    }

    /// <summary>One market window: its index, when it opens, and when it closes.</summary>
    public readonly record struct MarketWindow(int Index, DateTime OpensUtc, DateTime ClosesUtc)
    {
        public bool IsOpenAt(DateTime nowUtc) => nowUtc >= OpensUtc && nowUtc < ClosesUtc;
    }

    /// <summary>Resolve an IANA (or Windows) zone id, or null when it is missing/unknown. Never throws: an
    /// unknown id must degrade to the pre-12.3 relative calendar, not take a ladder down. .NET 6+ accepts
    /// IANA ids on Windows too (ICU), which is why the ids we store are IANA.</summary>
    public static TimeZoneInfo? ZoneOrNull(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    /// <summary>Wall-clock kickoff of matchday <paramref name="round"/> (1-based). Matchday 1 = season start.</summary>
    public static DateTime KickoffOf(DateTime seasonStartUtc, int round, int intervalSeconds)
        => KickoffOf(seasonStartUtc, round, KickoffClock.Relative(intervalSeconds));

    /// <summary>Wall-clock kickoff of matchday <paramref name="round"/> (1-based) on the given clock. With a
    /// local-hour anchor, matchday 1 is the FIRST such hour on or after the season start (so the pre-season
    /// market window is the lead-in), and every later matchday is re-normalised to that hour so a DST change
    /// moves the UTC instant rather than the coach's evening.</summary>
    public static DateTime KickoffOf(DateTime seasonStartUtc, int round, KickoffClock clock)
    {
        int interval = Math.Max(0, clock.IntervalSeconds);
        if (!clock.AnchorsToLocalHour)
            return seasonStartUtc.AddSeconds((long)(round - 1) * interval);

        TimeZoneInfo zone = clock.Zone!;
        DateTime first = FirstLocalHourAtOrAfter(seasonStartUtc, zone, clock.HourLocal);
        DateTime naive = first.AddSeconds((long)(round - 1) * interval);
        return SnapToLocalHour(naive, zone, clock.HourLocal);
    }

    /// <summary>The round that opens the mid-season window (a bit past halfway so the first half is
    /// complete). Never below 2 so it is distinct from the opening window.</summary>
    public static int MidpointRound(int totalRounds) => Math.Max(2, totalRounds / 2 + 1);

    /// <summary>
    /// The two market windows for a season of <paramref name="totalRounds"/> matchdays.
    ///
    /// DELIBERATE LIMIT (task 12.3): the windows stay on the RELATIVE calendar even when the matchdays are
    /// anchored to a local hour. A market window is a PERIOD, not an appointment — nobody has to be present
    /// for it — and it is read by four services (the season tick, the auctions, the direct market and the
    /// daily digest). Anchoring it too would have meant threading the world's zone through all four so they
    /// could not disagree about when the market shuts, in exchange for moving a 24-hour window by at most a
    /// few hours. The kick-off is the thing a coach has to turn up for, so the kick-off is the thing that
    /// got the zone.
    /// </summary>
    public static IReadOnlyList<MarketWindow> Windows(
        DateTime seasonStartUtc, int totalRounds, int intervalSeconds, int windowDurationSeconds)
    {
        int dur = Math.Max(0, windowDurationSeconds);
        var window0 = new MarketWindow(0, seasonStartUtc, seasonStartUtc.AddSeconds(dur));

        DateTime midOpen = KickoffOf(seasonStartUtc, MidpointRound(totalRounds), intervalSeconds);
        var window1 = new MarketWindow(1, midOpen, midOpen.AddSeconds(dur));

        return new[] { window0, window1 };
    }

    /// <summary>The first window that OPENS strictly after <paramref name="nowUtc"/>, or null when the
    /// season has none left (task 12.2 — "when do auctions come back?" has to be answerable in-app, and the
    /// answer is calendar arithmetic the client should not be re-deriving).</summary>
    public static MarketWindow? NextWindow(
        DateTime seasonStartUtc, int totalRounds, int intervalSeconds, int windowDurationSeconds, DateTime nowUtc)
    {
        foreach (var w in Windows(seasonStartUtc, totalRounds, intervalSeconds, windowDurationSeconds))
            if (w.OpensUtc > nowUtc) return w;
        return null;
    }

    /// <summary>The window currently open at <paramref name="nowUtc"/>, or null when the market is shut.
    /// When the two windows overlap (e.g. a compressed calendar), the LATER-indexed one wins so the midpoint
    /// window still becomes observable once its distinct interval is reached.</summary>
    public static MarketWindow? CurrentWindow(
        DateTime seasonStartUtc, int totalRounds, int intervalSeconds, int windowDurationSeconds, DateTime nowUtc)
    {
        MarketWindow? current = null;
        foreach (var w in Windows(seasonStartUtc, totalRounds, intervalSeconds, windowDurationSeconds))
            if (w.IsOpenAt(nowUtc)) current = w; // later windows override earlier ones on overlap
        return current;
    }

    // --- local-hour anchoring (task 12.3) ----------------------------------------------------------

    /// <summary>The first <paramref name="hourLocal"/>:00 in <paramref name="zone"/> at or after
    /// <paramref name="fromUtc"/>, as a UTC instant.</summary>
    private static DateTime FirstLocalHourAtOrAfter(DateTime fromUtc, TimeZoneInfo zone, int hourLocal)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), zone);
        DateTime candidate = local.Date.AddHours(hourLocal);
        if (candidate < local) candidate = candidate.AddDays(1);
        return ToUtcSafe(candidate, zone);
    }

    /// <summary>Move <paramref name="approxUtc"/> onto <paramref name="hourLocal"/>:00 of the LOCAL day it
    /// falls on. This is the DST absorber: adding 24h across a spring-forward lands on 22:00 local, and this
    /// snaps it back to 21:00 — the appointment a coach keeps is a local hour, not a UTC offset.</summary>
    private static DateTime SnapToLocalHour(DateTime approxUtc, TimeZoneInfo zone, int hourLocal)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(approxUtc, DateTimeKind.Utc), zone);
        return ToUtcSafe(local.Date.AddHours(hourLocal), zone);
    }

    /// <summary>Convert a LOCAL wall-clock time to UTC without ever throwing. A local time that does not
    /// exist (the hour a spring-forward skips) is nudged forward until it does; an ambiguous one (the hour a
    /// fall-back repeats) resolves to the FIRST occurrence, which is what a fixture list should say.</summary>
    private static DateTime ToUtcSafe(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (int i = 0; i < 4 && zone.IsInvalidTime(unspecified); i++)
            unspecified = unspecified.AddHours(1);

        if (zone.IsAmbiguousTime(unspecified))
        {
            TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(unspecified);
            TimeSpan first = offsets[0];
            foreach (TimeSpan o in offsets) if (o > first) first = o; // the larger offset = the earlier instant
            return DateTime.SpecifyKind(unspecified - first, DateTimeKind.Utc);
        }

        try { return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone); }
        catch (ArgumentException) { return DateTime.SpecifyKind(unspecified, DateTimeKind.Utc); }
    }
}
