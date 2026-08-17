using Fts.Application.Balance;
using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sim.Core.Config;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedSeasonEndService"/> implementation (Phase 9.3) — everything that happens when a ranked
/// division season finishes, kept out of the 9.2 calendar so that one keeps owning only the clock.
///
/// TWO steps with the between-seasons break in between (the ladder vision: a week off):
/// <list type="number">
/// <item><b>Close</b> (<see cref="CompleteDivisionSeasonAsync"/>, fired by the last matchday) — each human
/// coach gets the finishing bonus/malus on top of the per-matchday Elo, plus the append-only palmarès rows
/// (title / promotion / relegation / season played). The group goes into its break with the schedule and
/// final table still readable.</item>
/// <item><b>Reset</b> (<see cref="ApplySeasonResetAsync"/>, fired once the break elapses) — the top
/// <see cref="RankedOptions.PromotionSlots"/> coaches move a tier up and the bottom
/// <see cref="RankedOptions.RelegationSlots"/> a tier down (tier 1 has nowhere up, the bottom tier nowhere
/// down), coaches who opted out of auto re-enrolment are released, then the season's rows are wiped and the
/// squads re-equalised (neutral condition, equal budgets) and the group reopens. The calendar then starts the
/// next season, whose first market window re-auctions the free agents — "squads reset via a new auction".</item>
/// </list>
///
/// Seats are never created or destroyed by any of this: a coach who leaves a seat simply leaves an AI club
/// behind, so the fixed division size from 9.1 survives every reset. Both steps are idempotent, so a double
/// tick can neither double-reward nor double-promote.
///
/// v1 simplification, documented: groups run their seasons independently, so a promoted coach can join a
/// group whose season is already under way (they take over its AI club mid-table). Synchronising the whole
/// pyramid onto one clock is a later refinement.
/// </summary>
public sealed class RankedSeasonEndService : IRankedSeasonEndService
{
    private const int NeutralForm = 50;
    private const int NeutralMorale = 50;
    private const int FullFitness = 100;

    private readonly FtsDbContext _db;
    private readonly IRankedService _ranked;
    private readonly IRankedRankingService _ranking;
    private readonly INotificationService _notify;
    private readonly RankedOptions _opt;
    /// <summary>The server's ACTIVE balance (Phase 10.3), snapshotted for the lifetime of this scoped
    /// service so one request - or one calendar tick - resolves against ONE set of numbers even if an
    /// admin pushes a revision half way through it. Before 10.3 this was `new BalanceConfig()`, i.e. the
    /// balance embedded in the build; with nothing ever pushed it still is, byte for byte.</summary>
    private readonly BalanceConfig _config;

    public RankedSeasonEndService(
        FtsDbContext db, IRankedService ranked, IRankedRankingService ranking,
        INotificationService notify, IOptions<RankedOptions> options, IBalanceProvider balance)
    {
        _db = db;
        _ranked = ranked;
        _ranking = ranking;
        _notify = notify;
        _opt = options.Value;
        _config = balance.Current;
    }

    // --- step 1: close the season (rate + reward) ---------------------------------------------------

    public async Task<RankedSeasonEndSummary> CompleteDivisionSeasonAsync(
        Guid groupId, IReadOnlyList<RankedStandingDto> standings, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return Empty(groupId, string.Empty, 0);

        // Idempotence: a group already in its break (or reset) has been rewarded once and only once.
        if (group.SeasonEndedUtc is not null || group.Status == RankedGroupStatus.Completed)
            return Empty(groupId, group.Name, group.SeasonNumber);

        string worldName = await WorldNameAsync(group, ct);
        var userByClubExternal = await HumanCoachesByClubExternalAsync(groupId, ct);

        int groupSize = standings.Count > 0 ? standings.Count : group.Capacity;
        int tierCount = _opt.TierCount();
        int evaluated = 0, awards = 0;

        for (int i = 0; i < standings.Count; i++)
        {
            int position = i + 1;
            if (!userByClubExternal.TryGetValue(standings[i].ClubExternalId, out var userId)) continue;
            evaluated++;

            RankedTierMove move = MoveFor(group.Tier, tierCount, position, groupSize);

            int before = await _ranking.RatingOfAsync(userId, ct);
            int after = await _ranking.ApplySeasonEndAsync(userId, position, groupSize, group.Tier, move, ct);
            int delta = after - before;

            // The palmarès: the title (a top-tier title is the ladder's real trophy), the tier move, and
            // always the season-played record.
            if (position == 1)
            {
                await _ranking.GrantAwardAsync(userId,
                    group.Tier == 1 ? RankedAwardKind.TopFlightTitle : RankedAwardKind.Champion,
                    group.Id, worldName, group.Name, group.Tier, position, group.SeasonNumber, after, delta, ct);
                awards++;
            }
            if (move != RankedTierMove.Stay)
            {
                await _ranking.GrantAwardAsync(userId,
                    move == RankedTierMove.Promotion ? RankedAwardKind.Promotion : RankedAwardKind.Relegation,
                    group.Id, worldName, group.Name, group.Tier, position, group.SeasonNumber, after, delta, ct);
                awards++;
            }
            await _ranking.GrantAwardAsync(userId, RankedAwardKind.SeasonPlayed,
                group.Id, worldName, group.Name, group.Tier, position, group.SeasonNumber, after, delta, ct);
            awards++;

            await SafeSend(userId, "Stagione conclusa",
                SeasonEndMessage(group.Name, position, move, after, delta),
                new Dictionary<string, string>
                {
                    ["kind"] = "ranked_season_end",
                    ["groupId"] = group.Id.ToString(),
                    ["position"] = position.ToString(),
                    ["rating"] = after.ToString(),
                }, ct);
        }

        // Into the break: the schedule + final table stay readable until the reset reopens the group.
        group.Status = RankedGroupStatus.Completed;
        group.SeasonEndedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new RankedSeasonEndSummary(
            group.Id, group.Name, group.SeasonNumber, evaluated, 0, 0, awards, false);
    }

