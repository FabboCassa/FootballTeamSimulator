using Fts.Application.Leagues;
using Microsoft.AspNetCore.SignalR;

namespace Fts.Api.Matches;

/// <summary>
/// The real <see cref="ILiveMatchBroadcaster"/> (Phase 8.6): pushes live-match state to a fixture's
/// <see cref="MatchHub"/> group over SignalR. Registered in Program.cs, replacing the Infrastructure
/// no-op. Never throws — a broadcast failure (e.g. a dropped connection) must not fail the change that
/// already committed authoritatively; it is logged and swallowed. With no connected clients (e.g. the
/// unit tests) the broadcast is a harmless no-op.
/// </summary>
public sealed class SignalRMatchBroadcaster : ILiveMatchBroadcaster
{
    private readonly IHubContext<MatchHub, IMatchClient> _hub;
    private readonly ILogger<SignalRMatchBroadcaster> _log;

    public SignalRMatchBroadcaster(IHubContext<MatchHub, IMatchClient> hub, ILogger<SignalRMatchBroadcaster> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task MatchChangedAsync(Guid fixtureId, LiveMatchStateDto state, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(MatchHub.Group(fixtureId)).MatchChanged(state);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Live-match broadcast failed for fixture {FixtureId}", fixtureId);
        }
    }
}
