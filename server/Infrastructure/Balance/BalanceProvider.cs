using Fts.Application.Balance;
using Sim.Core.Config;

namespace Fts.Infrastructure.Balance;

/// <summary>
/// <see cref="IBalanceProvider"/> implementation (Phase 10.3): a process-wide holder for the active
/// <see cref="BalanceConfig"/>, swapped by reference.
///
/// No lock on the read path. A reference assignment is atomic on every runtime we target, and the config
/// object is treated as IMMUTABLE once published — a push builds a fresh object and swaps it in, it never
/// mutates the one in flight. That is what lets a request that started before a push finish on the old
/// config instead of seeing half of each, which for a match sim is the difference between a coherent
/// result and a nonsensical one.
/// </summary>
public sealed class BalanceProvider : IBalanceProvider
{
    private readonly object _gate = new();
    private volatile BalanceConfig _current = new();
    private volatile int _revision;
    private DateTime? _loadedUtc;

    public BalanceConfig Current => _current;

    public int Revision => _revision;

    public DateTime? LoadedUtc
    {
        get { lock (_gate) return _loadedUtc; }
    }

    public void Set(BalanceConfig config, int revision)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            // Never move backwards. The reload job and a push can race (the job polls every minute, the
            // push writes immediately); without this a reload that read the table just before the push
            // could land after it and quietly revert the operator's change. Revisions start at 1, so the
            // baseline (0) always loses to a real one and a repeat of the same revision is a no-op.
            if (revision <= _revision) return;

            _current = config;
            _revision = revision;
            _loadedUtc = DateTime.UtcNow;
        }
    }

    public void ResetToBaseline()
    {
        lock (_gate)
        {
            _current = new BalanceConfig();
            _revision = 0;
            _loadedUtc = null;
        }
    }
}
