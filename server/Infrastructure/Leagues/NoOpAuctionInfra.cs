using Fts.Application.Leagues;

namespace Fts.Infrastructure.Leagues;

/// <summary>Default <see cref="IAuctionBroadcaster"/> that pushes nothing (Phase 8.5). Registered in
/// Infrastructure so the auction service resolves everywhere; the Api replaces it with the SignalR
/// implementation when the real host runs.</summary>
public sealed class NoOpAuctionBroadcaster : IAuctionBroadcaster
{
    public Task LotChangedAsync(Guid leagueId, AuctionLotDto lot, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>Default <see cref="IAuctionScheduler"/> that schedules nothing (Phase 8.5). Registered in
/// Infrastructure; when Hangfire background jobs are enabled the real scheduler replaces it. With no
/// scheduler, lots are settled by the creator's explicit "close window" action (the unit-test path).</summary>
public sealed class NoOpAuctionScheduler : IAuctionScheduler
{
    public void ScheduleSettlement(Guid auctionId, DateTime whenUtc) { }
}
