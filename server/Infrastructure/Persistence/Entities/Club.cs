namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A club instance inside a world. <see cref="ExternalId"/> is the integer id Sim.Core uses
/// for the club within the world's in-memory model, so a persisted world round-trips to the
/// shared logic without renumbering.
/// </summary>
public sealed class Club
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }
    public World? World { get; set; }

    public Guid LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>Sim.Core club id within the world (unique per world).</summary>
    public int ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Coarse squad strength used for seeding budgets, finances and objectives.</summary>
    public int Strength { get; set; }

    /// <summary>Spendable transfer kitty (seeded from finances each season).</summary>
    public long TransferBudget { get; set; }

    /// <summary>Operating cash balance (finances live from Phase 5.5).</summary>
    public long Balance { get; set; }

    public ICollection<Player> Players { get; set; } = new List<Player>();

    /// <summary>The current coach, if any (a club can be between coaches — carousel, Phase 5.6).</summary>
    public Coach? Coach { get; set; }
}
