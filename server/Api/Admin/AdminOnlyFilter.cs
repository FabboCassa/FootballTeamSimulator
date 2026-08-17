using System.Security.Claims;
using Fts.Infrastructure.Admin;
using Fts.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace Fts.Api.Admin;

/// <summary>
/// The one authorisation check for the whole <c>/admin</c> surface (Phase 10.3): a valid access token whose
/// account is in the <c>admin</c> role.
///
/// Deliberately a DATABASE check rather than a role claim baked into the JWT. A claim would be cheaper, but
/// an access token lives up to 15 minutes, so a claim-based check means a revoked admin keeps their powers
/// for a quarter of an hour and a freshly granted one has to sign out and back in. For a surface that is
/// called a few times a day by a handful of people, one indexed lookup per request is the right trade — and
/// it makes "revoke now" mean now, which is the whole point of having the button.
///
/// It also answers 404, not 403, to a signed-in non-admin. There is no reason to confirm to a curious
/// player that an admin API exists at this path.
/// </summary>
public sealed class AdminOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var principal = http.User;

        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(id, out var userId)) return Results.Unauthorized();

        var users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return Results.Unauthorized();

        // A locked-out admin is not an admin. Cheap, and it closes the window between locking an account
        // and its access token expiring.
        if (await users.IsLockedOutAsync(user)) return Results.NotFound();
        if (!await users.IsInRoleAsync(user, AdminService.AdminRole)) return Results.NotFound();

        http.Items[ActorUserIdKey] = userId;
        return await next(context);
    }

    /// <summary>Where the filter stashes the verified admin's id so each handler can read it without
    /// re-parsing the principal.</summary>
    public const string ActorUserIdKey = "fts.admin.actor";

    /// <summary>The verified admin behind the current request. Only valid inside a filtered endpoint.</summary>
    public static Guid ActorOf(HttpContext http) =>
        http.Items[ActorUserIdKey] is Guid id ? id : Guid.Empty;
}
