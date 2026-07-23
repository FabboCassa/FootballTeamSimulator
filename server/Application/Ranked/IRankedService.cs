namespace Fts.Application.Ranked;

/// <summary>
/// The public ranked ladder use cases (Phase 9.1): enrol into a server-managed world, read your own
/// state / a group's fixed-size seat list, toggle auto re-enrolment, and close a placement season by
/// sorting its coaches into divisions.
///
/// The account id is always passed in by the Api from the access token — never trusted from the body.
/// Expected failures come back as <see cref="RankedResult{T}"/>, not exceptions.
/// </summary>
public interface IRankedService
{
    /// <summary>Join the ladder. Idempotent: an already-enrolled account gets its current state back.
    /// A newcomer is seated in a placement group (a new one, and if necessary a whole new ranked world,
    /// is opened when the existing ones have no room).</summary>
    Task<RankedResult<RankedStateDto>> EnrolAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The caller's ladder state — <see cref="RankedStateDto.Enrolled"/> is false if they never joined.</summary>
    Task<RankedResult<RankedStateDto>> GetMineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>A group with every seat (occupied or AI) — public to any signed-in account.</summary>
    Task<RankedResult<RankedGroupDto>> GetGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    /// <summary>Opt in/out of automatic re-enrolment next season (ranking is kept either way).</summary>
    Task<RankedResult<RankedStateDto>> SetAutoEnrolAsync(Guid userId, bool autoEnrol, CancellationToken ct = default);

    /// <summary>Close a placement season: sort its coaches into division seats by final position
    /// (top finishers into the higher tier, the rest into the lowest), seed their ranking, and free the
    /// placement seats. Called by the season scheduler in 9.2; exposed on a dev-only internal endpoint until then.</summary>
    Task<RankedResult<PlacementResultDto>> ResolvePlacementAsync(
        Guid groupId, IReadOnlyList<Guid>? finalOrder, CancellationToken ct = default);
}
