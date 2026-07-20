using Fts.Application.Leagues;

namespace Fts.Infrastructure.Leagues;

/// <summary>Default <see cref="ILiveMatchBroadcaster"/> that pushes nothing (Phase 8.6). Registered in
/// Infrastructure so the live-match service resolves everywhere (including the unit tests, which prove the
/// state via the REST reads); the Api replaces it with the SignalR implementation when the real host
/// runs.</summary>
public sealed class NoOpLiveMatchBroadcaster : ILiveMatchBroadcaster
{
    public Task MatchChangedAsync(Guid fixtureId, LiveMatchStateDto state, CancellationToken ct = default) =>
        Task.CompletedTask;
}
