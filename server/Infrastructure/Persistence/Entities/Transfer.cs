namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A completed transfer within a world (AI↔AI or involving a human club). Either side may be
/// null (a free-agent signing has no <see cref="FromClubId"/>; a release has no
/// <see cref="ToClubId"/>). This is the transaction log the market UI / inbox read from.
/// </summary>
public sealed class Transfer
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }
    public World? World { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public Guid? FromClubId { get; set; }
    public Guid? ToClubId { get; set; }

    public long Fee { get; set; }

    /// <summary>In-game season year and day the deal closed.</summary>
    public int SeasonYear { get; set; }
    public int Day { get; set; }

    public DateTime CreatedUtc { get; set; }
}
