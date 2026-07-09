namespace Fts.Infrastructure.Auth;

/// <summary>
/// Account-level coach profile (ARCHITECTURE §6.3 "coach_profiles"): the display identity of a
/// human manager, separate from Identity's auth fields and from the per-world <c>Coach</c> rows.
/// One-to-one with <see cref="AppUser"/>. Career/reputation aggregates across worlds can be
/// added here additively in later phases; for 7.2 it just carries the display name.
/// </summary>
public sealed class CoachProfile
{
    /// <summary>Primary key AND FK to <see cref="AppUser"/> (shared-PK 1:1).</summary>
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
