using System.Security.Claims;
using Fts.Application.Leagues;

namespace Fts.Api.Leagues;

/// <summary>
/// The online-auction HTTP surface (Phase 8.5): open/close a window (creator), read the lots, and place a
/// bid. All JWT-protected — the account id comes from the access token, never the body. Thin: bind,
/// resolve the user, call <see cref="IAuctionService"/>, map the result. The live push channel is the
/// SignalR <c>AuctionHub</c>; these REST endpoints stay the source of truth (a client can poll them).
/// </summary>
public static class AuctionEndpoints
{
    public static IEndpointRouteBuilder MapAuctionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/leagues/{id:guid}/auctions").RequireAuthorization();

        // Open the next auction window (creator only): a lot per free agent.
        group.MapPost("/open", async (
            Guid id, ClaimsPrincipal user, IAuctionService auctions, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.OpenWindowAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // The current auction view (members only): lots + the caller's budget picture.
        group.MapGet("", async (
            Guid id, ClaimsPrincipal user, IAuctionService auctions, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.GetAuctionsAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Place a bid on a lot.
        group.MapPost("/{auctionId:guid}/bid", async (
            Guid id, Guid auctionId, PlaceBidRequest req, ClaimsPrincipal user, IAuctionService auctions, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.PlaceBidAsync(userId, id, auctionId, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Close the window now (creator only): settle every open lot immediately.
        group.MapPost("/close", async (
            Guid id, ClaimsPrincipal user, IAuctionService auctions, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.CloseWindowAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        return app;
    }
}
