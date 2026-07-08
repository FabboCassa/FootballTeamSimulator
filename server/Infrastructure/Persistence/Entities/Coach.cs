namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A coach in a world. May be human-controlled (linked to a user account added in Phase 7.2
/// via <see cref="OwnerUserId"/>) or an AI coach in the whole-world carousel. A coach may be
/// between clubs (<see cref="ClubId"/> null) after a sacking.
/// </summary>
public sealed class Coach
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }
    public World? World { get; set; }

    public Guid? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Sim.Core coach id within the world (unique per world).</summary>
    public int ExternalId { get; set; }

    /// <summary>
    /// The account controlling this coach, once auth exists (Phase 7.2, ASP.NET Identity).
    /// Null for AI coaches. No FK constraint yet — the users table lands with Identity.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsHuman { get; set; }

    public int Reputation { get; set; }
    public int BoardConfidence { get; set; }
    public int ObjectiveExpectedPosition { get; set; }
    public int LastFinishPosition { get; set; }
}
