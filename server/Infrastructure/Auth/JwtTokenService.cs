using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fts.Infrastructure.Auth;

/// <summary>
/// Creates signed JWT access tokens and opaque refresh tokens. The access token carries the
/// account identity as standard claims (sub/email/name); the refresh token is a random opaque
/// string whose SHA-256 hash is stored server-side (<see cref="RefreshToken.TokenHash"/>) — the
/// raw value is returned to the client once and never persisted.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _opts;

    public JwtTokenService(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public (string Token, DateTime ExpiresUtc, int ExpiresInSeconds) CreateAccessToken(AppUser user, CoachProfile profile)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_opts.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, profile.DisplayName),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expires, _opts.AccessTokenMinutes * 60);
    }

    /// <summary>A fresh opaque refresh token: the raw value (for the client) plus its stored hash
    /// and absolute expiry.</summary>
    public (string Raw, string Hash, DateTime ExpiresUtc) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Base64UrlEncode(bytes);
        return (raw, Hash(raw), DateTime.UtcNow.AddDays(_opts.RefreshTokenDays));
    }

    /// <summary>SHA-256 (lowercase hex) of a raw refresh token — the DB lookup key on refresh.</summary>
    public static string Hash(string raw)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
