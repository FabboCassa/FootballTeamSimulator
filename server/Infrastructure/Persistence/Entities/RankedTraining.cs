namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A ranked coach's stored training plan for their seat's club (Phase 9.4) — the ladder twin of
/// <see cref="LeagueTraining"/> (8.4). The server-authoritative weekly development tick that runs with every
/// resolved matchday reuses it until the coach changes it; a club with no row trains the AI default
/// (<c>TrainingPlan.Balanced</c>), which is exactly what every ranked club did before 9.4.
///
/// This is one of the 9.4 "smart defaults": a balanced plan is seeded for every human coach when their season
/// starts, so the daily loop never requires a visit to the training screen — it only rewards one.
///
/// One row per (group, club) — the upsert key. <see cref="UserId"/> is a plain denormalised column (no FK),
/// keeping a single cascade path (ranked_trainings → ranked_groups), the same convention as
/// <see cref="RankedLineup"/>.
/// </summary>
public sealed class RankedTraining
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>The account the plan belongs to (plain column — seat/club integrity is server-authoritative).</summary>
    public Guid UserId { get; set; }

    public Guid ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Serialized <c>Sim.Core.Development.TrainingPlan</c> (team focus + individual focuses).</summary>
    public string TrainingJson { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; }
}
