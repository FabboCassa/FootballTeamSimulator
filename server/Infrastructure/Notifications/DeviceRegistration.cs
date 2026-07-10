using Fts.Application.Notifications;
using Fts.Infrastructure.Auth;

namespace Fts.Infrastructure.Notifications;

/// <summary>
/// A push-capable device belonging to an account (Phase 7.4, ARCHITECTURE §6.3 "notifications"
/// device side). Stores the FCM registration token so the server can target the user's phones/
/// browsers. The token is globally unique (a physical device has one per app install); re-registering
/// the same token re-owns it and bumps <see cref="LastSeenUtc"/>. Deleted with the account (cascade).
/// </summary>
public sealed class DeviceRegistration
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>The FCM registration token — the send target. Unique across all accounts.</summary>
    public string Token { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}
