namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A footballer instance inside a world (players are unique per world — ARCHITECTURE §6.3).
/// The 10 Sim.Core skills are stored verbatim as a jsonb blob (<see cref="AttributesJson"/>)
/// so the persisted player round-trips exactly to <c>Sim.Core.Domain.PlayerAttributes</c>,
/// while the denormalised columns (overall/potential/age/role/value) support querying and
/// sorting without deserialising every row.
/// </summary>
public sealed class Player
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }
    public World? World { get; set; }

    /// <summary>Owning club, or null for a free agent.</summary>
    public Guid? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Sim.Core player id within the world (unique per world).</summary>
    public int ExternalId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int Age { get; set; }

    /// <summary>Cast of <c>Sim.Core.Domain.PositionRole</c> (0 = GK … 7 = Striker).</summary>
    public int Role { get; set; }

    public int Overall { get; set; }
    public int Potential { get; set; }
    public long MarketValue { get; set; }

    public long WeeklyWage { get; set; }
    public int ContractSeasonsRemaining { get; set; }

    /// <summary>
    /// The full <c>PlayerAttributes</c> (10 skills) serialised as JSON, stored in a jsonb
    /// column. The attributes evolve with the server-authoritative development tick (Phase 8.4)
    /// and are written back after every round-week.
    /// </summary>
    public string AttributesJson { get; set; } = "{}";

    // --- Server-authoritative condition (Phase 8.4) -------------------------------------------
    // Form/Morale/Fitness (each 0–100, mirroring Sim.Core.Domain.PlayerCondition) are now stored
    // and evolved by the online season's weekly condition tick, so a private-league match resolves
    // from the current condition instead of raw attributes. Seeded neutral (Form/Morale 50,
    // Fitness 100) at world creation; the migration backfills existing rows via the column defaults.

    /// <summary>Short-term performance trend, 0–100 (50 = neutral).</summary>
    public int Form { get; set; } = 50;

    /// <summary>Mental wellbeing, 0–100 (50 = neutral).</summary>
    public int Morale { get; set; } = 50;

    /// <summary>Physical freshness, 0–100 (100 = fully fit).</summary>
    public int Fitness { get; set; } = 100;
}
