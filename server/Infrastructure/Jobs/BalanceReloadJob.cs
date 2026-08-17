using Fts.Application.Balance;
using Fts.Infrastructure.Balance;
using Fts.Infrastructure.Persistence;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Fts.Infrastructure.Jobs;

/// <summary>
/// Keeps every API instance on the newest balance revision (Phase 10.3).
///
/// A push writes the revision row and swaps the config on the instance that served the request — but a
/// deployment runs more than one instance, and the others would happily keep simulating on the old
/// numbers. Rather than a message bus for one small piece of state, each instance re-reads the highest
/// revision once a minute; the provider ignores anything not newer than what it holds, so this is a
/// no-op read in the overwhelming majority of runs.
///
/// A minute of skew between instances is acceptable for a tuning change and is stated in the runbook.
/// What is NOT acceptable is skew nobody can see, which is why <c>GET /admin/metrics</c> reports the
/// revision of the instance that answered.
/// </summary>
public sealed class BalanceReloadJob
{
    private readonly FtsDbContext _db;
    private readonly IBalanceProvider _balance;
    private readonly ILogger<BalanceReloadJob> _log;

    public BalanceReloadJob(FtsDbContext db, IBalanceProvider balance, ILogger<BalanceReloadJob> log)
    {
        _db = db;
        _balance = balance;
        _log = log;
    }

    public const string RecurringJobId = "fts-balance-reload";

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task ExecuteAsync(CancellationToken ct = default) =>
        BalanceStore.LoadActiveAsync(_db, _balance, _log, ct);
}
