using System.Security.Claims;
using Fts.Application.Leagues;

namespace Fts.Api.Leagues;

/// <summary>
/// The live match-control HTTP surface (Phase 8.6): open/join/leave a live session, submit a pause-point
/// change (sub / instruction), and confirm full-time — for one current-round human-vs-human fixture. All
/// JWT-protected; the account id comes from the access token, never the body, and the side a change acts
/// on is inferred from the caller's own club. Thin: bind, resolve the user, call
/// <see cref="ILiveMatchService"/>, map the result. The live push channel is the SignalR <c>MatchHub</c>;
/// these REST endpoints stay the source of truth (a client can also just poll the GET).
/// </summary>
public static class LiveMatchEndpoints
{
    public static IEndpointRouteBuilder MapLiveMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/leagues/{id:guid}/live/{fixtureId:guid}").RequireAuthorization();

        // Open (or return) the live session for a fixture — marks the caller present.
        group.MapPost("/open", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.OpenAsync(userId, id, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Join — mark present; both present ⇒ the match kicks off (Live).
        group.MapPost("/join", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.JoinAsync(userId, id, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // The current live-match state (members only).
        group.MapGet("", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.GetAsync(userId, id, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Submit a pause-point change for the caller's own side (sub / instruction change) → re-sim + push.
        group.MapPost("/change", async (
            Guid id, Guid fixtureId, SubmitLiveChangeRequest req, ClaimsPrincipal user,
            ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.SubmitChangeAsync(userId, id, fixtureId, req, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Mark the caller absent (disconnect) — the match keeps running on the accumulated plan.
        group.MapPost("/leave", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.LeaveAsync(userId, id, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        // Confirm full-time — the stored report becomes the fixture's official result at round resolution.
        group.MapPost("/finish", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILiveMatchService live, CancellationToken ct) =>
        {
            if (!LeagueEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.FinishAsync(userId, id, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : LeagueEndpoints.MapError(result.Error, result.Message);
        });

        return app;
    }
}
