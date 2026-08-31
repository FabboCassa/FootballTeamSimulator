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
    /// <summary>Task 12.2: a seller pulled his own lot off the board before anyone bid on it.</summary>
    Cancelled = 3,
}

/// <summary>
/// What KIND of lot this is (task 12.2). Until 12.2 every lot was a free agent opened by the calendar when
/// a market window opened; a coach can now put one of HIS OWN players up, with a timer he chooses. The two
/// kinds share one board, one bidding mechanism and one settlement path — the difference is who is paid and
/// which guards apply (a seller lot moves money between two coaches, so the 9.5 integrity band and the
/// squad-size floor bind; a free agent belongs to nobody, so they do not).
/// </summary>
public enum RankedLotKind
{
    FreeAgent = 0,
    Seller = 1,
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
    int SecondsRemaining,
    // --- task 12.2: who is selling (null on a free-agent lot) -------------------------------------
    RankedLotKind Kind = RankedLotKind.FreeAgent,
    int? SellerClubExternalId = null,
    string SellerClubName = "",
    bool YouAreSeller = false);

/// <summary>The caller's auction view: the open lots + their budget picture (available = budget minus their
/// current leading bids) + whether a market window is open.</summary>
public sealed record RankedAuctionsDto(
    long Budget,
    long Committed,
    long Available,
    bool WindowOpen,
    IReadOnlyList<RankedAuctionLotDto> Lots,
    // --- task 12.2: "when do auctions come back?", answered in-app ---------------------------------
    // WindowClosesUtc: when the window currently open closes (null when the market is shut).
    // NextWindowOpensUtc: when the NEXT window opens (null when this season has no further window).
    // MinLotSeconds / MaxLotSeconds: the duration picker's bounds RIGHT NOW — the configured 1h-24h range,
    // with the ceiling clamped to what is left of the window, because a lot never outlives the market that
    // allowed it. YourClubExternalId: whose squad the sell flow offers up (0 = the caller holds no club).
    DateTime? WindowClosesUtc = null,
    DateTime? NextWindowOpensUtc = null,
    int MinLotSeconds = 0,
    int MaxLotSeconds = 0,
    int YourClubExternalId = 0);

/// <summary>
/// Put one of your OWN players up for auction (task 12.2). <paramref name="Reserve"/> is the opening
/// price — the first bid has to meet it — and 0 means "price him for me" (half his market value, the
/// private-league rule). <paramref name="DurationSeconds"/> is the timer the seller chooses, refused
/// outside the configured 1h-24h range and then clamped to the market window's close.
/// </summary>
public sealed record ListRankedLotRequest(int PlayerExternalId, long Reserve, int DurationSeconds);

/// <summary>Place a bid on a lot.</summary>
public sealed record PlaceRankedBidRequest(long Amount);

/// <summary>The outcome of a bid: the updated lot + whether it displaced a previous leader + the caller's
/// remaining available budget.</summary>
public sealed record RankedBidResultDto(
    RankedAuctionLotDto Lot,
    bool OutbidPrevious,
    int? PreviousLeaderClubExternalId,
    long Available,
    // Extended (task 12.2): the bid landed inside the anti-snipe window and pushed THIS lot's end back.
    bool Extended = false);

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

    /// <summary>Put one of the caller's own players up for auction with a timer of his choosing (task
    /// 12.2). Requires an open window; refuses a duration outside the allowed range, a player who is not
    /// his or already on the board, a reserve outside the 9.5 integrity band, and a listing that would take
    /// his squad below the playable floor (open lots counted).</summary>
    Task<RankedResult<RankedAuctionsDto>> ListLotAsync(
        Guid userId, ListRankedLotRequest request, CancellationToken ct = default);

    /// <summary>Pull one of the caller's own lots off the board (task 12.2) — only while nobody has bid on
    /// it; once there is a bid the timer is the only thing that ends it.</summary>
    Task<RankedResult<RankedAuctionsDto>> UnlistLotAsync(
        Guid userId, Guid auctionId, CancellationToken ct = default);

    /// <summary>Open a lot for every free agent in a group's world for the given window (idempotent per
    /// group+window). Called by the season tick when a market window opens.</summary>
    Task<int> OpenWindowLotsAsync(Guid rankedGroupId, int windowIndex, DateTime endsUtc, CancellationToken ct = default);

    /// <summary>Let the group's AI clubs (vacant seats) bid on the SELLER lots — never on free agents, whose
    /// board is the coaches' own (task 12.2). One raise per lot per call, at the lot's minimum next bid and
    /// bounded by a ceiling on the player's market value, so a coach can always come back over the top.
    /// Called by the season tick. Returns the number of bids placed.</summary>
    Task<int> RunBotBidsAsync(CancellationToken ct = default);

    /// <summary>Settle every open lot whose timer has elapsed (assign the player to the top bidder + charge
    /// them, or mark it unsold). <paramref name="force"/> settles all open lots regardless of the timer — the
    /// dev/staging fast-forward. Called by the season tick each run. Returns the number of lots settled.</summary>
    Task<int> SettleDueAsync(bool force = false, CancellationToken ct = default);
}
