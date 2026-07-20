namespace Fts.Application.Leagues;

/// <summary>
/// Pushes live auction updates to connected clients (Phase 8.5). Implemented in the Api layer over a
/// SignalR <c>AuctionHub</c>; a no-op default is registered in Infrastructure so the authoritative service
/// stays testable without SignalR. The REST endpoints remain the source of truth — this is only the live
/// broadcast layer, so a client can also just poll <see cref="IAuctionService.GetAuctionsAsync"/>. Never
/// throws (a broadcast failure must not fail the bid that already committed).
/// </summary>
public interface IAuctionBroadcaster
{
    /// <summary>A lot changed (new high bid / timer extended / settled) — pushed to the league's group.</summary>
    Task LotChangedAsync(Guid leagueId, AuctionLotDto lot, CancellationToken ct = default);
}
