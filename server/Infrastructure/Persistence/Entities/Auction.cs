using Fts.Application.Leagues;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A single free-agent auction lot inside a private league (Phase 8.5). One <see cref="Player"/>
/// (seeded unattached at world creation, <see cref="Fts.Infrastructure.Leagues.FreeAgentFactory"/>) is
/// put up for auction in a window (start / mid-season). Members place ascending bids until the
/// per-lot timer (<see cref="EndsUtc"/>) elapses; a bid inside the anti-snipe window extends it. The
/// lot is the authoritative live state (this is the Postgres/EF store chosen for 8.5 — see ROADMAP):
/// <see cref="HighBid"/>/<see cref="HighBidClubId"/> track the current leader, and settlement assigns
/// the player to the winner and charges their transfer budget.
/// </summary>
public sealed class Auction
{
    public Guid Id { get; set; }

    /// <summary>The lobby this auction belongs to. Cascade: disbanding the league removes its lots.</summary>
    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>Denormalised for query/teardown convenience (same world as the private league).</summary>
    public Guid WorldId { get; set; }

    /// <summary>The free-agent player being auctioned. Restrict (not cascade): players already cascade
    /// from worlds, so a second cascade path would be rejected by some providers; the disband teardown
    /// deletes auctions explicitly before players.</summary>
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>Which auction window opened this lot: 0 = season start, 1 = mid-season.</summary>
    public int WindowIndex { get; set; }

    /// <summary>The minimum first bid (a fraction of the player's market value).</summary>
    public long StartPrice { get; set; }

    /// <summary>The current highest bid (0 while no one has bid).</summary>
    public long HighBid { get; set; }

    /// <summary>The club (id / Sim.Core ExternalId / owner account) currently leading, or null if no bids.
    /// Plain columns (no FK relationship) to avoid extra cascade paths — referential integrity is kept by
    /// the server-authoritative <see cref="Fts.Infrastructure.Leagues.AuctionService"/>.</summary>
    public Guid? HighBidClubId { get; set; }
    public int? HighBidClubExternalId { get; set; }
    public Guid? HighBidUserId { get; set; }

    public AuctionStatus Status { get; set; } = AuctionStatus.Open;

    /// <summary>When the lot closes (unless extended by an anti-snipe bid).</summary>
    public DateTime EndsUtc { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }

    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}
