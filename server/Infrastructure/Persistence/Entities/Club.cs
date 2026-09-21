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

    /// <summary>
    /// The club's persistent stature (0-100), mirroring <see cref="Sim.Core.Domain.Club.Stature"/>
    /// (task: club stature, R4) — set once at <see cref="Leagues.WorldFactory"/> generation time from
    /// the Sim.Core world, so a private-league/ranked club's intra-league wealth spread
    /// (<see cref="Sim.Core.Market.FinanceModel.StatureMultiplierPermille"/>) round-trips to the server
    /// the same way <see cref="Strength"/> already does. Additive — defaults 0, so pre-existing rows
    /// (migration <c>AddClubStature</c>) simply earn no stature-driven revenue until the world is
    /// regenerated. Deliberately NOT used by <see cref="Leagues.LeagueMarketEngine"/>'s wage demand,
    /// which keeps using <see cref="Strength"/> as its stature proxy (task: wages set by the paying
    /// club, R7) — changing that wage path is out of scope here and would move
    /// <c>LeagueMarketWageScaleTests</c>' pinned absolute scale.
    /// </summary>
    public int Stature { get; set; }

    /// <summary>Spendable transfer kitty (seeded from finances each season).</summary>
    public long TransferBudget { get; set; }

    /// <summary>Operating cash balance (finances live from Phase 5.5).</summary>
    public long Balance { get; set; }

    public ICollection<Player> Players { get; set; } = new List<Player>();

    /// <summary>The current coach, if any (a club can be between coaches — carousel, Phase 5.6).</summary>
    public Coach? Coach { get; set; }
}
