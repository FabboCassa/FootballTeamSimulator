using Fts.Application.Leagues;
using Microsoft.AspNetCore.SignalR;

namespace Fts.Api.Auctions;

/// <summary>
/// The real <see cref="IAuctionBroadcaster"/> (Phase 8.5): pushes lot updates to a league's <see
/// cref="AuctionHub"/> group over SignalR. Registered in Program.cs, replacing the Infrastructure no-op.
/// Never throws — a broadcast failure (e.g. a dropped connection) must not fail the bid that already
/// committed authoritatively; it is logged and swallowed. With no connected clients (e.g. the unit tests)
/// the broadcast is a harmless no-op.
/// </summary>
public sealed class SignalRAuctionBroadcaster : IAuctionBroadcaster
{
    private readonly IHubContext<AuctionHub, IAuctionClient> _hub;
    private readonly ILogger<SignalRAuctionBroadcaster> _log;

    public SignalRAuctionBroadcaster(IHubContext<AuctionHub, IAuctionClient> hub, ILogger<SignalRAuctionBroadcaster> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task LotChangedAsync(Guid leagueId, AuctionLotDto lot, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(AuctionHub.Group(leagueId)).LotChanged(lot);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auction broadcast failed for league {LeagueId}", leagueId);
        }
    }
}
