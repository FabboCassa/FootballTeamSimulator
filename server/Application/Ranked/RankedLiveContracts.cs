using Fts.Application.Leagues;
using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Application.Ranked;

/// <summary>
/// Request/response DTOs + the service interface for LIVE RANKED MATCHES (task 12.3) — turning the ladder's
/// one match a day from something you read about afterwards into an appointment you keep.
///
/// The mechanism is 8.6's, unchanged and shared rather than re-invented: "deterministic re-sim + broadcast".
/// The server holds the fixture's seed and an accumulated <see cref="LiveChange"/> plan; every pause-point
/// (a substitution or an instruction change) is appended and the whole 90' is re-run from the SAME seed, so
/// the minutes before the change stay byte-identical and only the remainder re-rolls. The live vocabulary
/// itself — <see cref="LiveSide"/>, <see cref="LiveMatchStatus"/>, <see cref="LiveChange"/> — is reused from
/// <c>Fts.Application.Leagues</c> on purpose: it describes a live football match, not a private league, and
/// two copies of it would be two things to keep in step.
///
/// What the ladder adds:
/// <list type="bullet">
/// <item><b>The clock is the calendar's, not the players'.</b> A ranked match kicks off at its scheduled
/// instant — 21:00 of the world's own time zone — whether or not anybody is there. Presence is decoration;
/// the appointment is the mechanic.</item>
/// <item><b>You can attend a match against an AI seat.</b> A ladder group is mostly AI until the pyramid
/// fills, and a live mode that needed two humans would almost never fire.</item>
/// <item><b>An absent side is not a special case.</b> It plays its stored lineup / tactic / pre-match plan,
/// which is precisely what the headless matchday does — so attending changes the result only through the
/// changes you actually make.</item>
/// </list>
/// </summary>

/// <summary>The caller submits a pause-point change for THEIR side (the server infers the side from the
/// caller's seat). At least one of <see cref="Lineup"/> (a substitution / reshaped XI) or
/// <see cref="Tactic"/> (an instruction change) must be present. <see cref="FromMinute"/> is the client's
/// current rendered minute (1..90); it may not move behind an already-applied change, nor run ahead of the
/// minute the wall clock says has actually been played.</summary>
public sealed record SubmitRankedLiveChangeRequest(int FromMinute, LineupPlan? Lineup, TacticPlan? Tactic);

/// <summary>A light description of an applied change for the opponent's UI timeline (who changed when) —
/// the detail is already baked into the streamed report.</summary>
public sealed record RankedLiveChangeDto(int FromMinute, LiveSide Side);

/// <summary>
/// The full live-match state pushed to the connected clients (and returned by every call). Carries the whole
/// serialized <c>MatchReport</c> in <see cref="ReportJson"/> so the client re-renders the changed remainder
/// in one hop.
///
/// The three clock fields are the point of the task: <see cref="KickoffUtc"/> is the appointment,
/// <see cref="ServerUtc"/> lets a client correct its own drift instead of guessing, and
/// <see cref="SecondsPerMatchMinute"/> is the playback rate the SERVER validates changes against — so the
/// client renders on the same number the server judges by, rather than on a constant of its own.
/// </summary>
public sealed record RankedLiveStateDto(
    Guid FixtureId,
    Guid GroupId,
    int Round,
    int HomeClubExternalId,
    string HomeClubName,
    int AwayClubExternalId,
    string AwayClubName,
    LiveMatchStatus Status,
    LiveSide? YourSide,
    bool HomeIsAi,
    bool AwayIsAi,
    bool HomePresent,
    bool AwayPresent,
    DateTime KickoffUtc,
    DateTime OpensUtc,
    DateTime ClosesUtc,
    DateTime ServerUtc,
    int SecondsPerMatchMinute,
    int HomeGoals,
    int AwayGoals,
    IReadOnlyList<RankedLiveChangeDto> Changes,
    string? ReportJson);

/// <summary>
/// Live ranked match use cases (task 12.3). A coach opens his own current-matchday fixture from
/// <c>LiveOpensBeforeSeconds</c> before kickoff, watches it play out on the shared clock, and sends
/// pause-point changes for his own side; the calendar holds the matchday's resolution while a session is
/// still being played and then consumes its report verbatim, exactly as the private-league round does with
/// an 8.6 session. Nothing here can change a result that the headless path would not also have produced from
/// the same inputs and the same seed — determinism is the acceptance test.
/// </summary>
public interface IRankedLiveMatchService
{
    /// <summary>Open (or rejoin) the live session for one of the caller's own fixtures, marking him present.
    /// Refuses outside the window around kickoff, for a fixture that is not the caller's, and for one whose
    /// matchday has already been resolved.</summary>
    Task<RankedResult<RankedLiveStateDto>> OpenAsync(Guid userId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>The current live state. Any coach in the group may watch; only the two sides may change.</summary>
    Task<RankedResult<RankedLiveStateDto>> GetAsync(Guid userId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>Submit a pause-point change for the caller's own side: append it to the authoritative plan,
    /// re-simulate the 90' from the fixture seed, store the new report and broadcast the state.</summary>
    Task<RankedResult<RankedLiveStateDto>> SubmitChangeAsync(
        Guid userId, Guid fixtureId, SubmitRankedLiveChangeRequest request, CancellationToken ct = default);

    /// <summary>Mark the caller absent (he closed the screen). The match keeps running on the accumulated
    /// plan — the ladder's clock does not care who is watching.</summary>
    Task<RankedResult<RankedLiveStateDto>> LeaveAsync(Guid userId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>Confirm full-time: the stored report is what the matchday will consume, and the calendar
    /// stops waiting for this fixture.</summary>
    Task<RankedResult<RankedLiveStateDto>> FinishAsync(Guid userId, Guid fixtureId, CancellationToken ct = default);
}

/// <summary>
/// Pushes ranked live-match state to the connected clients (task 12.3). Implemented in the Api layer over
/// the same SignalR <c>MatchHub</c> the private leagues use — one hub, one per-fixture group, two payload
/// shapes — with a no-op default registered in Infrastructure so the authoritative service stays testable
/// without SignalR. The REST endpoints remain the source of truth: a client can simply poll the GET.
/// Never throws (a broadcast failure must not fail the change that already committed).
/// </summary>
public interface IRankedLiveBroadcaster
{
    Task RankedMatchChangedAsync(Guid fixtureId, RankedLiveStateDto state, CancellationToken ct = default);
}
