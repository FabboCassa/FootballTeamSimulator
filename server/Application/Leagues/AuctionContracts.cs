namespace Fts.Application.Leagues;

/// <summary>Request/response DTOs for online auctions (Phase 8.5). Plain records so the Api binds them
/// from JSON and the Unity client mirrors them. <see cref="IAuctionService"/> lives here (Application);
/// the implementation is in Infrastructure (EF + Sim.Core valuation). Auctions run on free agents seeded
/// unattached at world creation; a window (start / mid-season) opens a lot per free agent with a per-lot
/// timer, members place ascending bids, and settlement assigns the player to the winner.</summary>

/// <summary>Lifecycle of one auction lot.</summary>
public enum AuctionStatus
{
    /// <summary>Accepting bids until the timer elapses.</summary>
    Open = 0,
    /// <summary>Closed with a winner — the player has been assigned and the fee charged.</summary>
    Settled = 1,
    /// <summary>Closed with no bids — the player stays a free agent.</summary>
    Unsold = 2,
}

/// <summary>One auction lot as the client sees it. <see cref="PlayerExternalId"/> is the Sim.Core id
/// (unique per world). <see cref="SecondsRemaining"/> is derived from <see cref="EndsUtc"/> at read time
/// (0 once elapsed) so a client without a synced clock can still show a countdown.</summary>
public sealed record AuctionLotDto(
    Guid AuctionId,
    int PlayerExternalId,
    string PlayerName,
    int Age,
    int Role,
    int Overall,
    int Potential,
    long MarketValue,
    long StartPrice,
    long HighBid,
    int? HighBidClubExternalId,
    string? HighBidClubName,
    AuctionStatus Status,
    DateTime EndsUtc,
    int SecondsRemaining);

/// <summary>The whole auction view for a member: the lots + the caller's budget picture. <see
/// cref="Committed"/> is the sum of the caller's currently-leading bids; <see cref="Available"/> =
/// <see cref="Budget"/> − <see cref="Committed"/> is what a new bid may not exceed.</summary>
public sealed record AuctionsDto(
    IReadOnlyList<AuctionLotDto> Lots,
    int? YourClubExternalId,
    long Budget,
    long Committed,
    long Available,
    bool WindowOpen);

/// <summary>Place a bid on a lot. The amount is validated server-side against the current high bid,
/// the minimum increment and the caller's available budget.</summary>
public sealed record PlaceBidRequest(long Amount);

/// <summary>Outcome of a bid: the updated lot, whether it displaced a previous leader (who is notified),
/// and whether the timer was extended by the anti-snipe rule. <see cref="Available"/> is the caller's
/// remaining available budget after this bid.</summary>
public sealed record BidResultDto(
    AuctionLotDto Lot,
    bool OutbidPrevious,
    int? PreviousLeaderClubExternalId,
    bool Extended,
    long Available);
