namespace Fts.Application.Leagues;

/// <summary>
/// Online-auction use cases (Phase 8.5). A window opens a lot per free agent (seeded unattached at world
/// creation); members place ascending bids with a per-lot timer and anti-sniping; settlement assigns the
/// player to the highest bidder and charges their transfer budget, leaving the losers untouched ("others
/// refunded" — no money is moved until a lot settles, so an outbid simply frees the reservation). The
/// account id is always passed in by the Api from the access token. Expected failures come back as
/// <see cref="LeagueResult{T}"/> (no exceptions). All state is authoritative in Postgres/EF; live pushes
/// are a thin broadcast layer over the top (see <see cref="IAuctionBroadcaster"/>).
/// </summary>
public interface IAuctionService
{
    /// <summary>Opens the next auction window (creator only): creates a lot for every free agent in the
    /// world with a per-lot timer. Requires the season to be Active and no window already open.</summary>
    Task<LeagueResult<AuctionsDto>> OpenWindowAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>The current auction view for a member: the lots + the caller's budget picture.</summary>
    Task<LeagueResult<AuctionsDto>> GetAuctionsAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>Places a bid on a lot. Validated against the current high bid, the minimum increment and
    /// the caller's available budget (transfer budget minus the club's other leading bids). Displacing a
    /// previous leader notifies them (outbid) and, on a last-moments bid, extends the timer.</summary>
    Task<LeagueResult<BidResultDto>> PlaceBidAsync(
        Guid userId, Guid leagueId, Guid auctionId, PlaceBidRequest request, CancellationToken ct = default);

    /// <summary>Closes the window now (creator only): settles every open lot immediately (highest bidder
    /// wins, player assigned + fee charged; a lot with no bids goes Unsold). The anti-stall / testing
    /// driver; production also settles each lot automatically when its own timer elapses.</summary>
    Task<LeagueResult<AuctionsDto>> CloseWindowAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>Settles a single lot if its timer has elapsed (the Hangfire per-lot job entry point). A
    /// no-op if the lot is already closed or was extended past now (the caller re-schedules for the new
    /// end time). Not member-gated — it is a server-internal settlement.</summary>
    Task SettleDueAsync(Guid auctionId, CancellationToken ct = default);
}
