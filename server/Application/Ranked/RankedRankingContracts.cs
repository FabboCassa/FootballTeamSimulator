namespace Fts.Application.Ranked;

/// <summary>
/// Coach ranking, seasonal rewards and the seasonal reset (Phase 9.3) — the ladder's persistent
/// progression on top of the 9.2 real-time season.
///
/// Two meters, decided with the user up front:
/// <list type="bullet">
/// <item><b>Rating</b> (Elo-style) — moves after every matchday (each human coach vs their opponent's
/// rating; a vacant AI seat plays at a tier-derived baseline) plus a finishing bonus/malus at season end.
/// PostgreSQL is authoritative (<c>ranked_coaches.Rating</c>); Redis only carries a rebuildable sorted-set
/// leaderboard for fast top-N reads.</item>
/// <item><b>Palmarès</b> — an append-only history of what a coach won (<c>ranked_awards</c>): titles,
/// promotions, relegations, seasons played. Permanent: it survives a world teardown and a reset.</item>
/// </list>
/// </summary>

/// <summary>What a coach earned. Stable numeric values — they are persisted.</summary>
public enum RankedAwardKind
{
    /// <summary>A season completed in a division (the participation record every season leaves).</summary>
    SeasonPlayed = 0,
    /// <summary>Won the group (finished 1st).</summary>
    Champion = 1,
    /// <summary>Promoted to the tier above.</summary>
    Promotion = 2,
    /// <summary>Relegated to the tier below.</summary>
    Relegation = 3,
    /// <summary>Finished a placement season and entered the pyramid.</summary>
    PlacementCompleted = 4,
    /// <summary>Won the TOP tier — the ladder's real trophy.</summary>
    TopFlightTitle = 5,
}

/// <summary>Which way a coach moved at season end (drives both the award and the rating adjustment).</summary>
public enum RankedTierMove
{
    Stay = 0,
    Promotion = 1,
    Relegation = 2,
}

/// <summary>A match result from one coach's point of view (the Elo input).</summary>
public enum RankedMatchOutcome
{
    Loss = 0,
    Draw = 1,
    Win = 2,
}

/// <summary>One line of a coach's palmarès.</summary>
public sealed record RankedAwardDto(
    Guid Id,
    RankedAwardKind Kind,
    string WorldName,
    string GroupName,
    int Tier,
    int Position,
    int SeasonNumber,
    int RatingAfter,
    int RatingDelta,
    DateTime AwardedUtc);

/// <summary>A row of the global ladder.</summary>
public sealed record RankedLeaderboardEntryDto(
    int Rank,
    Guid UserId,
    string DisplayName,
    int Rating,
    int PeakRating,
    int? Tier,
    string? GroupName,
    int SeasonsPlayed,
    int Titles,
    bool IsYou);

/// <summary>The global ladder's top slice plus the caller's own row (even when outside the slice).</summary>
public sealed record RankedLeaderboardDto(
    IReadOnlyList<RankedLeaderboardEntryDto> Entries,
    RankedLeaderboardEntryDto? You,
    int TotalCoaches);

/// <summary>A coach's persistent record: rating, counters and the full award history.</summary>
public sealed record RankedPalmaresDto(
    Guid UserId,
    string DisplayName,
    int Rating,
    int PeakRating,
    int SeasonsPlayed,
    int Titles,
    int Promotions,
    int Relegations,
    IReadOnlyList<RankedAwardDto> Awards);

/// <summary>What closing one division season did (returned by the tick summary / dev tooling).</summary>
public sealed record RankedSeasonEndSummary(
    Guid GroupId,
    string GroupName,
    int SeasonNumber,
    int CoachesEvaluated,
    int Promotions,
    int Relegations,
    int AwardsGranted,
    bool SquadsReset);

/// <summary>
/// Ranking reads (leaderboard + palmarès) and the authoritative rating writes (Phase 9.3). The writes live
/// here so Elo and the Redis cache refresh have exactly one owner: the season engine calls
/// <see cref="ApplyMatchAsync"/> per resolved matchday and <see cref="ApplySeasonEndAsync"/> when a season
/// closes, and never touches <c>Rating</c> itself.
/// </summary>
public interface IRankedRankingService
{
    /// <summary>The global ladder: the top <paramref name="top"/> coaches by rating, plus the caller's own
    /// row when they are outside that slice.</summary>
    Task<RankedResult<RankedLeaderboardDto>> GetLeaderboardAsync(
        Guid userId, int top = 50, CancellationToken ct = default);

