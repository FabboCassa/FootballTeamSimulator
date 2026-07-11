using System.Security.Claims;
using Fts.Application.Leagues;

namespace Fts.Api.Leagues;

/// <summary>
/// The private-league HTTP surface (Phase 8.1): create/join/leave a friend league and read the
/// caller's leagues + a league's detail. All JWT-protected — the account id always comes from the
/// access token, never the body, so a caller can only act as themselves. Thin: bind, resolve the
/// user, call <see cref="ILeagueService"/>, map the result to a status code.
/// </summary>
public static class LeagueEndpoints
{
    public static IEndpointRouteBuilder MapLeagueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/leagues").RequireAuthorization();

        // Create a private league (server-generates a fresh world) → the caller is the first member.
        group.MapPost("", async (
            CreateLeagueRequest req, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.CreateAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Join by invite code.
        group.MapPost("/join", async (
            JoinLeagueRequest req, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.JoinAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The caller's leagues.
        group.MapGet("", async (ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            return Results.Ok(await leagues.ListMineAsync(userId, ct));
        });

        // Full detail — members only.
        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.GetAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Start the season snake draft (creator only): equalise squads + budgets, open the pick order.
        group.MapPost("/{id:guid}/draft/start", async (
            Guid id, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.StartDraftAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Claim a club during the draft (only on your turn, only an unclaimed club).
        group.MapPost("/{id:guid}/draft/pick", async (
            Guid id, PickClubRequest req, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.PickClubAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Leave (or disband if last member out).
        group.MapPost("/{id:guid}/leave", async (
            Guid id, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.LeaveAsync(userId, id, ct);
            return result.Success ? Results.NoContent() : MapError(result.Error, result.Message);
        });

        return app;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(id, out userId);
    }

    private static IResult MapError(LeagueError error, string? message) => error switch
    {
        LeagueError.ValidationFailed => Results.BadRequest(new { error = "validation_failed", message }),
        LeagueError.NotFound => Results.NotFound(new { error = "not_found", message }),
        LeagueError.AlreadyMember => Results.Conflict(new { error = "already_member", message }),
        LeagueError.LeagueFull => Results.Conflict(new { error = "league_full", message }),
        LeagueError.NotJoinable => Results.Conflict(new { error = "not_joinable", message }),
        LeagueError.WrongPhase => Results.Conflict(new { error = "wrong_phase", message }),
        LeagueError.NotYourTurn => Results.Conflict(new { error = "not_your_turn", message }),
        LeagueError.ClubUnavailable => Results.Conflict(new { error = "club_unavailable", message }),
        LeagueError.TooFewMembers => Results.BadRequest(new { error = "too_few_members", message }),
        LeagueError.Forbidden => Results.Json(
            new { error = "forbidden", message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "league_error", message }),
    };
}
