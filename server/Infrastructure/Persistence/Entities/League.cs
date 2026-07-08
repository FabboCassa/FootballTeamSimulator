namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>A division within a world. Clubs belong to exactly one league.</summary>
public sealed class League
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }
    public World? World { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>1 = top flight, 2 = second division, … (mirrors the SP two-division world).</summary>
    public int Division { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<Club> Clubs { get; set; } = new List<Club>();
}
