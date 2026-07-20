namespace Fts.Application.Leagues;

/// <summary>Request/response DTOs for the private-league lifecycle (Phase 8.1). Plain records so the
/// Api layer binds them from JSON and the client mirrors them. <see cref="ILeagueService"/> lives here
/// (Application); the implementation is in Infrastructure (needs EF + Sim.Core world generation).</summary>

/// <summary>How a private league advances. 8.1 only stores the choice — the scheduled/real-time
/// resolution logic lands in 8.3/8.4. The default 8.1 mode is <see cref="AllReady"/>.</summary>
public enum LeagueMode
{
    /// <summary>"Advance when everyone is ready" — the friend-league default (chosen for 8.1).</summary>
    AllReady = 0,
    /// <summary>Fixed real-time schedule like the public ranked mode (logic in Phase 9).</summary>
    RealTime = 1,
}

/// <summary>Where a private league is in its lifecycle. 8.1 leaves a freshly created league
/// <see cref="Forming"/> (members join, clubs assigned at the 8.2 draft, season starts later).</summary>
public enum LeagueStatus
{
    /// <summary>Accepting members via the invite code; the season has not started.</summary>
    Forming = 0,
    /// <summary>Season under way (set once the draft completes and, later, fixtures land, 8.3).</summary>
    Active = 1,
    /// <summary>Season finished (8.7).</summary>
    Completed = 2,
    /// <summary>The snake draft is running: squads have been equalised, members are picking their
    /// club (identity) in turn (8.2). Appended (=3, not inserted) so stored 0/1/2 values are unchanged.</summary>
    Drafting = 3,
}

/// <summary>Create a private league: a name, the number of clubs (2–20, mirroring real league sizes),
/// and the advance mode. The server generates a fresh world (unique players) and makes the caller the
/// first member.</summary>
public sealed record CreateLeagueRequest(string Name, int Size, LeagueMode Mode);

/// <summary>Join an existing private league by its invite code.</summary>
public sealed record JoinLeagueRequest(string InviteCode);

/// <summary>Pick a club (by its Sim.Core <c>ExternalId</c>) during the snake draft. Only the member
/// whose turn it is may pick, and only a club no one else has taken (8.2).</summary>
public sealed record PickClubRequest(int ClubExternalId);

/// <summary>A member of a private league — an account, optionally already assigned a club (the club
/// assignment is the 8.2 draft; null until then).</summary>
public sealed record LeagueMemberDto(
    Guid UserId,
    string DisplayName,
    int? ClubExternalId,
    string? ClubName,
    bool IsCreator,
    bool IsReady);

/// <summary>A player in the generated world, trimmed for the lobby squad view. <see cref="ExternalId"/>
/// is the Sim.Core id (unique per world), so the test can assert no duplicates.</summary>
public sealed record LeaguePlayerDto(int ExternalId, string Name, int Age, int Role, int Overall);

/// <summary>A club in the generated world, with its squad (for the "everyone sees the same squads"
/// lobby view). <see cref="TransferBudget"/> is 0 until the draft runs, then an equal figure for every
/// club (8.2 "pari budget a tutti").</summary>
public sealed record LeagueClubDto(
    int ExternalId,
    string Name,
    string ShortName,
    int Strength,
    long TransferBudget,
    IReadOnlyList<LeaguePlayerDto> Players);

/// <summary>Snapshot of the snake draft (8.2). While <see cref="InProgress"/>, the member whose turn it
/// is (<see cref="CurrentPickUserId"/>) picks one of the unclaimed clubs; the client derives which clubs
/// are still available from the members' assigned clubs. Once every member has a club the league flips to
/// <see cref="LeagueStatus.Active"/> and <see cref="InProgress"/> is false.</summary>
public sealed record DraftStateDto(
    bool InProgress,
    Guid? CurrentPickUserId,
    int PicksMade,
    int TotalPicks);

