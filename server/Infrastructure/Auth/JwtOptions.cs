namespace Fts.Infrastructure.Auth;

/// <summary>
/// JWT + refresh-token settings (bound from the "Jwt" configuration section). The
/// <see cref="SigningKey"/> MUST be overridden in every real environment (min 32 bytes for
/// HS256) — the appsettings value is a dev-only placeholder. Access tokens are short-lived
/// (<see cref="AccessTokenMinutes"/>); the long-lived rotating <see cref="RefreshTokenDays"/>
/// token lets the client stay signed in without re-entering the password.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "fts";
    public string Audience { get; set; } = "fts-client";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
