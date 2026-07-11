namespace Fts.Application.Leagues;

/// <summary>
/// Private-league lifecycle use cases (Phase 8.1): create a league (which server-generates a fresh
/// world with unique players), join/leave by invite code, and read the caller's leagues + a league's
/// detail. The account id is always passed in by the Api from the access token — the service never
/// trusts a caller-supplied id. Expected failures come back as <see cref="LeagueResult{T}"/> (no
/// exceptions). The scheduled/real-time season resolution builds on this in 8.3+.
/// </summary>
public interface ILeagueService
{
    Task<LeagueResult<LeagueDetailDto>> CreateAsync(
        Guid userId, CreateLeagueRequest request, CancellationToken ct = default);

    Task<LeagueResult<LeagueDetailDto>> JoinAsync(
        Guid userId, JoinLeagueRequest request, CancellationToken ct = default);

    /// <summary>Starts the season draft (8.2): only the creator, only while <see cref="LeagueStatus.Forming"/>
    /// and with at least two members. Equalises every club's squad to the same strength and gives every club
    /// the same transfer budget (online fairness — "pari budget a tutti"), then opens the snake pick so members
    /// choose their club (identity) in turn. Moves the league to <see cref="LeagueStatus.Drafting"/>.</summary>
    Task<LeagueResult<LeagueDetailDto>> StartDraftAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>Claims a club during the snake draft (8.2): valid only while <see cref="LeagueStatus.Drafting"/>,
    /// only for the member whose turn it is, and only for a club no one else has taken. When the last member
    /// picks, the league flips to <see cref="LeagueStatus.Active"/>.</summary>
    Task<LeagueResult<LeagueDetailDto>> PickClubAsync(
        Guid userId, Guid leagueId, PickClubRequest request, CancellationToken ct = default);

    /// <summary>Removes the caller's membership. If the creator leaves, the earliest remaining member
    /// inherits ownership; if the last member leaves, the league and its world are deleted.</summary>
    Task<LeagueResult<bool>> LeaveAsync(Guid userId, Guid leagueId, CancellationToken ct = default);

    Task<IReadOnlyList<LeagueSummaryDto>> ListMineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Full detail — only for a member of the league.</summary>
    Task<LeagueResult<LeagueDetailDto>> GetAsync(Guid userId, Guid leagueId, CancellationToken ct = default);
}
