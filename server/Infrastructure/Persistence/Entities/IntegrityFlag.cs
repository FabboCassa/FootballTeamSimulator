using Fts.Application.Integrity;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One entry in the abuse &amp; integrity review queue (Phase 9.5): something the server refused, allowed
/// but doubted, or was told about by a player.
///
/// Append-only and deliberately FK-FREE (plain denormalised ids), for the same reason as
/// <see cref="RankedAward"/>: the audit trail must OUTLIVE the group, world and season it refers to — a
/// seasonal reset reopens groups and a world can be retired, and neither may quietly erase the evidence.
/// Nothing in the ladder ever reads a flag back to make a decision; flags exist to be looked at.
/// </summary>
public sealed class IntegrityFlag
{
    public Guid Id { get; set; }

    public IntegrityFlagKind Kind { get; set; }

    public IntegrityFlagStatus Status { get; set; } = IntegrityFlagStatus.Open;

    /// <summary>0..100 — how strongly the heuristic fired (a blocked transfer is 100, a grey-band one
    /// scales with how far off the fee was, a link score is the score itself).</summary>
    public int Severity { get; set; }

    /// <summary>The account the flag is ABOUT (the buyer in a transfer, the reporter's target, the account
    /// enrolling when a link was found).</summary>
    public Guid? UserId { get; set; }

    /// <summary>The other account involved: the seller, the linked account, the reported coach.</summary>
    public Guid? SubjectUserId { get; set; }

    /// <summary>The ranked group it happened in, when applicable.</summary>
    public Guid? RankedGroupId { get; set; }

    /// <summary>World-unique player external id for transfer flags (0 otherwise) — plain id, no FK.</summary>
    public int PlayerExternalId { get; set; }

    /// <summary>The money involved and what the player was actually worth, so a reviewer can judge the
    /// call without recomputing anything.</summary>
    public long Fee { get; set; }
    public long MarketValue { get; set; }

    /// <summary>Short machine-readable context (reason code + numbers). Never user-supplied text alone:
    /// a player's free-text report detail is length-capped before it lands here.</summary>
    public string Details { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
