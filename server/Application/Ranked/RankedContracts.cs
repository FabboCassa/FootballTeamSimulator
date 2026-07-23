namespace Fts.Application.Ranked;

/// <summary>
/// Request/response DTOs for the public ranked ladder (Phase 9.1). Plain records so the Api binds them
/// from JSON and the client mirrors them. <see cref="IRankedService"/> lives here (Application); the
/// implementation is in Infrastructure (needs EF + the shared Sim.Core world generation).
///
/// Vocabulary (ARCHITECTURE §5.6 "public ranked"):
/// <list type="bullet">
/// <item><b>Ranked world</b> — one self-contained pyramid. Fixed shape: tier 1 = 1 group, tier 2 = 2 groups,
/// tier 3 = 4 groups, 8 clubs each (56 seats). When a world runs out of room the server opens another one.</item>
/// <item><b>Group</b> (girone) — one competition of <see cref="RankedGroupDto.Capacity"/> clubs. Its seats are
/// created up front and never grow, which is what keeps division sizes fixed; a vacant seat is an AI club.</item>
/// <item><b>Placement</b> — a new coach does NOT drop straight into a division: they play a placement season in
/// a placement group, and their finishing position decides which division they are sorted into.</item>
/// </list>
/// </summary>

/// <summary>What a group is for. Placement groups are transient (one placement season, then their coaches
/// are sorted into divisions and the group is <see cref="RankedGroupStatus.Completed"/>); division groups are
/// the permanent pyramid seats.</summary>
public enum RankedGroupKind
{
    /// <summary>A placement season: 8 newcomers play each other, then get sorted by final position.</summary>
    Placement = 0,
    /// <summary>A permanent pyramid group at some tier (1 = top).</summary>
    Division = 1,
}

/// <summary>Where a group is in its season. 9.1 only fills the seats and flips a full group to
/// <see cref="Active"/>; actually running the season on a real-time calendar is 9.2.</summary>
public enum RankedGroupStatus
{
    /// <summary>Still filling seats (a division group is permanently Forming until its season starts).</summary>
    Forming = 0,
    /// <summary>Every seat is claimed — the season can run.</summary>
    Active = 1,
    /// <summary>Season over. For a placement group this also means its coaches have been sorted.</summary>
    Completed = 2,
}

/// <summary>Where an enrolled coach is in the ladder.</summary>
public enum RankedCoachStatus
{
    /// <summary>Playing (or waiting to play) the placement season — not yet in the pyramid.</summary>
    Placement = 0,
    /// <summary>Holding a division seat.</summary>
    Placed = 1,
    /// <summary>Left the ladder (kept for ranking history).</summary>
    Retired = 2,
}

/// <summary>Whether a ranked world can still take new coaches.</summary>
public enum RankedWorldStatus
{
    /// <summary>Has room: free division seats not already reserved by a pending placement cohort.</summary>
    Open = 0,
    /// <summary>Every placeable seat is taken (or reserved) — enrolment spills over to another world.</summary>
    Full = 1,
    /// <summary>Retired world (no longer scheduled).</summary>
    Closed = 2,
}

/// <summary>One seat in a group: a club, optionally held by a coach. A seat with no
/// <see cref="UserId"/> is an AI club — that is how a division keeps a fixed number of teams
/// regardless of how many humans are in it.</summary>
public sealed record RankedSeatDto(
    int SeatIndex,
    Guid? UserId,
    string? DisplayName,
    int? ClubExternalId,
    string? ClubName,
    int ClubStrength,
    bool IsYou);

/// <summary>A group (girone) with its full, fixed-size seat list.</summary>
public sealed record RankedGroupDto(
    Guid Id,
    Guid RankedWorldId,
    string WorldName,
    RankedGroupKind Kind,
    int Tier,
    int GroupIndex,
    string Name,
    int Capacity,
    int Occupied,
    RankedGroupStatus Status,
    IReadOnlyList<RankedSeatDto> Seats);

/// <summary>The caller's ladder state. <see cref="Enrolled"/> is false (everything else null/default)
/// for an account that has never joined the ranked mode.</summary>
public sealed record RankedStateDto(
    bool Enrolled,
    Guid? RankedWorldId,
    string? WorldName,
    Guid? GroupId,
    string? GroupName,
    RankedGroupKind? Kind,
    int? Tier,
    int? SeatIndex,
    int? ClubExternalId,
    string? ClubName,
    int Rating,
    RankedCoachStatus Status,
    int? PlacementPosition,
    bool AutoEnrol)
{
    public static RankedStateDto NotEnrolled() => new(
        false, null, null, null, null, null, null, null, null, null, 0,
        RankedCoachStatus.Placement, null, false);
}

/// <summary>Close a placement season and sort its coaches into divisions. <see cref="FinalOrder"/> is the
/// final standings (best first) as account ids — supplied by the season scheduler in 9.2. When omitted the
/// server falls back to a deterministic order (squad strength), so a placement can always be closed.</summary>
public sealed record ResolvePlacementRequest(IReadOnlyList<Guid>? FinalOrder);

/// <summary>Where one coach ended up after their placement season.</summary>
public sealed record PlacementAssignmentDto(
    Guid UserId,
    int Position,
    Guid RankedWorldId,
    string WorldName,
    Guid GroupId,
    string GroupName,
    int Tier,
    int SeatIndex,
    int ClubExternalId,
    string ClubName,
    int Rating);

/// <summary>The outcome of closing a placement season.</summary>
public sealed record PlacementResultDto(Guid GroupId, IReadOnlyList<PlacementAssignmentDto> Assignments);

/// <summary>Opt in/out of automatic re-enrolment for the next season (the ladder auto-enrols by default;
/// a coach can opt out and keep their ranking).</summary>
public sealed record SetAutoEnrolRequest(bool AutoEnrol);

/// <summary>Why a ranked use case failed — the Api maps these to HTTP status codes.</summary>
public enum RankedError
{
    None = 0,
    ValidationFailed,
    NotFound,
    /// <summary>The caller has never joined the ranked ladder.</summary>
    NotEnrolled,
    Forbidden,
    /// <summary>The action does not apply in the group's current state (e.g. resolving a placement twice,
    /// or resolving a group that is not a placement group).</summary>
    WrongPhase,
    /// <summary>No seat could be found or created (should be unreachable — the server opens a new world).</summary>
    NoCapacity,
    /// <summary>The requested fixture does not exist in the caller's group (Phase 9.2).</summary>
    FixtureNotFound,
    /// <summary>The fixture has not been played yet, so there is no replay to serve (Phase 9.2).</summary>
    ReplayNotReady,
    /// <summary>The buyer's transfer budget cannot cover the offered fee (Phase 9.2b market).</summary>
    InsufficientBudget,
}

/// <summary>Result wrapper so the service never throws for expected failures — mirrors
/// <c>LeagueResult&lt;T&gt;</c>. Exactly one of <see cref="Value"/> or <see cref="Error"/> is meaningful.</summary>
public sealed record RankedResult<T>(bool Success, T? Value, RankedError Error, string? Message)
{
    public static RankedResult<T> Ok(T value) => new(true, value, RankedError.None, null);
    public static RankedResult<T> Fail(RankedError error, string? message = null) =>
        new(false, default, error, message);
}
