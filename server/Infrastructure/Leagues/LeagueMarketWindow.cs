using Fts.Application.Leagues;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// When a private league's transfer market is open (Phase 12.1). A private league advances on ROUNDS,
/// not on a clock — "advance when everyone is ready" — so a time-based window (the ranked model) has
/// nothing to hang off. The windows are therefore round-based, mirroring the single-player career's two
/// windows (decision taken with the user, 2026-08-25):
///
/// <list type="bullet">
///   <item>window 0 — before the first round is played (the pre-season campaign);</item>
///   <item>window 1 — at the mid-season round (<c>totalRounds / 2</c> rounds played).</item>
/// </list>
///
/// A window shuts the moment the next round resolves, which is also when unanswered offers expire. Pure
/// and static so both the service and the round-resolution hook read the SAME rule.
/// </summary>
public static class LeagueMarketWindow
{
    /// <summary>How many windows a season has.</summary>
    public const int WindowCount = 2;

    /// <summary>The round after which the mid-season window opens (0 when the schedule is too short).</summary>
    public static int MidRound(int totalRounds) => totalRounds >= 2 ? totalRounds / 2 : 0;

    /// <summary>The window state for a league that has played <paramref name="roundsPlayed"/> of
    /// <paramref name="totalRounds"/> rounds. <paramref name="seasonActive"/> is false before the draft
    /// completes and after the season finishes — the market is shut in both cases.</summary>
    public static LeagueMarketWindowDto State(bool seasonActive, int roundsPlayed, int totalRounds)
    {
        int mid = MidRound(totalRounds);

        bool open = seasonActive
                    && totalRounds > 0
                    && (roundsPlayed == 0 || (mid > 0 && roundsPlayed == mid));

        int index = !open ? -1 : roundsPlayed == 0 ? 0 : 1;
        int closesAfter = open ? roundsPlayed + 1 : 0;

        int nextOpensAfter = 0;
        if (!open && seasonActive && mid > 0 && roundsPlayed < mid) nextOpensAfter = mid;

        return new LeagueMarketWindowDto(open, index, roundsPlayed, totalRounds, closesAfter, nextOpensAfter);
    }
}
