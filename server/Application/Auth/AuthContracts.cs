namespace Fts.Application.Auth;

/// <summary>Request/response DTOs for the auth use cases (Phase 7.2). Plain records so the Api
/// layer can bind them from JSON and the client can mirror them. The <see cref="IAuthService"/>
/// lives here (Application), its implementation in Infrastructure (needs Identity + EF).</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>The token pair + profile returned on register/login/refresh.</summary>
public sealed record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTime RefreshTokenExpiresUtc,
    CoachProfileDto Profile);

/// <summary>Account-level coach profile surfaced to the client (also the /auth/me payload).</summary>
public sealed record CoachProfileDto(Guid UserId, string Email, string DisplayName);

/// <summary>Deleting your own account (Phase 10.2a). The password is re-checked at the moment of the
/// request: an access token can be minutes old and sitting on an unlocked phone, and this is the one
/// call in the API that cannot be undone.</summary>
public sealed record DeleteAccountRequest(string Password);

/// <summary>What the deletion actually did. Returned to the caller (and asserted by the tests) so the
/// operation is auditable from the outside without exposing anyone else's data.</summary>
public sealed record DeleteAccountResult(
    int PrivateLeaguesLeft,
    int SessionsRevoked,
    int DevicesRemoved,
    int SignalsRemoved,
    bool RankedHistoryAnonymised,
    int RankedAwardsAnonymised);

/// <summary>Why an auth call failed — the Api maps these to HTTP status codes without leaking
/// which of email/password was wrong (login/refresh both return a generic message).</summary>
public enum AuthError
{
    None = 0,
    EmailAlreadyInUse,
    WeakPassword,
    InvalidCredentials,
    InvalidRefreshToken,
    ValidationFailed,
    /// <summary>The account is locked out by live ops (Phase 10.3). Appended at the END of the enum so
    /// every existing value keeps its number — the client mirrors this by name, but the wire format is the
    /// error STRING, so nothing needs to be rebuilt in step.
    ///
    /// Distinct from <see cref="InvalidCredentials"/> on purpose: a suspended player told "wrong password"
    /// will reset it, fail again, and open a support ticket. Saying the account is suspended is the honest
    /// answer and leaks nothing — they already know their own password.</summary>
    AccountLocked
}

/// <summary>Result wrapper so the service never throws for expected failures. Exactly one of
/// <see cref="Value"/> (on success) or <see cref="Error"/> (on failure) is meaningful.</summary>
public sealed record AuthResult<T>(bool Success, T? Value, AuthError Error, string? Message)
{
    public static AuthResult<T> Ok(T value) => new(true, value, AuthError.None, null);
    public static AuthResult<T> Fail(AuthError error, string? message = null) =>
        new(false, default, error, message);
}
