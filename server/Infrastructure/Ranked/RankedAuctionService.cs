using Fts.Application.Integrity;
using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Integrity;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedAuctionService"/> implementation — the ranked ladder's auction board.
///
/// Phase 9.2b built half of it: when a market window opened, the calendar put every free agent up as a lot
/// and the whole board closed together when the window did. Task 12.2 finishes it. A coach can now put one
/// of HIS OWN players up, with a timer he chooses between 1h and 24h; every lot carries its own
/// <see cref="RankedAuction.EndsUtc"/> and is settled on its own, so one lot can change hands while the rest
/// of the board is still bidding; a bid in the last seconds extends THAT lot (the private leagues'
/// anti-snipe rule); and the fee is PAID TO THE SELLER at settlement.
///
/// The two kinds of lot share one mechanism and differ only in who is on the other side of the money:
/// <list type="bullet">
/// <item><b>Free agent</b> (calendar-opened) — belongs to nobody, so nothing is paid out and the 9.5
/// integrity band does not apply: the flat opening price deliberately has no relation to market value.</item>
/// <item><b>Seller lot</b> (coach-opened) — moves money between two accounts, so the full 9.5 kit binds:
/// the band is checked on the reserve when it is listed AND on the winning bid when the money moves, the
/// squad-size floor counts what is already on the board, and a completed sale is assessed for the grey
/// band and the repeated-pair heuristic exactly as a direct offer is.</item>
/// </list>
///
/// NO Sim.Core change: this is server bookkeeping over the persisted world, unreachable from the match
/// engine.
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
    private readonly IIntegrityService _integrity;
    private readonly IntegrityOptions _integrityOpt;

    public RankedAuctionService(
        FtsDbContext db,
        INotificationService notify,
        IOptions<RankedOptions> options,
        IIntegrityService integrity,
        IOptions<IntegrityOptions> integrityOptions)
    {
        _db = db;
        _notify = notify;
        _opt = options.Value;
        _integrity = integrity;
        _integrityOpt = integrityOptions.Value;
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
        // Task 12.2: a seller bidding on his own lot would be bidding against himself with money that comes
        // back to him — the cheapest way there is to fake a price.
        if (lot.SellerClubId == clubId || lot.SellerUserId == userId)
            return RankedResult<RankedBidResultDto>.Fail(RankedError.ValidationFailed, "You cannot bid on your own lot.");
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

        // ANTI-SNIPE (task 12.2): a lot with its own short timer can be stolen in its last second, which is
        // what killed the fun of the private-league auctions before 8.5 grew the same rule. A bid inside the
        // window pushes THIS lot's end back — the rest of the board is untouched, which is the whole point
        // of per-lot timers.
        bool extended = false;
        int antiSnipe = Math.Max(0, _opt.AuctionAntiSnipeSeconds);
        if (antiSnipe > 0 && (lot.EndsUtc - now).TotalSeconds < antiSnipe)
        {
            lot.EndsUtc = now.AddSeconds(antiSnipe);
            extended = true;
        }

        await _db.SaveChangesAsync(ct);

        if (prevUserId is { } prev && prev != userId)
            await SafeSend(prev, "Offerta superata",
                "Sei stato superato in un'asta.",
                new Dictionary<string, string> { ["kind"] = "ranked_outbid", ["auctionId"] = lot.Id.ToString() }, ct);
        if (lot.SellerUserId is { } sellerUid && sellerUid != userId)
            await SafeSend(sellerUid, "Rilancio sul tuo giocatore",
                $"C'è un'offerta di {request.Amount:N0} sul giocatore che hai messo all'asta.",
                new Dictionary<string, string> { ["kind"] = "ranked_lot_bid", ["auctionId"] = lot.Id.ToString() }, ct);

        long remaining = club.TransferBudget - committedElsewhere - request.Amount;
        var dto = await MapLotAsync(lot, userId, now, ct);
        return RankedResult<RankedBidResultDto>.Ok(new RankedBidResultDto(
            dto, prevUserId is not null, prevClubExt, remaining, extended));
    }

    // --- selling your own players (task 12.2) ------------------------------------------------------

    public async Task<RankedResult<RankedAuctionsDto>> ListLotAsync(
        Guid userId, ListRankedLotRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked club.");

        var window = await CurrentWindowAsync(group, ct);
        if (window is not { } win)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.WrongPhase, "The market is closed right now.");

        // THE DURATION IS THE TASK'S ✅: 1h-24h, and anything outside is refused rather than quietly fixed —
        // a coach who asked for 25h must be told the board does not do that, not handed 24.
        int min = Math.Max(1, _opt.SellerLotMinSeconds);
        int max = Math.Max(min, _opt.SellerLotMaxSeconds);
        if (request.DurationSeconds < min || request.DurationSeconds > max)
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.LotDurationInvalid,
                $"An auction must run between {min / 3600}h and {max / 3600}h.");

        var now = DateTime.UtcNow;
        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.WorldId == group.WorldId && p.ExternalId == request.PlayerExternalId, ct);
        if (player is null)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.NotFound, "No such player in your group.");
        if (player.ClubId != clubId)
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.PlayerUnavailable, "You can only auction a player of your own club.");

        var openLots = await _db.RankedAuctions
            .Where(a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open
                        && a.SellerClubId == clubId)
            .ToListAsync(ct);
        if (openLots.Any(a => a.PlayerId == player.Id))
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.PlayerUnavailable, "That player is already on the auction board.");

        // THE SQUAD FLOOR, COUNTING THE BOARD (task 12.2). Checking the squad alone would let a coach list
        // his way under the floor one lot at a time and only discover it at settlement, when the money is
        // already promised. Every open lot is a player who is on his way out, so he is counted as gone here.
        int squad = await _db.Players.CountAsync(p => p.ClubId == clubId, ct);
        if (squad - openLots.Count - 1 < _opt.MinSquadSizeForSale)
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.SquadTooSmall,
                $"Selling him would leave you below the {_opt.MinSquadSizeForSale}-player floor, counting "
                + $"the {openLots.Count} you already have on the board.");

        // A reserve of 0 means "price him for me": half his market value, the private-league opening rule.
        // The flat ladder opening price is deliberately NOT used here — it exists so every free agent is
        // biddable with the same kitty, which has nothing to do with what another coach's player is worth.
        long reserve = request.Reserve > 0
            ? request.Reserve
            : Math.Max(MinStartPrice, player.MarketValue * StartPricePermille / 1000);
        if (reserve < MinStartPrice)
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.ValidationFailed, $"The reserve cannot be below {MinStartPrice:N0}.");

        // COLLUSION GUARD (Phase 9.5), first half: the reserve has to look like a price. A star opened at
        // pocket change is a gift with extra steps — and unlike a direct offer, an auction's opening price
        // is the one number the seller controls outright.
        var assessment = TransferIntegrity.Assess(reserve, player.MarketValue, _integrityOpt.Bands());
        if (assessment.IsBlocked)
        {
            await _integrity.FlagAsync(
                IntegrityFlagKind.BlockedTransfer, severity: 100, userId: userId, subjectUserId: null,
                rankedGroupId: group.Id, fee: reserve, marketValue: player.MarketValue,
                details: $"{assessment.Reason} as auction reserve; reserve={assessment.FeePercentOfValue}% of value; "
                         + $"player={player.ExternalId}",
                ct);
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.IntegrityBlocked,
                $"That reserve is {assessment.FeePercentOfValue}% of the player's market value — it must stay "
                + $"between {_integrityOpt.MinFeePercentOfValue}% and {_integrityOpt.MaxFeePercentOfValue}%.");
        }

        var club = await _db.Clubs.FirstAsync(c => c.Id == clubId, ct);
        var ends = ClampToWindow(now.AddSeconds(request.DurationSeconds), win);

        _db.RankedAuctions.Add(new RankedAuction
        {
            Id = Guid.NewGuid(),
            RankedGroupId = group.Id,
            PlayerId = player.Id,
            PlayerExternalId = player.ExternalId,
            WindowIndex = win.Index,
            StartPrice = reserve,
            HighBid = 0,
            Status = RankedAuctionStatus.Open,
            EndsUtc = ends,
            CreatedUtc = now,
            SellerClubId = club.Id,
            SellerClubExternalId = club.ExternalId,
            SellerUserId = userId,
        });
        await _db.SaveChangesAsync(ct);

        foreach (var uid in await OtherHumansAsync(group.Id, userId, ct))
            await SafeSend(uid, "Nuovo giocatore all'asta",
                $"{PlayerName(player)} è all'asta: base {reserve:N0}.",
                new Dictionary<string, string> { ["kind"] = "ranked_lot_listed", ["groupId"] = group.Id.ToString() }, ct);

        return RankedResult<RankedAuctionsDto>.Ok(await BuildAuctionsAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<RankedAuctionsDto>> UnlistLotAsync(
        Guid userId, Guid auctionId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var lot = await _db.RankedAuctions.FirstOrDefaultAsync(a => a.Id == auctionId, ct);
        if (lot is null)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.AuctionNotFound, "No such lot.");
        if (lot.SellerUserId != userId)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.Forbidden, "Only the seller can pull his own lot.");
        if (lot.Status != RankedAuctionStatus.Open)
            return RankedResult<RankedAuctionsDto>.Fail(RankedError.AuctionClosed, "This lot is no longer open.");
        // Once somebody has bid, the timer is the only thing that ends the lot: a seller who could withdraw
        // after seeing the bidding would be running a fake auction, and the bidder's budget is committed.
        if (lot.HighBid > 0)
            return RankedResult<RankedAuctionsDto>.Fail(
                RankedError.AuctionClosed, "Someone has already bid — the lot now runs to its timer.");

        lot.Status = RankedAuctionStatus.Cancelled;
        lot.SettledUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        return RankedResult<RankedAuctionsDto>.Ok(await BuildAuctionsAsync(group, seat, userId, ct));
    }

    // --- tick-driven lifecycle ---------------------------------------------------------------------

    public async Task<int> OpenWindowLotsAsync(
        Guid rankedGroupId, int windowIndex, DateTime endsUtc, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == rankedGroupId, ct);
        if (group?.WorldId is not { } worldId) return 0;

        // Idempotent: never open a second batch for the same window. Counts only the CALENDAR's own lots —
        // a coach listing one of his players inside the window must not make the sweep think it already ran.
        bool already = await _db.RankedAuctions.AnyAsync(
            a => a.RankedGroupId == rankedGroupId && a.WindowIndex == windowIndex && a.SellerClubId == null, ct);
        if (already) return 0;

        var freeAgents = await _db.Players
            .Where(p => p.WorldId == worldId && p.ClubId == null)
            .Select(p => new { p.Id, p.ExternalId, p.MarketValue })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        // Task 12.2: the free-agent lots' duration is a knob now rather than "whenever the window shuts",
        // so both kinds of lot run on one mechanism. Clamped to the window's close, so with the shipped
        // defaults (a 24h lot in a 24h window) the behaviour is exactly what 9.2b had.
        var lotEnds = EarliestOf(now.AddSeconds(Math.Max(1, _opt.AuctionLotSeconds)), endsUtc);
        foreach (var fa in freeAgents)
        {
            _db.RankedAuctions.Add(new RankedAuction
            {
                Id = Guid.NewGuid(),
                RankedGroupId = rankedGroupId,
                PlayerId = fa.Id,
                PlayerExternalId = fa.ExternalId,
                WindowIndex = windowIndex,
                StartPrice = OpeningPrice(fa.MarketValue),
                HighBid = 0,
                Status = RankedAuctionStatus.Open,
                EndsUtc = lotEnds,
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
            .OrderBy(a => a.EndsUtc).ThenBy(a => a.Id)
            .ToListAsync(ct);
        if (lots.Count == 0) return 0;

        int settled = 0;
        var repairs = new List<(Guid GroupId, Guid ClubId)>();
        var sales = new List<(RankedAuction Lot, Player Player, TransferAssessment Assessment)>();

        foreach (var lot in lots)
        {
            settled++;

            // A winner is a CLUB with money on the table, not an account: since task 12.2 the leader may be
            // an AI club bidding on a coach's lot, and it has no user id behind it.
            bool hasWinner = lot.HighBidClubId is not null && lot.HighBid > 0;
            if (!hasWinner)
            {
                Close(lot, RankedAuctionStatus.Unsold, now);
                continue;
            }

            var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == lot.PlayerId, ct);
            var winner = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == lot.HighBidClubId!.Value, ct);
            if (player is null || winner is null || winner.TransferBudget < lot.HighBid)
            {
                Close(lot, RankedAuctionStatus.Unsold, now);
                continue;
            }

            // --- a free agent: nobody is paid, nothing is policed (the flat opening price has no relation
            // to market value by design, so an integrity band here would refuse the ladder's own lots).
            if (lot.SellerClubId is not { } sellerClubId)
            {
                if (player.ClubId is not null) { Close(lot, RankedAuctionStatus.Unsold, now); continue; }

                player.ClubId = winner.Id;
                player.ContractSeasonsRemaining = SignedContractSeasons;
                winner.TransferBudget = Math.Max(0, winner.TransferBudget - lot.HighBid);
                _db.Transfers.Add(NewTransfer(winner.WorldId, player.Id, null, winner.Id, lot.HighBid, lot.WindowIndex, now));
                Close(lot, RankedAuctionStatus.Settled, now);
                if (lot.HighBidUserId is { } faWinner)
                    await SafeSend(faWinner, "Asta vinta",
                        $"Hai vinto un'asta per {lot.HighBid:N0}.",
                        new Dictionary<string, string> { ["kind"] = "ranked_auction_won" }, ct);
                continue;
            }

            // --- a coach's own player: the money has a destination, so every 9.5 guard applies.
            var seller = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == sellerClubId, ct);
            if (seller is null || player.ClubId != sellerClubId)
            {
                Close(lot, RankedAuctionStatus.Unsold, now);
                continue;
            }

            // Defensive: listing already counts the board against the floor, so this should be unreachable.
            // It is kept because "unreachable" and "cannot happen after a season reset moved players around"
            // are not the same sentence.
            int sellerSquad = await _db.Players.CountAsync(p => p.ClubId == sellerClubId, ct);
            if (sellerSquad - 1 < _opt.MinSquadSizeForSale)
            {
                Close(lot, RankedAuctionStatus.Unsold, now);
                await SafeSend(lot.SellerUserId ?? Guid.Empty, "Asta annullata",
                    "L'asta è stata annullata: la rosa sarebbe scesa sotto il minimo.",
                    new Dictionary<string, string> { ["kind"] = "ranked_lot_unsold" }, ct);
                continue;
            }

            // COLLUSION GUARD, second half (Phase 9.5): the WINNING price is the one that moves money, and a
            // pair of accounts can bid a lot to any number they like. Blocked ⇒ the lot dies unsold; nobody
            // is charged and nobody is paid.
            var assessment = TransferIntegrity.Assess(lot.HighBid, player.MarketValue, _integrityOpt.Bands());
            if (assessment.IsBlocked)
            {
                Close(lot, RankedAuctionStatus.Unsold, now);
                await _integrity.FlagAsync(
                    IntegrityFlagKind.BlockedTransfer, severity: 100, userId: lot.HighBidUserId,
                    subjectUserId: lot.SellerUserId, rankedGroupId: lot.RankedGroupId,
                    fee: lot.HighBid, marketValue: player.MarketValue,
                    details: $"{assessment.Reason} at auction settlement; fee={assessment.FeePercentOfValue}% of "
                             + $"value; player={player.ExternalId}",
                    ct);
                continue;
            }

            player.ClubId = winner.Id;
            player.ContractSeasonsRemaining = SignedContractSeasons;
            winner.TransferBudget = Math.Max(0, winner.TransferBudget - lot.HighBid);
            seller.TransferBudget += lot.HighBid;   // THE POINT OF THE TASK: the seller is paid.
            _db.Transfers.Add(NewTransfer(
                winner.WorldId, player.Id, sellerClubId, winner.Id, lot.HighBid, lot.WindowIndex, now));
            Close(lot, RankedAuctionStatus.Settled, now);

            repairs.Add((lot.RankedGroupId, sellerClubId));
            sales.Add((lot, player, assessment));
        }

        await _db.SaveChangesAsync(ct);

        // SMART DEFAULT (Phase 9.4), same hook the direct market uses: the seller just lost a player who may
        // have been in his stored XI — rebuild it now instead of letting it fail at the next kickoff.
        bool repaired = false;
        foreach (var (groupId, clubId) in repairs.Distinct())
            repaired |= await RankedInputDefaults.RepairAfterSquadChangeAsync(_db, groupId, clubId, ct);
        if (repaired) await _db.SaveChangesAsync(ct);

        foreach (var (lot, player, assessment) in sales)
        {
            if (assessment.IsSuspicious)
            {
                await _integrity.FlagAsync(
                    IntegrityFlagKind.SuspiciousTransfer, severity: SuspicionSeverity(assessment.FeePercentOfValue),
                    userId: lot.HighBidUserId, subjectUserId: lot.SellerUserId, rankedGroupId: lot.RankedGroupId,
                    fee: lot.HighBid, marketValue: player.MarketValue,
                    details: $"{assessment.Reason} at auction; fee={assessment.FeePercentOfValue}% of value; "
                             + $"player={player.ExternalId}",
                    ct);
            }

            // The repeated-pair heuristic only makes sense between two accounts — a bot's club has none.
            if (lot.HighBidUserId is { } buyerUid && lot.SellerUserId is { } sellerUid)
            {
                int tradesBetween = await _integrity.CompletedTradesBetweenAsync(
                    lot.RankedGroupId, buyerUid, sellerUid, ct);
                if (tradesBetween >= _integrityOpt.RepeatedTradesPerPairThreshold)
                    await _integrity.FlagAsync(
                        IntegrityFlagKind.RepeatedTradingPair, severity: Math.Min(100, tradesBetween * 20),
                        userId: buyerUid, subjectUserId: sellerUid, rankedGroupId: lot.RankedGroupId,
                        fee: lot.HighBid, marketValue: player.MarketValue,
                        details: $"{tradesBetween} completed transfers between the same two coaches in one group", ct);

                await SafeSend(sellerUid, "Giocatore venduto",
                    $"Hai venduto {PlayerName(player)} per {lot.HighBid:N0}.",
                    new Dictionary<string, string> { ["kind"] = "ranked_lot_sold" }, ct);
            }

            if (lot.HighBidUserId is { } winnerUid)
                await SafeSend(winnerUid, "Asta vinta",
                    $"Hai vinto l'asta per {PlayerName(player)}: {lot.HighBid:N0}.",
                    new Dictionary<string, string> { ["kind"] = "ranked_auction_won" }, ct);
        }

        return settled;
    }

    /// <summary>
    /// The AI clubs go shopping on the coaches' board (task 12.2, decided with the user). A ranked group is
    /// mostly vacant seats until the ladder fills, so a sell flow that only works when another human happens
    /// to be online would be a sell flow on paper — the same reasoning that put bot buyers into the private
    /// leagues in 12.1. Deliberately restrained: one raise per lot per tick, always at the lot's MINIMUM
    /// next bid, capped at a percentage of market value that sits inside the 9.5 band, and never on a
    /// free-agent lot. A coach can always come back over the top, and the bot stops where a real buyer would.
    /// </summary>
    public async Task<int> RunBotBidsAsync(CancellationToken ct = default)
    {
        if (!_opt.AiBidsOnSellerLots) return 0;

        var now = DateTime.UtcNow;
        var groupIds = await _db.RankedAuctions
            .Where(a => a.Status == RankedAuctionStatus.Open && a.SellerClubId != null && a.EndsUtc > now)
            .Select(a => a.RankedGroupId)
            .Distinct()
            .ToListAsync(ct);
        if (groupIds.Count == 0) return 0;

        int bids = 0;
        foreach (var groupId in groupIds)
        {
            // Every OPEN lot of the group: the seller lots are what the bots bid on, but a club's committed
            // money spans the whole board, free agents included.
            var lots = await _db.RankedAuctions
                .Where(a => a.RankedGroupId == groupId && a.Status == RankedAuctionStatus.Open)
                .ToListAsync(ct);

            var aiClubIds = await _db.RankedSeats
                .Where(s => s.RankedGroupId == groupId && s.UserId == null && s.ClubId != null)
                .Select(s => s.ClubId!.Value)
                .ToListAsync(ct);
            if (aiClubIds.Count == 0) continue;

            var aiClubs = await _db.Clubs
                .Where(c => aiClubIds.Contains(c.Id))
                .OrderBy(c => c.ExternalId)
                .ToListAsync(ct);

            // One squad read per bot, reused across the lots — a tick must stay linear in the board.
            var squads = new Dictionary<Guid, List<Player>>();
            foreach (var c in aiClubs)
                squads[c.Id] = await _db.Players.Where(p => p.ClubId == c.Id).ToListAsync(ct);

            foreach (var lot in lots.Where(a => a.SellerClubId != null && a.EndsUtc > now)
                                    .OrderBy(a => a.EndsUtc).ThenBy(a => a.Id))
            {
                var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == lot.PlayerId, ct);
                if (player is null) continue;

                long minNext = MinNextBid(lot);
                long valueCeiling = player.MarketValue * Math.Max(0, _opt.AiSellerLotMaxPercentOfValue) / 100;

                Club? best = null;
                int bestGap = int.MinValue;
                foreach (var club in aiClubs)
                {
                    if (club.Id == lot.SellerClubId || lot.HighBidClubId == club.Id) continue;

                    var squad = squads[club.Id];
                    int inRole = squad.Count(p => p.Role == player.Role);
                    int bestInRole = squad.Where(p => p.Role == player.Role)
                                          .Select(p => p.Overall).DefaultIfEmpty(0).Max();
                    // A bot buys for a reason: he is short in the role, or the player is an upgrade on what
                    // he has there. Anything else and the ladder's bots would hoover the board.
                    int gap = inRole < 2 ? 100 : player.Overall - bestInRole;
                    if (gap <= 0) continue;

                    long committed = lots.Where(a => a.Id != lot.Id && a.HighBidClubId == club.Id).Sum(a => a.HighBid);
                    long ceiling = Math.Min(club.TransferBudget - committed, valueCeiling);
                    if (ceiling < minNext) continue;

                    if (gap > bestGap) { best = club; bestGap = gap; }
                }
                if (best is null) continue;

                Guid? prevUserId = lot.HighBidUserId;
                lot.HighBid = minNext;
                lot.HighBidClubId = best.Id;
                lot.HighBidClubExternalId = best.ExternalId;
                lot.HighBidUserId = null;   // a bot has no account behind it
                if (_opt.AuctionAntiSnipeSeconds > 0
                    && (lot.EndsUtc - now).TotalSeconds < _opt.AuctionAntiSnipeSeconds)
                    lot.EndsUtc = now.AddSeconds(_opt.AuctionAntiSnipeSeconds);
                bids++;

                await _db.SaveChangesAsync(ct);

                if (prevUserId is { } prev)
                    await SafeSend(prev, "Offerta superata",
                        "Sei stato superato in un'asta.",
                        new Dictionary<string, string> { ["kind"] = "ranked_outbid", ["auctionId"] = lot.Id.ToString() }, ct);
                if (lot.SellerUserId is { } sellerUid)
                    await SafeSend(sellerUid, "Rilancio sul tuo giocatore",
                        $"C'è un'offerta di {minNext:N0} sul giocatore che hai messo all'asta.",
                        new Dictionary<string, string> { ["kind"] = "ranked_lot_bid", ["auctionId"] = lot.Id.ToString() }, ct);
            }
        }
        return bids;
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// What a FREE-AGENT lot opens at. With <see cref="RankedOptions.AuctionFlatStartPrice"/> set (the
    /// ladder default) EVERY player opens at the SAME price, champion and squad filler alike, and the
    /// auction decides what he is worth: with equal budgets, anyone can open on anyone, and taking a star
    /// means giving up the three good players his final price would have bought. Set it to 0 to fall back on
    /// the private-league rule (half of market value), where the valuation model prices the lot before
    /// anyone bids and the top of the market is simply out of reach. A SELLER's lot does not come through
    /// here — his reserve is his own decision, policed by the 9.5 band.
    /// </summary>
    private long OpeningPrice(long marketValue) =>
        _opt.AuctionFlatStartPrice > 0
            ? Math.Max(MinStartPrice, _opt.AuctionFlatStartPrice)
            : Math.Max(MinStartPrice, marketValue * StartPricePermille / 1000);

    private static long MinNextBid(RankedAuction lot) =>
        lot.HighBid <= 0
            ? lot.StartPrice
            : lot.HighBid + Math.Max(MinIncrementFloor, lot.HighBid * MinIncrementPermille / 1000);

    private static void Close(RankedAuction lot, RankedAuctionStatus status, DateTime now)
    {
        lot.Status = status;
        lot.SettledUtc = now;
    }

    private static Transfer NewTransfer(
        Guid worldId, Guid playerId, Guid? fromClubId, Guid toClubId, long fee, int windowIndex, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            PlayerId = playerId,
            FromClubId = fromClubId,
            ToClubId = toClubId,
            Fee = fee,
            SeasonYear = 0,
            Day = windowIndex,
            CreatedUtc = now,
        };

    private static DateTime EarliestOf(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>A lot never outlives the market that allowed it (decided with the user): the chosen
    /// duration is truncated at the window's close.</summary>
    private static DateTime ClampToWindow(DateTime ends, RankedCalendar.MarketWindow window) =>
        EarliestOf(ends, window.ClosesUtc);

    /// <summary>How loud a grey-band flag is — mirrors <c>RankedMarketService</c> so an auction and a direct
    /// offer at the same price produce the same severity.</summary>
    private static int SuspicionSeverity(int feePercentOfValue) =>
        Math.Clamp(Math.Abs(100 - feePercentOfValue), 10, 90);

    private async Task<(RankedGroup? group, RankedSeat? seat)> CurrentGroupSeatAsync(
        RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is not { } seatId) return (null, null);
        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return (null, null);
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct);
        return (group, seat);
    }

    private async Task<int> TotalRoundsAsync(RankedGroup group, CancellationToken ct) =>
        (await _db.RankedFixtures
            .Where(f => f.RankedGroupId == group.Id)
            .Select(f => (int?)f.Round)
            .MaxAsync(ct)) ?? 0;

    /// <summary>The window open right now, or null when the market is shut.</summary>
    private async Task<RankedCalendar.MarketWindow?> CurrentWindowAsync(RankedGroup group, CancellationToken ct)
    {
        if (group.SeasonStartedUtc is not { } start) return null;
        int totalRounds = await TotalRoundsAsync(group, ct);
        if (totalRounds == 0) return null;
        return RankedCalendar.CurrentWindow(
            start, totalRounds, _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, DateTime.UtcNow);
    }

    private async Task<(bool open, int index)> MarketWindowAsync(RankedGroup group, CancellationToken ct)
    {
        var w = await CurrentWindowAsync(group, ct);
        return w is { } win ? (true, win.Index) : (false, -1);
    }

    private async Task<IReadOnlyList<Guid>> OtherHumansAsync(Guid groupId, Guid exceptUserId, CancellationToken ct) =>
        await _db.RankedSeats
            .Where(s => s.RankedGroupId == groupId && s.UserId != null && s.UserId != exceptUserId)
            .Select(s => s.UserId!.Value)
            .ToListAsync(ct);

    private async Task<RankedAuctionsDto> BuildAuctionsAsync(
        RankedGroup? group, RankedSeat? seat, Guid userId, CancellationToken ct)
    {
        long budget = 0;
        int yourClubExternal = 0;
        if (seat?.ClubId is { } clubId)
        {
            var mine = await _db.Clubs.Where(c => c.Id == clubId)
                .Select(c => new { c.TransferBudget, c.ExternalId }).FirstOrDefaultAsync(ct);
            if (mine is not null) { budget = mine.TransferBudget; yourClubExternal = mine.ExternalId; }
        }

        int minLot = Math.Max(1, _opt.SellerLotMinSeconds);
        if (group is null)
            return new RankedAuctionsDto(
                budget, 0, budget, false, Array.Empty<RankedAuctionLotDto>(),
                null, null, minLot, 0, yourClubExternal);

        var now = DateTime.UtcNow;
        var window = await CurrentWindowAsync(group, ct);

        DateTime? nextOpens = null;
        if (group.SeasonStartedUtc is { } start)
        {
            int totalRounds = await TotalRoundsAsync(group, ct);
            if (totalRounds > 0)
                nextOpens = RankedCalendar.NextWindow(
                    start, totalRounds, _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, now)
                    ?.OpensUtc;
        }

        // What the duration picker may offer RIGHT NOW: the configured ceiling, cut down to what is left of
        // the window. 0 when the market is shut — there is nothing to list into.
        int maxLot = 0;
        if (window is { } w)
        {
            int left = (int)Math.Max(0, Math.Min(int.MaxValue, (w.ClosesUtc - now).TotalSeconds));
            maxLot = Math.Min(Math.Max(minLot, _opt.SellerLotMaxSeconds), left);
        }

        var lots = await _db.RankedAuctions
            .Where(a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open)
            .OrderBy(a => a.EndsUtc).ThenByDescending(a => a.HighBid).ThenBy(a => a.PlayerExternalId)
            .ToListAsync(ct);

        long committed = lots.Where(a => a.HighBidUserId == userId).Sum(a => a.HighBid);

        var playerIds = lots.Select(a => a.PlayerId).ToList();
        var players = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var clubNames = await _db.Clubs
            .Where(c => c.WorldId == group.WorldId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var dtos = lots.Select(a =>
        {
            players.TryGetValue(a.PlayerId, out var p);
            return MapLot(a, p, clubNames, userId, now);
        }).ToList();

        return new RankedAuctionsDto(
            budget, committed, budget - committed, window is not null, dtos,
            window?.ClosesUtc, nextOpens, minLot, maxLot, yourClubExternal);
    }

    private async Task<RankedAuctionLotDto> MapLotAsync(
        RankedAuction lot, Guid userId, DateTime now, CancellationToken ct)
    {
        var p = await _db.Players.FirstOrDefaultAsync(x => x.Id == lot.PlayerId, ct);
        var names = new Dictionary<Guid, string>();
        if (lot.SellerClubId is { } sellerId)
        {
            var name = await _db.Clubs.Where(c => c.Id == sellerId).Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (name is not null) names[sellerId] = name;
        }
        return MapLot(lot, p, names, userId, now);
    }

    private static RankedAuctionLotDto MapLot(
        RankedAuction lot, Player? p, IReadOnlyDictionary<Guid, string> clubNames, Guid userId, DateTime now) =>
        new(
            Id: lot.Id,
            PlayerExternalId: lot.PlayerExternalId,
            PlayerName: p is null ? string.Empty : PlayerName(p),
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
            SecondsRemaining: SecondsLeft(lot.EndsUtc, now),
            Kind: lot.SellerClubId is null ? RankedLotKind.FreeAgent : RankedLotKind.Seller,
            SellerClubExternalId: lot.SellerClubExternalId,
            SellerClubName: lot.SellerClubId is { } sid && clubNames.TryGetValue(sid, out var n) ? n : string.Empty,
            YouAreSeller: lot.SellerUserId == userId);

    private static int SecondsLeft(DateTime endsUtc, DateTime now) =>
        endsUtc <= now ? 0 : (int)Math.Min(int.MaxValue, (endsUtc - now).TotalSeconds);

    private static string Combine(string first, string last) =>
        string.IsNullOrEmpty(first) ? last : $"{first} {last}";

    private static string PlayerName(Player p) => Combine(p.FirstName, p.LastName);

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        if (userId == Guid.Empty) return;
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* best-effort. */ }
    }
}
