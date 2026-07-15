namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A member's submitted training plan for their club in a private league (Phase 8.4). Stores the LAST
/// <c>Sim.Core.Development.TrainingPlan</c> the member submitted (team focus + per-player individual
/// focuses); the server-authoritative weekly development tick reuses it each round-week until it is
/// changed. A club without a submission develops the AI default (<c>TrainingPlan.Balanced</c>). Keyed by
/// (league, club): one live plan per club, mirroring <see cref="LeagueLineup"/>. The serialized shape is
/// the shared Sim.Core plan so the server deserialises exactly what the client produced.
/// </summary>
public sealed class LeagueTraining
{
    public Guid Id { get; set; }

    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>The account that submitted the plan (kept for authorisation/audit).</summary>
    public Guid UserId { get; set; }

    /// <summary>The club the plan applies to (the member's assigned club).</summary>
    public Guid ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Serialized <c>Sim.Core.Development.TrainingPlan</c> (team focus + individual focuses).</summary>
    public string TrainingJson { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; }
}
