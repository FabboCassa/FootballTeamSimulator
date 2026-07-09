namespace Fts.Application.Auth;

/// <summary>
/// The auth use cases (Phase 7.2). Implemented in Infrastructure (ASP.NET Core Identity for
/// users/passwords + EF for the rotating refresh tokens). The Api exposes each as an endpoint.
/// </summary>
public interface IAuthService
{
    Task<AuthResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Rotates the presented refresh token: validates it, revokes it, and issues a fresh
    /// access+refresh pair. The old token cannot be reused.</summary>
    Task<AuthResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Revokes the presented refresh token (server-side logout). Idempotent.</summary>
    Task LogoutAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>The authenticated account's profile (for /auth/me), or null if the id is unknown.</summary>
    Task<CoachProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default);
}
