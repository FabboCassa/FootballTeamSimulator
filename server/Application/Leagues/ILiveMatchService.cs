namespace Fts.Application.Leagues;

/// <summary>
/// Live-match use cases (Phase 8.6). One private-league fixture between two human members can be played
/// "live": both connect at kickoff, then either sends pause-point inputs (subs / instruction changes)
/// that the server processes authoritatively by re-simulating the deterministic engine from the fixture
/// seed and broadcasting the new state. Only the current round's human-vs-human fixture is eligible; every
/// other fixture stays instant (8.3). Disconnect is graceful by construction: an absent member simply
/// stops sending changes, so the match plays out on their last-submitted lineup and pre-match plan (the
/// engine fires those rules automatically — the "plan/AI fallback"). The account id is always passed in by
/// the Api from the access token. Expected failures come back as <see cref="LeagueResult{T}"/> (no
/// exceptions). All state is authoritative in Postgres/EF; live pushes are a thin broadcast over the top
/// (see <see cref="ILiveMatchBroadcaster"/>).
/// </summary>
public interface ILiveMatchService
{
    /// <summary>Open (or return the existing) live session for a fixture. The caller must be one of the two
    /// human members of a current-round, unplayed, human-vs-human fixture; marks the caller present.</summary>
    Task<LeagueResult<LiveMatchStateDto>> OpenAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>Join a session (mark the caller present). When both members are present the match goes Live
    /// and kickoff is stamped.</summary>
    Task<LeagueResult<LiveMatchStateDto>> JoinAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>The current live-match state (any league member may watch; only the two side owners may
    /// change).</summary>
    Task<LeagueResult<LiveMatchStateDto>> GetAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>Submit a pause-point change for the caller's own side: append it to the authoritative plan,
    /// re-simulate the 90' from the fixture seed, store the new report and broadcast the state.</summary>
    Task<LeagueResult<LiveMatchStateDto>> SubmitChangeAsync(
        Guid userId, Guid leagueId, Guid fixtureId, SubmitLiveChangeRequest request, CancellationToken ct = default);

    /// <summary>Mark the caller absent (disconnect). The match keeps running on the accumulated plan.</summary>
    Task<LeagueResult<LiveMatchStateDto>> LeaveAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);

    /// <summary>Confirm full-time (either side owner): the stored report becomes the fixture's official
    /// result when the round resolves.</summary>
    Task<LeagueResult<LiveMatchStateDto>> FinishAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default);
}
