using System.Security.Claims;
using Fts.Api.Integrity;
using Fts.Application.Ranked;

namespace Fts.Api.Ranked;

/// <summary>
/// The LIVE RANKED MATCH surface (task 12.3): open (or rejoin) your own matchday fixture, read its streamed
/// state, send a pause-point change for your side, step away, and confirm full-time. All JWT-protected — the
/// account id comes from the access token, never the body, and the side a change acts on is inferred from the
/// caller's own seat, so a coach can only ever control his own team.
///
/// Thin by design, exactly like the 8.6 private-league endpoints: bind, resolve the user, call
/// <see cref="IRankedLiveMatchService"/>, map the result. The live push channel is the SignalR
/// <c>MatchHub</c>; these REST endpoints stay the source of truth, so a client that cannot hold a socket open
/// can simply poll the GET and lose nothing but a second of latency.
///
/// Note the route shape: a ranked fixture is addressed by its own id alone. There is no lobby to scope it to
/// — a coach has exactly one seat and therefore exactly one group — and the service resolves the group from
/// the fixture and checks the caller sits in it.
/// </summary>
public static class RankedLiveEndpoints
{
    public static IEndpointRouteBuilder MapRankedLiveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ranked/live/{fixtureId:guid}").RequireAuthorization();

        // Open (or rejoin) the live session — marks the caller present. Refused outside the window around
        // kickoff, for a fixture that is not the caller's, and once the matchday has been resolved.
        group.MapPost("/open", async (
            Guid fixtureId, ClaimsPrincipal user, IRankedLiveMatchService live, CancellationToken ct) =>
        {
            if (!RankedEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.OpenAsync(userId, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : RankedEndpoints.MapErrorShared(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // The current live state. Any coach in the group may watch; only the two sides may change.
        group.MapGet("", async (
            Guid fixtureId, ClaimsPrincipal user, IRankedLiveMatchService live, CancellationToken ct) =>
        {
            if (!RankedEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.GetAsync(userId, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : RankedEndpoints.MapErrorShared(result.Error, result.Message);
        });

        // A pause-point change for the caller's own side (substitution and/or instructions) → re-sim + push.
        // NOT rate-limited as a "write": a live match is a burst of legitimate taps in three real minutes,
        // and the guards that matter here are the match's own (your side, forward in time, a legal XI).
        group.MapPost("/change", async (
            Guid fixtureId, SubmitRankedLiveChangeRequest req, ClaimsPrincipal user,
            IRankedLiveMatchService live, CancellationToken ct) =>
        {
            if (!RankedEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.SubmitChangeAsync(userId, fixtureId, req, ct);
            return result.Success ? Results.Ok(result.Value) : RankedEndpoints.MapErrorShared(result.Error, result.Message);
        });

        // Step away — the match plays on from the accumulated plan, on your stored orders.
        group.MapPost("/leave", async (
            Guid fixtureId, ClaimsPrincipal user, IRankedLiveMatchService live, CancellationToken ct) =>
        {
            if (!RankedEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.LeaveAsync(userId, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : RankedEndpoints.MapErrorShared(result.Error, result.Message);
        });

        // Confirm full-time: the stored report is what the matchday consumes, and the calendar stops waiting.
        group.MapPost("/finish", async (
            Guid fixtureId, ClaimsPrincipal user, IRankedLiveMatchService live, CancellationToken ct) =>
        {
            if (!RankedEndpoints.TryGetUserIdShared(user, out var userId)) return Results.Unauthorized();
            var result = await live.FinishAsync(userId, fixtureId, ct);
            return result.Success ? Results.Ok(result.Value) : RankedEndpoints.MapErrorShared(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        return app;
    }
}
