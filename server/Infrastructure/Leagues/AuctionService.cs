using Fts.Application.Leagues;
using Fts.Application.Notifications;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// The authoritative online-auction engine (Phase 8.5). All live state lives in Postgres/EF (the store
/// chosen for 8.5 — durable + ACID, ample for a friend league); live pushes and timed settlement are thin
/// layers over the top (<see cref="IAuctionBroadcaster"/> / <see cref="IAuctionScheduler"/>). A window
/// opens a lot per free agent; members place ascending bids validated against the current high bid, a
/// minimum increment, and their available budget (transfer budget minus their other leading bids — so a
/// club can never win more than it can pay for across concurrent lots). No money moves until a lot
/// settles, so an outbid simply frees the reservation ("others refunded"); settlement assigns the player
/// to the highest bidder and charges only them.
/// </summary>
public sealed class AuctionService : IAuctionService
{
    // --- Tunables (server-side, like BudgetModel's seeding constants) ---------------------------
    /// <summary>How long a fresh lot runs before its timer elapses.</summary>
    private const int AuctionDurationSeconds = 120;
    /// <summary>A bid placed within this window of the end extends the timer to now + this (anti-sniping).</summary>
    private const int AntiSnipeSeconds = 30;
    /// <summary>Opening price = this permille of the player's market value (50%).</summary>
    private const long StartPricePermille = 500;
    /// <summary>Minimum raise over the current high bid = this permille of it (5%)…</summary>
    private const long MinIncrementPermille = 50;
    /// <summary>…but never less than this absolute floor.</summary>
    private const long MinIncrementFloor = 25_000;
    /// <summary>A start price never dips below this.</summary>
    private const long MinStartPrice = 25_000;
    /// <summary>The contract length a won free agent signs.</summary>
    private const int SignedContractSeasons = 4;

    private readonly FtsDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IAuctionBroadcaster _broadcaster;
    private readonly IAuctionScheduler _scheduler;

    public AuctionService(
        FtsDbContext db,
        INotificationService notifications,
        IAuctionBroadcaster broadcaster,
        IAuctionScheduler scheduler)
    {
        _db = db;
        _notifications = notifications;
        _broadcaster = broadcaster;
        _scheduler = scheduler;
    }

    // --- Open a window --------------------------------------------------------------------------

