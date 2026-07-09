using Fts.Application.Auth;
using Fts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Auth;

/// <summary>
/// <see cref="IAuthService"/> implementation: ASP.NET Core Identity owns users/passwords, EF owns
/// the rotating refresh tokens. Expected failures come back as <see cref="AuthResult{T}"/> (no
/// exceptions), and login/refresh never reveal which part was wrong.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _users;
    private readonly FtsDbContext _db;
    private readonly JwtTokenService _tokens;

    public AuthService(UserManager<AppUser> users, FtsDbContext db, JwtTokenService tokens)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
    }

    public async Task<AuthResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim();
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(displayName))
            return AuthResult<AuthResponse>.Fail(AuthError.ValidationFailed, "Email, password and display name are required.");

        if (await _users.FindByEmailAsync(email) is not null)
            return AuthResult<AuthResponse>.Fail(AuthError.EmailAlreadyInUse, "That email is already registered.");

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            CreatedUtc = DateTime.UtcNow,
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Duplicate can still surface here under a race; treat as email-in-use.
            var duplicate = created.Errors.Any(e =>
                e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
            var error = duplicate ? AuthError.EmailAlreadyInUse : AuthError.WeakPassword;
            var message = string.Join(" ", created.Errors.Select(e => e.Description));
            return AuthResult<AuthResponse>.Fail(error, message);
        }

        var profile = new CoachProfile
        {
            UserId = user.Id,
            DisplayName = displayName,
            CreatedUtc = DateTime.UtcNow,
        };
        _db.CoachProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);

        var response = await IssueAsync(user, profile, ct);
        return AuthResult<AuthResponse>.Ok(response);
    }

    public async Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return AuthResult<AuthResponse>.Fail(AuthError.InvalidCredentials, "Invalid email or password.");

        var user = await _users.FindByEmailAsync(email);
        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
            return AuthResult<AuthResponse>.Fail(AuthError.InvalidCredentials, "Invalid email or password.");

        var profile = await _db.CoachProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct)
            ?? await CreateFallbackProfileAsync(user, ct);

        var response = await IssueAsync(user, profile, ct);
        return AuthResult<AuthResponse>.Ok(response);
    }

    public async Task<AuthResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return AuthResult<AuthResponse>.Fail(AuthError.InvalidRefreshToken, "Invalid refresh token.");

        var hash = JwtTokenService.Hash(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive || stored.User is null)
            return AuthResult<AuthResponse>.Fail(AuthError.InvalidRefreshToken, "Invalid or expired refresh token.");

        var profile = await _db.CoachProfiles.FirstOrDefaultAsync(p => p.UserId == stored.UserId, ct)
            ?? await CreateFallbackProfileAsync(stored.User, ct);

        // Rotate: mint the replacement first so we can record the link, then revoke the old one.
        var response = await IssueAsync(stored.User, profile, ct, revoking: stored);
        return AuthResult<AuthResponse>.Ok(response);
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;

        var hash = JwtTokenService.Hash(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedUtc: null })
        {
            stored.RevokedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<CoachProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        var profile = await _db.CoachProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? await CreateFallbackProfileAsync(user, ct);

        return new CoachProfileDto(user.Id, user.Email ?? string.Empty, profile.DisplayName);
    }

    /// <summary>Mints an access token + a new refresh token, persists the refresh token, and (on
    /// rotation) revokes the one being replaced — all in one save.</summary>
    private async Task<AuthResponse> IssueAsync(
        AppUser user, CoachProfile profile, CancellationToken ct, RefreshToken? revoking = null)
    {
        var (accessToken, _, expiresIn) = _tokens.CreateAccessToken(user, profile);
        var (rawRefresh, refreshHash, refreshExpires) = _tokens.CreateRefreshToken();

        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = refreshExpires,
        };
        _db.RefreshTokens.Add(refresh);

        if (revoking is not null)
        {
            revoking.RevokedUtc = DateTime.UtcNow;
            revoking.ReplacedByTokenHash = refreshHash;
        }

        await _db.SaveChangesAsync(ct);

        return new AuthResponse(
            AccessToken: accessToken,
            ExpiresInSeconds: expiresIn,
            RefreshToken: rawRefresh,
            RefreshTokenExpiresUtc: refreshExpires,
            Profile: new CoachProfileDto(user.Id, user.Email ?? string.Empty, profile.DisplayName));
    }

    /// <summary>Defensive: if a user somehow lacks a profile row (e.g. a pre-7.2 account), create
    /// one from the email local-part so the rest of the flow always has a display name.</summary>
    private async Task<CoachProfile> CreateFallbackProfileAsync(AppUser user, CancellationToken ct)
    {
        var name = (user.Email ?? "coach").Split('@')[0];
        var profile = new CoachProfile
        {
            UserId = user.Id,
            DisplayName = string.IsNullOrWhiteSpace(name) ? "coach" : name,
            CreatedUtc = DateTime.UtcNow,
        };
        _db.CoachProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }
}
