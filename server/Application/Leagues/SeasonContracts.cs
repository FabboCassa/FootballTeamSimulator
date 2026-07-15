using Sim.Core.Match;
using Sim.Core.Tactics;

namespace Fts.Application.Leagues;

/// <summary>
/// Request/response DTOs for the private-league season (Phase 8.3): submitting match inputs, marking
/// ready, advancing a round, and reading the schedule/standings + a fixture replay. The submitted
/// inputs are the shared Sim.Core plan types (<see cref="LineupPlan"/>/<see cref="TacticPlan"/>/
/// <see cref="PrematchPlan"/>) so the server round-trips exactly what the client built; the server
/// stores them verbatim and reuses the last submission each matchday (falling back to the best XI when
/// none is present or it no longer materialises).
/// </summary>

/// <summary>Submit (or replace) the caller's match inputs for their club: the lineup is required, the
/// tactic and pre-match plan are optional (null = neutral tactic / no conditional rules).</summary>
public sealed record SubmitLineupRequest(LineupPlan Lineup, TacticPlan? Tactic, PrematchPlan? Plan);

/// <summary>Mark (or clear) the caller as ready to advance. When every member is ready the next round
/// resolves automatically (the all-ready mode).</summary>
public sealed record SetReadyRequest(bool Ready);

/// <summary>One scheduled/played match in the season.</summary>
public sealed record LeagueFixtureDto(
    Guid Id,
    int Round,
    int Day,
    int HomeClubExternalId,
    string HomeClubName,
    int AwayClubExternalId,
    string AwayClubName,
    bool Played,
    int HomeGoals,
    int AwayGoals);

/// <summary>A club's standing computed from the played fixtures.</summary>
public sealed record LeagueStandingDto(
    int ClubExternalId,
    string ClubName,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points);

/// <summary>Where the season is + the caller's own state (for the lobby's advance/ready UI).</summary>
public sealed record SeasonStateDto(
    bool Started,
    int TotalRounds,
    int RoundsPlayed,
    int? NextRound,
    bool SeasonComplete,
    int MembersTotal,
    int MembersReady,
    bool YouAreReady,
    int? YourClubExternalId,
    bool YouSubmittedLineup);

/// <summary>The full season view: state + the whole fixture list + the current table.</summary>
public sealed record LeagueSeasonDto(
    SeasonStateDto Season,
    IReadOnlyList<LeagueFixtureDto> Fixtures,
    IReadOnlyList<LeagueStandingDto> Standings);
