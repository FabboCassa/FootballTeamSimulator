namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One pushed balance payload (Phase 10.3). Append-only: nothing ever updates or deletes a revision, and
/// the ACTIVE balance is simply the row with the highest <see cref="Revision"/>.
///
/// That choice is deliberate. An "IsActive" flag would need a transaction to move and would make the
/// question "what was live at 14:05?" unanswerable after two moves; a monotonic counter answers it by
/// reading the table. A rollback therefore does not resurrect an old row — it copies its payload forward
/// as a NEW revision with <see cref="RolledBackFrom"/> set, so the history stays a straight line.
///
/// <see cref="Json"/> holds a WHOLE serialised <c>Sim.Core.Config.BalanceConfig</c>, not a diff: a config
/// is a few tens of KB, and storing the complete object means a revision is readable on its own, without
/// replaying every push before it. FK-free like <c>ranked_awards</c> — the author's account may be deleted
/// long before the revision stops being interesting, so the email is captured by value.
/// </summary>
public sealed class BalanceRevision
{
    public Guid Id { get; set; }

    /// <summary>Monotonic, unique, starts at 1. The highest one is the active balance.</summary>
    public int Revision { get; set; }

    /// <summary>The complete serialised <c>BalanceConfig</c>.</summary>
    public string Json { get; set; } = "{}";

    /// <summary><c>BalanceConfig.Version</c> inside the payload — bumped by hand whenever a change
    /// invalidates stored seeds/replays, so it is worth surfacing next to the revision number.</summary>
    public int ConfigVersion { get; set; }

    /// <summary>Why this push happened. Short and human — it is the only thing anyone reads at 3am.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Set when this revision is a rollback: the revision whose payload was copied forward.</summary>
    public int? RolledBackFrom { get; set; }

    /// <summary>Who pushed it. Kept as a plain id + the email captured at the time (no FK) so the record
    /// survives the account.</summary>
    public Guid CreatedByUserId { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
