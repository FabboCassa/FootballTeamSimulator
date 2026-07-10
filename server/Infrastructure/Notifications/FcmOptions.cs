namespace Fts.Infrastructure.Notifications;

/// <summary>
/// Firebase Cloud Messaging configuration (Phase 7.4), bound from the "Fcm" config section. Left
/// disabled/empty by default so the server builds and runs without a Firebase project: the
/// notification service then logs and skips instead of sending. To go live, drop a service-account
/// JSON key on the box (or inline it) and set <see cref="Enabled"/> true.
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Master switch. When false (default) pushes are skipped+logged — no Firebase needed.</summary>
    public bool Enabled { get; set; }

    /// <summary>Firebase project id (informational / for logging; the credential file carries its own).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Path to the service-account JSON key file. Takes precedence over <see cref="CredentialsJson"/>.</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>Inline service-account JSON (e.g. from a secret/env). Used if <see cref="CredentialsPath"/> is unset.</summary>
    public string? CredentialsJson { get; set; }
}
