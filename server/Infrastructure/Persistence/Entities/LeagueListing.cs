namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A player his club has put on the transfer list inside a private league (Phase 12.1) — the "sell" half
/// of the market. A listing advertises an asking price: other coaches see it on the market screen, and the
/// bot clubs that need him bid straight away, so selling never depends on a friend being online.
///
/// One live row per (league, player); unlisting deletes it. Plain denormalised ids (no FK) keep a single
/// cascade path (league_listings → private_leagues), as with <c>league_offers</c>.
/// </summary>
public sealed class LeagueListing
{
    public Guid Id { get; set; }

    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>Denormalised for query/teardown convenience (same world as the private league).</summary>
    public Guid WorldId { get; set; }

    public Guid PlayerId { get; set; }
    public int PlayerExternalId { get; set; }

    /// <summary>The club advertising him at the moment the listing was made.</summary>
    public Guid ClubId { get; set; }
    public int ClubExternalId { get; set; }

    /// <summary>The coach who listed him, or null when a bot club listed him.</summary>
    public Guid? UserId { get; set; }

    /// <summary>What his club is asking. Offers below it are still allowed — this is a shop window, not a floor.</summary>
    public long AskingPrice { get; set; }

    public DateTime CreatedUtc { get; set; }
}
