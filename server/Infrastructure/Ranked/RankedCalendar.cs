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
/// </summary>
public static class RankedCalendar
{
    /// <summary>One market window: its index, when it opens, and when it closes.</summary>
    public readonly record struct MarketWindow(int Index, DateTime OpensUtc, DateTime ClosesUtc)
    {
        public bool IsOpenAt(DateTime nowUtc) => nowUtc >= OpensUtc && nowUtc < ClosesUtc;
    }

    /// <summary>Wall-clock kickoff of matchday <paramref name="round"/> (1-based). Matchday 1 = season start.</summary>
    public static DateTime KickoffOf(DateTime seasonStartUtc, int round, int intervalSeconds)
        => seasonStartUtc.AddSeconds((long)(round - 1) * Math.Max(0, intervalSeconds));

    /// <summary>The round that opens the mid-season window (a bit past halfway so the first half is
    /// complete). Never below 2 so it is distinct from the opening window.</summary>
    public static int MidpointRound(int totalRounds) => Math.Max(2, totalRounds / 2 + 1);

    /// <summary>The two market windows for a season of <paramref name="totalRounds"/> matchdays.</summary>
    public static IReadOnlyList<MarketWindow> Windows(
        DateTime seasonStartUtc, int totalRounds, int intervalSeconds, int windowDurationSeconds)
    {
        int dur = Math.Max(0, windowDurationSeconds);
        var window0 = new MarketWindow(0, seasonStartUtc, seasonStartUtc.AddSeconds(dur));

        DateTime midOpen = KickoffOf(seasonStartUtc, MidpointRound(totalRounds), intervalSeconds);
        var window1 = new MarketWindow(1, midOpen, midOpen.AddSeconds(dur));

        return new[] { window0, window1 };
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
}