    /// <summary>The caller's persistent record (rating, counters, award history).</summary>
    Task<RankedResult<RankedPalmaresDto>> GetPalmaresAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Applies one matchday result to a coach's rating (Elo vs <paramref name="opponentRating"/>)
    /// and refreshes the leaderboard cache. Returns the new rating (0 when the account is not enrolled).</summary>
    Task<int> ApplyMatchAsync(
        Guid userId, int opponentRating, RankedMatchOutcome outcome, CancellationToken ct = default);

    /// <summary>Applies the season-end finishing bonus/malus (position within the group, plus a
    /// promotion/relegation adjustment) and bumps the coach's seasons-played counter.</summary>
    Task<int> ApplySeasonEndAsync(
        Guid userId, int position, int groupSize, int tier, RankedTierMove move, CancellationToken ct = default);

    /// <summary>Appends one award row to a coach's palmarès (append-only history). The world/group names are
    /// captured by value so the line still reads correctly after the group has been reset.</summary>
    Task GrantAwardAsync(
        Guid userId, RankedAwardKind kind, Guid rankedGroupId, string worldName, string groupName,
        int tier, int position, int seasonNumber, int ratingAfter, int ratingDelta, CancellationToken ct = default);

    /// <summary>A coach's current rating, or the configured starting rating when they are not enrolled.</summary>
    Task<int> RatingOfAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The rebuildable global-ladder cache (a Redis sorted set in production, a no-op when Redis is not
/// wired — e.g. under the Testing environment). Best-effort by contract: every method swallows transport
/// failures, because PostgreSQL is the authoritative store and the cache can always be rebuilt from it.
/// </summary>
public interface IRankedLeaderboardCache
{
    /// <summary>Whether the cache is actually backed by a store (false = no-op).</summary>
    bool IsEnabled { get; }

    /// <summary>Records a coach's current rating.</summary>
    Task SetRatingAsync(Guid userId, int rating, CancellationToken ct = default);

    /// <summary>Removes a coach (e.g. retired from the ladder).</summary>
    Task RemoveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The best <paramref name="count"/> coach ids, highest rating first — empty when the cache
    /// is cold or disabled (the caller then reads PostgreSQL).</summary>
    Task<IReadOnlyList<Guid>> TopAsync(int count, CancellationToken ct = default);

    /// <summary>Replaces the whole cache with the supplied ranking (used to warm/repair it).</summary>
    Task RebuildAsync(IReadOnlyList<(Guid UserId, int Rating)> entries, CancellationToken ct = default);
}

/// <summary>
/// Closing a ranked division season (Phase 9.3): the finishing rating bonus, the awards, promotion /
/// relegation across the pyramid and the squad reset that makes the next season fair again. Split out of
/// <see cref="IRankedSeasonService"/> so the real-time calendar keeps owning only the clock.
///
/// Deliberately TWO steps with a break in between (the user's ladder vision — a week off between seasons):
/// the season <b>closes</b> the moment the last matchday resolves (ratings settle, awards are handed out,
/// the final table stays readable), and only when the break has elapsed does the <b>reset</b> move coaches
/// up/down a tier and rebuild fair squads for the next season.
/// </summary>
public interface IRankedSeasonEndService
{
    /// <summary>Step 1 — the last matchday just resolved: rate and reward every human coach by final
    /// position and put the group into its between-seasons break (the schedule and table stay readable).
    /// Idempotent: a group already in the break returns a zeroed summary.</summary>
    Task<RankedSeasonEndSummary> CompleteDivisionSeasonAsync(
        Guid groupId, IReadOnlyList<RankedStandingDto> standings, CancellationToken ct = default);

    /// <summary>Step 2 — the break has elapsed: apply promotion/relegation, release coaches who opted out of
    /// auto re-enrolment, wipe the finished season's rows and re-equalise the squads (neutral condition,
    /// equal budgets), then reopen the group so the calendar starts the next season with a fresh free-agent
    /// auction. Idempotent: a group that is not in a break is left alone.</summary>
    Task<RankedSeasonEndSummary> ApplySeasonResetAsync(Guid groupId, CancellationToken ct = default);
}
