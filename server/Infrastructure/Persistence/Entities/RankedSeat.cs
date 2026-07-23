namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A club slot in a <see cref="RankedGroup"/> (Phase 9.1). Created with the group and never deleted:
/// claiming a seat only sets <see cref="UserId"/>, so the group's team count can never drift. A seat with
/// no user is an AI club.
///
/// <see cref="ClubId"/> is filled when the group is materialised (its world generated) and stays put
/// afterwards — a coach takes over an existing club rather than bringing one with them.
/// </summary>
public sealed class RankedSeat
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>0-based, stable slot number within the group.</summary>
    public int SeatIndex { get; set; }

    /// <summary>The club this seat plays. Null until the group is materialised.</summary>
    public Guid? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>The account holding the seat, or null for an AI club. Plain column (no FK) — the same
    /// convention as <c>league_members.UserId</c>.</summary>
    public Guid? UserId { get; set; }

    public DateTime? OccupiedUtc { get; set; }
}
