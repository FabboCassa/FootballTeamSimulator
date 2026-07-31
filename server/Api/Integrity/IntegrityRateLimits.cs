using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Fts.Infrastructure.Integrity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fts.Api.Integrity;

/// <summary>
/// Per-account rate limits on the ranked write surface (Phase 9.5), built on the framework's own
/// <c>AddRateLimiter</c> — no new dependency, and the limits live in <see cref="IntegrityOptions"/> so a
/// live ladder can be tightened from configuration.
///
/// Partitioning is <b>per account</b>, not per address: the ladder is authenticated everywhere that
/// matters, and partitioning by IP would punish everyone behind one household or campus NAT — the same
/// people the multi-account heuristics are careful not to ban. An unauthenticated request (which the
/// endpoints reject anyway) falls back to its address so an anonymous flood still cannot spin the CPU.
///
/// Three buckets, because the abuse shapes differ:
/// <list type="bullet">
/// <item><b>bids</b> — the classic spam surface: an auction lot with a script hammering it.</item>
/// <item><b>writes</b> — lineups, training, confirmations, offers, enrolment.</item>
/// <item><b>reports</b> — filing reports is itself a harassment vector (the service also caps them daily).</item>
/// </list>
///
/// Rejection is a plain 429 with a <c>Retry-After</c>, in the same error-envelope shape the ranked
/// endpoints use, so the client can localise it like any other ranked failure.
/// </summary>
public static class IntegrityRateLimits
{
    public const string Writes = "ranked-writes";
    public const string Bids = "ranked-bids";
    public const string Reports = "ranked-reports";

    public static IServiceCollection AddFtsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Writes, ctx => Partition(ctx, o => o.WritesPerWindow));
            options.AddPolicy(Bids, ctx => Partition(ctx, o => o.BidsPerWindow));
            options.AddPolicy(Reports, ctx => Partition(ctx, o => o.ReportsPerWindow));

            options.OnRejected = async (context, ct) =>
            {
                var opt = Options(context.HttpContext);
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter =
                    (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                        ? (int)retry.TotalSeconds
                        : opt.RateWindowSeconds)
                    .ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "rate_limited",
                    message = "Too many requests — slow down and try again shortly."
                }, ct);
            };
        });

        return services;
    }

    /// <summary>One fixed window per account (or address when anonymous). Queueing is deliberately zero:
    /// a coach who is over the limit should be told immediately, not held on a socket.</summary>
    private static RateLimitPartition<string> Partition(
        HttpContext ctx, Func<IntegrityOptions, int> permits)
    {
        var opt = Options(ctx);
        if (!opt.EnableRateLimiting) return RateLimitPartition.GetNoLimiter("disabled");

        string key = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? ctx.User.FindFirstValue("sub")
                     ?? ctx.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits(opt)),
            Window = TimeSpan.FromSeconds(Math.Max(1, opt.RateWindowSeconds)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }

    private static IntegrityOptions Options(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<IOptions<IntegrityOptions>>().Value;
}
