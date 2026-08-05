using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedRankingService"/> implementation (Phase 9.3) — the single owner of a coach's rating
/// and palmarès.
///
/// Storage split (decided with the user): <b>PostgreSQL is authoritative</b> (<c>ranked_coaches.Rating</c>
/// + the append-only <c>ranked_awards</c>), and <b>Redis carries a rebuildable sorted-set leaderboard</b> for
/// fast top-N reads. Every write refreshes the cache; every read falls back to an ordered SQL query when the
/// cache is cold, empty or unreachable — so losing Redis costs speed, never progression.
///
/// The rating maths itself is in the pure <see cref="EloModel"/>; this class only decides <i>when</i> it is
/// applied (per resolved matchday, and once at season end) and persists the outcome.
/// </summary>
public sealed class RankedRankingService : IRankedRankingService
{
    /// <summary>Shown where a coach row has no profile behind it any more — an account deleted under
    /// 10.2a keeps its ladder record, re-stamped with an id that belongs to nobody.</summary>
    public const string RemovedCoachName = "Allenatore rimosso";

    private readonly FtsDbContext _db;
    private readonly IRankedLeaderboardCache _cache;
    private readonly RankedOptions _opt;

    public RankedRankingService(FtsDbContext db, IRankedLeaderboardCache cache, IOptions<RankedOptions> options)
    {
        _db = db;
        _cache = cache;
        _opt = options.Value;
    }

    // --- rating writes -----------------------------------------------------------------------------

    public async Task<int> ApplyMatchAsync(
        Guid userId, int opponentRating, RankedMatchOutcome outcome, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null) return 0;

