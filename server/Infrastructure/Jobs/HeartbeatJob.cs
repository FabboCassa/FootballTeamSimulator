using Microsoft.Extensions.Logging;

namespace Fts.Infrastructure.Jobs;

/// <summary>
/// A trivial recurring job (Phase 7.4) that proves the Hangfire scheduler is wired and firing on
/// time — it just logs a UTC heartbeat. It is the placeholder the real scheduled work of Phase 8
/// (kickoff match resolution, auction settlement, season rollover) will replace/join; keeping a
/// live recurring job here makes "scheduled job fires on time" verifiable in the dashboard now.
/// Resolved from DI by Hangfire, so it can take injected dependencies (here, just a logger).
/// </summary>
public sealed class HeartbeatJob
{
    private readonly ILogger<HeartbeatJob> _log;

    public HeartbeatJob(ILogger<HeartbeatJob> log) => _log = log;

    public const string RecurringJobId = "fts-heartbeat";

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        _log.LogInformation("[heartbeat] Hangfire recurring job fired at {UtcNow:O}.", DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
