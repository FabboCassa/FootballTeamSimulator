namespace Fts.Application.Leagues;

/// <summary>
/// Schedules the automatic settlement of an auction lot at its timer end (Phase 8.5). Implemented in
/// Infrastructure over Hangfire when background jobs are enabled; a no-op default is registered otherwise
/// (unit tests run with Hangfire disabled and settle synchronously via the creator "close window" action).
/// Separating this from the service keeps <see cref="IAuctionService"/> free of a hard Hangfire dependency.
/// </summary>
public interface IAuctionScheduler
{
    /// <summary>Enqueue a settlement of this lot to run at <paramref name="whenUtc"/>. Idempotent — the
    /// settlement job re-checks the lot's end time and re-schedules itself if an anti-snipe bid moved it.</summary>
    void ScheduleSettlement(Guid auctionId, DateTime whenUtc);
}
