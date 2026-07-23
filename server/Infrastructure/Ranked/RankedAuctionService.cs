using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedAuctionService"/> implementation (Phase 9.2b) — free-agent auctions in a ranked
/// season's market windows. Adapted from the 8.5 private-league auction (same ascending-bid + committed-budget
/// mechanics) but driven by the ranked calendar tick: a window opening creates a lot per free agent, and the
/// tick settles lots whose timer has elapsed (highest bidder gets the player and is charged). No SignalR /
/// per-lot Hangfire job — a day-long ranked window does not need live anti-snipe.
/// </summary>
public sealed class RankedAuctionService : IRankedAuctionService
{
    private const long StartPricePermille = 500;    // opening price = 50% of market value
    private const long MinIncrementPermille = 50;   // min raise = 5% of the current high bid…
    private const long MinIncrementFloor = 25_000;  // …but never less than this
    private const long MinStartPrice = 25_000;
    private const int SignedContractSeasons = 4;

    private readonly FtsDbContext _db;
    private readonly INotificationService _notify;
    private readonly RankedOptions _opt;

    public RankedAuctionService(FtsDbContext db, INotificationService notify, IOptions<RankedOptions> options)
    {
        _db = db;
        _notify = notify;
        _opt = options.Value;
    }

    // --- reads / bids ------------------------------------------------------------------------------

    public async Task<RankedResult<RankedAuctionsDto>> GetAuctionsAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        return RankedResult<RankedAuctionsDto>.Ok(await BuildAuctionsAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<RankedBidResultDto>> PlaceBidAsync(
        Guid userId, Guid auctionId, PlaceRankedBidRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked club.");

        var (open, _) = await MarketWindowAsync(group, ct);
        if (!open)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.WrongPhase, "The market is closed right now.");

        var lot = await _db.RankedAuctions.FirstOrDefaultAsync(
            a => a.Id == auctionId && a.RankedGroupId == group.Id, ct);
        if (lot is null)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.AuctionNotFound, "No such lot in your group.");

