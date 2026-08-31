using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fts.Api.Matches;

/// <summary>Strongly-typed client contract for live-match pushes (Phase 8.6) — the Unity/web client
/// implements a <c>MatchChanged</c> handler to re-render the match when the opponent makes a change.
/// Task 12.3 adds <c>RankedMatchChanged</c>: the ladder's live match rides the SAME hub and the same
/// per-fixture group (a fixture id is unique either way), because it is the same channel doing the same job
/// — a second hub would have meant a second connection per client for no gain.</summary>
public interface IMatchClient
{
    Task MatchChanged(LiveMatchStateDto state);

    Task RankedMatchChanged(RankedLiveStateDto state);
}

/// <summary>
/// The live match-control channel (Phase 8.6) — the second SignalR hub (named <c>MatchHub</c> in
/// ARCHITECTURE §6.1), mirroring the 8.5 <c>AuctionHub</c>. Thin by design: it only manages per-fixture
/// group membership; the authoritative work (validate the change, re-simulate, store) stays in
/// <see cref="ILiveMatchService"/> behind the REST endpoints, and the service broadcasts the resulting
/// state to this hub's fixture group. JWT-protected (the access token arrives via the <c>access_token</c>
/// query string for the WebSocket handshake — already wired in Program.cs for the <c>/hubs</c> paths).
/// </summary>
[Authorize]
public sealed class MatchHub : Hub<IMatchClient>
{
    /// <summary>Subscribe this connection to a fixture's live-match updates.</summary>
    public Task JoinMatch(Guid fixtureId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(fixtureId));

    /// <summary>Stop receiving a fixture's live-match updates.</summary>
    public Task LeaveMatch(Guid fixtureId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(fixtureId));

    /// <summary>The SignalR group name for a fixture's live viewers.</summary>
    public static string Group(Guid fixtureId) => $"match:{fixtureId}";
}
