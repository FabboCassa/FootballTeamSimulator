using System.Security.Claims;
using Fts.Application.Auth;

namespace Fts.Api.Auth;

/// <summary>
/// The auth HTTP surface (Phase 7.2): register/login/refresh/logout + a protected /auth/me.
/// Thin — each endpoint just binds the request, calls <see cref="IAuthService"/>, and maps the
/// result to a status code. Login and refresh return a generic 401 so they never reveal which
/// part of the credentials/token was wrong.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/register", async (RegisterRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(req, ct);
            return result.Success
                ? Results.Ok(result.Value)
                : MapError(result.Error, result.Message);
        });

        group.MapPost("/login", async (LoginRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(req, ct);
            return result.Success
                ? Results.Ok(result.Value)
                : MapError(result.Error, result.Message);
        });

        group.MapPost("/refresh", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(req, ct);
            return result.Success
                ? Results.Ok(result.Value)
                : MapError(result.Error, result.Message);
        });

        group.MapPost("/logout", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(req, ct);
            return Results.NoContent();
        });

        // Protected: the caller must present a valid access token; we read the account id from it.
        group.MapGet("/me", async (ClaimsPrincipal user, IAuthService auth, CancellationToken ct) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(id, out var userId))
                return Results.Unauthorized();

            var profile = await auth.GetProfileAsync(userId, ct);
            return profile is null ? Results.Unauthorized() : Results.Ok(profile);
        }).RequireAuthorization();

        // Protected + password-confirmed: delete this account for good (Phase 10.2a). Both mobile stores
        // require an in-app route; the public deletion web page logs in first and then calls this same
        // endpoint, so there is only ever one deletion path.
        group.MapPost("/account/delete", async (
            DeleteAccountRequest req, ClaimsPrincipal user, IAccountDeletionService deletion, CancellationToken ct) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(id, out var userId))
                return Results.Unauthorized();

            var result = await deletion.DeleteAsync(userId, req, ct);
            return result.Success
                ? Results.Ok(result.Value)
                : MapError(result.Error, result.Message);
        }).RequireAuthorization();

        return app;
    }

    private static IResult MapError(AuthError error, string? message) => error switch
    {
        AuthError.EmailAlreadyInUse => Results.Conflict(new { error = "email_in_use", message }),
        AuthError.WeakPassword => Results.BadRequest(new { error = "weak_password", message }),
        AuthError.ValidationFailed => Results.BadRequest(new { error = "validation_failed", message }),
        AuthError.InvalidCredentials => Results.Json(
            new { error = "invalid_credentials", message }, statusCode: StatusCodes.Status401Unauthorized),
        AuthError.InvalidRefreshToken => Results.Json(
            new { error = "invalid_refresh_token", message }, statusCode: StatusCodes.Status401Unauthorized),
        // 403, not 401 (Phase 10.3): the credentials WERE right, the account is not allowed in. A 401 would
        // send the client into its refresh-then-retry loop, which for a suspended account is a request
        // storm that can never succeed.
        AuthError.AccountLocked => Results.Json(
            new { error = "account_locked", message }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "auth_error", message }),
    };
}
