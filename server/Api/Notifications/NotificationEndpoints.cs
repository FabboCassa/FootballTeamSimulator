using System.Security.Claims;
using Fts.Application.Notifications;

namespace Fts.Api.Notifications;

/// <summary>
/// The push-notification HTTP surface (Phase 7.4). Device registration is a real, JWT-protected API
/// (a signed-in account manages its own device tokens): register/refresh a token, list tokens,
/// unregister one. The account id always comes from the access token, never the body, so a caller
/// can only touch its own devices. Thin — bind, resolve the user, call the service, map the result.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").RequireAuthorization();

        // Register or refresh the caller's device token (idempotent per token).
        group.MapPost("/devices", async (
            RegisterDeviceRequest req, ClaimsPrincipal user, IDeviceRegistrationService devices, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Token))
                return Results.BadRequest(new { error = "missing_token" });

            var dto = await devices.RegisterAsync(userId, req, ct);
            return Results.Ok(dto);
        });

        // List the caller's registered devices.
        group.MapGet("/devices", async (
            ClaimsPrincipal user, IDeviceRegistrationService devices, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            return Results.Ok(await devices.ListAsync(userId, ct));
        });

        // Unregister one of the caller's device tokens.
        group.MapDelete("/devices/{token}", async (
            string token, ClaimsPrincipal user, IDeviceRegistrationService devices, CancellationToken ct) =>
        {
            if (!TryGetUserId(user, out var userId)) return Results.Unauthorized();
            var removed = await devices.UnregisterAsync(userId, token, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(id, out userId);
    }
}
