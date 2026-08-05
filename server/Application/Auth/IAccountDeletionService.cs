namespace Fts.Application.Auth;

/// <summary>
/// Deleting an account for good (Phase 10.2a). Kept apart from <see cref="IAuthService"/> because this is
/// not an auth use case: it reaches across every feature that ever stored something about the account
/// (leagues, ranked ladder, auctions, devices, integrity signals) and is the only operation in the API
/// that destroys data on purpose.
///
/// Both mobile stores require it: Google Play wants an in-app route AND a publicly reachable one, Apple
/// requires in-app deletion from anyone who offers registration. The public web page reuses the very same
/// endpoint (log in, then delete) so there is exactly one deletion path to reason about.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>Deletes the caller's account. The password is re-checked first; a wrong one comes back as
    /// <see cref="AuthError.InvalidCredentials"/>. Safe to retry: the login itself is destroyed last, so a
    /// call that dies half-way leaves an account that can sign in again and finish the job.</summary>
    Task<AuthResult<DeleteAccountResult>> DeleteAsync(
        Guid userId, DeleteAccountRequest request, CancellationToken ct = default);
}
