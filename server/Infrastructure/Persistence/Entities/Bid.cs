namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One bid placed on an <see cref="Auction"/> lot (Phase 8.5). Append-only history — the current
/// leader is denormalised onto the <see cref="Auction"/> for O(1) reads, but every bid is recorded so
/// the client can show a bid log and so "who bid first" is auditable. The club / user columns are plain
/// (no FK relationship) to avoid extra cascade paths back to worlds; the disband teardown deletes bids
/// explicitly before players/clubs.
/// </summary>
public sealed class Bid
{
    public Guid Id { get; set; }

    public Guid AuctionId { get; set; }
    public Auction? Auction { get; set; }

    /// <summary>Denormalised so the disband teardown can bulk-delete a league's bids in one query.</summary>
    public Guid PrivateLeagueId { get; set; }

    public Guid ClubId { get; set; }
    public int ClubExternalId { get; set; }
    public Guid UserId { get; set; }

    public long Amount { get; set; }

    public DateTime PlacedUtc { get; set; }
}
