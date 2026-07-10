namespace Fts.Application.Notifications;

/// <summary>
/// Manages the device tokens an account can be pushed to (Phase 7.4). A token is globally unique;
/// registering an already-known token re-owns it and refreshes its last-seen timestamp (upsert),
/// so a device that reinstalls or moves accounts is handled cleanly. Implementation in Infrastructure
/// (EF over the <c>device_registrations</c> table).
/// </summary>
public interface IDeviceRegistrationService
{
    /// <summary>Register or refresh a device token for the account. Idempotent per token.</summary>
    Task<DeviceRegistrationDto> RegisterAsync(Guid userId, RegisterDeviceRequest request, CancellationToken ct = default);

    /// <summary>Remove a device token (e.g. on logout / notifications disabled). Returns true if one was removed.</summary>
    Task<bool> UnregisterAsync(Guid userId, string token, CancellationToken ct = default);

    /// <summary>The account's currently registered devices.</summary>
    Task<IReadOnlyList<DeviceRegistrationDto>> ListAsync(Guid userId, CancellationToken ct = default);
}
