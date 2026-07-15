namespace Fts.Application.Leagues;

/// <summary>
/// Private-league season use cases (Phase 8.3): submit match inputs, mark ready / advance a round, and
/// read the schedule + standings + a fixture replay. The season is the "advance when all ready" mode —
/// the next round resolves once every member is ready (or when the creator forces it), running the shared
/// Sim.Core engine from each club's submitted lineup/plan (or a best-XI AI fallback) with a deterministic
/// per-fixture seed. The account id is always passed in by the Api from the access token. Expected failures
/// come back as <see cref="LeagueResult{T}"/> (no exceptions).
/// </summary>
public interface ILeagueSeasonService
{
    /// <summary>Stores (or replaces) the caller's match inputs for their club. The lineup is validated
    /// against the current squad; an invalid lineup is rejected. Requires the season to be Active and the
    /// caller to have a club.</summary>
    Task<LeagueResult<SeasonStateDto>> SubmitLineupAsync(
        Guid userId, Guid leagueId, SubmitLineupRequest request, CancellationToken ct = default);

    /// <summary>Stores (or replaces) the caller's training plan for their club (Phase 8.4). The
    /// server-authoritative weekly development tick reuses it each round-week. Requires the season to be
    /// Active and the caller to have a club.</summary>
    Task<LeagueResult<SeasonStateDto>> SubmitTrainingAsync(
        Guid userId, Guid leagueId, SubmitTrainingRequest request, CancellationToken ct = default);

    /// <summary>Marks the caller ready (or not). When every member is ready, the next unplayed round
    /// resolves automatically and all ready flags are cleared.</summary>
    Task<LeagueResult<LeagueSeasonDto>> SetReadyAsync(
        Guid userId, Guid leagueId, SetReadyRequest request, CancellationToken ct = default);

    /// <summary>The canonical hash of the whole world's mutable player state (condition + attributes)
    /// after the rounds played so far (Phase 8.4 ✅). Members only. Lets a client verify it agrees with
    /// the server-authoritative state.</summary>
    Task<LeagueResult<StateHashDto>> GetStateHashAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>Forces the next unplayed round to resolve now (creator only), using each club's last
    /// submitted inputs or the best-XI fallback. Clears ready flags. The anti-stall / testing driver.</summary>
    Task<LeagueResult<LeagueSeasonDto>> AdvanceAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>The season view for a member: state + fixtures + standings.</summary>
    Task<LeagueResult<LeagueSeasonDto>> GetSeasonAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>The stored full <c>MatchReport</c> JSON for a played fixture (identical for every member),
    /// so the client renders the replay directly. Members only; the fixture must be played.</summary>
    Task<LeagueResult<string>> GetReplayAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);
}
