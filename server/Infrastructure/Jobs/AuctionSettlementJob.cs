using Fts.Application.Leagues;

namespace Fts.Infrastructure.Jobs;

/// <summary>
/// The Hangfire per-lot settlement job (Phase 8.5): scheduled at a lot's timer end, it settles the lot
/// (assign the player + charge the winner, or mark it unsold). Idempotent and self-healing — if an
/// anti-snipe bid extended the timer after the job was enqueued, <see cref="IAuctionService.SettleDueAsync"/>
/// re-schedules for the new end instead of settling early. DI-activated (like <see cref="HeartbeatJob"/>).
/// </summary>
public sealed class AuctionSettlementJob
{
    private readonly IAuctionService _auctions;

    public AuctionSettlementJob(IAuctionService auctions) => _auctions = auctions;

    public Task RunAsync(Guid auctionId, CancellationToken ct = default) =>
        _auctions.SettleDueAsync(auctionId, ct);
}
