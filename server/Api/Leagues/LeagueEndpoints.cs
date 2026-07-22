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

        // --- Season (Phase 8.3) --------------------------------------------------------------

        // Submit (or replace) the caller's match inputs for their club (reused each matchday).
        group.MapPost("/{id:guid}/lineup", async (
            Guid id, SubmitLineupRequest req, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.SubmitLineupAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Submit (or replace) the caller's training plan for their club (Phase 8.4) — reused each week.
        group.MapPost("/{id:guid}/training", async (
            Guid id, SubmitTrainingRequest req, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.SubmitTrainingAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Mark ready / not ready — when everyone is ready the next round resolves automatically.
        group.MapPost("/{id:guid}/ready", async (
            Guid id, SetReadyRequest req, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.SetReadyAsync(userId, id, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Force the next round to resolve now (creator only) — AI/last-lineup fallback for anyone idle.
        group.MapPost("/{id:guid}/advance", async (
            Guid id, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.AdvanceAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The season view: state + fixtures + standings (members only).
        group.MapGet("/{id:guid}/season", async (
            Guid id, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetSeasonAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The canonical whole-world state hash (condition + attributes) — client/server agreement (8.4 ✅).
        group.MapGet("/{id:guid}/state-hash", async (
            Guid id, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetStateHashAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The full stored MatchReport for a played fixture (identical for every member) — replay download.
        group.MapGet("/{id:guid}/fixtures/{fixtureId:guid}/replay", async (
            Guid id, Guid fixtureId, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetReplayAsync(userId, id, fixtureId, ct);
            return result.Success
                ? Results.Content(result.Value!, "application/json")
                : MapError(result.Error, result.Message);
        });

        // --- Season end (Phase 8.7) ----------------------------------------------------------

        // The end-of-season summary: final table + awards (champion / best defence / wooden spoon / top
        // scorer). Members only; provisional until the season is complete.
        group.MapGet("/{id:guid}/season/summary", async (
            Guid id, ClaimsPrincipal user, ILeagueSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetSeasonSummaryAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Start a fresh season after the current one has finished (creator only) — full reset → new draft.
        group.MapPost("/{id:guid}/season/new", async (
            Guid id, ClaimsPrincipal user, ILeagueService leagues, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await leagues.StartNewSeasonAsync(userId, id, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        return app;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(id, out userId);
    }

    // Internal so the auction endpoints (8.5) share the same league-error → status mapping.
    internal static IResult MapError(LeagueError error, string? message) => error switch
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
        LeagueError.NotAssignedClub => Results.Conflict(new { error = "not_assigned_club", message }),
        LeagueError.NothingToResolve => Results.Conflict(new { error = "nothing_to_resolve", message }),
        LeagueError.FixtureNotFound => Results.NotFound(new { error = "fixture_not_found", message }),
        LeagueError.ReplayNotReady => Results.Conflict(new { error = "replay_not_ready", message }),
        // Online auctions (8.5).
        LeagueError.AuctionNotFound => Results.NotFound(new { error = "auction_not_found", message }),
        LeagueError.AuctionClosed => Results.Conflict(new { error = "auction_closed", message }),
        LeagueError.BidTooLow => Results.BadRequest(new { error = "bid_too_low", message }),
        LeagueError.InsufficientBudget => Results.BadRequest(new { error = "insufficient_budget", message }),
        LeagueError.WindowAlreadyOpen => Results.Conflict(new { error = "window_already_open", message }),
        LeagueError.NoAuctionsOpen => Results.Conflict(new { error = "no_auctions_open", message }),
        // Live match control (8.6).
        LeagueError.LiveMatchNotFound => Results.NotFound(new { error = "live_match_not_found", message }),
        LeagueError.LiveMatchNotJoinable => Results.Conflict(new { error = "live_match_not_joinable", message }),
        LeagueError.NotYourSide => Results.Json(
            new { error = "not_your_side", message }, statusCode: StatusCodes.Status403Forbidden),
        LeagueError.LiveMatchNotLive => Results.Conflict(new { error = "live_match_not_live", message }),
        LeagueError.LiveMatchAlreadyFinished => Results.Conflict(new { error = "live_match_already_finished", message }),
        LeagueError.InvalidLiveChange => Results.BadRequest(new { error = "invalid_live_change", message }),
        LeagueError.Forbidden => Results.Json(
            new { error = "forbidden", message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "league_error", message }),
    };

    // Internal so the auction endpoints (8.5) share the same token → user-id extraction.
    internal static bool TryGetUserIdShared(ClaimsPrincipal user, out Guid userId) => TryGetUserId(user, out userId);
}
