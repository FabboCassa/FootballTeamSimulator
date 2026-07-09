using Microsoft.AspNetCore.Identity;

namespace Fts.Infrastructure.Auth;

/// <summary>
/// The account entity (ARCHITECTURE §6.3 "users"), backed by ASP.NET Core Identity with a
/// <see cref="Guid"/> key so it lines up with the rest of the schema. Identity owns
/// email/password/hash/security-stamp; the game-facing display data lives on the 1:1
/// <see cref="CoachProfile"/>. Single-player stays offline-first — an account is only needed
/// for the online phases, and a user owns world <c>coaches</c> via <c>Coach.OwnerUserId</c>.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public DateTime CreatedUtc { get; set; }

    /// <summary>Account-level coach profile (created together with the account at register).</summary>
    public CoachProfile? Profile { get; set; }

    /// <summary>Active/expired refresh tokens issued to this account (rotation history).</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
