using Fts.Application.Notifications;
using Fts.Infrastructure.Persistence;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Notifications;

/// <summary>
/// Firebase Cloud Messaging sender (Phase 7.4). When <see cref="FcmOptions.Enabled"/> and a valid
/// service-account credential are present it pushes to the account's device tokens via the FirebaseAdmin
/// SDK; otherwise it logs and returns <see cref="PushOutcome.Skipped"/> so the whole notification path
/// (device registration → job → send) runs end-to-end in dev/CI without a Firebase project.
///
/// The <see cref="FirebaseApp"/> is process-global, so it is created exactly once (guarded static),
/// while this service itself is scoped so it can read the device table via <see cref="FtsDbContext"/>.
/// Tokens FCM reports as unregistered/invalid are pruned so the table self-heals.
/// </summary>
public sealed class FirebaseNotificationService : INotificationService
{
    private static readonly object InitLock = new();
    private static bool _initAttempted;
    private static bool _initialized;

    private readonly FtsDbContext _db;
    private readonly ILogger<FirebaseNotificationService> _log;
    private readonly FcmOptions _options;

    public FirebaseNotificationService(
        FtsDbContext db,
        IOptions<FcmOptions> options,
        ILogger<FirebaseNotificationService> log)
    {
        _db = db;
        _log = log;
        _options = options.Value;
        EnsureFirebaseInitialized(_options, _log);
    }

    public bool IsConfigured => _initialized;

    public async Task<PushSendResult> SendToUserAsync(
        Guid userId, PushMessage message, CancellationToken ct = default)
    {
        var tokens = await _db.DeviceRegistrations
            .Where(d => d.UserId == userId)
            .Select(d => d.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0)
            return PushSendResult.NoDevices();

        if (!_initialized)
        {
            // Dev/CI path: no Firebase configured — log what WOULD have been sent and skip.
            _log.LogInformation(
                "FCM disabled — skipping push to user {UserId} ({DeviceCount} device(s)): {Title}",
                userId, tokens.Count, message.Title);
            return PushSendResult.Skipped($"FCM not configured; {tokens.Count} device(s) would be targeted.");
        }

        var multicast = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = message.Title, Body = message.Body },
            Data = message.Data is null ? null : new Dictionary<string, string>(message.Data)
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(multicast, ct);
            await PruneInvalidTokensAsync(tokens, response, ct);

            _log.LogInformation(
                "FCM push to user {UserId}: {Success} ok, {Failure} failed",
                userId, response.SuccessCount, response.FailureCount);
            return PushSendResult.Sent(response.SuccessCount, response.FailureCount);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "FCM push to user {UserId} failed.", userId);
            return PushSendResult.Failed(ex.Message);
        }
    }

    /// <summary>Remove tokens FCM rejected as unregistered/invalid so we stop targeting dead devices.</summary>
    private async Task PruneInvalidTokensAsync(
        List<string> tokens, BatchResponse response, CancellationToken ct)
    {
        var dead = new List<string>();
        for (var i = 0; i < response.Responses.Count && i < tokens.Count; i++)
        {
            var r = response.Responses[i];
            if (r.IsSuccess) continue;

            var code = r.Exception?.MessagingErrorCode;
            if (code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                dead.Add(tokens[i]);
        }

        if (dead.Count == 0) return;

        await _db.DeviceRegistrations
            .Where(d => dead.Contains(d.Token))
            .ExecuteDeleteAsync(ct);
        _log.LogInformation("Pruned {Count} invalid FCM token(s).", dead.Count);
    }

    /// <summary>Create the process-global FirebaseApp once, from config. Failures are logged and leave
    /// the service in the disabled (skip) state rather than crashing startup.</summary>
    private static void EnsureFirebaseInitialized(FcmOptions options, ILogger log)
    {
        if (_initAttempted) return;
        lock (InitLock)
        {
            if (_initAttempted) return;
            _initAttempted = true;

            if (!options.Enabled)
            {
                log.LogInformation("FCM disabled (Fcm:Enabled=false) — pushes will be skipped.");
                return;
            }

            try
            {
                GoogleCredential credential;
                if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
                    credential = GoogleCredential.FromFile(options.CredentialsPath);
                else if (!string.IsNullOrWhiteSpace(options.CredentialsJson))
                    credential = GoogleCredential.FromJson(options.CredentialsJson);
                else
                {
                    log.LogWarning("Fcm:Enabled=true but no credentials provided — pushes will be skipped.");
                    return;
                }

                if (FirebaseApp.DefaultInstance is null)
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = credential,
                        ProjectId = options.ProjectId
                    });
                }

                _initialized = true;
                log.LogInformation("FCM initialized (project {ProjectId}).", options.ProjectId ?? "default");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "FCM initialization failed — pushes will be skipped.");
            }
        }
    }
}
