using Fts.Application.Ranked;

namespace Fts.Application.Integrity;

/// <summary>
/// Abuse &amp; integrity contracts (Phase 9.5) — the layer that keeps the public ladder honest.
///
/// Three things live here:
/// <list type="bullet">
/// <item><b>Flags.</b> Anything the server found worth a second look (a lopsided transfer that was refused,
/// one that was allowed but looks off, two accounts that keep showing up from the same place, a player
/// report) is written to an append-only flag store. Flags never block by themselves — the blocking is done
/// inline by the use case; a flag is the audit trail that makes the block reviewable.</item>
/// <item><b>Account signals.</b> A privacy-preserving fingerprint (hashed IP, optional device id) per
/// account, used to score how likely two accounts are the same person. Consumed by the enrolment guard so
/// two linked accounts are not seated in the same group.</item>
/// <item><b>Reports.</b> A coach can report another coach in their group; that becomes a flag too.</item>
/// </list>
///
/// The heuristics themselves are PURE (see <see cref="TransferIntegrity"/> and <see cref="LinkHeuristics"/>)
/// so they are unit-testable without a database, exactly like the Sim.Core balance models.
/// </summary>

/// <summary>What kind of thing the server noticed.</summary>
public enum IntegrityFlagKind
{
    /// <summary>A transfer whose fee was so far from the player's market value that it was REFUSED.</summary>
    BlockedTransfer = 0,
    /// <summary>A transfer that went through but sits in the grey band around market value.</summary>
    SuspiciousTransfer = 1,
    /// <summary>The same two coaches keep trading with each other inside one group.</summary>
    RepeatedTradingPair = 2,
    /// <summary>Two accounts look like the same person (shared address/device, created together).</summary>
    LinkedAccounts = 3,
    /// <summary>A coach reported another coach.</summary>
    PlayerReport = 4,
}

/// <summary>Where a flag is in review. 9.5 only ever writes <see cref="Open"/>; the admin dashboard
/// (10.3) is what will move them along.</summary>
public enum IntegrityFlagStatus
{
    Open = 0,
    Reviewed = 1,
    Dismissed = 2,
}

/// <summary>Why a coach is reporting another coach. Kept as an enum (no free-text category) so the client
/// localises it and the review queue can group by reason.</summary>
public enum RankedReportReason
{
    /// <summary>Suspected collusion — gifted transfers, thrown matches.</summary>
    Collusion = 0,
    /// <summary>Abandoned club: never sets a lineup, never plays their part.</summary>
    Inactivity = 1,
    /// <summary>Offensive club or coach name.</summary>
    OffensiveName = 2,
    /// <summary>Suspected cheating / exploiting.</summary>
    Cheating = 3,
    Other = 4,
}

/// <summary>Report another coach in your group. Identify them by their club's external id (what the client
/// actually shows) — <see cref="SubjectUserId"/> is accepted too for tooling.</summary>
public sealed record SubmitRankedReportRequest(
    int? SubjectClubExternalId,
    Guid? SubjectUserId,
    RankedReportReason Reason,
    string? Details);

/// <summary>Acknowledgement of a filed report (deliberately thin: a reporter learns nothing about what
/// happens next, so reports cannot be used to probe other accounts).</summary>
public sealed record RankedReportDto(Guid FlagId, DateTime CreatedUtc);

/// <summary>One flag as the review queue sees it.</summary>
public sealed record IntegrityFlagDto(
    Guid Id,
    IntegrityFlagKind Kind,
    IntegrityFlagStatus Status,
    int Severity,
    Guid? UserId,
    Guid? SubjectUserId,
    Guid? RankedGroupId,
    long Fee,
    long MarketValue,
    string Details,
    DateTime CreatedUtc);

/// <summary>The review queue.</summary>
public sealed record IntegrityFlagsDto(int Total, IReadOnlyList<IntegrityFlagDto> Flags);

/// <summary>
/// The integrity use cases. Deliberately small: the ranked services call <see cref="FlagAsync"/> when they
/// refuse (or allow-but-doubt) something, the enrolment path asks <see cref="LinkedUserIdsAsync"/> who not
/// to seat a coach next to, and the Api records a signal per authenticated ranked request.
/// </summary>
public interface IIntegrityService
{
    /// <summary>Remember that this account was seen from this address/device. Cheap and idempotent:
    /// an existing row is just touched. Never throws — a signal is diagnostics, not a use case.</summary>
    Task RecordAccountSignalAsync(Guid userId, string? ipAddress, string? deviceId, CancellationToken ct = default);

    /// <summary>Accounts that score above the link threshold against this one (shared address/device,
    /// created around the same time). Empty when the heuristics are disabled.</summary>
    Task<IReadOnlyCollection<Guid>> LinkedUserIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Append a flag to the review queue. Returns the new flag id.</summary>
    Task<Guid> FlagAsync(
        IntegrityFlagKind kind,
        int severity,
        Guid? userId,
        Guid? subjectUserId,
        Guid? rankedGroupId,
        long fee,
        long marketValue,
        string details,
        CancellationToken ct = default);

    /// <summary>How many transfers the same two coaches have already completed inside one group — the
    /// input to the repeated-pair flag.</summary>
    Task<int> CompletedTradesBetweenAsync(Guid groupId, Guid a, Guid b, CancellationToken ct = default);

    /// <summary>File a report against another coach in the caller's group.</summary>
    Task<RankedResult<RankedReportDto>> ReportAsync(
        Guid userId, SubmitRankedReportRequest request, CancellationToken ct = default);

    /// <summary>The review queue (dev/admin surface).</summary>
    Task<IntegrityFlagsDto> GetFlagsAsync(
        IntegrityFlagStatus? status, int take, CancellationToken ct = default);
}
