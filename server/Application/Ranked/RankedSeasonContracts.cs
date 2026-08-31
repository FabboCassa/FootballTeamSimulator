using Sim.Core.Development;
using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Application.Ranked;

/// <summary>
/// Request/response DTOs + the service interface for the ranked real-time season (Phase 9.2). The ladder
/// runs each group's double round-robin on a wall-clock calendar (one matchday per interval), resolving a
/// matchday once its kickoff has passed from each seat's last submitted lineup (best-XI fallback). There is
/// no "all ready" trigger — the clock drives it, so a coach who forgets to submit simply plays their last
/// lineup, then BestEleven. Placement seasons run the same way and auto-sort their coaches into divisions
/// when they finish.
///
/// The submitted inputs are the shared Sim.Core plan types, exactly like the private-league season (8.3),
/// so the client round-trips what it built and the server stores it verbatim.
/// </summary>

/// <summary>Submit (or replace) the caller's match inputs for their ranked seat's club: the lineup is
/// required, the tactic and pre-match plan are optional.</summary>
public sealed record SubmitRankedLineupRequest(LineupPlan Lineup, TacticPlan? Tactic, PrematchPlan? Plan);

/// <summary>Submit (or replace) the training plan the caller's ranked club develops on each matchday-week
/// (Phase 9.4). A club without a submission trains the AI default — the server seeds a balanced plan when the
/// season starts, so this only ever refines an existing sensible default.</summary>
public sealed record SubmitRankedTrainingRequest(TrainingPlan Training);

/// <summary>
/// One scheduled/played match in a ranked group's season, with its real-time kickoff.
///
/// <see cref="KickoffUtc"/> is a UTC instant and the client renders it in the DEVICE's local time, which is
/// the whole point of task 12.3's time-zone work: an Italian world's 21:00 shows as 21:00 in Milan and 20:00
/// in London, for the same match. The two live flags are computed SERVER-side rather than derived from the
/// client's own clock — a device whose time is a few minutes off would otherwise offer (or hide) the
/// "guardala dal vivo" button at the wrong moment, which is the one moment that matters.
/// </summary>
public sealed record RankedFixtureDto(
    Guid Id,
    int Round,
    int Day,
    DateTime KickoffUtc,
    int HomeClubExternalId,
    string HomeClubName,
    int AwayClubExternalId,
    string AwayClubName,
    bool Played,
    int HomeGoals,
    int AwayGoals,
    bool IsYours,
    // True when this is the caller's own fixture and the live door is open right now (task 12.3):
    // the row becomes "guarda dal vivo" instead of "replay".
    bool LiveOpen = false,
    // Where an existing live session for this fixture is, or null when nobody has opened one.
    Fts.Application.Leagues.LiveMatchStatus? LiveStatus = null);

/// <summary>A club's standing in the group, computed from the played fixtures.</summary>
public sealed record RankedStandingDto(
    int ClubExternalId,
    string ClubName,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    bool IsYou);

/// <summary>A market window on the season calendar (season start + midpoint). 9.2a reports it and announces
/// its opening; the free-agent auctions + direct coach offers that happen inside it are 9.2b.</summary>
public sealed record RankedMarketWindowDto(int Index, DateTime OpensUtc, DateTime ClosesUtc, bool IsOpen);

/// <summary>Where the caller's ranked season is + their own state (drives the season screen).</summary>
public sealed record RankedSeasonStateDto(
    Guid GroupId,
    string GroupName,
    RankedGroupKind Kind,
    int Tier,
    bool Started,
    int TotalRounds,
    int RoundsPlayed,
    int? NextRound,
    DateTime? NextKickoffUtc,
    bool SeasonComplete,
    int? YourClubExternalId,
    bool YouSubmittedLineup,
    RankedMarketWindowDto? CurrentWindow,
    string StateHashHex,
    // The server's own clock at the moment this was built (task 12.3). A countdown to kickoff has
    // to be right to the second, and a device's clock is not: the client measures its offset from this once
    // and counts down against a corrected now.
    DateTime ServerUtc = default,
    // The IANA zone the world's kick-offs are anchored to, e.g. Europe/Rome (task 12.3), or
    // empty for a season on the pre-12.3 relative calendar. Informational — the client always RENDERS in
    // the device's own zone; this is what lets it say "21:00 ora del mondo" when the two differ.
    string WorldTimeZoneId = "",
    // How long before kickoff the live door opens (task 12.3).
    int LiveOpensBeforeSeconds = 0,
    // How long after kickoff the calendar waits for a live match before resolving the matchday
    // headless (task 12.3).
    int LiveGraceSeconds = 0,
    // Real seconds per match minute during a live match (task 12.3) — the client renders on this
    // number because the server judges pause-point changes by it.
    int LiveSecondsPerMatchMinute = 0,
    // Whether attending a ranked match is switched on in this environment (task 12.3).
    bool LiveEnabled = false);

