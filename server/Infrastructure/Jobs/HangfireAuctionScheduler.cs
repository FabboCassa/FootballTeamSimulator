using Fts.Application.Leagues;
using Hangfire;

namespace Fts.Infrastructure.Jobs;

/// <summary>
/// Schedules auction-lot settlement over Hangfire (Phase 8.5). Registered only when background jobs are
/// enabled; uses the DI <see cref="IBackgroundJobClient"/> (NOT the static <c>BackgroundJob</c> API, which
/// needs <c>JobStorage.Current</c> that the service-based Hangfire setup does not populate — same reason
/// Program.cs uses the DI recurring-job manager). Enqueues <see cref="AuctionSettlementJob"/> with a delay
/// to the lot's end time; the job re-checks and re-schedules itself if an anti-snipe bid moved the end.
/// </summary>
public sealed class HangfireAuctionScheduler : IAuctionScheduler
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireAuctionScheduler(IBackgroundJobClient jobs) => _jobs = jobs;

    public void ScheduleSettlement(Guid auctionId, DateTime whenUtc)
    {
        var delay = whenUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        _jobs.Schedule<AuctionSettlementJob>(j => j.RunAsync(auctionId, CancellationToken.None), delay);
    }
}
