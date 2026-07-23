using Fts.Application.Dev;

namespace Fts.Api.Dev;

/// <summary>
/// Dev-only test-league seeding endpoints (dev tooling). Mapped by Program.cs ONLY outside Production and
/// behind the <c>Dev:ExposeSeedEndpoints</c> flag, so they never exist in a real deployment. Deliberately
/// unauthenticated — the whole point is to bootstrap a ready online league (with tokens) without first
/// hand-creating accounts. Thin: bind, call <see cref="IDevSeedService"/>, return the result.
/// </summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/dev");

        // Create a ready-to-test league (bots + completed draft) in one call.
        group.MapPost("/test-league", async (DevSeedRequest? req, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.SeedTestLeagueAsync(req ?? new DevSeedRequest(), ct)));

        // Bot autopilot: place a round of legal bids on every open auction lot.
        group.MapPost("/leagues/{id:guid}/auctions/botbid", async (
            Guid id, DevBotBidRequest? req, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.BotBidAsync(id, req ?? new DevBotBidRequest(), ct)));

        // Bot autopilot: mark every bot member ready (advances the all-ready season one round once the
        // human is ready too).
        group.MapPost("/leagues/{id:guid}/bots/ready", async (
            Guid id, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.BotReadyAsync(id, ct)));

        // Bot autopilot: the fixture's bot opponent joins the live match (kicking it off once the human is
        // present) and optionally makes a substitution — so a single human can test live control solo (8.6).
        group.MapPost("/leagues/{id:guid}/live/{fixtureId:guid}/bot", async (
            Guid id, Guid fixtureId, DevBotLiveRequest? req, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.BotLiveAsync(id, fixtureId, req ?? new DevBotLiveRequest(), ct)));

        // Cleanup: the deterministic bots leave every league they are in.
        group.MapPost("/reset", async (int? bots, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.ResetAsync(bots ?? 8, ct)));

        // Ranked dev tooling (9.2): fill the forming ranked placement groups with bot coaches so a solo
        // human's cohort completes and its season can start (then advance it via /internal/ranked/tick).
        // `count` is a query param (optional) so a bodyless POST binds cleanly — same shape as /reset.
        group.MapPost("/ranked/fill", async (int? count, IDevSeedService dev, CancellationToken ct) =>
            Results.Ok(await dev.FillRankedAsync(new DevRankedFillRequest(count), ct)));

        return app;
    }
}
