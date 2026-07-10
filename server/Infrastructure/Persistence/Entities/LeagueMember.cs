namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// An account's membership of a <see cref="PrivateLeague"/> (Phase 8.1) — ARCHITECTURE §6.3
/// <c>league_members(club)</c>. The <see cref="ClubId"/> is assigned at the 8.2 draft/auction; it is
/// null while the league is still forming. <see cref="IsReady"/> supports the "advance when all ready"
/// mode (the flag is stored now; the advance logic lands in 8.4).
/// </summary>
public sealed class LeagueMember
{
    public Guid Id { get; set; }

    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>The account controlling this membership (FK to the Identity users table).</summary>
    public Guid UserId { get; set; }

    /// <summary>The club this member controls in the world, once assigned (8.2). Null while forming.</summary>
    public Guid? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>"Ready to advance" flag for the all-ready mode (8.4 consumes it).</summary>
    public bool IsReady { get; set; }

    public DateTime JoinedUtc { get; set; }
}
