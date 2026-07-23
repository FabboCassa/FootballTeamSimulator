using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedMarketService"/> implementation (Phase 9.2b) — direct coach-to-coach transfers in a
/// ranked season's market windows. Each club is seeded a budget when its season starts (see
/// <c>RankedSeasonService</c>); a coach browses a rival's squad, offers a fee for a player, and the owner
/// accepts (the player changes clubs, both budgets settle, a <see cref="Transfer"/> is logged) or rejects.
/// Everything is gated to an open market window (via <see cref="RankedCalendar"/>). Budgets are re-validated
/// at accept time (no reservation) — a friend-scale v1 simplification, like the 8.5 auctions' documented
/// limitations. NO Sim.Core change: this is pure server bookkeeping over the persisted world.
/// </summary>
public sealed class RankedMarketService : IRankedMarketService
{
    private const int SignedContractSeasons = 4;

    private readonly FtsDbContext _db;
    private readonly INotificationService _notify;
    private readonly RankedOptions _opt;

    public RankedMarketService(FtsDbContext db, INotificationService notify, IOptions<RankedOptions> options)
    {
        _db = db;
        _notify = notify;
        _opt = options.Value;
    }

    // --- browse ------------------------------------------------------------------------------------

    public async Task<RankedResult<RankedSquadDto>> GetClubSquadAsync(
        Guid userId, int clubExternalId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedSquadDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, _) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null)
            return RankedResult<RankedSquadDto>.Fail(RankedError.NotFound, "You are not in a ranked group.");

        var club = await _db.Clubs.FirstOrDefaultAsync(
            c => c.WorldId == group.WorldId && c.ExternalId == clubExternalId, ct);
        if (club is null)
            return RankedResult<RankedSquadDto>.Fail(RankedError.NotFound, "No such club in your group.");

        var players = await _db.Players
            .Where(p => p.ClubId == club.Id)
            .OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId)
            .ToListAsync(ct);
        bool isHuman = await _db.RankedSeats.AnyAsync(
            s => s.RankedGroupId == group.Id && s.ClubId == club.Id && s.UserId != null, ct);

        return RankedResult<RankedSquadDto>.Ok(new RankedSquadDto(
            club.ExternalId, club.Name, isHuman,
            players.Select(p => new RankedPlayerDto(
                p.ExternalId, PlayerName(p), p.Age, p.Role, p.Overall, p.MarketValue)).ToList()));
    }

    public async Task<RankedResult<RankedOffersDto>> GetOffersAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        return RankedResult<RankedOffersDto>.Ok(await BuildOffersAsync(group, seat, userId, ct));
    }

    // --- make / respond / withdraw -----------------------------------------------------------------

    public async Task<RankedResult<RankedOffersDto>> MakeOfferAsync(
        Guid userId, MakeRankedOfferRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } buyerClubId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked club.");

        var (open, windowIndex) = await MarketWindowAsync(group, ct);
        if (!open)
            return RankedResult<RankedOffersDto>.Fail(RankedError.WrongPhase, "The market is closed right now.");
        if (request.Fee <= 0)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "The fee must be positive.");

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.WorldId == group.WorldId && p.ExternalId == request.PlayerExternalId, ct);
        if (player is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotFound, "No such player in your group.");
        if (player.ClubId is not { } sellerClubId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "That player is a free agent.");
        if (sellerClubId == buyerClubId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "You cannot buy your own player.");

        var sellerSeat = await _db.RankedSeats.FirstOrDefaultAsync(
            s => s.RankedGroupId == group.Id && s.ClubId == sellerClubId && s.UserId != null, ct);
        if (sellerSeat?.UserId is not { } sellerUserId)
            return RankedResult<RankedOffersDto>.Fail(
                RankedError.ValidationFailed, "That club is run by the AI — you can only send offers to coaches.");

        var buyerClub = await _db.Clubs.FirstAsync(c => c.Id == buyerClubId, ct);
        if (buyerClub.TransferBudget < request.Fee)
            return RankedResult<RankedOffersDto>.Fail(RankedError.InsufficientBudget, "Your budget cannot cover that fee.");

        var now = DateTime.UtcNow;
        // Upsert: one live pending offer per (buyer, player) — re-offering just updates the fee.
        var existing = await _db.RankedOffers.FirstOrDefaultAsync(
            o => o.RankedGroupId == group.Id && o.BuyerUserId == userId
                 && o.PlayerId == player.Id && o.Status == RankedOfferStatus.Pending, ct);
        if (existing is null)
        {
            _db.RankedOffers.Add(new RankedOffer
            {
                Id = Guid.NewGuid(),
                RankedGroupId = group.Id,
                WindowIndex = windowIndex,
                PlayerId = player.Id,
                PlayerExternalId = player.ExternalId,
                BuyerUserId = userId,
                BuyerClubId = buyerClubId,
                SellerUserId = sellerUserId,
                SellerClubId = sellerClubId,
                Fee = request.Fee,
                Status = RankedOfferStatus.Pending,
                CreatedUtc = now,
            });
        }
        else
        {
            existing.Fee = request.Fee;
            existing.WindowIndex = windowIndex;
            existing.CreatedUtc = now;
        }
        await _db.SaveChangesAsync(ct);

        await SafeSend(sellerUserId, "Offerta ricevuta",
            $"Hai ricevuto un'offerta di {request.Fee:N0} per {PlayerName(player)}.",
            new Dictionary<string, string> { ["kind"] = "ranked_offer", ["groupId"] = group.Id.ToString() }, ct);

        return RankedResult<RankedOffersDto>.Ok(await BuildOffersAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<RankedOffersDto>> RespondAsync(
        Guid userId, Guid offerId, bool accept, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var offer = await _db.RankedOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotFound, "Offer not found.");
        if (offer.SellerUserId != userId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.Forbidden, "Only the player's owner can respond.");
        if (offer.Status != RankedOfferStatus.Pending)
            return RankedResult<RankedOffersDto>.Fail(RankedError.WrongPhase, "This offer has already been resolved.");

        var group = await _db.RankedGroups.FirstAsync(g => g.Id == offer.RankedGroupId, ct);
        var (group2, seat) = await CurrentGroupSeatAsync(coach, ct);
        var now = DateTime.UtcNow;

        if (!accept)
        {
            offer.Status = RankedOfferStatus.Rejected;
            offer.ResolvedUtc = now;
            await _db.SaveChangesAsync(ct);
            await SafeSend(offer.BuyerUserId, "Offerta rifiutata",
                "La tua offerta è stata rifiutata.",
                new Dictionary<string, string> { ["kind"] = "ranked_offer_rejected" }, ct);
            return RankedResult<RankedOffersDto>.Ok(await BuildOffersAsync(group2, seat, userId, ct));
        }

        var (open, _) = await MarketWindowAsync(group, ct);
        if (!open)
            return RankedResult<RankedOffersDto>.Fail(RankedError.WrongPhase, "The market is closed right now.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == offer.PlayerId, ct);
        if (player is null || player.ClubId != offer.SellerClubId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "That player is no longer available.");

        var buyerClub = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == offer.BuyerClubId, ct);
        var sellerClub = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == offer.SellerClubId, ct);
        if (buyerClub is null || sellerClub is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "A club in the deal no longer exists.");
        if (buyerClub.TransferBudget < offer.Fee)
            return RankedResult<RankedOffersDto>.Fail(RankedError.InsufficientBudget, "The buyer can no longer afford this.");

        int sellerSquad = await _db.Players.CountAsync(p => p.ClubId == offer.SellerClubId, ct);
        if (sellerSquad - 1 < _opt.MinSquadSizeForSale)
            return RankedResult<RankedOffersDto>.Fail(RankedError.ValidationFailed, "Selling would leave your squad too small.");

        // Execute the transfer.
        player.ClubId = offer.BuyerClubId;
        player.ContractSeasonsRemaining = SignedContractSeasons;
        buyerClub.TransferBudget = Math.Max(0, buyerClub.TransferBudget - offer.Fee);
        sellerClub.TransferBudget += offer.Fee;
        _db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            WorldId = group.WorldId!.Value,
            PlayerId = player.Id,
            FromClubId = offer.SellerClubId,
            ToClubId = offer.BuyerClubId,
            Fee = offer.Fee,
            SeasonYear = 0,
            Day = offer.WindowIndex,
            CreatedUtc = now,
        });
        offer.Status = RankedOfferStatus.Accepted;
        offer.ResolvedUtc = now;
        await _db.SaveChangesAsync(ct);

        await SafeSend(offer.BuyerUserId, "Offerta accettata",
            $"Hai acquistato {PlayerName(player)} per {offer.Fee:N0}.",
            new Dictionary<string, string> { ["kind"] = "ranked_offer_accepted" }, ct);

        return RankedResult<RankedOffersDto>.Ok(await BuildOffersAsync(group2, seat, userId, ct));
    }

    public async Task<RankedResult<RankedOffersDto>> WithdrawAsync(
        Guid userId, Guid offerId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var offer = await _db.RankedOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null)
            return RankedResult<RankedOffersDto>.Fail(RankedError.NotFound, "Offer not found.");
        if (offer.BuyerUserId != userId)
            return RankedResult<RankedOffersDto>.Fail(RankedError.Forbidden, "Only the coach who made the offer can withdraw it.");
        if (offer.Status != RankedOfferStatus.Pending)
            return RankedResult<RankedOffersDto>.Fail(RankedError.WrongPhase, "This offer has already been resolved.");

        offer.Status = RankedOfferStatus.Withdrawn;
        offer.ResolvedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await SafeSend(offer.SellerUserId, "Offerta ritirata",
            "Un'offerta che avevi ricevuto è stata ritirata.",
            new Dictionary<string, string> { ["kind"] = "ranked_offer_withdrawn" }, ct);

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        return RankedResult<RankedOffersDto>.Ok(await BuildOffersAsync(group, seat, userId, ct));
    }

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<(RankedGroup? group, RankedSeat? seat)> CurrentGroupSeatAsync(
        RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is not { } seatId) return (null, null);
        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return (null, null);
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct);
        return (group, seat);
    }

    /// <summary>Whether a market window is open for the group right now, and which one.</summary>
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

    private async Task<RankedOffersDto> BuildOffersAsync(
        RankedGroup? group, RankedSeat? seat, Guid userId, CancellationToken ct)
    {
        long budget = 0;
        if (seat?.ClubId is { } clubId)
            budget = await _db.Clubs.Where(c => c.Id == clubId).Select(c => c.TransferBudget).FirstOrDefaultAsync(ct);

        if (group is null)
            return new RankedOffersDto(budget, false, Array.Empty<RankedOfferDto>(), Array.Empty<RankedOfferDto>());

        var (open, _) = await MarketWindowAsync(group, ct);

        var offers = await _db.RankedOffers
            .Where(o => o.RankedGroupId == group.Id && (o.BuyerUserId == userId || o.SellerUserId == userId))
            .OrderByDescending(o => o.CreatedUtc)
            .ToListAsync(ct);

        var clubs = await _db.Clubs.Where(c => c.WorldId == group.WorldId)
            .Select(c => new { c.Id, c.ExternalId, c.Name })
            .ToListAsync(ct);
        var clubById = clubs.ToDictionary(c => c.Id);

        var playerIds = offers.Select(o => o.PlayerId).Distinct().ToList();
        var playerNames = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.Id, p => Combine(p.FirstName, p.LastName), ct);

        RankedOfferDto Map(RankedOffer o) => new(
            Id: o.Id,
            WindowIndex: o.WindowIndex,
            PlayerExternalId: o.PlayerExternalId,
            PlayerName: playerNames.TryGetValue(o.PlayerId, out var pn) ? pn : string.Empty,
            BuyerClubExternalId: clubById.TryGetValue(o.BuyerClubId, out var bc) ? bc.ExternalId : 0,
            BuyerClubName: clubById.TryGetValue(o.BuyerClubId, out var bc2) ? bc2.Name : string.Empty,
            SellerClubExternalId: clubById.TryGetValue(o.SellerClubId, out var sc) ? sc.ExternalId : 0,
            SellerClubName: clubById.TryGetValue(o.SellerClubId, out var sc2) ? sc2.Name : string.Empty,
            Fee: o.Fee,
            Status: o.Status,
            YouAreBuyer: o.BuyerUserId == userId,
            YouAreSeller: o.SellerUserId == userId);

        var incoming = offers.Where(o => o.SellerUserId == userId).Select(Map).ToList();
        var outgoing = offers.Where(o => o.BuyerUserId == userId).Select(Map).ToList();
        return new RankedOffersDto(budget, open, incoming, outgoing);
    }

    private static string Combine(string first, string last) =>
        string.IsNullOrEmpty(first) ? last : $"{first} {last}";

    private static string PlayerName(Player p) => Combine(p.FirstName, p.LastName);

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* best-effort — a push failure must never break a transfer. */ }
    }
}
