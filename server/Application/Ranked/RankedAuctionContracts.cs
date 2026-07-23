namespace Fts.Application.Ranked;

/// <summary>
/// Free-agent auctions in a ranked season's market windows (Phase 9.2b). When a window opens, a lot is
/// created for each free agent in the group's world; coaches place ascending bids (validated against the
/// current high bid, a minimum increment, and their available budget = transfer budget minus their other
/// leading bids, so a club can never win more than it can pay). No money moves until a lot settles, so an
/// outbid frees the reservation; at close the highest bidder gets the player and is charged. Adapted from the
/// 8.5 private-league auctions, but settled by the ranked calendar tick.
/// </summary>

/// <summary>Lifecycle of an auction lot. Stable numeric values (stored + on the wire).</summary>
public enum RankedAuctionStatus
{
    Open = 0,
    Settled = 1,
    Unsold = 2,
}

/// <summary>One auction lot as seen by the caller.</summary>
public sealed record RankedAuctionLotDto(
    Guid Id,
    int PlayerExternalId,
    string PlayerName,
    int Age,
    int Role,
    int Overall,
    long MarketValue,
    long StartPrice,
    long HighBid,
    int? HighBidClubExternalId,
    bool YouAreLeading,
    long MinNextBid,
    RankedAuctionStatus Status,
    DateTime EndsUtc,
    int SecondsRemaining);

/// <summary>The caller's auction view: the open lots + their budget picture (available = budget minus their
/// current leading bids) + whether a market window is open.</summary>
public sealed record RankedAuctionsDto(
    long Budget,
    long Committed,
    long Available,
    bool WindowOpen,
    IReadOnlyList<RankedAuctionLotDto> Lots);

/// <summary>Place a bid on a lot.</summary>
public sealed record PlaceRankedBidRequest(long Amount);

/// <summary>The outcome of a bid: the updated lot + whether it displaced a previous leader + the caller's
/// remaining available budget.</summary>
public sealed record RankedBidResultDto(
    RankedAuctionLotDto Lot,
    bool OutbidPrevious,
    int? PreviousLeaderClubExternalId,
    long Available);

/// <summary>
/// Ranked free-agent auction use cases (Phase 9.2b). Lot creation + settlement are driven by the season
/// tick (<see cref="OpenWindowLotsAsync"/> / <see cref="SettleDueAsync"/>); the coach-facing calls read the
/// lots and place bids.
/// </summary>
public interface IRankedAuctionService
{
    /// <summary>The caller's auction view (open lots + budget + window state).</summary>
    Task<RankedResult<RankedAuctionsDto>> GetAuctionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Place an ascending bid on a lot in the caller's group (requires an open window).</summary>
    Task<RankedResult<RankedBidResultDto>> PlaceBidAsync(
        Guid userId, Guid auctionId, PlaceRankedBidRequest request, CancellationToken ct = default);

    /// <summary>Open a lot for every free agent in a group's world for the given window (idempotent per
    /// group+window). Called by the season tick when a market window opens.</summary>
    Task<int> OpenWindowLotsAsync(Guid rankedGroupId, int windowIndex, DateTime endsUtc, CancellationToken ct = default);

    /// <summary>Settle every open lot whose timer has elapsed (assign the player to the top bidder + charge
    /// them, or mark it unsold). <paramref name="force"/> settles all open lots regardless of the timer — the
    /// dev/staging fast-forward. Called by the season tick each run. Returns the number of lots settled.</summary>
    Task<int> SettleDueAsync(bool force = false, CancellationToken ct = default);
}