        int delta = EloModel.MatchDelta(coach.Rating, opponentRating, outcome, _opt.EloKFactor);
        return await StoreRatingAsync(coach, delta, ct);
    }

    public async Task<int> ApplySeasonEndAsync(
        Guid userId, int position, int groupSize, int tier, RankedTierMove move, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null) return 0;

        int delta = EloModel.SeasonEndDelta(position, groupSize, _opt.SeasonEndPositionSwing)
                  + EloModel.TierMoveDelta(move, _opt.PromotionRatingBonus);

        coach.SeasonsPlayed++;
        return await StoreRatingAsync(coach, delta, ct);
    }

    public async Task GrantAwardAsync(
        Guid userId, RankedAwardKind kind, Guid rankedGroupId, string worldName, string groupName,
        int tier, int position, int seasonNumber, int ratingAfter, int ratingDelta, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        _db.RankedAwards.Add(new RankedAward
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            RankedWorldId = coach?.RankedWorldId ?? Guid.Empty,
            RankedGroupId = rankedGroupId,
            WorldName = worldName,
            GroupName = groupName,
            Tier = tier,
            Position = position,
            SeasonNumber = seasonNumber,
            RatingAfter = ratingAfter,
            RatingDelta = ratingDelta,
            AwardedUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> RatingOfAsync(Guid userId, CancellationToken ct = default)
    {
        var rating = await _db.RankedCoaches
            .Where(c => c.UserId == userId)
            .Select(c => (int?)c.Rating)
            .FirstOrDefaultAsync(ct);
        return rating ?? _opt.StartingRating;
    }

    /// <summary>Applies a delta, keeps <c>PeakRating</c> up to date, persists and refreshes the cache.</summary>
    private async Task<int> StoreRatingAsync(RankedCoach coach, int delta, CancellationToken ct)
    {
        coach.Rating = EloModel.Apply(coach.Rating, delta, _opt.MinRating);
        if (coach.Rating > coach.PeakRating) coach.PeakRating = coach.Rating;
        await _db.SaveChangesAsync(ct);

        // Best-effort by contract — the cache implementation never throws.
        await _cache.SetRatingAsync(coach.UserId, coach.Rating, ct);
        return coach.Rating;
    }

    // --- reads -------------------------------------------------------------------------------------

    public async Task<RankedResult<RankedLeaderboardDto>> GetLeaderboardAsync(
        Guid userId, int top = 50, CancellationToken ct = default)
    {
        int take = top > 0 ? Math.Min(top, 500) : _opt.LeaderboardTopCount;

        int total = await _db.RankedCoaches.CountAsync(c => c.Status != RankedCoachStatus.Retired, ct);

        // Fast path: the Redis sorted set gives the ordered ids; PostgreSQL then hydrates them. When the
        // cache is cold/disabled/unreachable it returns nothing and we simply order in SQL — and warm it.
        var cachedIds = await _cache.TopAsync(take, ct);

        List<RankedCoach> coaches;
        if (cachedIds.Count > 0)
        {
            var idList = cachedIds.ToList();
            var byId = await _db.RankedCoaches
                .Where(c => idList.Contains(c.UserId))
                .ToDictionaryAsync(c => c.UserId, ct);
            coaches = idList.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }
        else
        {
            coaches = await _db.RankedCoaches
                .Where(c => c.Status != RankedCoachStatus.Retired)
                .OrderByDescending(c => c.Rating).ThenBy(c => c.EnrolledUtc)
                .Take(take)
                .ToListAsync(ct);

            if (_cache.IsEnabled) await WarmCacheAsync(ct);
        }

        var entries = await BuildEntriesAsync(coaches, userId, startRank: 1, ct);

        RankedLeaderboardEntryDto? you = entries.FirstOrDefault(e => e.IsYou);
        if (you is null)
        {
            var mine = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
            if (mine is not null)
            {
                // Your rank = how many active coaches are strictly above you, + 1.
                int above = await _db.RankedCoaches.CountAsync(
                    c => c.Status != RankedCoachStatus.Retired && c.Rating > mine.Rating, ct);
                var rows = await BuildEntriesAsync(new List<RankedCoach> { mine }, userId, above + 1, ct);
                you = rows.FirstOrDefault();
            }
        }

        return RankedResult<RankedLeaderboardDto>.Ok(new RankedLeaderboardDto(entries, you, total));
    }

    public async Task<RankedResult<RankedPalmaresDto>> GetPalmaresAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedPalmaresDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var awards = await _db.RankedAwards
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AwardedUtc)
            .ToListAsync(ct);

        string displayName = await _db.CoachProfiles
            .Where(p => p.UserId == userId).Select(p => p.DisplayName).FirstOrDefaultAsync(ct) ?? string.Empty;

        var dtos = awards.Select(a => new RankedAwardDto(
            Id: a.Id,
            Kind: a.Kind,
            WorldName: a.WorldName,
            GroupName: a.GroupName,
            Tier: a.Tier,
            Position: a.Position,
            SeasonNumber: a.SeasonNumber,
            RatingAfter: a.RatingAfter,
            RatingDelta: a.RatingDelta,
            AwardedUtc: a.AwardedUtc)).ToList();

        return RankedResult<RankedPalmaresDto>.Ok(new RankedPalmaresDto(
            UserId: userId,
            DisplayName: displayName,
            Rating: coach.Rating,
            PeakRating: Math.Max(coach.PeakRating, coach.Rating),
            SeasonsPlayed: coach.SeasonsPlayed,
            Titles: awards.Count(a => a.Kind is RankedAwardKind.Champion or RankedAwardKind.TopFlightTitle),
            Promotions: awards.Count(a => a.Kind == RankedAwardKind.Promotion),
            Relegations: awards.Count(a => a.Kind == RankedAwardKind.Relegation),
            Awards: dtos));
    }

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<List<RankedLeaderboardEntryDto>> BuildEntriesAsync(
        List<RankedCoach> coaches, Guid callerId, int startRank, CancellationToken ct)
    {
        if (coaches.Count == 0) return new List<RankedLeaderboardEntryDto>();

        var userIds = coaches.Select(c => c.UserId).ToList();

        var names = await _db.CoachProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);

        var titles = await _db.RankedAwards
            .Where(a => userIds.Contains(a.UserId)
                        && (a.Kind == RankedAwardKind.Champion || a.Kind == RankedAwardKind.TopFlightTitle))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Where each coach currently sits (tier + group name), via their seat.
        var seatIds = coaches.Where(c => c.SeatId is not null).Select(c => c.SeatId!.Value).ToList();
        var seats = await _db.RankedSeats.Where(s => seatIds.Contains(s.Id)).ToListAsync(ct);
        var groupIds = seats.Select(s => s.RankedGroupId).Distinct().ToList();
        var groups = await _db.RankedGroups
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, ct);
        var seatById = seats.ToDictionary(s => s.Id);

        var list = new List<RankedLeaderboardEntryDto>(coaches.Count);
        for (int i = 0; i < coaches.Count; i++)
        {
            var c = coaches[i];
            RankedGroup? group = c.SeatId is { } sid && seatById.TryGetValue(sid, out var seat)
                                 && groups.TryGetValue(seat.RankedGroupId, out var g) ? g : null;

            list.Add(new RankedLeaderboardEntryDto(
                Rank: startRank + i,
                UserId: c.UserId,
                DisplayName: names.TryGetValue(c.UserId, out var n) ? n : RemovedCoachName,
                Rating: c.Rating,
                PeakRating: Math.Max(c.PeakRating, c.Rating),
                Tier: group?.Tier,
                GroupName: group?.Name,
                SeasonsPlayed: c.SeasonsPlayed,
                Titles: titles.TryGetValue(c.UserId, out var t) ? t : 0,
                IsYou: c.UserId == callerId));
        }
        return list;
    }

    /// <summary>Rebuilds the whole sorted set from PostgreSQL (the authoritative store).</summary>
    private async Task WarmCacheAsync(CancellationToken ct)
    {
        var all = await _db.RankedCoaches
            .Where(c => c.Status != RankedCoachStatus.Retired)
            .Select(c => new { c.UserId, c.Rating })
            .ToListAsync(ct);
        await _cache.RebuildAsync(all.Select(x => (x.UserId, x.Rating)).ToList(), ct);
    }
}
