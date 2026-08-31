using System.Security.Claims;
using Fts.Application.Leagues;

namespace Fts.Api.Leagues;

/// <summary>
/// The private-league transfer market's HTTP surface (Phase 12.1): read the market, offer for a player,
/// answer a negotiation, list one of your own, and agree terms with a free agent. All JWT-protected — the
/// account id comes from the access token, never the body. Thin: bind, resolve the user, call
/// <see cref="ILeagueMarketService"/>, map the result.
/// </summary>
public static class LeagueMarketEndpoints
{
    public static IEndpointRouteBuilder MapLeagueMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/leagues/{id:guid}/market").RequireAuthorization();

        // The caller's whole market view.
        group.MapGet("", async (
            Guid id, ClaimsPrincipal user, ILeagueMarketService market, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await market.GetMarketAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Offer a fee for another club's player (a bot answers inside this call).
        group.MapPost("/offers", async (
            Guid id, MakeLeagueOfferRequest req, ClaimsPrincipal user, ILeagueMarketService market,
            CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await market.MakeOfferAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Accept / reject / counter / withdraw a negotiation.
        group.MapPost("/offers/{offerId:guid}", async (
            Guid id, Guid offerId, RespondLeagueOfferRequest req, ClaimsPrincipal user,
            ILeagueMarketService market, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await market.RespondAsync(userId, id, offerId, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Put one of your own players on (or off) the transfer list.
        group.MapPost("/listings", async (
            Guid id, ListPlayerRequest req, ClaimsPrincipal user, ILeagueMarketService market,
            CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await market.SetListingAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Agree terms with a free agent — first come, first served.
        group.MapPost("/free-agents", async (
            Guid id, SignFreeAgentRequest req, ClaimsPrincipal user, ILeagueMarketService market,
            CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await market.SignFreeAgentAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        return app;
    }
}
