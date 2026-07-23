using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A direct coach-to-coach transfer offer inside a ranked group (Phase 9.2b): a BUYER offers a fee for a
/// player on a SELLER's club during a market window. The seller accepts (the player moves + both budgets
/// settle + a <see cref="Transfer"/> is logged) or rejects; the buyer can withdraw while it is Pending.
///
/// The player/club/user ids are plain denormalised columns (no FK) so the entity keeps a single cascade
/// path (ranked_offers → ranked_groups) — the same convention as <c>ranked_lineups</c>/<c>bids</c>. The
/// referential integrity (the player is on the seller's club, both coaches hold seats in the group) is
/// server-authoritative in <c>RankedMarketService</c>.
/// </summary>
public sealed class RankedOffer
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>The market window the offer was made in (0 = season start, 1 = midpoint).</summary>
    public int WindowIndex { get; set; }

    public Guid PlayerId { get; set; }
    public int PlayerExternalId { get; set; }

    /// <summary>The coach making the offer + the club the player would join.</summary>
    public Guid BuyerUserId { get; set; }
    public Guid BuyerClubId { get; set; }

    /// <summary>The coach who owns the player + the club it would leave.</summary>
    public Guid SellerUserId { get; set; }
    public Guid SellerClubId { get; set; }

    public long Fee { get; set; }

    public RankedOfferStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
}
