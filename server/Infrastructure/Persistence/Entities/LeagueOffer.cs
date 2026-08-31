using Fts.Application.Leagues;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One transfer negotiation inside a private league (Phase 12.1). A buyer club offers a fee for a player
/// on a seller club; the sides trade figures until one accepts, rejects, or the round resolves and the
/// talks expire. Either side may be a BOT (a club no member holds) — a bot answers synchronously inside
/// the request that provoked it, so a row is only ever <see cref="LeagueOfferStatus.Pending"/> while a
/// HUMAN is the one who has to answer.
///
/// <see cref="Amount"/> is the figure currently on the table and <see cref="ProposedBy"/> says who put it
/// there: the other side is the one being waited on. The player/club/user ids are plain denormalised
/// columns (no FK) so the entity keeps a single cascade path (league_offers → private_leagues) — the same
/// convention as <c>bids</c> and <c>ranked_offers</c>; referential integrity is server-authoritative in
/// <c>LeagueMarketService</c>.
/// </summary>
public sealed class LeagueOffer
{
    public Guid Id { get; set; }

    /// <summary>The lobby this negotiation belongs to. Cascade: disbanding the league removes it.</summary>
    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>Denormalised for query/teardown convenience (same world as the private league).</summary>
    public Guid WorldId { get; set; }

    /// <summary>Which market window the talks opened in (0 = pre-season, 1 = mid-season).</summary>
    public int WindowIndex { get; set; }

    public Guid PlayerId { get; set; }
    public int PlayerExternalId { get; set; }

    public Guid BuyerClubId { get; set; }
    public int BuyerClubExternalId { get; set; }
    /// <summary>The buying coach, or null when a bot club is buying.</summary>
    public Guid? BuyerUserId { get; set; }

    public Guid SellerClubId { get; set; }
    public int SellerClubExternalId { get; set; }
    /// <summary>The selling coach, or null when a bot club is selling.</summary>
    public Guid? SellerUserId { get; set; }

    /// <summary>The figure currently on the table.</summary>
    public long Amount { get; set; }

    /// <summary>Who put <see cref="Amount"/> there — the OTHER side must answer it.</summary>
    public LeagueOfferParty ProposedBy { get; set; }

    /// <summary>Negotiation rounds consumed, capped by <c>TransferBalance.MaxNegotiationRounds</c>.</summary>
    public int Rounds { get; set; }

    public LeagueOfferStatus Status { get; set; } = LeagueOfferStatus.Pending;

    /// <summary>The agreed fee once accepted (0 otherwise).</summary>
    public long Fee { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
}
