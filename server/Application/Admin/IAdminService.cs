namespace Fts.Application.Admin;

/// <summary>
/// The live-ops use cases (Phase 10.3): read the operational metrics, inspect worlds and accounts, act on
/// an account, and push/roll back the server's balance. Every mutating call writes an
/// <see cref="AdminAuditDto"/> line, so "who changed what, when" is answerable without reading logs.
///
/// Implemented in Infrastructure (EF + Identity). The Api maps it under <c>/admin</c> behind the admin
/// role; nothing here checks authorisation — that is the endpoint filter's job, exactly once, at the edge.
/// </summary>
public interface IAdminService
{
    // --- monitoring ---
    Task<AdminMetricsDto> GetMetricsAsync(CancellationToken ct = default);

    // --- worlds ---
    Task<IReadOnlyList<AdminWorldDto>> GetWorldsAsync(CancellationToken ct = default);
    Task<AdminResult<AdminWorldDto>> SetWorldOpenAsync(
        Guid actorUserId, Guid worldId, SetWorldOpenRequest request, CancellationToken ct = default);

    // --- users ---
    /// <summary>Search by email or display name (case-insensitive, contains). An empty query returns the
    /// most recently created accounts, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<AdminUserDto>> SearchUsersAsync(
        string? query, int limit = 50, CancellationToken ct = default);

    Task<AdminResult<AdminUserDto>> GetUserAsync(Guid userId, CancellationToken ct = default);

    Task<AdminResult<AdminUserDto>> SetUserLockAsync(
        Guid actorUserId, Guid userId, SetUserLockRequest request, CancellationToken ct = default);

    Task<AdminResult<AdminUserDto>> SetUserAdminAsync(
        Guid actorUserId, Guid userId, SetUserAdminRequest request, CancellationToken ct = default);

    // --- balance ---
    Task<AdminBalanceDto> GetBalanceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdminBalanceRevisionDto>> GetBalanceHistoryAsync(
        int limit = 50, CancellationToken ct = default);
    Task<AdminResult<AdminBalanceDto>> PushBalanceAsync(
        Guid actorUserId, PushBalanceRequest request, CancellationToken ct = default);
    Task<AdminResult<AdminBalanceDto>> RollbackBalanceAsync(
        Guid actorUserId, int revision, CancellationToken ct = default);

    // --- audit ---
    Task<IReadOnlyList<AdminAuditDto>> GetAuditAsync(int limit = 100, CancellationToken ct = default);
}
