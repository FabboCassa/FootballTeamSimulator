namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A privacy-preserving fingerprint of where an account has been seen from (Phase 9.5), used only to score
/// whether two ranked accounts are probably the same person.
///
/// <b>No raw address is ever stored.</b> The Api hashes the client address (SHA-256 over a configured salt
/// plus the address) before it reaches this table, so the column is a correlation key and nothing else —
/// it cannot be turned back into an IP, and rotating the salt makes the whole history uncorrelatable.
/// The device id is whatever stable client identifier the app sends, hashed the same way, or empty when
/// the client sends none.
///
/// Cascade-deleted with the account (an account that is gone leaves no fingerprints behind).
/// </summary>
public sealed class AccountSignal
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Salted hash of the client address (never the address itself).</summary>
    public string AddressHash { get; set; } = string.Empty;

    /// <summary>Salted hash of a client-supplied device id, or empty when unknown. Empty is stored rather
    /// than null so the uniqueness index behaves the same on PostgreSQL and SQLite.</summary>
    public string DeviceHash { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>How many times this (account, address, device) combination has been recorded — a rough
    /// "how much do they really play from here" weight for a reviewer.</summary>
    public int SeenCount { get; set; }
}