    public async Task<LeagueResult<AuctionsDto>> OpenWindowAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.NotFound, "League not found.");
        if (league.CreatorUserId != userId)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.Forbidden, "Only the creator can open an auction window.");
        if (league.Status != LeagueStatus.Active)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.WrongPhase, "Auctions run only while the season is active.");

        bool anyOpen = await _db.Auctions.AnyAsync(
            a => a.PrivateLeagueId == leagueId && a.Status == AuctionStatus.Open, ct);
        if (anyOpen)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.WindowAlreadyOpen, "An auction window is already open.");

        int? maxWindow = await _db.Auctions.Where(a => a.PrivateLeagueId == leagueId)
            .Select(a => (int?)a.WindowIndex).MaxAsync(ct);
        int nextWindow = maxWindow.HasValue ? maxWindow.Value + 1 : 0;

        // Lots = every current free agent in the world (a previous window's unsold players may re-list).
        var freeAgents = await _db.Players
            .Where(p => p.WorldId == league.WorldId && p.ClubId == null)
            .OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var endsUtc = now.AddSeconds(AuctionDurationSeconds);
        var created = new List<Auction>();
        foreach (var p in freeAgents)
        {
            long start = Math.Max(MinStartPrice, p.MarketValue * StartPricePermille / 1000);
            var lot = new Auction
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = leagueId,
                WorldId = league.WorldId,
                PlayerId = p.Id,
                WindowIndex = nextWindow,
                StartPrice = start,
                HighBid = 0,
                Status = AuctionStatus.Open,
                EndsUtc = endsUtc,
                CreatedUtc = now,
            };
            _db.Auctions.Add(lot);
            created.Add(lot);
        }

        await _db.SaveChangesAsync(ct);

        // Schedule each lot's automatic settlement at its timer end (no-op when Hangfire is disabled;
        // the creator "close window" action settles synchronously in that case).
        foreach (var lot in created) _scheduler.ScheduleSettlement(lot.Id, lot.EndsUtc);

        return await BuildViewAsync(league, userId, ct);
    }

    // --- Read -----------------------------------------------------------------------------------

    public async Task<LeagueResult<AuctionsDto>> GetAuctionsAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.NotFound, "League not found.");
        bool isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        return await BuildViewAsync(league, userId, ct);
    }

    // --- Bid ------------------------------------------------------------------------------------

    public async Task<LeagueResult<BidResultDto>> PlaceBidAsync(
        Guid userId, Guid leagueId, Guid auctionId, PlaceBidRequest request, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<BidResultDto>.Fail(LeagueError.NotFound, "League not found.");

        var member = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (member is null)
            return LeagueResult<BidResultDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (member.ClubId is not { } clubId)
            return LeagueResult<BidResultDto>.Fail(LeagueError.NotAssignedClub, "You have no club in this league.");

        var auction = await _db.Auctions.Include(a => a.Player)
            .FirstOrDefaultAsync(a => a.Id == auctionId && a.PrivateLeagueId == leagueId, ct);
        if (auction is null)
            return LeagueResult<BidResultDto>.Fail(LeagueError.AuctionNotFound, "Auction not found.");

        var now = DateTime.UtcNow;
        if (auction.Status != AuctionStatus.Open || now >= auction.EndsUtc)
            return LeagueResult<BidResultDto>.Fail(LeagueError.AuctionClosed, "This lot is no longer open for bids.");

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null)
            return LeagueResult<BidResultDto>.Fail(LeagueError.NotAssignedClub, "Your club could not be found.");

        // You cannot bid against yourself.
        if (auction.HighBidClubId == clubId)
            return LeagueResult<BidResultDto>.Fail(LeagueError.BidTooLow, "You already hold the highest bid on this lot.");

        long minimum = auction.HighBid <= 0
            ? auction.StartPrice
            : auction.HighBid + Math.Max(MinIncrementFloor, auction.HighBid * MinIncrementPermille / 1000);
        if (request.Amount < minimum)
            return LeagueResult<BidResultDto>.Fail(LeagueError.BidTooLow,
                $"The minimum bid is {minimum:N0}.");

        // Budget the club has already committed as the leader on OTHER open lots — it may not exceed its
        // transfer budget across everything it is winning at once.
        long committedElsewhere = await _db.Auctions
            .Where(a => a.PrivateLeagueId == leagueId && a.Status == AuctionStatus.Open
                        && a.Id != auctionId && a.HighBidClubId == clubId)
            .SumAsync(a => a.HighBid, ct);
        long available = club.TransferBudget - committedElsewhere;
        if (request.Amount > available)
            return LeagueResult<BidResultDto>.Fail(LeagueError.InsufficientBudget,
                $"Your available budget is {available:N0}.");

        // Anti-sniping: a bid in the last seconds pushes the end back so no one can snipe unanswered.
        bool extended = false;
        if ((auction.EndsUtc - now).TotalSeconds < AntiSnipeSeconds)
        {
            auction.EndsUtc = now.AddSeconds(AntiSnipeSeconds);
            extended = true;
        }

        Guid? previousLeaderUserId = auction.HighBidUserId;
        int? previousLeaderClubExternalId = auction.HighBidClubExternalId;

        auction.HighBid = request.Amount;
        auction.HighBidClubId = clubId;
        auction.HighBidClubExternalId = club.ExternalId;
        auction.HighBidUserId = userId;

        _db.Bids.Add(new Bid
        {
            Id = Guid.NewGuid(),
            AuctionId = auction.Id,
            PrivateLeagueId = leagueId,
            ClubId = clubId,
            ClubExternalId = club.ExternalId,
            UserId = userId,
            Amount = request.Amount,
            PlacedUtc = now,
        });

        await _db.SaveChangesAsync(ct);

        if (extended) _scheduler.ScheduleSettlement(auction.Id, auction.EndsUtc);

        var lotDto = ToDto(auction, now);

        // Notify the displaced leader (outbid) — the 8.5 ✅. Never throws (unconfigured FCM = a logged skip).
        bool outbid = previousLeaderUserId is { } prev && prev != userId;
        if (outbid)
        {
            await _notifications.SendToUserAsync(previousLeaderUserId!.Value, new PushMessage(
                "Outbid",
                $"Your bid for {PlayerName(auction.Player)} has been beaten.",
                new Dictionary<string, string>
                {
                    ["type"] = "auction_outbid",
                    ["leagueId"] = leagueId.ToString(),
                    ["auctionId"] = auction.Id.ToString(),
                    ["playerExternalId"] = auction.Player!.ExternalId.ToString(),
                }), ct);
        }

        await _broadcaster.LotChangedAsync(leagueId, lotDto, ct);

        long availableAfter = available - request.Amount;
        return LeagueResult<BidResultDto>.Ok(new BidResultDto(
            lotDto, outbid, previousLeaderClubExternalId, extended, availableAfter));
    }

    // --- Close the window (creator force / testing) ---------------------------------------------

    public async Task<LeagueResult<AuctionsDto>> CloseWindowAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.NotFound, "League not found.");
        if (league.CreatorUserId != userId)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.Forbidden, "Only the creator can close the auction window.");

        var openLots = await _db.Auctions.Include(a => a.Player)
            .Where(a => a.PrivateLeagueId == leagueId && a.Status == AuctionStatus.Open)
            .ToListAsync(ct);
        if (openLots.Count == 0)
            return LeagueResult<AuctionsDto>.Fail(LeagueError.NoAuctionsOpen, "There are no open auctions to close.");

        var now = DateTime.UtcNow;
        var settled = new List<Auction>();
        foreach (var lot in openLots)
        {
            await SettleLotAsync(lot, now, ct);
            settled.Add(lot);
        }
        await _db.SaveChangesAsync(ct);

        foreach (var lot in settled) await NotifyAndBroadcastSettlementAsync(league, lot, now, ct);

        return await BuildViewAsync(league, userId, ct);
    }

    // --- Timed settlement (Hangfire per-lot job) ------------------------------------------------

    public async Task SettleDueAsync(Guid auctionId, CancellationToken ct = default)
    {
        var auction = await _db.Auctions.Include(a => a.Player)
            .FirstOrDefaultAsync(a => a.Id == auctionId, ct);
        if (auction is null || auction.Status != AuctionStatus.Open) return;

        var now = DateTime.UtcNow;
        if (now < auction.EndsUtc)
        {
            // Extended by an anti-snipe bid since this job was scheduled — try again at the new end.
            _scheduler.ScheduleSettlement(auction.Id, auction.EndsUtc);
            return;
        }

        await SettleLotAsync(auction, now, ct);
        await _db.SaveChangesAsync(ct);

        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == auction.PrivateLeagueId, ct);
        if (league is not null) await NotifyAndBroadcastSettlementAsync(league, auction, now, ct);
    }

    // --- Settlement core ------------------------------------------------------------------------

    /// <summary>Settles one lot: no bids ⇒ Unsold; otherwise the player joins the winner's club, the
    /// winner's transfer budget is charged the winning fee (only them — losers were never charged), and a
    /// transfer-log row is written. Mutates entities on the tracked context; the caller saves.</summary>
    private async Task SettleLotAsync(Auction auction, DateTime now, CancellationToken ct)
    {
        if (auction.HighBidClubId is not { } winnerClubId || auction.HighBid <= 0)
        {
            auction.Status = AuctionStatus.Unsold;
            auction.SettledUtc = now;
            return;
        }

        var winner = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == winnerClubId, ct);
        var player = auction.Player ?? await _db.Players.FirstOrDefaultAsync(p => p.Id == auction.PlayerId, ct);

        if (winner is not null && player is not null)
        {
            player.ClubId = winner.Id;
            player.ContractSeasonsRemaining = SignedContractSeasons;
            winner.TransferBudget = Math.Max(0, winner.TransferBudget - auction.HighBid);

            _db.Transfers.Add(new Transfer
            {
                Id = Guid.NewGuid(),
                WorldId = auction.WorldId,
                PlayerId = player.Id,
                FromClubId = null,           // a free agent has no former club
                ToClubId = winner.Id,
                Fee = auction.HighBid,
                SeasonYear = 0,              // no online-season calendar yet; this is the transaction log
                Day = auction.WindowIndex,
                CreatedUtc = now,
            });
        }

        auction.Status = AuctionStatus.Settled;
        auction.SettledUtc = now;
    }

    /// <summary>After a lot settles: push the final state and, on a sale, tell the winner. Never throws.</summary>
    private async Task NotifyAndBroadcastSettlementAsync(PrivateLeague league, Auction auction, DateTime now, CancellationToken ct)
    {
        if (auction.Status == AuctionStatus.Settled && auction.HighBidUserId is { } winnerUserId)
        {
            await _notifications.SendToUserAsync(winnerUserId, new PushMessage(
                "Auction won",
                $"You signed {PlayerName(auction.Player)} for {auction.HighBid:N0}.",
                new Dictionary<string, string>
                {
                    ["type"] = "auction_won",
                    ["leagueId"] = league.Id.ToString(),
                    ["auctionId"] = auction.Id.ToString(),
                }), ct);
        }

        await _broadcaster.LotChangedAsync(league.Id, ToDto(auction, now), ct);
    }

    // --- View builder ---------------------------------------------------------------------------

    private async Task<LeagueResult<AuctionsDto>> BuildViewAsync(PrivateLeague league, Guid userId, CancellationToken ct)
    {
        var member = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == league.Id && m.UserId == userId, ct);

        // Show the latest window's lots (open ones + the results of the same window).
        int? latestWindow = await _db.Auctions.Where(a => a.PrivateLeagueId == league.Id)
            .Select(a => (int?)a.WindowIndex).MaxAsync(ct);

        var now = DateTime.UtcNow;
        var lots = new List<AuctionLotDto>();
        bool windowOpen = false;
        if (latestWindow is { } window)
        {
            var rows = await _db.Auctions.Include(a => a.Player)
                .Where(a => a.PrivateLeagueId == league.Id && a.WindowIndex == window)
                .ToListAsync(ct);
            foreach (var a in rows.OrderByDescending(a => a.Status == AuctionStatus.Open)
                         .ThenByDescending(a => a.Player!.Overall).ThenBy(a => a.Player!.ExternalId))
            {
                lots.Add(ToDto(a, now));
                if (a.Status == AuctionStatus.Open) windowOpen = true;
            }
        }

        long budget = 0, committed = 0;
        int? clubExternalId = null;
        if (member?.ClubId is { } clubId)
        {
            var club = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct);
            if (club is not null)
            {
                budget = club.TransferBudget;
                clubExternalId = club.ExternalId;
                committed = await _db.Auctions
                    .Where(a => a.PrivateLeagueId == league.Id && a.Status == AuctionStatus.Open
                                && a.HighBidClubId == clubId)
                    .SumAsync(a => a.HighBid, ct);
            }
        }

        return LeagueResult<AuctionsDto>.Ok(new AuctionsDto(
            lots, clubExternalId, budget, committed, budget - committed, windowOpen));
    }

    private static AuctionLotDto ToDto(Auction a, DateTime now)
    {
        int secondsRemaining = a.Status == AuctionStatus.Open
            ? Math.Max(0, (int)(a.EndsUtc - now).TotalSeconds)
            : 0;
        var p = a.Player;
        return new AuctionLotDto(
            a.Id,
            p?.ExternalId ?? 0,
            PlayerName(p),
            p?.Age ?? 0,
            p?.Role ?? 0,
            p?.Overall ?? 0,
            p?.Potential ?? 0,
            p?.MarketValue ?? 0,
            a.StartPrice,
            a.HighBid,
            a.HighBidClubExternalId,
            null,
            a.Status,
            a.EndsUtc,
            secondsRemaining);
    }

    private static string PlayerName(Player? p) =>
        p is null ? string.Empty : $"{p.FirstName} {p.LastName}".Trim();
}
