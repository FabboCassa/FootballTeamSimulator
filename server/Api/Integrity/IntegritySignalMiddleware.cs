using System.Security.Claims;
using Fts.Application.Integrity;

namespace Fts.Api.Integrity;

/// <summary>
/// Records where an authenticated ranked request came from (Phase 9.5), so the multi-account heuristics
/// have something to work with at enrolment time.
///
/// Scope is deliberately narrow — only authenticated <c>/ranked/*</c> calls. That is where the competitive
/// stakes are; the rest of the API is left alone. The address is hashed inside
/// <c>IntegrityService</c> (never stored raw) and re-recorded at most once per cooldown window, so this
/// costs one indexed lookup on a ladder request and nothing at all elsewhere.
///
/// The device id is whatever stable identifier the client chooses to send in <c>X-Fts-Device</c>; it is
/// optional, and a client that sends none simply scores lower on the link heuristics — no request is ever
/// refused for its absence.
///
/// The signal is recorded BEFORE the endpoint runs, not after it. That ordering is the whole point: the
/// enrolment guard asks who the caller is linked to, and an account whose very first ranked call IS
/// <c>POST /ranked/enrol</c> would otherwise have no fingerprint yet and sail straight past it.
/// </summary>
public static class IntegritySignalMiddleware
{
    public const string DeviceHeader = "X-Fts-Device";

    public static IApplicationBuilder UseFtsIntegritySignals(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            await RecordAsync(ctx);
            await next();
        });
    }

    private static async Task RecordAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/ranked")) return;
        if (ctx.User?.Identity?.IsAuthenticated != true) return;

        var id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub");
        if (!Guid.TryParse(id, out var userId)) return;

        try
        {
            var integrity = ctx.RequestServices.GetRequiredService<IIntegrityService>();
            await integrity.RecordAccountSignalAsync(userId, ResolveClientAddress(ctx), DeviceId(ctx));
        }
        catch
        {
            // A fingerprint is diagnostics, never a use case: it must never be able to fail the request
            // it was riding along with.
        }
    }

    /// <summary>The client address as best the deployment can tell: the first hop of an
    /// <c>X-Forwarded-For</c> chain when behind a proxy, else the socket address. Roadmap 10.4 moved the
    /// reading into <see cref="Fts.Api.Integrity.ClientAddress"/> so the rate limiter resolves the caller
    /// exactly the same way — behind the production proxy the socket address is the proxy's, and the two
    /// address-partitioned features must not disagree about that.</summary>
    private static string? ResolveClientAddress(HttpContext ctx) => ClientAddress.Resolve(ctx);

    private static string? DeviceId(HttpContext ctx)
    {
        var device = ctx.Request.Headers[DeviceHeader].ToString();
        return string.IsNullOrWhiteSpace(device) ? null : device.Trim();
    }
}