        var now = DateTime.UtcNow;
        if (lot.Status != RankedAuctionStatus.Open || lot.EndsUtc <= now)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.AuctionClosed, "This lot is no longer open.");
        if (lot.HighBidUserId == userId)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.ValidationFailed, "You are already the leading bidder.");
        if (request.Amount < MinNextBid(lot))
            return RankedResult<RankedBidResultDto>.Fail(RankedError.BidTooLow, "Your bid is below the minimum.");

        var club = await _db.Clubs.FirstAsync(c => c.Id == clubId, ct);
        long committedElsewhere = await _db.RankedAuctions
            .Where(a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open
                        && a.Id != auctionId && a.HighBidUserId == userId)
            .SumAsync(a => a.HighBid, ct);
        long available = club.TransferBudget - committedElsewhere;
        if (request.Amount > available)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.InsufficientBudget, "That is more than your available budget.");

        Guid? prevUserId = lot.HighBidUserId;
        int? prevClubExt = lot.HighBidClubExternalId;

        lot.HighBid = request.Amount;
        lot.HighBidClubId = club.Id;
        lot.HighBidClubExternalId = club.ExternalId;
        lot.HighBidUserId = userId;
        await _db.SaveChangesAsync(ct);

        if (prevUserId is { } prev && prev != userId)
            await SafeSend(prev, "Offerta superata",
                "Sei stato superato in un'asta.",
                new Dictionary<string, string> { ["kind"] = "ranked_outbid", ["auctionId"] = lot.Id.ToString() }, ct);

        long remaining = club.TransferBudget - committedElsewhere - request.Amount;
        var dto = await MapLotAsync(lot, group.WorldId, userId, now, ct);
        return RankedResult<RankedBidResultDto>.Ok(new RankedBidResultDto(
            dto, prevUserId is not null, prevClubExt, remaining));
    }

    // --- tick-driven lifecycle ---------------------------------------------------------------------

    public async Task<int> OpenWindowLotsAsync(
        Guid rankedGroupId, int windowIndex, DateTime endsUtc, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == rankedGroupId, ct);
        if (group?.WorldId is not { } worldId) return 0;

        // Idempotent: never open a second batch for the same window.
        bool already = await _db.RankedAuctions.AnyAsync(
            a => a.RankedGroupId == rankedGroupId && a.WindowIndex == windowIndex, ct);
        if (already) return 0;

        var freeAgents = await _db.Players
            .Where(p => p.WorldId == worldId && p.ClubId == null)
            .Select(p => new { p.Id, p.ExternalId, p.MarketValue })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var fa in freeAgents)
        {
            _db.RankedAuctions.Add(new RankedAuction
            {
                Id = Guid.NewGuid(),
                RankedGroupId = rankedGroupId,
                PlayerId = fa.Id,
                PlayerExternalId = fa.ExternalId,
                WindowIndex = windowIndex,
                StartPrice = Math.Max(MinStartPrice, fa.MarketValue * StartPricePermille / 1000),
                HighBid = 0,
                Status = RankedAuctionStatus.Open,
                EndsUtc = endsUtc,
                CreatedUtc = now,
            });
        }
        await _db.SaveChangesAsync(ct);
        return freeAgents.Count;
    }

    public async Task<int> SettleDueAsync(bool force = false, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lots = await _db.RankedAuctions
            .Where(a => a.Status == RankedAuctionStatus.Open && (force || a.EndsUtc <= now))
            .ToListAsync(ct);
        if (lots.Count == 0) return 0;

        int settled = 0;
        foreach (var lot in lots)
        {
            if (lot.HighBidUserId is { } winnerUserId && lot.HighBidClubId is { } winnerClubId && lot.HighBid > 0)
            {
                var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == lot.PlayerId, ct);
                var winner = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == winnerClubId, ct);
                if (player is not null && winner is not null && winner.TransferBudget >= lot.HighBid)
                {
                    player.ClubId = winner.Id;
                    player.ContractSeasonsRemaining = SignedContractSeasons;
                    winner.TransferBudget = Math.Max(0, winner.TransferBudget - lot.HighBid);
                    _db.Transfers.Add(new Transfer
                    {
                        Id = Guid.NewGuid(),
                        WorldId = winner.WorldId,
                        PlayerId = player.Id,
                        FromClubId = null,               // a free agent has no former club
                        ToClubId = winner.Id,
                        Fee = lot.HighBid,
                        SeasonYear = 0,
                        Day = lot.WindowIndex,
                        CreatedUtc = now,
                    });
                    lot.Status = RankedAuctionStatus.Settled;
                    lot.SettledUtc = now;
                    await SafeSend(winnerUserId, "Asta vinta",
                        $"Hai vinto un'asta per {lot.HighBid:N0}.",
                        new Dictionary<string, string> { ["kind"] = "ranked_auction_won" }, ct);
                }
                else
                {
                    lot.Status = RankedAuctionStatus.Unsold;
                    lot.SettledUtc = now;
                }
            }
            else
            {
                lot.Status = RankedAuctionStatus.Unsold;
                lot.SettledUtc = now;
            }
            settled++;
        }
        await _db.SaveChangesAsync(ct);
        return settled;
    }

    // --- helpers -----------------------------------------------------------------------------------

    private static long MinNextBid(RankedAuction lot) =>
        lot.HighBid <= 0
            ? lot.StartPrice
            : lot.HighBid + Math.Max(MinIncrementFloor, lot.HighBid * MinIncrementPermille / 1000);

    private async Task<(RankedGroup? group, RankedSeat? seat)> CurrentGroupSeatAsync(
        RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is not { } seatId) return (null, null);
        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return (null, null);
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct);
        return (group, seat);
    }

    private async Task<(bool open, int index)> MarketWindowAsync(RankedGroup group, CancellationToken ct)
    {
        if (group.SeasonStartedUtc is not { } start) return (false, -1);
        int totalRounds = (await _db.RankedFixtures
            .Where(f => f.RankedGroupId == group.Id)
            .Select(f => (int?)f.Round)
            .MaxAsync(ct)) ?? 0;
        if (totalRounds == 0) return (false, -1);

        var w = RankedCalendar.CurrentWindow(
            start, totalRounds, _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, DateTime.UtcNow);
        return w is { } win ? (true, win.Index) : (false, -1);
    }

    private async Task<RankedAuctionsDto> BuildAuctionsAsync(
        RankedGroup? group, RankedSeat? seat, Guid userId, CancellationToken ct)
    {
        long budget = 0;
        if (seat?.ClubId is { } clubId)
            budget = await _db.Clubs.Where(c => c.Id == clubId).Select(c => c.TransferBudget).FirstOrDefaultAsync(ct);

        if (group is null)
            return new RankedAuctionsDto(budget, 0, budget, false, Array.Empty<RankedAuctionLotDto>());

        var (open, _) = await MarketWindowAsync(group, ct);
        var now = DateTime.UtcNow;

        var lots = await _db.RankedAuctions
            .Where(a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open)
            .OrderByDescending(a => a.HighBid).ThenBy(a => a.PlayerExternalId)
            .ToListAsync(ct);

        long committed = lots.Where(a => a.HighBidUserId == userId).Sum(a => a.HighBid);

        var playerIds = lots.Select(a => a.PlayerId).ToList();
        var players = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var dtos = lots.Select(a =>
        {
            players.TryGetValue(a.PlayerId, out var p);
            return new RankedAuctionLotDto(
                Id: a.Id,
                PlayerExternalId: a.PlayerExternalId,
                PlayerName: p is null ? string.Empty : Combine(p.FirstName, p.LastName),
                Age: p?.Age ?? 0,
                Role: p?.Role ?? 0,
                Overall: p?.Overall ?? 0,
                MarketValue: p?.MarketValue ?? 0,
                StartPrice: a.StartPrice,
                HighBid: a.HighBid,
                HighBidClubExternalId: a.HighBidClubExternalId,
                YouAreLeading: a.HighBidUserId == userId,
                MinNextBid: MinNextBid(a),
                Status: a.Status,
                EndsUtc: a.EndsUtc,
                SecondsRemaining: SecondsLeft(a.EndsUtc, now));
        }).ToList();

        return new RankedAuctionsDto(budget, committed, budget - committed, open, dtos);
    }

    private async Task<RankedAuctionLotDto> MapLotAsync(
        RankedAuction lot, Guid? worldId, Guid userId, DateTime now, CancellationToken ct)
    {
        var p = await _db.Players.FirstOrDefaultAsync(x => x.Id == lot.PlayerId, ct);
        return new RankedAuctionLotDto(
            Id: lot.Id,
            PlayerExternalId: lot.PlayerExternalId,
            PlayerName: p is null ? string.Empty : Combine(p.FirstName, p.LastName),
            Age: p?.Age ?? 0,
            Role: p?.Role ?? 0,
            Overall: p?.Overall ?? 0,
            MarketValue: p?.MarketValue ?? 0,
            StartPrice: lot.StartPrice,
            HighBid: lot.HighBid,
            HighBidClubExternalId: lot.HighBidClubExternalId,
            YouAreLeading: lot.HighBidUserId == userId,
            MinNextBid: MinNextBid(lot),
            Status: lot.Status,
            EndsUtc: lot.EndsUtc,
            SecondsRemaining: SecondsLeft(lot.EndsUtc, now));
    }

    private static int SecondsLeft(DateTime endsUtc, DateTime now) =>
        endsUtc <= now ? 0 : (int)Math.Min(int.MaxValue, (endsUtc - now).TotalSeconds);

    private static string Combine(string first, string last) =>
        string.IsNullOrEmpty(first) ? last : $"{first} {last}";

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* best-effort. */ }
    }
}
