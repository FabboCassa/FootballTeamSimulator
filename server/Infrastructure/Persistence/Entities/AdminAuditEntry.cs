using Fts.Application.Admin;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One line of the admin trail (Phase 10.3): who did what, to what, when. Append-only, and deliberately
/// foreign-key-free with the actor's email captured by value — same convention as <c>ranked_awards</c>
/// and <c>integrity_flags</c>, and for the same reason: an audit trail that a cascade delete can erase is
/// not an audit trail. The subject is stored as a plain string <see cref="Target"/> (an account id, a
/// world id, a revision number) because the surface acts on several different kinds of thing and a typed
/// FK per kind would buy nothing an operator can use.
///
/// Written only by <c>AdminService</c>, on the same SaveChanges as the action itself, so an action can
/// never be committed without its audit line.
/// </summary>
public sealed class AdminAuditEntry
{
    public Guid Id { get; set; }

    public Guid ActorUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;

    public AdminAction Action { get; set; }

    /// <summary>What the action was aimed at — an account id, a world id, a revision number.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Free-text context: the operator's reason, or what changed. Capped at 512.</summary>
    public string Details { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