/// <summary>Lightweight league row for the "my leagues" list.</summary>
public sealed record LeagueSummaryDto(
    Guid Id,
    string Name,
    string InviteCode,
    int Size,
    int MemberCount,
    LeagueStatus Status,
    LeagueMode Mode,
    bool IsCreator);

/// <summary>Full league view: the summary + members + the generated clubs/squads + the draft state.</summary>
public sealed record LeagueDetailDto(
    LeagueSummaryDto League,
    IReadOnlyList<LeagueMemberDto> Members,
    IReadOnlyList<LeagueClubDto> Clubs,
    DraftStateDto Draft);

/// <summary>Why a league use case failed — the Api maps these to HTTP status codes.</summary>
public enum LeagueError
{
    None = 0,
    ValidationFailed,
    NotFound,
    AlreadyMember,
    LeagueFull,
    NotJoinable,
    Forbidden,
    /// <summary>The action is not valid in the league's current status (e.g. starting a draft that has
    /// already started, or picking before the draft begins) — 8.2.</summary>
    WrongPhase,
    /// <summary>It is not this member's turn to pick in the snake draft — 8.2.</summary>
    NotYourTurn,
    /// <summary>The requested club does not exist in the world or has already been taken — 8.2.</summary>
    ClubUnavailable,
    /// <summary>The league has too few members to start the season (need at least two) — 8.2.</summary>
    TooFewMembers,
    /// <summary>The caller has no club assigned in this league, so cannot submit inputs — 8.3.</summary>
    NotAssignedClub,
    /// <summary>There is no round left to resolve (the season is complete) — 8.3.</summary>
    NothingToResolve,
    /// <summary>No fixture with that id exists in this league — 8.3.</summary>
    FixtureNotFound,
    /// <summary>The fixture has not been played yet, so no replay is available — 8.3.</summary>
    ReplayNotReady,

    // --- Online auctions (Phase 8.5) --------------------------------------------------------
    /// <summary>No auction with that id exists in this league — 8.5.</summary>
    AuctionNotFound,
    /// <summary>The auction is no longer open for bids (settled/unsold, or the window closed) — 8.5.</summary>
    AuctionClosed,
    /// <summary>The bid is below the required minimum (start price, or current high bid + increment) — 8.5.</summary>
    BidTooLow,
    /// <summary>The bid exceeds the club's available budget (transfer budget minus its leading bids) — 8.5.</summary>
    InsufficientBudget,
    /// <summary>An auction window is already open in this league — 8.5.</summary>
    WindowAlreadyOpen,
    /// <summary>There are no open auctions to close/settle — 8.5.</summary>
    NoAuctionsOpen,

    // --- Live match control (Phase 8.6) -----------------------------------------------------
    /// <summary>No live session exists for that fixture (it was never opened) — 8.6.</summary>
    LiveMatchNotFound,
    /// <summary>The fixture cannot be played live: it is not the current round, already played, or not a
    /// human-vs-human fixture (one side is AI / unclaimed) — 8.6.</summary>
    LiveMatchNotJoinable,
    /// <summary>The caller tried to change a side they do not control (only your own club) — 8.6.</summary>
    NotYourSide,
    /// <summary>The live match is not running (still Pending, or already Finished) so changes are rejected — 8.6.</summary>
    LiveMatchNotLive,
    /// <summary>The live match is already finished — 8.6.</summary>
    LiveMatchAlreadyFinished,
    /// <summary>The submitted change is invalid (minute out of range, moves backwards past an applied
    /// change, or carries no lineup and no tactic) — 8.6.</summary>
    InvalidLiveChange,
}

/// <summary>Result wrapper so the service never throws for expected failures. Exactly one of
/// <see cref="Value"/> (on success) or <see cref="Error"/> (on failure) is meaningful.</summary>
public sealed record LeagueResult<T>(bool Success, T? Value, LeagueError Error, string? Message)
{
    public static LeagueResult<T> Ok(T value) => new(true, value, LeagueError.None, null);
    public static LeagueResult<T> Fail(LeagueError error, string? message = null) =>
        new(false, default, error, message);
}
