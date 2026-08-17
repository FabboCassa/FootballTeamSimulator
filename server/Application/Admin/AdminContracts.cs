namespace Fts.Application.Admin;

/// <summary>
/// Request/response DTOs for the live-ops admin surface (Phase 10.3). Plain records so the Api binds
/// them from JSON and the static dashboard page (<c>web/admin.html</c>) reads them without a client
/// library. <see cref="IAdminService"/> lives here (Application); the implementation is in
/// Infrastructure (it needs EF + Identity).
///
/// Vocabulary:
/// <list type="bullet">
/// <item><b>Metrics</b> — the operational numbers a human looks at to answer "is the ladder healthy?".
/// NOT gameplay telemetry (that is the 10.1 balance harness): these are counts and lags that only mean
/// something about the SERVER.</item>
/// <item><b>Balance revision</b> — an append-only row holding a whole serialised <c>BalanceConfig</c>.
/// The ACTIVE one is simply the highest revision; a rollback pushes an old payload forward as a NEW
/// revision, so the history is never rewritten and "what was live at 14:05" always has an answer.</item>
/// <item><b>Audit entry</b> — one line per admin action, append-only and foreign-key-free (same
/// convention as <c>ranked_awards</c> / <c>integrity_flags</c>) so the trail outlives the account,
/// world or revision it refers to.</item>
/// </list>
/// </summary>

/// <summary>What an admin did. Stored as an int; the dashboard renders the label.</summary>
public enum AdminAction
{
    None = 0,
    /// <summary>A new balance revision was pushed.</summary>
    BalancePush = 1,
    /// <summary>An older revision's payload was pushed forward as a new revision.</summary>
    BalanceRollback = 2,
    /// <summary>An account was locked out (banned) — it can no longer sign in.</summary>
    UserLock = 3,
    /// <summary>An account's lockout was lifted.</summary>
    UserUnlock = 4,
    /// <summary>An account was granted the admin role.</summary>
    AdminGrant = 5,
    /// <summary>An account had the admin role removed.</summary>
    AdminRevoke = 6,
    /// <summary>A ranked world was closed to new enrolments (existing seasons keep running).</summary>
    WorldClose = 7,
    /// <summary>A closed ranked world was reopened to enrolments.</summary>
    WorldReopen = 8,
}

// --- monitoring ------------------------------------------------------------------------------------

/// <summary>
/// The one call the dashboard (and the watchdog script) polls. Everything here is a number a human can
/// judge at a glance; <see cref="OverdueFixtures"/> and <see cref="FailedJobs"/> are the two that page
/// somebody — the first says the calendar is behind, the second says the scheduler is dropping work.
/// </summary>
public sealed record AdminMetricsDto(
    string Version,
    string SimCoreVersion,
    string Environment,
    DateTime UtcNow,
    double UptimeSeconds,

    // accounts
    int Accounts,
    int AccountsCreated24h,
    int LockedAccounts,

    // ladder
    int RankedWorlds,
    int OpenRankedWorlds,
    int RankedGroups,
    int ActiveGroups,
    int RankedCoaches,
    int PlacedCoaches,

    // the calendar's health
    int FixturesTotal,
    int FixturesPlayed,
    // Fixtures whose kickoff has passed and that are still unresolved. In a healthy ladder this is 0 most
    // of the time and briefly non-zero between a kickoff and the next minutely tick.
    int OverdueFixtures,
    // How far behind the oldest overdue fixture is. THE number to alert on.
    int WorstFixtureLagSeconds,
    DateTime? LastFixtureResolvedUtc,

    // market
    int OpenAuctionLots,
    int PendingOffers,

    // integrity
    int OpenIntegrityFlags,

    // background jobs (null when the scheduler is not running, e.g. under the test environment)
    int? FailedJobs,
    int? EnqueuedJobs,
    int? JobServers,

    // balance
    int BalanceRevision,
    DateTime? BalanceLoadedUtc);

// --- worlds ----------------------------------------------------------------------------------------

