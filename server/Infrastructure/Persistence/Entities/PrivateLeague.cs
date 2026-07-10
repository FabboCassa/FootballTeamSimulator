using Fts.Application.Leagues;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A private friend league (Phase 8.1) — a lobby of accounts sharing one server-generated
/// <see cref="World"/> (unique players per world, ARCHITECTURE §6.3). Distinct from the world-division
/// <see cref="League"/> entity: this is the online competition/lobby, the <see cref="Entities.League"/>
/// row(s) are the world's divisions. Reached by <see cref="InviteCode"/>. The season lifecycle
/// (draft, fixtures, resolution) builds on this in 8.2+.
/// </summary>
public sealed class PrivateLeague
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Short, unguessable, globally-unique code friends use to join.</summary>
    public string InviteCode { get; set; } = string.Empty;

    /// <summary>Number of clubs in the world = the member cap.</summary>
    public int Size { get; set; }

    /// <summary>Advance mode (8.1 stores it; resolution logic lands in 8.3/8.4).</summary>
    public LeagueMode Mode { get; set; }

    public LeagueStatus Status { get; set; }

    /// <summary>The account that created (and, until they leave, owns) the league.</summary>
    public Guid CreatorUserId { get; set; }

    /// <summary>The generated world this league plays in (1:1 for a private league).</summary>
    public Guid WorldId { get; set; }
    public World? World { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<LeagueMember> Members { get; set; } = new List<LeagueMember>();
}
