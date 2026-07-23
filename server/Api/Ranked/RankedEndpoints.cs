using System.Security.Claims;
using Fts.Application.Ranked;

namespace Fts.Api.Ranked;

/// <summary>
/// The public ranked ladder HTTP surface (Phase 9.1): join the ladder, read your own state or a group's
/// fixed-size seat list, and toggle auto re-enrolment. All JWT-protected — the account id always comes
/// from the access token, never the body. Thin: bind, resolve the user, call <see cref="IRankedService"/>,
/// map the result to a status code.
///
/// Closing a placement season lives on a separate, dev-gated internal endpoint
/// (<see cref="MapRankedInternalEndpoints"/>) until the real-time season scheduler drives it in 9.2 —
/// it is a server-side lifecycle action, not something a player may trigger.
/// </summary>
public static class RankedEndpoints
{
    public static IEndpointRouteBuilder MapRankedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ranked").RequireAuthorization();

        // Join the ladder → a placement seat (idempotent: returns the current state if already enrolled).
        group.MapPost("/enrol", async (ClaimsPrincipal user, IRankedService ranked, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranked.EnrolAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The caller's ladder state (Enrolled=false when they never joined).
        group.MapGet("/me", async (ClaimsPrincipal user, IRankedService ranked, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranked.GetMineAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // A group with every seat (occupied or AI) — the fixed-size view.
        group.MapGet("/groups/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IRankedService ranked, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranked.GetGroupAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Opt in/out of automatic re-enrolment next season (ranking is kept either way).
        group.MapPost("/auto-enrol", async (
            SetAutoEnrolRequest req, ClaimsPrincipal user, IRankedService ranked, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranked.SetAutoEnrolAsync(userId, req.AutoEnrol, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // --- Real-time season (Phase 9.2) ---------------------------------------------------

        // The caller's current ranked season: schedule + standings + state (InSeason=false before kickoff).
        group.MapGet("/season", async (ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetMySeasonAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Submit (or replace) the caller's lineup/tactic/plan for their ranked club — reused each matchday.
        group.MapPost("/lineup", async (
            SubmitRankedLineupRequest req, ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.SubmitLineupAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The stored full MatchReport for a played fixture in the caller's group (identical bytes for all).
        group.MapGet("/season/fixtures/{fixtureId:guid}/replay", async (
            Guid fixtureId, ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetReplayAsync(userId, fixtureId, ct);
            return result.Success
                ? Results.Content(result.Value!, "application/json")
                : MapError(result.Error, result.Message);
        });

        return app;
    }

    /// <summary>Server-side ladder lifecycle, mapped by Program.cs only outside Production and behind the
    /// <c>Ranked:ExposeInternalEndpoints</c> flag (same pattern as the internal sim endpoints). The 9.2
    /// season scheduler will call <see cref="IRankedService.ResolvePlacementAsync"/> directly instead.</summary>
    public static IEndpointRouteBuilder MapRankedInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/ranked");

        // Close a placement season and sort its coaches into divisions.
        group.MapPost("/placement/{groupId:guid}/resolve", async (
            Guid groupId, ResolvePlacementRequest? req, IRankedService ranked, CancellationToken ct) =>
        {
            var result = await ranked.ResolvePlacementAsync(groupId, req?.FinalOrder, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Advance the ranked real-time calendar once by hand (Phase 9.2) — starts due seasons, resolves
        // due matchdays, opens windows, closes finished seasons. The recurring Hangfire job does this
        // automatically; this is for a manual smoke test / staging fast-forward.
        group.MapPost("/tick", async (IRankedSeasonService season, CancellationToken ct) =>
            Results.Ok(await season.TickAsync(ct)));

        return app;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(id, out userId);
    }

    private static IResult MapError(RankedError error, string? message) => error switch
    {
        RankedError.ValidationFailed => Results.BadRequest(new { error = "validation_failed", message }),
        RankedError.NotFound => Results.NotFound(new { error = "not_found", message }),
        RankedError.NotEnrolled => Results.NotFound(new { error = "not_enrolled", message }),
        RankedError.FixtureNotFound => Results.NotFound(new { error = "fixture_not_found", message }),
        RankedError.WrongPhase => Results.Conflict(new { error = "wrong_phase", message }),
        RankedError.ReplayNotReady => Results.Conflict(new { error = "replay_not_ready", message }),
        RankedError.NoCapacity => Results.Conflict(new { error = "no_capacity", message }),
        RankedError.Forbidden => Results.Json(
            new { error = "forbidden", message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "ranked_error", message }),
    };
}
