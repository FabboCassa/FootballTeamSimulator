using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// An account's enrolment in the public ranked ladder (Phase 9.1) — one row per user, ever. Holds the
/// persistent progression (<see cref="Rating"/>, the ladder's real reward per ARCHITECTURE §5.6) plus
/// where the coach currently sits.
///
/// <see cref="SeatId"/>/<see cref="PlacementGroupId"/> are plain denormalised columns (no FK) so the
/// entity graph keeps a single cascade path (ranked_coaches → ranked_worlds) — the same convention as
/// <c>bids</c>/<c>live_matches</c>.
/// </summary>
public sealed class RankedCoach
{
    public Guid Id { get; set; }

    /// <summary>The account. Globally unique — a coach belongs to at most one ranked world at a time.</summary>
    public Guid UserId { get; set; }

    public Guid RankedWorldId { get; set; }
    public RankedWorld? RankedWorld { get; set; }

    /// <summary>Elo-style ladder rating — the ladder's persistent progression. Seeded at placement, then
    /// moved by <c>IRankedRankingService</c> after every matchday and at each season end (Phase 9.3).
    /// PostgreSQL is authoritative; the Redis leaderboard is a rebuildable cache of this column.</summary>
    public int Rating { get; set; }

    /// <summary>Highest rating ever held (never decreases) — the "career best" shown on the palmarès.</summary>
    public int PeakRating { get; set; }

    /// <summary>Division seasons completed (Phase 9.3). Placement does not count.</summary>
    public int SeasonsPlayed { get; set; }

    public RankedCoachStatus Status { get; set; }

    /// <summary>The seat currently held (placement seat while placing, division seat once placed).</summary>
    public Guid? SeatId { get; set; }

    /// <summary>The placement group played (kept after placement as history).</summary>
    public Guid? PlacementGroupId { get; set; }

    /// <summary>Final position in the placement season (1-based), null until it is resolved.</summary>
    public int? PlacementPosition { get; set; }

    /// <summary>Automatically re-enrol next season (the ladder default); a coach can opt out and keep
    /// their ranking. Consumed by the seasonal reset in 9.3.</summary>
    public bool AutoEnrol { get; set; } = true;

    public DateTime EnrolledUtc { get; set; }

    public DateTime? PlacedUtc { get; set; }
}
