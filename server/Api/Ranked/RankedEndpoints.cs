using System.Security.Claims;
using Fts.Api.Integrity;
using Fts.Application.Integrity;
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
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

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
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

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
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // The caller's currently submitted lineup (the stored LineupPlan JSON, or empty when none) — so the
        // client's lineup editor re-opens on the saved XI instead of the best-XI default.
        group.MapGet("/lineup", async (ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetMyLineupAsync(userId, ct);
            if (!result.Success) return MapError(result.Error, result.Message);
            return string.IsNullOrEmpty(result.Value)
                ? Results.Content("{}", "application/json")
                : Results.Content(result.Value!, "application/json");
        });

        // Submit (or replace) the training plan the caller's ranked club develops on (Phase 9.4).
        group.MapPost("/training", async (
            SubmitRankedTrainingRequest req, ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.SubmitTrainingAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // The caller's stored training plan (the serialized TrainingPlan JSON, or {} when none) — so the
        // client's training screen opens on the saved plan instead of the seeded default.
        group.MapGet("/training", async (ClaimsPrincipal user, IRankedSeasonService season, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await season.GetMyTrainingAsync(userId, ct);
            if (!result.Success) return MapError(result.Error, result.Message);
            return string.IsNullOrEmpty(result.Value)
                ? Results.Content("{}", "application/json")
                : Results.Content(result.Value!, "application/json");
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

        // --- Direct coach-to-coach market (Phase 9.2b) --------------------------------------

        // Browse a club's squad in the caller's group (to decide who to bid on).
        group.MapGet("/clubs/{clubExternalId:int}/squad", async (
            int clubExternalId, ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.GetClubSquadAsync(userId, clubExternalId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The caller's market view: budget + incoming/outgoing offers + whether a window is open.
        group.MapGet("/offers", async (ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.GetOffersAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Offer a fee for a player on another coach's club (requires an open window + enough budget).
        group.MapPost("/offers", async (
            MakeRankedOfferRequest req, ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.MakeOfferAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // Accept / reject an incoming offer (only the player's owner). Accepting moves the player + settles budgets.
        group.MapPost("/offers/{offerId:guid}/accept", async (
            Guid offerId, ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.RespondAsync(userId, offerId, accept: true, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        group.MapPost("/offers/{offerId:guid}/reject", async (
            Guid offerId, ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.RespondAsync(userId, offerId, accept: false, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // Withdraw an offer the caller made (while still Pending).
        group.MapPost("/offers/{offerId:guid}/withdraw", async (
            Guid offerId, ClaimsPrincipal user, IRankedMarketService market, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await market.WithdrawAsync(userId, offerId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // --- Free-agent auctions (Phase 9.2b) -----------------------------------------------

        // The caller's open auction lots + budget picture + window state.
        group.MapGet("/auctions", async (ClaimsPrincipal user, IRankedAuctionService auctions, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.GetAuctionsAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // Place an ascending bid on a lot (requires an open window + available budget).
        group.MapPost("/auctions/{auctionId:guid}/bid", async (
            Guid auctionId, PlaceRankedBidRequest req, ClaimsPrincipal user,
            IRankedAuctionService auctions, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.PlaceBidAsync(userId, auctionId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Bids);

        // --- Selling your own players by auction (task 12.2) --------------------------------

        // Put one of your own players up as a lot, with a timer you choose (1h-24h, clamped to the market
        // window's close). Refuses a duration outside the range, a player who is not yours or already on the
        // board, a reserve outside the 9.5 integrity band, and a listing that would take your squad below
        // the floor (the lots you already have out are counted).
        group.MapPost("/auctions/list", async (
            ListRankedLotRequest req, ClaimsPrincipal user,
            IRankedAuctionService auctions, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.ListLotAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // Pull one of your own lots off the board — only while nobody has bid on it.
        group.MapPost("/auctions/{auctionId:guid}/unlist", async (
            Guid auctionId, ClaimsPrincipal user, IRankedAuctionService auctions, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await auctions.UnlistLotAsync(userId, auctionId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // --- Daily digest (Phase 9.4) -------------------------------------------------------

        // "What do I need to do today?" — the whole daily loop in one call (state, next match, last result,
        // table position, market window + pending offers/lots, whether the inputs are ready) plus a short
        // prioritised to-do list. Never 404s for a signed-in account: a coach who never joined gets the
        // "enrol" digest.
        group.MapGet("/today", async (ClaimsPrincipal user, IRankedTodayService today, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await today.GetTodayAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // One-tap "my day is handled": seed/repair the stored inputs and mark the upcoming matchday as
        // confirmed. Idempotent — confirming twice changes nothing. Returns the refreshed digest.
        group.MapPost("/today/confirm", async (ClaimsPrincipal user, IRankedTodayService today, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await today.ConfirmMatchdayAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Writes);

        // --- Coach ranking & palmarès (Phase 9.3) -------------------------------------------

        // The global ladder: the top coaches by rating, plus the caller's own row when outside the slice.
        group.MapGet("/leaderboard", async (
            int? top, ClaimsPrincipal user, IRankedRankingService ranking, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranking.GetLeaderboardAsync(userId, top ?? 0, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // The caller's persistent record: rating, career best, seasons played and the full award history.
        group.MapGet("/palmares", async (ClaimsPrincipal user, IRankedRankingService ranking, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await ranking.GetPalmaresAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // --- Abuse & integrity (Phase 9.5) --------------------------------------------------

        // Report another coach in your group (collusion, an abandoned club, an offensive name…). The
        // response is deliberately thin — a reporter learns nothing about the outcome, so the endpoint
        // cannot be used to probe other accounts. Capped per window here and per day in the service.
        group.MapPost("/report", async (
            SubmitRankedReportRequest req, ClaimsPrincipal user, IIntegrityService integrity, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var result = await integrity.ReportAsync(userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        }).RequireRateLimiting(IntegrityRateLimits.Reports);

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

        // Time-travel the ladder forward N matchdays (Phase 9.3 dev-sim tooling): every running season's
        // clock — and any between-seasons break — is pulled back one interval per step and ticked, so a solo
        // tester can watch a whole season, the seasonal reset and the season after it in seconds instead of
        // the real ~2 weeks. `matchdays` is a query param so a bodyless POST binds cleanly.
        group.MapPost("/fast-forward", async (
            int? matchdays, IRankedSeasonService season, CancellationToken ct) =>
            Results.Ok(await season.FastForwardAsync(matchdays ?? 1, ct)));

        // TASK 12.3 dev-sim tooling: pull the running ladder's next matchday forward to `seconds` from now
        // (default 30) so a solo tester can attend a live match at 15:40 instead of waiting for 21:00. Every
        // remaining kickoff, the season start and the open lots shift together, so the calendar stays
        // consistent and this is time travel rather than a special code path. `seconds` is a query param so a
        // bodyless POST binds cleanly.
        group.MapPost("/kickoff-now", async (
            int? seconds, IRankedSeasonService season, CancellationToken ct) =>
            Results.Ok(new { shifted = await season.BringKickoffForwardAsync(seconds ?? 30, ct) }));

        // Force-settle every open free-agent auction lot now, regardless of its timer (Phase 9.2b) — the
        // dev/staging fast-forward for auctions (the season tick settles them at the window close normally).
        group.MapPost("/auctions/settle", async (IRankedAuctionService auctions, CancellationToken ct) =>
            Results.Ok(new { settled = await auctions.SettleDueAsync(force: true, ct) }));

        // The abuse & integrity review queue (Phase 9.5). Read-only, dev-gated: the real moderation
        // surface is the 10.3 admin dashboard, but the flags have to be inspectable NOW — both for the
        // scripted abuse smoke and so a live incident can be looked at without opening a psql session.
        group.MapGet("/integrity/flags", async (
            int? status, int? take, IIntegrityService integrity, CancellationToken ct) =>
        {
            IntegrityFlagStatus? filter = status is { } s && Enum.IsDefined(typeof(IntegrityFlagStatus), s)
                ? (IntegrityFlagStatus)s
                : null;
            return Results.Ok(await integrity.GetFlagsAsync(filter, take ?? 0, ct));
        });

        return app;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(id, out userId);
    }

    /// <summary>The same token → account id resolution, for the sibling ranked endpoint files (task 12.3's
    /// live matches). Mirrors <c>LeagueEndpoints.TryGetUserIdShared</c>: one implementation, so a second
    /// ranked surface cannot end up trusting a different claim.</summary>
    internal static bool TryGetUserIdShared(ClaimsPrincipal user, out Guid userId) => TryGetUserId(user, out userId);

    /// <summary>The same RankedError → status-code mapping, for the sibling ranked endpoint files (task
    /// 12.3). Keeping one map is what stops the live endpoints answering 400 where the rest of the ladder
    /// answers 409 for the same refusal.</summary>
    internal static IResult MapErrorShared(RankedError error, string? message) => MapError(error, message);

    private static IResult MapError(RankedError error, string? message) => error switch
    {
        RankedError.ValidationFailed => Results.BadRequest(new { error = "validation_failed", message }),
        RankedError.NotFound => Results.NotFound(new { error = "not_found", message }),
        RankedError.NotEnrolled => Results.NotFound(new { error = "not_enrolled", message }),
        RankedError.FixtureNotFound => Results.NotFound(new { error = "fixture_not_found", message }),
        RankedError.WrongPhase => Results.Conflict(new { error = "wrong_phase", message }),
        RankedError.ReplayNotReady => Results.Conflict(new { error = "replay_not_ready", message }),
        RankedError.NoCapacity => Results.Conflict(new { error = "no_capacity", message }),
        RankedError.InsufficientBudget => Results.BadRequest(new { error = "insufficient_budget", message }),
        RankedError.AuctionNotFound => Results.NotFound(new { error = "auction_not_found", message }),
        RankedError.AuctionClosed => Results.Conflict(new { error = "auction_closed", message }),
        RankedError.BidTooLow => Results.BadRequest(new { error = "bid_too_low", message }),
        // Integrity (Phase 9.5): a refused deal is a CONFLICT with the ladder's rules, not a malformed
        // request — the client shows the message as-is so the coach knows which band they broke.
        RankedError.IntegrityBlocked => Results.Conflict(new { error = "integrity_blocked", message }),
        RankedError.DeadlinePassed => Results.Conflict(new { error = "deadline_passed", message }),
        // Task 12.2 seller lots: a bad duration is a malformed request; the other two are conflicts with
        // the board's current state, which is what the client turns into a readable sentence.
        RankedError.LotDurationInvalid => Results.BadRequest(new { error = "lot_duration_invalid", message }),
        RankedError.SquadTooSmall => Results.Conflict(new { error = "squad_too_small", message }),
        RankedError.PlayerUnavailable => Results.Conflict(new { error = "player_unavailable", message }),
        // Task 12.3 live matches: "not found" for a session that is not there, CONFLICT for the two that are
        // about state rather than about the request — the door being shut and the match being over are both
        // things the client turns into a readable sentence, not things the coach typed wrong. A malformed
        // pause-point change is the one genuine bad request of the set.
        RankedError.LiveNotFound => Results.NotFound(new { error = "live_not_found", message }),
        RankedError.LiveNotOpen => Results.Conflict(new { error = "live_not_open", message }),
        RankedError.LiveAlreadyFinished => Results.Conflict(new { error = "live_already_finished", message }),
        RankedError.InvalidLiveChange => Results.BadRequest(new { error = "invalid_live_change", message }),
        RankedError.NotYourMatch => Results.Json(
            new { error = "not_your_match", message }, statusCode: StatusCodes.Status403Forbidden),
        RankedError.RateLimited => Results.Json(
            new { error = "rate_limited", message }, statusCode: StatusCodes.Status429TooManyRequests),
        RankedError.Forbidden => Results.Json(
            new { error = "forbidden", message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "ranked_error", message }),
    };
}
