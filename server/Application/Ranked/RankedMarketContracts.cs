namespace Fts.Application.Ranked;

/// <summary>
/// The ranked market (Phase 9.2b, increment 2): direct coach-to-coach transfer offers during a season's
/// market windows. Each ranked club is seeded a transfer budget when its season starts; a coach browses a
/// rival's squad, offers a fee for a player, and the owner accepts (the player moves + budgets settle) or
/// rejects. All gated to an open market window (see <see cref="RankedMarketWindowDto"/> on the season).
///
/// This is the "comprare da altri giocatori mandando offerte" half of the market decision; the free-agent
/// AUCTIONS (reusing 8.5) are a later increment.
/// </summary>

/// <summary>Lifecycle of a direct offer. Stable numeric values (stored + on the wire).</summary>
public enum RankedOfferStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Withdrawn = 3,
}

/// <summary>One player in a browsed ranked squad. <see cref="Role"/> is the numeric
/// <c>Sim.Core.Domain.PositionRole</c> (0 = GK … 7 = Striker); the client maps it to a label.</summary>
public sealed record RankedPlayerDto(
    int ExternalId,
    string Name,
    int Age,
    int Role,
    int Overall,
    long MarketValue);

/// <summary>A club's squad, for browsing who to bid on. <see cref="IsHuman"/> is false for an AI club
/// (a vacant seat) — you can only send a direct offer to a human-held club.</summary>
public sealed record RankedSquadDto(
    int ClubExternalId,
    string ClubName,
    bool IsHuman,
    IReadOnlyList<RankedPlayerDto> Players);

/// <summary>Offer a fee for a player on another coach's club (found by its world-unique external id).</summary>
public sealed record MakeRankedOfferRequest(int PlayerExternalId, long Fee);

/// <summary>A direct offer as seen by the caller.</summary>
public sealed record RankedOfferDto(
    Guid Id,
    int WindowIndex,
    int PlayerExternalId,
    string PlayerName,
    int BuyerClubExternalId,
    string BuyerClubName,
    int SellerClubExternalId,
    string SellerClubName,
    long Fee,
    RankedOfferStatus Status,
    bool YouAreBuyer,
    bool YouAreSeller);

/// <summary>The caller's market view: their budget, the offers to their club (incoming) and the offers they
/// sent (outgoing), and whether a market window is open right now.</summary>
public sealed record RankedOffersDto(
    long YourBudget,
    bool MarketOpen,
    IReadOnlyList<RankedOfferDto> Incoming,
    IReadOnlyList<RankedOfferDto> Outgoing);

/// <summary>
/// Direct coach-to-coach market use cases (Phase 9.2b). The account id is always passed in by the Api from
/// the token; expected failures come back as <see cref="RankedResult{T}"/>.
/// </summary>
public interface IRankedMarketService
{
    /// <summary>Browse a club's squad in the caller's group (to decide who to bid on).</summary>
    Task<RankedResult<RankedSquadDto>> GetClubSquadAsync(Guid userId, int clubExternalId, CancellationToken ct = default);

    /// <summary>The caller's market view (budget + incoming/outgoing offers + window state).</summary>
    Task<RankedResult<RankedOffersDto>> GetOffersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Offer a fee for a player on another human coach's club — requires an open market window and
    /// enough budget.</summary>
    Task<RankedResult<RankedOffersDto>> MakeOfferAsync(
        Guid userId, MakeRankedOfferRequest request, CancellationToken ct = default);

    /// <summary>Accept (true) or reject (false) an incoming offer — only the player's owner may respond.
    /// Accepting moves the player + settles both budgets + logs the transfer (requires an open window).</summary>
    Task<RankedResult<RankedOffersDto>> RespondAsync(
        Guid userId, Guid offerId, bool accept, CancellationToken ct = default);

    /// <summary>Withdraw an offer the caller made (while it is still Pending).</summary>
    Task<RankedResult<RankedOffersDto>> WithdrawAsync(Guid userId, Guid offerId, CancellationToken ct = default);
}
