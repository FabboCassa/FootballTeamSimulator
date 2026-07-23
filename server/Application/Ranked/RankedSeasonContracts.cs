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

/// <summary>One scheduled/played match in a ranked group's season, with its real-time kickoff.</summary>
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
    bool IsYours);

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
    string StateHashHex);

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

/// <summary>What one calendar tick did — for the dashboard/logs and to assert on in tests.</summary>
public sealed record RankedTickSummary(
    int SeasonsStarted,
    int MatchdaysResolved,
    int FixturesResolved,
    int PlacementsResolved,
    int DivisionsCompleted,
    int MarketWindowsOpened);

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
}