/// <summary>One ranked world (a whole pyramid) with the counts an operator cares about.</summary>
public sealed record AdminWorldDto(
    Guid Id,
    string Name,
    long Seed,
    string Status,
    int SeasonNumber,
    int Groups,
    int Seats,
    int OccupiedSeats,
    int HumanCoaches,
    DateTime CreatedUtc,
    IReadOnlyList<AdminGroupDto> GroupList);

/// <summary>One girone inside a world: where its season is and whether its calendar is on time.</summary>
public sealed record AdminGroupDto(
    Guid Id,
    string Name,
    string Kind,
    int Tier,
    int Capacity,
    int Occupied,
    string Status,
    int SeasonNumber,
    DateTime? SeasonStartedUtc,
    DateTime? SeasonEndedUtc,
    int RoundsPlayed,
    int RoundsTotal,
    DateTime? NextKickoffUtc,
    int OverdueFixtures);

/// <summary>Close a world to new enrolments (or reopen it). Seasons under way are NOT touched — this only
/// decides whether the enrolment path may hand out one of its seats.</summary>
public sealed record SetWorldOpenRequest(bool Open, string? Reason);

// --- users -----------------------------------------------------------------------------------------

/// <summary>An account as live ops sees it: who they are, whether they can sign in, and where they sit on
/// the ladder. No password material, no tokens, no raw fingerprints — the integrity hashes stay hidden
/// even from this surface because knowing them adds nothing to an operator's decision.</summary>
public sealed record AdminUserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    DateTime CreatedUtc,
    bool IsAdmin,
    bool IsLocked,
    DateTime? LockoutEndUtc,
    bool Enrolled,
    string? RankedStatus,
    int? Rating,
    int? PeakRating,
    int? SeasonsPlayed,
    string? WorldName,
    string? GroupName,
    int? Tier,
    int OpenFlags);

/// <summary>Lock (ban) or unlock an account. A lock is Identity's own lockout with a far-future end, so a
/// locked account fails at sign-in with no special-casing anywhere else in the server.</summary>
public sealed record SetUserLockRequest(bool Locked, string? Reason);

/// <summary>Grant or revoke the admin role on an account.</summary>
public sealed record SetUserAdminRequest(bool IsAdmin);

// --- balance ---------------------------------------------------------------------------------------

/// <summary>The active balance plus the full payload, so the dashboard can show it and edit from it.</summary>
public sealed record AdminBalanceDto(
    int Revision,
    int ConfigVersion,
    string Note,
    string CreatedByEmail,
    DateTime? CreatedUtc,
    DateTime? LoadedUtc,
    // True when nothing has ever been pushed and the server is running the balance embedded in the
    // build — the same defaults the client ships with.
    bool IsBaseline,
    string Json);

/// <summary>One line of the balance history (without the payload, which is large).</summary>
public sealed record AdminBalanceRevisionDto(
    int Revision,
    int ConfigVersion,
    string Note,
    string CreatedByEmail,
    DateTime CreatedUtc,
    int? RolledBackFrom,
    bool IsActive);

/// <summary>Push a new balance revision. <see cref="Json"/> must deserialise into a whole
/// <c>BalanceConfig</c> — a partial document is rejected rather than silently defaulted, because a
/// missing section would quietly reset every knob in it.</summary>
public sealed record PushBalanceRequest(string Json, string? Note);

// --- audit -----------------------------------------------------------------------------------------

public sealed record AdminAuditDto(
    Guid Id,
    Guid ActorUserId,
    string ActorEmail,
    AdminAction Action,
    string Target,
    string Details,
    DateTime CreatedUtc);

// --- result ----------------------------------------------------------------------------------------

public enum AdminError
{
    None = 0,
    NotFound = 1,
    ValidationFailed = 2,
    Conflict = 3,
}

/// <summary>Mirrors <c>RankedResult&lt;T&gt;</c> / <c>LeagueResult&lt;T&gt;</c> so the Api maps errors to
/// status codes the same way everywhere.</summary>
public sealed record AdminResult<T>(bool Success, T? Value, AdminError Error, string? Message)
{
    public static AdminResult<T> Ok(T value) => new(true, value, AdminError.None, null);
    public static AdminResult<T> Fail(AdminError error, string? message = null) =>
        new(false, default, error, message);
}
