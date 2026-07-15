namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A member's submitted match inputs for their club in a private league (Phase 8.3). Stores the LAST
/// lineup/tactic/plan the member submitted; the round-resolution reuses it each matchday until it is
/// changed. When a member has never submitted (or the stored lineup no longer materialises against the
/// current squad), the resolver falls back to <c>LineupSelector.BestEleven</c> — "ultima formazione
/// inviata, poi BestEleven" (the 8.3 fallback decision). Keyed by (league, club): one live submission
/// per club. The serialized shapes mirror the shared Sim.Core plans (<c>LineupPlan</c>/<c>TacticPlan</c>/
/// <c>PrematchPlan</c>) so the server deserialises them with the exact same types the client produced.
/// </summary>
public sealed class LeagueLineup
{
    public Guid Id { get; set; }

    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>The account that submitted these inputs (kept for authorisation/audit).</summary>
    public Guid UserId { get; set; }

    /// <summary>The club the inputs apply to (the member's assigned club).</summary>
    public Guid ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Serialized <c>Sim.Core.Match.LineupPlan</c> (formation slots + player ids).</summary>
    public string LineupJson { get; set; } = string.Empty;

    /// <summary>Serialized <c>Sim.Core.Tactics.TacticPlan</c>, or null for a neutral tactic.</summary>
    public string? TacticJson { get; set; }

    /// <summary>Serialized <c>Sim.Core.Match.PrematchPlan</c> (conditional rules), or null for none.</summary>
    public string? PrematchPlanJson { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
