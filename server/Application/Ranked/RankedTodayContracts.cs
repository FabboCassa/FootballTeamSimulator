namespace Fts.Application.Ranked;

/// <summary>
/// The ranked DAILY DIGEST (Phase 9.4) — one response that answers "what do I need to do today?" so the
/// ladder's daily loop fits in the ≤10 minutes ARCHITECTURE §6.2 designs it around.
///
/// Everything here is a PROJECTION over state the ladder already owns (the coach's seat, the group's
/// calendar, the standings, the market window, the pending offers/lots and the stored match inputs) plus a
/// short, prioritised <see cref="RankedTodoDto"/> list. No user-facing text crosses the wire: a todo item is
/// a <see cref="RankedTodoKind"/> + a count, and the client renders the localised sentence — the same
/// convention as every other ranked DTO (only club/player names are strings).
///
/// The digest works WITH the 9.4 smart defaults: a coach's lineup is persisted (and repaired) server-side, so
/// "formazione pronta" is a truth and not a hope, and <see cref="IRankedTodayService.ConfirmMatchdayAsync"/>
/// closes an uneventful day in a single tap.
/// </summary>

/// <summary>What a coach still has to do. Stable numeric values (on the wire; the client maps them to
/// localised strings). Ordered by <see cref="RankedTodoDto.Priority"/>, not by this enum.</summary>
public enum RankedTodoKind
{
    /// <summary>Not on the ladder (never enrolled, or released after opting out) — join it.</summary>
    Enrol = 0,
    /// <summary>The next matchday's inputs have not been confirmed yet (one tap closes it).</summary>
    ConfirmMatchday = 1,
    /// <summary>Incoming transfer offers are waiting for an answer (count = how many).</summary>
    RespondOffer = 2,
    /// <summary>A market window is open right now (count = open free-agent lots).</summary>
    MarketWindow = 3,
    /// <summary>The season is over and the group is in its between-seasons break — read the summary.</summary>
    SeasonSummary = 4,
}

/// <summary>One prioritised thing to do. <see cref="Count"/> is 1 for a single action, or how many items
/// need attention (offers to answer, lots on the market). Lower <see cref="Priority"/> = more urgent.</summary>
public sealed record RankedTodoDto(RankedTodoKind Kind, int Count, int Priority);

/// <summary>The next match on the coach's calendar.</summary>
public sealed record RankedTodayNextMatchDto(
    Guid FixtureId,
    int Round,
    DateTime KickoffUtc,
    int SecondsToKickoff,
    bool YouAreHome,
    int OpponentClubExternalId,
    string OpponentClubName);

/// <summary>The coach's most recently played match (from their own point of view).</summary>
public sealed record RankedTodayLastResultDto(
    Guid FixtureId,
    int Round,
    bool YouAreHome,
    int OpponentClubExternalId,
    string OpponentClubName,
    int GoalsFor,
    int GoalsAgainst);

/// <summary>
/// The whole daily loop on one screen. <see cref="Enrolled"/> false (and <see cref="InSeason"/> false) is a
/// valid, fully-populated answer — the digest never 404s for a signed-in account that simply has not joined.
/// </summary>
public sealed record RankedTodayDto(
    bool Enrolled,
    RankedCoachStatus Status,
    int Rating,
    // Whether the ladder will auto re-enrol you next season — on the digest so the daily screen can offer
    // the toggle without a second round-trip.
    bool AutoEnrol,
    Guid? GroupId,
    string? GroupName,
    RankedGroupKind? Kind,
    int? Tier,
    int? ClubExternalId,
    string? ClubName,

    // --- season / calendar ---
    bool InSeason,
    bool SeasonComplete,
    int TotalRounds,
    int RoundsPlayed,
    int? NextRound,
    int? YourPosition,
    int? YourPoints,
    RankedTodayNextMatchDto? NextMatch,
    RankedTodayLastResultDto? LastResult,

    // --- your inputs (the smart defaults) ---
    // LineupReady      = a usable lineup is stored for your club (the server seeds AND repairs one, so it is
    //                    true for the whole season once it starts).
    // LineupConfirmed  = you signed off on NextRound, by submitting a lineup or with the one-tap confirm.
    // TrainingSet      = a training plan is stored (seeded balanced when the season starts).
    // TrainingTeamFocus= that plan's team focus as the numeric Sim.Core TeamTrainingFocus (0 = Balanced …).
    bool LineupReady,
    bool LineupConfirmed,
    bool TrainingSet,
    int? TrainingTeamFocus,

    // --- market ---
    RankedMarketWindowDto? MarketWindow,
    long Budget,
    int IncomingOffers,
    int OutgoingOffers,
    int OpenLots,
    int LotsYouLead,

    // --- the to-do list ---
    int ActionCount,
    IReadOnlyList<RankedTodoDto> Todo)
{
    /// <summary>The digest for a signed-in account that has never joined the ladder: one action, join it.</summary>
    public static RankedTodayDto NotEnrolled() => new(
        Enrolled: false, Status: RankedCoachStatus.Placement, Rating: 0, AutoEnrol: false,
        GroupId: null, GroupName: null, Kind: null, Tier: null, ClubExternalId: null, ClubName: null,
        InSeason: false, SeasonComplete: false, TotalRounds: 0, RoundsPlayed: 0, NextRound: null,
        YourPosition: null, YourPoints: null, NextMatch: null, LastResult: null,
        LineupReady: false, LineupConfirmed: false, TrainingSet: false, TrainingTeamFocus: null,
        MarketWindow: null, Budget: 0, IncomingOffers: 0, OutgoingOffers: 0, OpenLots: 0, LotsYouLead: 0,
        ActionCount: 1,
        Todo: new[] { new RankedTodoDto(RankedTodoKind.Enrol, 1, 0) });
}

/// <summary>
/// The daily-digest use cases (Phase 9.4). Read-only apart from <see cref="ConfirmMatchdayAsync"/>, which is
/// the one-tap "my day is handled" action: it makes sure the stored inputs are valid and marks the upcoming
/// matchday as confirmed. Idempotent — confirming twice changes nothing.
/// </summary>
public interface IRankedTodayService
{
    /// <summary>The caller's daily digest. Always succeeds for a signed-in account (a coach who has never
    /// enrolled gets <see cref="RankedTodayDto.NotEnrolled"/>).</summary>
    Task<RankedResult<RankedTodayDto>> GetTodayAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Confirm the upcoming matchday in one tap: ensure a valid lineup + a training plan are stored
    /// (seeding/repairing the defaults if needed) and stamp the next round as confirmed. Fails only when the
    /// caller is not enrolled, or holds no club in a season that is under way.</summary>
    Task<RankedResult<RankedTodayDto>> ConfirmMatchdayAsync(Guid userId, CancellationToken ct = default);
}
