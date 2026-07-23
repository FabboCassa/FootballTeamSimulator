using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A free-agent auction lot inside a ranked group (Phase 9.2b): one unattached <see cref="Player"/> (seeded
/// by <see cref="Fts.Infrastructure.Leagues.FreeAgentFactory"/> at world creation) put up for auction when a
/// market window opens. Coaches place ascending bids until the lot's <see cref="EndsUtc"/> (the window's
/// close) passes, when the ranked season tick settles it — the highest bidder gets the player and is charged.
///
/// Adapted from the 8.5 private-league auction, but settled by the ranked calendar tick (no per-lot Hangfire
/// job / SignalR anti-snipe — those don't fit a day-long ranked window). The current leader is tracked on the
/// lot itself (no separate bid-history table in v1); the player/club/user ids are plain denormalised columns
/// so the entity keeps a single cascade path (ranked_auctions → ranked_groups).
/// </summary>
public sealed class RankedAuction
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    public Guid PlayerId { get; set; }
    public int PlayerExternalId { get; set; }

    /// <summary>Which market window opened this lot (0 = season start, 1 = midpoint).</summary>
    public int WindowIndex { get; set; }

    /// <summary>Minimum first bid (a fraction of the player's market value).</summary>
    public long StartPrice { get; set; }

    /// <summary>Current highest bid (0 while no one has bid).</summary>
    public long HighBid { get; set; }

    /// <summary>The club / its external id / the owner account currently leading, or null with no bids.</summary>
    public Guid? HighBidClubId { get; set; }
    public int? HighBidClubExternalId { get; set; }
    public Guid? HighBidUserId { get; set; }

    public RankedAuctionStatus Status { get; set; } = RankedAuctionStatus.Open;

    /// <summary>When the lot closes (the market window's close instant).</summary>
    public DateTime EndsUtc { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }
}
