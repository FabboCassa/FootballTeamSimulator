using Fts.Application.Ranked;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// The global-ladder cache backed by a Redis <b>sorted set</b> (Phase 9.3): member = the account id,
/// score = the rating. That is exactly what a leaderboard wants — O(log n) writes and an O(log n + k)
/// top-k read, no scan of <c>ranked_coaches</c>.
///
/// PostgreSQL stays authoritative (<c>ranked_coaches.Rating</c>): this cache is a pure read accelerator and
/// is fully rebuildable from it, so every operation swallows transport failures and a cold or unreachable
/// Redis simply degrades to the database path. That is why the ladder is not stored here — losing Redis must
/// never lose a coach's progression.
/// </summary>
public sealed class RedisRankedLeaderboardCache : IRankedLeaderboardCache
{
    /// <summary>The sorted-set key. Versioned so a scoring change can start a clean set.</summary>
    private const string Key = "fts:ranked:leaderboard:v1";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRankedLeaderboardCache> _log;

    public RedisRankedLeaderboardCache(IConnectionMultiplexer redis, ILogger<RedisRankedLeaderboardCache> log)
    {
        _redis = redis;
        _log = log;
    }

    public bool IsEnabled => true;

    public async Task SetRatingAsync(Guid userId, int rating, CancellationToken ct = default)
    {
        try { await Db().SortedSetAddAsync(Key, userId.ToString("N"), rating); }
        catch (Exception ex) { Swallow(ex, "set rating"); }
    }

    public async Task RemoveAsync(Guid userId, CancellationToken ct = default)
    {
        try { await Db().SortedSetRemoveAsync(Key, userId.ToString("N")); }
        catch (Exception ex) { Swallow(ex, "remove"); }
    }

    public async Task<IReadOnlyList<Guid>> TopAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<Guid>();
        try
        {
            var values = await Db().SortedSetRangeByRankAsync(Key, 0, count - 1, Order.Descending);
            var ids = new List<Guid>(values.Length);
            foreach (var v in values)
                if (Guid.TryParseExact(v.ToString(), "N", out var id)) ids.Add(id);
            return ids;
        }
        catch (Exception ex)
        {
            Swallow(ex, "top");
            return Array.Empty<Guid>();
        }
    }

    public async Task RebuildAsync(IReadOnlyList<(Guid UserId, int Rating)> entries, CancellationToken ct = default)
    {
        try
        {
            var db = Db();
            await db.KeyDeleteAsync(Key);
            if (entries.Count == 0) return;

            var members = new SortedSetEntry[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                members[i] = new SortedSetEntry(entries[i].UserId.ToString("N"), entries[i].Rating);
            await db.SortedSetAddAsync(Key, members);
        }
        catch (Exception ex) { Swallow(ex, "rebuild"); }
    }

    private IDatabase Db() => _redis.GetDatabase();

    private void Swallow(Exception ex, string op) =>
        _log.LogDebug(ex, "[ranked-leaderboard] Redis {Operation} failed; falling back to PostgreSQL.", op);
}

/// <summary>
/// The default cache: does nothing. Registered wherever Redis is not wired up (notably the Testing
/// environment, which runs on in-memory SQLite with no Redis container) so the ranking service always has a
/// dependency to talk to and simply reads PostgreSQL.
/// </summary>
public sealed class NoOpRankedLeaderboardCache : IRankedLeaderboardCache
{
    public bool IsEnabled => false;

    public Task SetRatingAsync(Guid userId, int rating, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Guid>> TopAsync(int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

    public Task RebuildAsync(IReadOnlyList<(Guid UserId, int Rating)> entries, CancellationToken ct = default) =>
        Task.CompletedTask;
}
