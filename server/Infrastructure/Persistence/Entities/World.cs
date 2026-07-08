namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A generated game world — the root aggregate that owns leagues, clubs, players and
/// coaches. Players are unique per world (ARCHITECTURE §6.3), so every world-scoped row
/// carries a <see cref="Id"/> foreign key back here.
/// </summary>
public sealed class World
{
    public Guid Id { get; set; }

    /// <summary>Human-readable world name (e.g. the public-ranked world label).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic generation seed. The same seed re-runs Sim.Core generation to the
    /// byte-identical world, so persistence stays consistent with the shared logic.
    /// </summary>
    public long Seed { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<League> Leagues { get; set; } = new List<League>();
    public ICollection<Club> Clubs { get; set; } = new List<Club>();
    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<Coach> Coaches { get; set; } = new List<Coach>();
    public ICollection<Transfer> Transfers { get; set; } = new List<Transfer>();
}