/// <summary>The caller's ranked season: state + full schedule + standings. <see cref="InSeason"/> is false
/// when the caller is enrolled but their group has not started a season yet.</summary>
public sealed record RankedSeasonDto(
    bool InSeason,
    RankedSeasonStateDto? State,
    IReadOnlyList<RankedFixtureDto> Fixtures,
    IReadOnlyList<RankedStandingDto> Standings)
{
    public static RankedSeasonDto NotInSeason() =>
        new(false, null, Array.Empty<RankedFixtureDto>(), Array.Empty<RankedStandingDto>());
}

/// <summary>What one calendar tick did — for the dashboard/logs and to assert on in tests.
/// <c>SeasonsReset</c> (Phase 9.3) counts the groups whose between-seasons break ran out and were reset:
/// promotions/relegations applied, squads re-equalised, the group reopened for the next season.</summary>
public sealed record RankedTickSummary(
    int SeasonsStarted,
    int MatchdaysResolved,
    int FixturesResolved,
    int PlacementsResolved,
    int DivisionsCompleted,
    int MarketWindowsOpened,
    int SeasonsReset = 0);

/// <summary>
/// The ranked real-time season use cases (Phase 9.2). <see cref="TickAsync"/> is the whole engine — the
/// recurring job (or a test) calls it and it starts due seasons, resolves due matchdays, opens market
/// windows and closes finished seasons. The per-coach calls read/submit for the caller's current seat.
/// </summary>
public interface IRankedSeasonService
{
    /// <summary>Advance the ladder's clock once: start any group whose season is due, resolve every group's
    /// lowest due matchday, announce any market window that just opened, and close/sort finished seasons.
    /// Idempotent — calling it when nothing is due is a no-op. Safe to run on a frequent timer.</summary>
    Task<RankedTickSummary> TickAsync(CancellationToken ct = default);

    /// <summary>Submit (or replace) the caller's lineup/tactic/plan for their current ranked club. Requires
    /// the caller to be enrolled, hold a seat with a club, and be in a group whose season is under way.</summary>
    Task<RankedResult<RankedSeasonDto>> SubmitLineupAsync(
        Guid userId, SubmitRankedLineupRequest request, CancellationToken ct = default);

    /// <summary>The caller's current ranked season (schedule + standings + state), or a "not in season"
    /// result when they are enrolled but their group has not kicked off yet.</summary>
    Task<RankedResult<RankedSeasonDto>> GetMySeasonAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The stored full <c>MatchReport</c> JSON for a played fixture in the caller's group.</summary>
    Task<RankedResult<string>> GetReplayAsync(Guid userId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>The caller's currently submitted lineup for their ranked club as the stored
    /// <c>LineupPlan</c> JSON, or an empty string when they have not submitted one — so the client editor
    /// re-opens on the saved XI instead of the best-XI default.</summary>
    Task<RankedResult<string>> GetMyLineupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Submit (or replace) the training plan the caller's ranked club develops on (Phase 9.4). Used
    /// by the weekly development tick that runs with every resolved matchday.</summary>
    Task<RankedResult<RankedSeasonDto>> SubmitTrainingAsync(
        Guid userId, SubmitRankedTrainingRequest request, CancellationToken ct = default);

    /// <summary>The caller's stored training plan as the serialized <c>TrainingPlan</c> JSON, or an empty
    /// string when nothing is stored — so the client's training screen opens on the saved plan.</summary>
    Task<RankedResult<string>> GetMyTrainingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>DEV/STAGING ONLY (task 12.3 dev-sim tooling): pull the caller-visible next matchday's
    /// kick-off forward so a solo tester can attend a live match NOW instead of waiting for 21:00. Every
    /// running season's remaining kickoffs — and its season start, so the market windows stay aligned — are
    /// shifted back by the same amount, which is what makes it a genuine time-travel rather than a special
    /// code path. Returns how many groups were shifted.</summary>
    Task<int> BringKickoffForwardAsync(int seconds, CancellationToken ct = default);

    /// <summary>DEV/STAGING ONLY (Phase 9.3 dev-sim tooling): time-travel the ladder forward. Every running
    /// season is shifted back by one matchday interval and then ticked, <paramref name="matchdays"/> times —
    /// so a solo tester can watch a whole season (and the season after the reset) play out in seconds instead
    /// of the real ~2 weeks. Shifting BOTH the season start and every kickoff by the same amount keeps the
    /// market windows aligned, so it is a genuine fast-forward and not a special code path.</summary>
    Task<RankedTickSummary> FastForwardAsync(int matchdays, CancellationToken ct = default);
}
