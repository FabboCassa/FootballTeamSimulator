namespace Fts.Application.Notifications;

/// <summary>
/// Sends push notifications to an account's registered devices (Phase 7.4). The implementation
/// (Infrastructure) uses Firebase Cloud Messaging when configured, and is a logging no-op otherwise
/// so the online notification flow (auction outbid, match reminders, …) can be wired and exercised
/// without a Firebase project in dev/CI. Never throws for an unconfigured/empty case — it returns a
/// <see cref="PushSendResult"/> describing what happened.
/// </summary>
public interface INotificationService
{
    /// <summary>True when a real FCM sender is configured in this environment (else sends are skipped).</summary>
    bool IsConfigured { get; }

    /// <summary>Push a message to every device registered by the given account. Invalid/expired tokens
    /// reported by FCM are pruned so the device table self-heals.</summary>
    Task<PushSendResult> SendToUserAsync(Guid userId, PushMessage message, CancellationToken ct = default);
}
