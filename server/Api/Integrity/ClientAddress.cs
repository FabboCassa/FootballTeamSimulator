namespace Fts.Api.Integrity;

/// <summary>
/// The caller's address as best this deployment can tell it (Roadmap 10.4).
///
/// Behind the production reverse proxy every request arrives on the proxy's socket, so
/// <c>Connection.RemoteIpAddress</c> is the SAME value for every player on the planet. Anything that
/// partitions by address — the anonymous rate-limit bucket, and the 9.5 shared-address heuristic —
/// therefore has to read the forwarded chain instead, or it silently degenerates into one global bucket
/// and a "everyone lives at the same address" link signal.
///
/// The first entry of <c>X-Forwarded-For</c> is the original client; the rest are proxies. It is
/// client-supplied and so spoofable — which is exactly why it is used only for RATE LIMITING and for a
/// heuristic that scores rather than bans, never for authorisation. TRUSTING it is a deployment
/// statement: it is correct only when a proxy that overwrites the header sits in front (the shipped
/// Caddyfile does, and the API port is not published to the host), which is what
/// <c>docs/ops/deploy.md</c> requires.
/// </summary>
public static class ClientAddress
{
    public const string ForwardedForHeader = "X-Forwarded-For";

    public static string? Resolve(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers[ForwardedForHeader].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0) return first;
        }

        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
