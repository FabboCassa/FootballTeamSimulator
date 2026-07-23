namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A coach's persisted match input for their ranked seat's club (Phase 9.2) — the "last submitted lineup"
/// reused each matchday when the round resolves on the real-time calendar. Mirrors <c>LeagueLineup</c>:
/// a serialized Sim.Core <c>LineupPlan</c> plus optional <c>TacticPlan</c>/<c>PrematchPlan</c>. One row per
/// (group, club) — the upsert key — so a coach always has exactly one live submission for their club.
///
/// <see cref="UserId"/> is a plain denormalised column (no FK), keeping a single cascade path
/// (ranked_lineups → ranked_groups), the same convention as <c>ranked_seats.UserId</c>/<c>league_lineups</c>.
/// </summary>
public sealed class RankedLineup
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>The account that submitted it (plain column — the seat/club integrity is server-authoritative).</summary>
    public Guid UserId { get; set; }

    public Guid ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Serialized Sim.Core <c>LineupPlan</c> (required).</summary>
    public string LineupJson { get; set; } = string.Empty;

    /// <summary>Serialized Sim.Core <c>TacticPlan</c>, or null.</summary>
    public string? TacticJson { get; set; }

    /// <summary>Serialized Sim.Core <c>PrematchPlan</c>, or null.</summary>
    public string? PrematchPlanJson { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
