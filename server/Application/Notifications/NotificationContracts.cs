namespace Fts.Application.Notifications;

/// <summary>
/// Request/response DTOs for the push-notification use cases (Phase 7.4). Plain records so the Api
/// layer binds them from JSON and the client mirrors them. The services (<see cref="INotificationService"/>,
/// <see cref="IDeviceRegistrationService"/>) live here; their implementations are in Infrastructure
/// (they need EF for the device table and the FirebaseAdmin SDK for the actual send).
/// </summary>

/// <summary>Which client platform a device token belongs to. FCM handles Android + web push;
/// iOS goes through FCM→APNs. Stable numeric values so they round-trip through the DB and the wire.</summary>
public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2
}

/// <summary>A client registering (or refreshing) its FCM device token so the server can push to it.</summary>
public sealed record RegisterDeviceRequest(string Token, DevicePlatform Platform);

/// <summary>A device token the server can push to, surfaced back to the owning account.</summary>
public sealed record DeviceRegistrationDto(
    Guid Id,
    DevicePlatform Platform,
    DateTime CreatedUtc,
    DateTime LastSeenUtc);

/// <summary>A push payload: a visible title/body plus optional data key/values the client app reads
/// (e.g. a deep-link target). Kept transport-agnostic so a future APNs/web-push path can reuse it.</summary>
public sealed record PushMessage(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>Why a send did what it did — the caller (or a job) can log/telemetry on it without
/// throwing. <see cref="Skipped"/> is the normal outcome when FCM is not configured in this
/// environment (dev/tests), so the pipeline still runs end-to-end without a Firebase account.</summary>
public enum PushOutcome
{
    Sent = 0,
    NoDevices,
    Skipped,
    Failed
}

/// <summary>Outcome of pushing a message to an account's devices.</summary>
public sealed record PushSendResult(PushOutcome Outcome, int SuccessCount, int FailureCount, string? Message = null)
{
    public static PushSendResult Sent(int success, int failure) =>
        new(PushOutcome.Sent, success, failure);
    public static PushSendResult NoDevices() => new(PushOutcome.NoDevices, 0, 0);
    public static PushSendResult Skipped(string? message = null) => new(PushOutcome.Skipped, 0, 0, message);
    public static PushSendResult Failed(string? message) => new(PushOutcome.Failed, 0, 0, message);
}
