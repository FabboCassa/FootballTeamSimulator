using Fts.Application.Ranked;
using Microsoft.AspNetCore.SignalR;

namespace Fts.Api.Matches;

/// <summary>
/// The real <see cref="IRankedLiveBroadcaster"/> (task 12.3): pushes ranked live-match state to a fixture's
/// <see cref="MatchHub"/> group over SignalR — the same hub and the same group naming the private leagues
/// use, since a fixture id identifies a match whichever competition it belongs to. Registered in Program.cs,
/// replacing the Infrastructure no-op. Never throws: a broadcast failure (a dropped connection, a client
/// that closed the screen mid-substitution) must not fail the change that already committed authoritatively.
/// With no connected clients — the unit tests, or a coach who is polling the GET instead — it is a harmless
/// no-op, which is exactly why the REST endpoints stay the source of truth.
/// </summary>
public sealed class SignalRRankedLiveBroadcaster : IRankedLiveBroadcaster
{
    private readonly IHubContext<MatchHub, IMatchClient> _hub;
    private readonly ILogger<SignalRRankedLiveBroadcaster> _log;

    public SignalRRankedLiveBroadcaster(
        IHubContext<MatchHub, IMatchClient> hub, ILogger<SignalRRankedLiveBroadcaster> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task RankedMatchChangedAsync(
        Guid fixtureId, RankedLiveStateDto state, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(MatchHub.Group(fixtureId)).RankedMatchChanged(state);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ranked live-match broadcast failed for fixture {FixtureId}", fixtureId);
        }
    }
}
