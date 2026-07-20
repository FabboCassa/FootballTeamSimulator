using Fts.Application.Leagues;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fts.Api.Auctions;

/// <summary>Strongly-typed client contract for auction pushes (Phase 8.5) — the Unity/web client
/// implements a <c>LotChanged</c> handler to update a lot live.</summary>
public interface IAuctionClient
{
    Task LotChanged(AuctionLotDto lot);
}

/// <summary>
/// The live auction channel (Phase 8.5) — the project's first SignalR hub. Thin by design: it only
/// manages per-league group membership; the authoritative work (validate bid, anti-snipe, settle) stays
/// in <see cref="IAuctionService"/> behind the REST endpoints, and the service broadcasts the resulting
/// lot state to this hub's league group. JWT-protected (the access token arrives via the <c>access_token</c>
/// query string for the WebSocket handshake — wired in Program.cs). This hub is reused for live match
/// control in 8.6.
/// </summary>
[Authorize]
public sealed class AuctionHub : Hub<IAuctionClient>
{
    /// <summary>Subscribe this connection to a league's live auction updates.</summary>
    public Task JoinLeague(Guid leagueId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(leagueId));

    /// <summary>Stop receiving a league's auction updates.</summary>
    public Task LeaveLeague(Guid leagueId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(leagueId));

    /// <summary>The SignalR group name for a league's auction viewers.</summary>
    public static string Group(Guid leagueId) => $"auction:{leagueId}";
}
