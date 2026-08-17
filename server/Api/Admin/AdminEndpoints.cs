using Fts.Application.Admin;

namespace Fts.Api.Admin;

/// <summary>
/// The live-ops HTTP surface (Phase 10.3): <c>/admin/*</c>, every route behind a valid access token AND
/// the <c>admin</c> role (<see cref="AdminOnlyFilter"/>). Thin, like every other endpoint file here — bind,
/// call <see cref="IAdminService"/>, map the result to a status code.
///
/// This is the surface the static dashboard at <c>web/admin.html</c> talks to, and the same one
/// <c>tools/watchdog.ps1</c> polls for <c>/admin/metrics</c>. Keeping them on one API rather than giving
/// the dashboard a private back door means the alerting script and the human are looking at literally the
/// same numbers.
///
/// Unlike the dev/internal endpoints, these ARE mapped in Production — an admin API that only exists
/// outside production is not live ops. The role check is the gate, not the environment.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // An INSTANCE, not the generic AddEndpointFilter<T>() overload: on a route GROUP that form needs
        // both type arguments spelled out, and the filter is stateless (it resolves what it needs from
        // HttpContext.RequestServices), so one shared instance is the simpler correct thing.
        var group = app.MapGroup("/admin")
            .RequireAuthorization()
            .AddEndpointFilter(new AdminOnlyFilter());

        // --- monitoring ---
        group.MapGet("/metrics", async (IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.GetMetricsAsync(ct)));

        // --- worlds ---
        group.MapGet("/worlds", async (IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.GetWorldsAsync(ct)));

        group.MapPost("/worlds/{worldId:guid}/open", async (
            Guid worldId, SetWorldOpenRequest req, HttpContext http, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.SetWorldOpenAsync(AdminOnlyFilter.ActorOf(http), worldId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // --- users ---
        group.MapGet("/users", async (
            string? q, int? limit, IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.SearchUsersAsync(q, limit ?? 50, ct)));

        group.MapGet("/users/{userId:guid}", async (
            Guid userId, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.GetUserAsync(userId, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        group.MapPost("/users/{userId:guid}/lock", async (
            Guid userId, SetUserLockRequest req, HttpContext http, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.SetUserLockAsync(AdminOnlyFilter.ActorOf(http), userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        group.MapPost("/users/{userId:guid}/admin", async (
            Guid userId, SetUserAdminRequest req, HttpContext http, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.SetUserAdminAsync(AdminOnlyFilter.ActorOf(http), userId, req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // --- balance ---
        group.MapGet("/balance", async (IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.GetBalanceAsync(ct)));

        group.MapGet("/balance/history", async (int? limit, IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.GetBalanceHistoryAsync(limit ?? 50, ct)));

        group.MapPost("/balance", async (
            PushBalanceRequest req, HttpContext http, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.PushBalanceAsync(AdminOnlyFilter.ActorOf(http), req, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        group.MapPost("/balance/rollback/{revision:int}", async (
            int revision, HttpContext http, IAdminService admin, CancellationToken ct) =>
        {
            var result = await admin.RollbackBalanceAsync(AdminOnlyFilter.ActorOf(http), revision, ct);
            return result.Success ? Results.Ok(result.Value) : MapError(result.Error, result.Message);
        });

        // --- audit ---
        group.MapGet("/audit", async (int? limit, IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.GetAuditAsync(limit ?? 100, ct)));

        return app;
    }

    private static IResult MapError(AdminError error, string? message) => error switch
    {
        AdminError.NotFound => Results.NotFound(new { error = "not_found", message }),
        AdminError.ValidationFailed => Results.BadRequest(new { error = "validation_failed", message }),
        AdminError.Conflict => Results.Conflict(new { error = "conflict", message }),
        _ => Results.BadRequest(new { error = "admin_error", message }),
    };
}