    // --- step 2: the reset (promotion/relegation + fresh fair squads) -------------------------------

    public async Task<RankedSeasonEndSummary> ApplySeasonResetAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return Empty(groupId, string.Empty, 0);

        // Idempotence: only a group sitting in its between-seasons break can be reset.
        if (group.SeasonEndedUtc is null || group.Status != RankedGroupStatus.Completed)
            return Empty(groupId, group.Name, group.SeasonNumber);

        var fixtures = await _db.RankedFixtures.Where(f => f.RankedGroupId == groupId).ToListAsync(ct);
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == group.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);

        // The same final table the coaches were rewarded on decides who goes up and who goes down.
        var standings = RankedStandings.Compute(
            clubs, fixtures, _config.Season.PointsForWin, _config.Season.PointsForDraw);

        var userByClubExternal = await HumanCoachesByClubExternalAsync(groupId, ct);
        int groupSize = standings.Count > 0 ? standings.Count : group.Capacity;
        int tierCount = _opt.TierCount();
        int promotions = 0, relegations = 0;

        for (int i = 0; i < standings.Count; i++)
        {
            int position = i + 1;
            if (!userByClubExternal.TryGetValue(standings[i].ClubExternalId, out var userId)) continue;

            var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
            if (coach is null) continue;

            // Opted out of auto re-enrolment → leaves the ladder here, keeping rating + palmarès.
            if (!coach.AutoEnrol) { await RetireAsync(coach, ct); continue; }

            RankedTierMove move = MoveFor(group.Tier, tierCount, position, groupSize);
            if (move == RankedTierMove.Stay) continue;

            int targetTier = move == RankedTierMove.Promotion ? group.Tier - 1 : group.Tier + 1;
            var moved = await _ranked.MoveToTierAsync(userId, targetTier, ct);
            if (moved.Success && moved.Value is { } a && a.Tier == targetTier)
            {
                if (move == RankedTierMove.Promotion) promotions++; else relegations++;
                await SafeSend(userId,
                    move == RankedTierMove.Promotion ? "Promosso!" : "Retrocesso",
                    $"Nuova stagione in {a.GroupName} ({a.WorldName}) con il club {a.ClubName}.",
                    new Dictionary<string, string> { ["kind"] = "ranked_tier_move", ["groupId"] = a.GroupId.ToString() }, ct);
            }
        }

        bool reset = await RebuildGroupAsync(group, clubs, ct);

        return new RankedSeasonEndSummary(
            group.Id, group.Name, group.SeasonNumber, standings.Count, promotions, relegations, 0, reset);
    }

    /// <summary>Wipes the finished season's rows, re-equalises the (now developed) squads with neutral
    /// condition and equal budgets, and reopens the group so the calendar starts the next season — whose
    /// first market window opens a fresh free-agent auction. Players KEEP the ability they developed; only
    /// the distribution is made fair again (the same model as the private-league 8.7 reset).</summary>
    private async Task<bool> RebuildGroupAsync(RankedGroup group, List<Club> clubs, CancellationToken ct)
    {
        // Season rows first: lineups/fixtures Restrict-reference clubs; auctions/offers hang off the group.
        await _db.RankedAuctions.Where(a => a.RankedGroupId == group.Id).ExecuteDeleteAsync(ct);
        await _db.RankedOffers.Where(o => o.RankedGroupId == group.Id).ExecuteDeleteAsync(ct);
        await _db.RankedLineups.Where(l => l.RankedGroupId == group.Id).ExecuteDeleteAsync(ct);
        // Stored training plans go with the season (Phase 9.4): the next season's start re-seeds a balanced
        // default for every human seat, and a coach's focus is re-chosen on the squad they actually have.
        await _db.RankedTrainings.Where(t => t.RankedGroupId == group.Id).ExecuteDeleteAsync(ct);
        await _db.RankedFixtures.Where(f => f.RankedGroupId == group.Id).ExecuteDeleteAsync(ct);

        bool equalised = false;
        if (_opt.ResetSquadsBetweenSeasons && group.WorldId is not null && clubs.Count > 0)
        {
            // Club players only — the unattached pool stays free so the new window re-auctions it.
            var clubPlayers = clubs.SelectMany(c => c.Players).ToList();
            SquadEqualizer.Equalize(clubs, clubPlayers);

            // Whatever the market piled on beyond the standard squad size goes back to free agency: squads
            // stay a fixed size season after season AND the pool the next auction sells is refilled.
            ReleaseSurplus(clubs, _opt.SeasonResetSquadSize);

            foreach (var club in clubs) club.TransferBudget = _opt.StartingTransferBudget;
            foreach (var p in clubPlayers)
            {
                p.Form = NeutralForm;
                p.Morale = NeutralMorale;
                p.Fitness = FullFitness;
            }
            equalised = true;
        }

        group.SeasonNumber++;
        group.SeasonStartedUtc = null;
        group.SeasonEndedUtc = null;
        group.LastMarketWindowOpened = -1;
        group.Status = RankedGroupStatus.Forming;
        await _db.SaveChangesAsync(ct);

        return equalised;
    }

    /// <summary>Trims every club back to <paramref name="targetSize"/> by unattaching its weakest surplus
    /// players (they become free agents again, i.e. lots for the next season's auction). Deterministic —
    /// lowest overall first, external id as tie-break — and it never empties a position: a role down to its
    /// last player is skipped, so no club can end up without a goalkeeper. Strength is recomputed after.</summary>
    private static void ReleaseSurplus(IReadOnlyList<Club> clubs, int targetSize)
    {
        if (targetSize <= 0) return;

        foreach (var club in clubs)
        {
            int surplus = club.Players.Count - targetSize;
            if (surplus <= 0) continue;

            var perRole = club.Players.GroupBy(p => p.Role).ToDictionary(g => g.Key, g => g.Count());
            var weakestFirst = club.Players.OrderBy(p => p.Overall).ThenBy(p => p.ExternalId).ToList();

            foreach (var p in weakestFirst)
            {
                if (surplus <= 0) break;
                if (perRole[p.Role] <= 1) continue;   // never leave a position unmanned

                perRole[p.Role]--;
                p.ClubId = null;
                p.Club = null;
                club.Players.Remove(p);
                surplus--;
            }
        }

        foreach (var club in clubs)
        {
            if (club.Players.Count == 0) { club.Strength = 0; continue; }
            long sum = 0;
            foreach (var p in club.Players) sum += p.Overall;
            club.Strength = (int)(sum / club.Players.Count);
        }
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>Where a finishing position sends a coach. Tier 1 cannot be promoted out of, the bottom tier
    /// cannot be relegated out of — so the pyramid's ends are stable by construction.</summary>
    private RankedTierMove MoveFor(int tier, int tierCount, int position, int groupSize)
    {
        if (tier > 1 && position <= _opt.PromotionSlots) return RankedTierMove.Promotion;
        if (tier < tierCount && position > groupSize - _opt.RelegationSlots) return RankedTierMove.Relegation;
        return RankedTierMove.Stay;
    }

    /// <summary>The group's human coaches keyed by the external id of the club they hold.</summary>
    private async Task<Dictionary<int, Guid>> HumanCoachesByClubExternalAsync(Guid groupId, CancellationToken ct)
    {
        var humanSeats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == groupId && s.UserId != null && s.ClubId != null)
            .Select(s => new { ClubId = s.ClubId!.Value, UserId = s.UserId!.Value })
            .ToListAsync(ct);

        var clubIds = humanSeats.Select(h => h.ClubId).ToList();
        var externalByClubId = await _db.Clubs
            .Where(c => clubIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ExternalId })
            .ToDictionaryAsync(x => x.Id, x => x.ExternalId, ct);

        var map = new Dictionary<int, Guid>();
        foreach (var h in humanSeats)
            if (externalByClubId.TryGetValue(h.ClubId, out var ext)) map[ext] = h.UserId;
        return map;
    }

    private async Task<string> WorldNameAsync(RankedGroup group, CancellationToken ct) =>
        await _db.RankedWorlds
            .Where(w => w.Id == group.RankedWorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

    /// <summary>A coach who turned auto re-enrolment off leaves the ladder at the reset: the seat is freed
    /// (it plays as AI), while the rating and the palmarès stay exactly as they are.</summary>
    private async Task RetireAsync(RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is { } seatId)
        {
            var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
            if (seat is not null) { seat.UserId = null; seat.OccupiedUtc = null; }
        }
        coach.SeatId = null;
        coach.Status = RankedCoachStatus.Retired;
        await _db.SaveChangesAsync(ct);
    }

    private static RankedSeasonEndSummary Empty(Guid groupId, string name, int season) =>
        new(groupId, name, season, 0, 0, 0, 0, false);

    private static string SeasonEndMessage(string groupName, int position, RankedTierMove move, int rating, int delta)
    {
        string movePart = move switch
        {
            RankedTierMove.Promotion => " Sei promosso!",
            RankedTierMove.Relegation => " Sei retrocesso.",
            _ => string.Empty,
        };
        string sign = delta >= 0 ? "+" : string.Empty;
        return $"{groupName}: hai chiuso {position}°. Punteggio {rating} ({sign}{delta}).{movePart}";
    }

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* notifications are best-effort — never let a push failure break the season end. */ }
    }
}
