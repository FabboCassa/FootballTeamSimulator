namespace Fts.Application.Leagues;

/// <summary>
/// Pushes live-match state to the two connected clients (Phase 8.6). Implemented in the Api layer over the
/// SignalR <c>MatchHub</c> (the §6.1-named second hub); a no-op default is registered in Infrastructure so
/// the authoritative <see cref="ILiveMatchService"/> stays testable without SignalR. The REST endpoints
/// remain the source of truth — this is only the live push layer, so a client can also just poll
/// <see cref="ILiveMatchService.GetAsync"/>. Never throws (a broadcast failure must not fail the change
/// that already committed).
/// </summary>
public interface ILiveMatchBroadcaster
{
    /// <summary>The live match changed (a sub / instruction change re-simulated) — pushed to the fixture's
    /// group so the opponent's view updates.</summary>
    Task MatchChangedAsync(Guid fixtureId, LiveMatchStateDto state, CancellationToken ct = default);
}
