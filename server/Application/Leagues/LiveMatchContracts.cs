using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Application.Leagues;

/// <summary>
/// Request/response DTOs for a live-controlled private-league match (Phase 8.6). The chosen model is
/// "deterministic re-sim + broadcast": the server holds the fixture's seed and an authoritative,
/// accumulated <see cref="MatchPlan"/>; every pause-point (a substitution or an instruction change) is a
/// <see cref="MatchInputChange"/> appended to that plan, and the server re-runs the whole 90' with the
/// SAME seed. Because the engine is a pure function of (plan, seed), the prefix before the change stays
/// byte-identical and only the remainder re-rolls (the 3.4 mechanic) — so both connected clients render
/// the same match in sync off a shared kickoff time, and a change is reflected to the opponent as soon as
/// the new report is pushed/polled. All state is authoritative in Postgres/EF (a <c>LiveMatch</c> row);
/// the SignalR <c>MatchHub</c> is only a thin live-push layer, so a client can also just poll the GET.
/// </summary>

/// <summary>Which side a change acts on. The server sets it from the caller's own club, never from the
/// request body, so a member can only control their own team.</summary>
public enum LiveSide
{
    Home = 0,
    Away = 1,
}

/// <summary>Where a live match is in its short lifecycle.</summary>
public enum LiveMatchStatus
{
    /// <summary>The session exists but both human members are not yet connected — kickoff has not fired.</summary>
    Pending = 0,
    /// <summary>Both members connected: the clock is running and pause-point inputs are accepted.</summary>
    Live = 1,
    /// <summary>Full-time confirmed: the stored report becomes the fixture's official result when the
    /// round resolves (all-ready / advance).</summary>
    Finished = 2,
}

/// <summary>One accumulated pause-point input on the authoritative plan: from <see cref="FromMinute"/>
/// the given side plays the new lineup and/or tactic. The shared Sim.Core plan types are stored verbatim
/// so the server materialises them into the engine exactly as the client built them (a null lineup/tactic
/// leaves that aspect unchanged for the side).</summary>
public sealed record LiveChange(int FromMinute, LiveSide Side, LineupPlan? Lineup, TacticPlan? Tactic);

/// <summary>The caller submits a pause-point change for THEIR side (the server infers the side from the
/// caller's club). At least one of <see cref="Lineup"/> (a substitution / reshaped XI) or
/// <see cref="Tactic"/> (an instruction change) must be present. <see cref="FromMinute"/> is the client's
/// current rendered minute (1..90) and must not move backwards past an already-applied change.</summary>
public sealed record SubmitLiveChangeRequest(int FromMinute, LineupPlan? Lineup, TacticPlan? Tactic);

/// <summary>A light description of an applied change for the opponent's UI timeline (who changed when) —
/// the detailed lineup/tactic is already baked into the streamed report, so it need not be echoed.</summary>
public sealed record LiveChangeDto(int FromMinute, LiveSide Side);

/// <summary>The full live-match state pushed to both clients (and returned by the GET). Carries the whole
/// serialized <see cref="MatchReport"/> in <see cref="ReportJson"/> so the client re-renders the changed
/// remainder directly — one hop, "state streamed". (A friend-league report is modest; if it ever bloats,
/// switch the push to a light signal + a GET of the report, as 8.3 noted for the replay blob.)</summary>
public sealed record LiveMatchStateDto(
    Guid FixtureId,
    int Round,
    int HomeClubExternalId,
    string HomeClubName,
    int AwayClubExternalId,
    string AwayClubName,
    LiveMatchStatus Status,
    LiveSide? YourSide,
    bool HomePresent,
    bool AwayPresent,
    DateTime? KickoffUtc,
    int HomeGoals,
    int AwayGoals,
    IReadOnlyList<LiveChangeDto> Changes,
    string? ReportJson);
