namespace Fts.Infrastructure.Auth;

/// <summary>
/// A persisted refresh token (rotating). Only a SHA-256 <see cref="TokenHash"/> is stored — the
/// raw token is returned to the client exactly once and never kept server-side. On /auth/refresh
/// the presented token is validated, then <see cref="RevokedUtc"/>+<see cref="ReplacedByTokenHash"/>
/// are set and a fresh pair is issued (rotation); /auth/logout just revokes. This gives real
/// server-side revocation and lets the short-lived access token be refreshed without re-login.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>SHA-256 (hex) of the raw refresh token. Unique — the lookup key on refresh.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }
    /// <summary>Hash of the token that superseded this one on rotation (audit trail).</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedUtc is null && DateTime.UtcNow < ExpiresUtc;
}
