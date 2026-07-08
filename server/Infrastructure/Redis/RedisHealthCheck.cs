using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Fts.Infrastructure.Redis;

/// <summary>
/// Readiness probe for Redis: PINGs the shared multiplexer. Redis isn't used by any feature
/// yet (auctions/live-match sessions land in Phase 7.4/8.x) — this just proves the connection
/// is alive so <c>/health/ready</c> reflects the full infra.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pong = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis PING {pong.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
