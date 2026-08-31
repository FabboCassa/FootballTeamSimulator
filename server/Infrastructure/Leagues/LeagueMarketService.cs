using Fts.Application.Balance;
using Fts.Application.Integrity;
using Fts.Application.Leagues;
using Fts.Application.Notifications;
using Fts.Infrastructure.Integrity;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sim.Core.Config;
using Sim.Core.Market;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using EntPlayer = Fts.Infrastructure.Persistence.Entities.Player;
using SimClub = Sim.Core.Domain.Club;
using SimPlayer = Sim.Core.Domain.Player;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// <see cref="ILeagueMarketService"/> (Phase 12.1) — a private league trades like the single-player career,
/// server-authoritatively. Everything the negotiation needs already exists in <c>Sim.Core/Market</c> and is
/// what runs here: <see cref="ValuationModel"/> prices the player, <see cref="SquadAnalysis"/> says whether
/// his club can afford to lose him and how important he is, and <see cref="NegotiationModel"/> decides what
/// a club will accept. The server only owns the bookkeeping — who holds which club, whose budget moves, and
/// what the other coach is allowed to see.
///
/// THE RULE THAT SHAPES THIS FILE: a human-run club is treated EXACTLY like a bot club (decision taken with
/// the user, 2026-08-25). Same valuation, same asking price, same floors, same guards. The one difference is
/// WHO answers: a bot answers inside the request that provoked it, a human answers when he opens the app. And
/// if he never does, the offer expires when the round resolves — no AI ever answers in his place, because a
/// private league is meant to be played together (the user's own words). The unanswered count rides on the
/// league summary so the home screen can shout about it.
///
/// NO Sim.Core change: this is pure server bookkeeping over the persisted world, so golden masters, replays
/// and the state hash are untouched.
/// </summary>
public sealed class LeagueMarketService : ILeagueMarketService
{
    /// <summary>How many completed deals the news feed carries.</summary>
    private const int NewsPageSize = 30;

    /// <summary>How many free agents the market screen lists (best first).</summary>
    private const int FreeAgentPageSize = 60;

    private readonly FtsDbContext _db;
    private readonly BalanceConfig _config;
    private readonly TransferBalance _t;
    private readonly INotificationService _notify;
    private readonly IIntegrityService _integrity;
    private readonly IntegrityOptions _integrityOpt;
    private readonly LeagueMarketEngine _engine;

    public LeagueMarketService(
        FtsDbContext db,
        IBalanceProvider balance,
        INotificationService notify,
        IIntegrityService integrity,
        IOptions<IntegrityOptions> integrityOptions)
    {
        _db = db;
        _config = balance.Current;
        _t = _config.Transfer;
        _notify = notify;
        _integrity = integrity;
        _integrityOpt = integrityOptions.Value;
        _engine = new LeagueMarketEngine(db, _config);
    }

    // --- read --------------------------------------------------------------------------------------

    public async Task<LeagueResult<LeagueMarketDto>> GetMarketAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var (league, me, error, message) = await ResolveMemberAsync(userId, leagueId, ct);
        if (league is null || me is null) return LeagueResult<LeagueMarketDto>.Fail(error, message);
        return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
    }

    // --- buy ---------------------------------------------------------------------------------------

    public async Task<LeagueResult<LeagueMarketDto>> MakeOfferAsync(
        Guid userId, Guid leagueId, MakeLeagueOfferRequest request, CancellationToken ct = default)
    {
        var (league, me, error, message) = await ResolveMemberAsync(userId, leagueId, ct);
        if (league is null || me is null) return LeagueResult<LeagueMarketDto>.Fail(error, message);
        if (me.ClubId is not { } buyerClubId)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.NotAssignedClub, "You do not hold a club in this league.");

        var window = await WindowAsync(league, ct);
        if (!window.Open)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.MarketClosed, "The transfer market is shut right now.");
        if (request.Fee <= 0)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.ValidationFailed, "The fee must be positive.");

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.WorldId == league.WorldId && p.ExternalId == request.PlayerExternalId, ct);
        if (player is null)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.NotFound, "No such player in this league.");
        if (player.ClubId is not { } sellerClubId)
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.ValidationFailed, "That player is a free agent — agree terms with him instead.");
        if (sellerClubId == buyerClubId)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.ValidationFailed, "He already plays for you.");

        var buyerClub = await _db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == buyerClubId, ct);
        var sellerClub = await _db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == sellerClubId, ct);

        if (buyerClub.Players.Count >= LeagueMarketEngine.MaxSquadSize)
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.SquadFull, $"Your squad is full ({LeagueMarketEngine.MaxSquadSize} players).");
        if (sellerClub.Players.Count - 1 < _t.MinSquadSize)
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.SquadTooSmall, "Selling him would leave that squad below the legal minimum.");
        if (buyerClub.TransferBudget < request.Fee)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.InsufficientBudget, "Your budget cannot cover that fee.");

        // COLLUSION GUARD (Phase 9.5), applied here exactly as in ranked: a transfer must look like a
        // transfer. A star handed over for pocket change is a free squad boost; a wildly inflated fee is a
        // budget transfused between accounts. Both are refused and recorded.
        var assessment = TransferIntegrity.Assess(request.Fee, player.MarketValue, _integrityOpt.Bands());
        if (assessment.IsBlocked)
        {
            await _integrity.FlagAsync(
                IntegrityFlagKind.BlockedTransfer, severity: 100, userId: userId,
                subjectUserId: await UserOfClubAsync(league.Id, sellerClubId, ct), rankedGroupId: null,
                fee: request.Fee, marketValue: player.MarketValue,
                details: $"{assessment.Reason}; league offer; fee={assessment.FeePercentOfValue}% of value; "
                         + $"player={player.ExternalId}",
                ct);
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.IntegrityBlocked,
                $"That fee is {assessment.FeePercentOfValue}% of his market value — offers must stay between "
                + $"{_integrityOpt.MinFeePercentOfValue}% and {_integrityOpt.MaxFeePercentOfValue}%.");
        }

        Guid? sellerUserId = await UserOfClubAsync(league.Id, sellerClubId, ct);
        var now = DateTime.UtcNow;

        // One live negotiation per (buyer, player): offering again just moves the figure.
        var offer = await _db.LeagueOffers.FirstOrDefaultAsync(
            o => o.PrivateLeagueId == league.Id && o.BuyerClubId == buyerClubId
                 && o.PlayerId == player.Id && o.Status == LeagueOfferStatus.Pending, ct);

        // If a bot seller has already countered, THAT is the ask it must concede from — not a fresh one.
        long standingAsk = offer is { ProposedBy: LeagueOfferParty.Seller } ? offer.Amount : 0;

        if (offer is null)
        {
            offer = new LeagueOffer
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = league.Id,
                WorldId = league.WorldId,
                WindowIndex = window.WindowIndex,
                PlayerId = player.Id,
                PlayerExternalId = player.ExternalId,
                BuyerClubId = buyerClubId,
                BuyerClubExternalId = buyerClub.ExternalId,
                BuyerUserId = userId,
                SellerClubId = sellerClubId,
                SellerClubExternalId = sellerClub.ExternalId,
                SellerUserId = sellerUserId,
                Amount = request.Fee,
                ProposedBy = LeagueOfferParty.Buyer,
                Rounds = 1,
                Status = LeagueOfferStatus.Pending,
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            _db.LeagueOffers.Add(offer);
        }
        else
        {
            offer.Amount = request.Fee;
            offer.ProposedBy = LeagueOfferParty.Buyer;
            offer.WindowIndex = window.WindowIndex;
            offer.Rounds++;
            offer.UpdatedUtc = now;
        }

        if (sellerUserId is null)
        {
            // A bot seller answers now, with the same model the career uses.
            await AnswerAsSellerBotAsync(league, offer, player, sellerClub, standingAsk, now, ct);
        }
        else
        {
            await _db.SaveChangesAsync(ct);
            await SafeSend(sellerUserId.Value, "Offerta ricevuta",
                $"{buyerClub.Name} offre {request.Fee:N0} per {FullName(player)}.",
                new Dictionary<string, string>
                {
                    ["kind"] = "league_offer",
                    ["leagueId"] = league.Id.ToString(),
                }, ct);
        }

        return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
    }

    // --- answer ------------------------------------------------------------------------------------

    public async Task<LeagueResult<LeagueMarketDto>> RespondAsync(
        Guid userId, Guid leagueId, Guid offerId, RespondLeagueOfferRequest request, CancellationToken ct = default)
    {
        var (league, me, error, message) = await ResolveMemberAsync(userId, leagueId, ct);
        if (league is null || me is null) return LeagueResult<LeagueMarketDto>.Fail(error, message);

        var offer = await _db.LeagueOffers.FirstOrDefaultAsync(
            o => o.Id == offerId && o.PrivateLeagueId == league.Id, ct);
        if (offer is null)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.OfferNotFound, "That negotiation does not exist.");
        if (offer.Status != LeagueOfferStatus.Pending)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.OfferResolved, "That negotiation is already over.");

        bool youAreBuyer = offer.BuyerUserId == userId;
        bool youAreSeller = offer.SellerUserId == userId;
        if (!youAreBuyer && !youAreSeller)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.Forbidden, "That negotiation is not yours.");

        var now = DateTime.UtcNow;
        var window = await WindowAsync(league, ct);

        if (request.Action == LeagueOfferAction.Withdraw)
        {
            if (!youAreBuyer)
                return LeagueResult<LeagueMarketDto>.Fail(LeagueError.Forbidden, "Only the buyer can withdraw an offer.");
            offer.Status = LeagueOfferStatus.Withdrawn;
            offer.UpdatedUtc = now;
            offer.ResolvedUtc = now;
            await _db.SaveChangesAsync(ct);
            if (offer.SellerUserId is { } sellerOut)
                await SafeSend(sellerOut, "Offerta ritirata", "Un'offerta che avevi ricevuto è stata ritirata.",
                    new Dictionary<string, string> { ["kind"] = "league_offer_withdrawn" }, ct);
            return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
        }

        // Everything else is an answer, and only the side being WAITED ON may answer.
        var awaiting = offer.ProposedBy == LeagueOfferParty.Buyer ? LeagueOfferParty.Seller : LeagueOfferParty.Buyer;
        bool yourTurn = awaiting == LeagueOfferParty.Seller ? youAreSeller : youAreBuyer;
        if (!yourTurn)
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.NotYourTurn, "The other coach has not answered your last figure yet.");

        if (request.Action == LeagueOfferAction.Reject)
        {
            offer.Status = LeagueOfferStatus.Rejected;
            offer.UpdatedUtc = now;
            offer.ResolvedUtc = now;
            await _db.SaveChangesAsync(ct);
            await NotifyOtherSideAsync(offer, youAreBuyer, "Trattativa chiusa", "La tua proposta è stata rifiutata.", ct);
            return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
        }

        if (!window.Open)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.MarketClosed, "The transfer market is shut right now.");

        if (request.Action == LeagueOfferAction.Accept)
        {
            var deal = await TryExecuteAsync(league, offer, offer.Amount, now, ct);
            return deal.Success
                ? LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct))
                : LeagueResult<LeagueMarketDto>.Fail(deal.Error, deal.Message);
        }

        // --- Counter -------------------------------------------------------------------------------
        if (request.Amount <= 0)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.ValidationFailed, "A counter needs a figure.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == offer.PlayerId, ct);
        if (player is null || player.ClubId != offer.SellerClubId)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.PlayerUnavailable, "He is no longer available.");

        if (youAreBuyer)
        {
            var buyerClub = await _db.Clubs.FirstAsync(c => c.Id == offer.BuyerClubId, ct);
            if (buyerClub.TransferBudget < request.Amount)
                return LeagueResult<LeagueMarketDto>.Fail(LeagueError.InsufficientBudget, "Your budget cannot cover that.");

            var assessment = TransferIntegrity.Assess(request.Amount, player.MarketValue, _integrityOpt.Bands());
            if (assessment.IsBlocked)
                return LeagueResult<LeagueMarketDto>.Fail(
                    LeagueError.IntegrityBlocked,
                    $"That figure is {assessment.FeePercentOfValue}% of his market value — it is outside the allowed band.");
        }

        long previous = offer.Amount;
        offer.Amount = request.Amount;
        offer.ProposedBy = youAreBuyer ? LeagueOfferParty.Buyer : LeagueOfferParty.Seller;
        offer.Rounds++;
        offer.UpdatedUtc = now;

        // Talks that go nowhere end, exactly as they do in the career (the same round cap).
        if (offer.Rounds > _t.MaxNegotiationRounds)
        {
            offer.Status = LeagueOfferStatus.Rejected;
            offer.ResolvedUtc = now;
            await _db.SaveChangesAsync(ct);
            await NotifyOtherSideAsync(offer, youAreBuyer, "Trattativa chiusa",
                "Le parti non hanno trovato un accordo.", ct);
            return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
        }

        bool otherSideIsBot = youAreBuyer ? offer.SellerUserId is null : offer.BuyerUserId is null;
        if (!otherSideIsBot)
        {
            await _db.SaveChangesAsync(ct);
            await NotifyOtherSideAsync(offer, youAreBuyer, "Controproposta",
                $"Nuova cifra sul tavolo: {request.Amount:N0}.", ct);
            return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
        }

        if (youAreBuyer)
        {
            var sellerClub = await _db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == offer.SellerClubId, ct);
            // `previous` was the bot's own ask — it concedes from there, it does not start again.
            await AnswerAsSellerBotAsync(league, offer, player, sellerClub, previous, now, ct);
        }
        else
        {
            await AnswerAsBuyerBotAsync(league, offer, player, previous, now, ct);
        }

        return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
    }

    // --- sell: the transfer list -------------------------------------------------------------------

    public async Task<LeagueResult<LeagueMarketDto>> SetListingAsync(
        Guid userId, Guid leagueId, ListPlayerRequest request, CancellationToken ct = default)
    {
        var (league, me, error, message) = await ResolveMemberAsync(userId, leagueId, ct);
        if (league is null || me is null) return LeagueResult<LeagueMarketDto>.Fail(error, message);
        if (me.ClubId is not { } clubId)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.NotAssignedClub, "You do not hold a club in this league.");

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.WorldId == league.WorldId && p.ExternalId == request.PlayerExternalId, ct);
        if (player is null)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.NotFound, "No such player in this league.");
        if (player.ClubId != clubId)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.Forbidden, "He is not your player.");

        var existing = await _db.LeagueListings.FirstOrDefaultAsync(
            l => l.PrivateLeagueId == league.Id && l.PlayerId == player.Id, ct);

        if (!request.Listed)
        {
            if (existing is not null) _db.LeagueListings.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
        }

        var window = await WindowAsync(league, ct);
        if (!window.Open)
            return LeagueResult<LeagueMarketDto>.Fail(LeagueError.MarketClosed, "The transfer market is shut right now.");

        var club = await _db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == clubId, ct);
        if (club.Players.Count - 1 < _t.MinSquadSize)
            return LeagueResult<LeagueMarketDto>.Fail(
                LeagueError.SquadTooSmall, "Selling him would leave your squad below the legal minimum.");

        // An asking price of 0 means "price him for me" — the same ask a club of your standing would make.
        long asking = request.AskingPrice;
        if (asking <= 0)
        {
            SimClub sim = WorldSquadReader.ToSimClub(club);
            var analysis = SquadAnalysis.Analyze(sim, _t);
            SimPlayer? sp = sim.Squad.Players.FirstOrDefault(p => p.Id == player.ExternalId);
            PlayerImportance importance = sp is null ? PlayerImportance.Squad : analysis.ImportanceOf(sp);
            PersonalityProfile profile = ClubPersonalities.Profile(
                ClubPersonalities.For(club.ExternalId, unchecked((ulong)await WorldSeedAsync(league, ct))));
            asking = NegotiationModel.AskingPrice(Math.Max(1, player.MarketValue), importance, profile, _t);
        }

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            _db.LeagueListings.Add(new LeagueListing
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = league.Id,
                WorldId = league.WorldId,
                PlayerId = player.Id,
                PlayerExternalId = player.ExternalId,
                ClubId = club.Id,
                ClubExternalId = club.ExternalId,
                UserId = userId,
                AskingPrice = asking,
                CreatedUtc = now,
            });
        }
        else
        {
            existing.AskingPrice = asking;
            existing.ClubId = club.Id;
            existing.ClubExternalId = club.ExternalId;
            existing.UserId = userId;
        }
        await _db.SaveChangesAsync(ct);

        // Selling must not depend on a friend being online: the bots that need him bid straight away.
        await _engine.InviteBotOffersForListingAsync(league, player, window.WindowIndex, ct);

        return LeagueResult<LeagueMarketDto>.Ok(await BuildMarketAsync(league, userId, ct));
    }

    // --- free agents -------------------------------------------------------------------------------

    public async Task<LeagueResult<FreeAgentSigningDto>> SignFreeAgentAsync(
        Guid userId, Guid leagueId, SignFreeAgentRequest request, CancellationToken ct = default)
    {
        var (league, me, error, message) = await ResolveMemberAsync(userId, leagueId, ct);
        if (league is null || me is null) return LeagueResult<FreeAgentSigningDto>.Fail(error, message);
        if (me.ClubId is not { } clubId)
            return LeagueResult<FreeAgentSigningDto>.Fail(LeagueError.NotAssignedClub, "You do not hold a club in this league.");

        var window = await WindowAsync(league, ct);
        if (!window.Open)
            return LeagueResult<FreeAgentSigningDto>.Fail(LeagueError.MarketClosed, "The transfer market is shut right now.");

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.WorldId == league.WorldId && p.ExternalId == request.PlayerExternalId, ct);
        if (player is null)
            return LeagueResult<FreeAgentSigningDto>.Fail(LeagueError.NotFound, "No such player in this league.");
        if (player.ClubId is not null)
            return LeagueResult<FreeAgentSigningDto>.Fail(LeagueError.PlayerUnavailable, "He already has a club.");

        long demanded = _engine.DemandedWage(player);
        long cost = LeagueMarketEngine.SigningCost(request.WeeklyWage);

        async Task<LeagueResult<FreeAgentSigningDto>> RefuseAsync(string why) =>
            LeagueResult<FreeAgentSigningDto>.Ok(new FreeAgentSigningDto(
                false, player.ExternalId, FullName(player), demanded,
                LeagueMarketEngine.FreeAgentMinSeasons, LeagueMarketEngine.FreeAgentMaxSeasons,
                LeagueMarketEngine.SigningCost(demanded), why, await BuildMarketAsync(league, userId, ct)));

        if (request.WeeklyWage < demanded)
            return await RefuseAsync($"Chiede {demanded:N0} a settimana.");
        if (request.Seasons < LeagueMarketEngine.FreeAgentMinSeasons
            || request.Seasons > LeagueMarketEngine.FreeAgentMaxSeasons)
            return await RefuseAsync(
                $"Firma solo contratti da {LeagueMarketEngine.FreeAgentMinSeasons} a "
                + $"{LeagueMarketEngine.FreeAgentMaxSeasons} stagioni.");

        var club = await _db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == clubId, ct);
        if (club.Players.Count >= LeagueMarketEngine.MaxSquadSize)
            return LeagueResult<FreeAgentSigningDto>.Fail(
                LeagueError.SquadFull, $"Your squad is full ({LeagueMarketEngine.MaxSquadSize} players).");
        if (club.TransferBudget < cost)
            return LeagueResult<FreeAgentSigningDto>.Fail(
                LeagueError.InsufficientBudget,
                $"Signing him costs {cost:N0} up front (a season of wages) and your budget cannot cover it.");

        // THE RACE, decided in one statement (decision taken with the user, 2026-08-25): the first coach to
        // agree terms signs him. A conditional UPDATE ... WHERE club_id IS NULL is atomic on both providers,
        // so a second coach's identical request updates zero rows and is told he is gone — no lock, no
        // read-then-write window, and no way for two clubs to end up with the same player.
        int claimed = await _db.Players
            .Where(p => p.Id == player.Id && p.ClubId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ClubId, (Guid?)clubId)
                .SetProperty(p => p.WeeklyWage, request.WeeklyWage)
                .SetProperty(p => p.ContractSeasonsRemaining, request.Seasons), ct);
        if (claimed == 0)
            return LeagueResult<FreeAgentSigningDto>.Fail(
                LeagueError.PlayerUnavailable, "Someone agreed terms with him first.");

        // The conditional UPDATE above went straight to the database and the change tracker never heard
        // about it — so the in-memory graph still had him unattached, and the market view built at the end
        // of this method showed a squad without the player it had just signed. Mirror the claim onto the
        // tracked entity: EF fixes up the club's squad from the FK, and the SaveChanges below re-writes the
        // same three values harmlessly. (The UPDATE stays the arbiter of the race; this only tells the
        // tracker who won.)
        player.ClubId = clubId;
        player.WeeklyWage = request.WeeklyWage;
        player.ContractSeasonsRemaining = request.Seasons;

        club.TransferBudget = Math.Max(0, club.TransferBudget - cost);
        _db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            WorldId = league.WorldId,
            PlayerId = player.Id,
            FromClubId = null,
            ToClubId = club.Id,
            Fee = 0,
            SeasonYear = 0,
            Day = window.WindowIndex,
            CreatedUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return LeagueResult<FreeAgentSigningDto>.Ok(new FreeAgentSigningDto(
            true, player.ExternalId, FullName(player), demanded,
            LeagueMarketEngine.FreeAgentMinSeasons, LeagueMarketEngine.FreeAgentMaxSeasons, cost,
            null, await BuildMarketAsync(league, userId, ct)));
    }

    // --- the bots' answers -------------------------------------------------------------------------

    /// <summary>A bot SELLER answers the figure on the table with <see cref="NegotiationModel"/> — accept,
    /// counter, or wave it away — exactly as the career's AI does. Saves.</summary>
    private async Task AnswerAsSellerBotAsync(
        PrivateLeague league, LeagueOffer offer, EntPlayer player, EntClub sellerClub,
        long standingAsk, DateTime now, CancellationToken ct)
    {
        SimClub sim = WorldSquadReader.ToSimClub(sellerClub);
        var analysis = SquadAnalysis.Analyze(sim, _t);
        SimPlayer? sp = sim.Squad.Players.FirstOrDefault(p => p.Id == player.ExternalId);
        PlayerImportance importance = sp is null ? PlayerImportance.Squad : analysis.ImportanceOf(sp);

        // A bot club will not gut itself: if letting him go would leave the squad or his role too thin, the
        // answer is no at any price. The same guard the career's AI sellers use.
        if (sp is not null && !analysis.CanSell(sp, _t))
        {
            offer.Status = LeagueOfferStatus.Rejected;
            offer.UpdatedUtc = now;
            offer.ResolvedUtc = now;
            await _db.SaveChangesAsync(ct);
            return;
        }

        long value = Math.Max(1, player.MarketValue);
        long worldSeed = await WorldSeedAsync(league, ct);
        PersonalityProfile profile = ClubPersonalities.Profile(
            ClubPersonalities.For(sellerClub.ExternalId, unchecked((ulong)worldSeed)));

        long minSale = NegotiationModel.MinSalePrice(value, importance, _t);
        long ask = NegotiationModel.AskingPrice(value, importance, profile, _t);
        // Once the bot has already countered, that standing ask is what it concedes from (it never
        // re-opens at full price after conceding) — otherwise a haggle could never converge.
        if (standingAsk > 0 && standingAsk < ask) ask = standingAsk;

        SellerResponse response = NegotiationModel.EvaluateOffer(ask, offer.Amount, minSale, _t);

        switch (response.Decision)
        {
            case SellerDecision.Accept:
                await _db.SaveChangesAsync(ct);
                var settled = await TryExecuteAsync(league, offer, offer.Amount, now, ct);
                if (!settled.Success) await CloseUnsettledAsync(offer, now, ct);
                return;

            case SellerDecision.Reject:
                offer.Status = LeagueOfferStatus.Rejected;
                offer.UpdatedUtc = now;
                offer.ResolvedUtc = now;
                await _db.SaveChangesAsync(ct);
                return;

            default:
                offer.Amount = response.CounterAsk;
                offer.ProposedBy = LeagueOfferParty.Seller;
                offer.UpdatedUtc = now;
                await _db.SaveChangesAsync(ct);
                return;
        }
    }

    /// <summary>A bot BUYER answers a human seller's counter-ask: it either meets it, improves its own
    /// figure, or gives up. Saves.</summary>
    private async Task AnswerAsBuyerBotAsync(
        PrivateLeague league, LeagueOffer offer, EntPlayer player, long lastOffer, DateTime now, CancellationToken ct)
    {
        var buyerClub = await _db.Clubs.FirstAsync(c => c.Id == offer.BuyerClubId, ct);
        long value = Math.Max(1, player.MarketValue);
        long worldSeed = await WorldSeedAsync(league, ct);
        PersonalityProfile profile = ClubPersonalities.Profile(
            ClubPersonalities.For(buyerClub.ExternalId, unchecked((ulong)worldSeed)));

        long buyerMax = NegotiationModel.BuyerMaxPrice(value, profile, buyerClub.TransferBudget, _t);
        BuyerResponse response = NegotiationModel.RespondToCounter(lastOffer, offer.Amount, buyerMax, _t);

        switch (response.Decision)
        {
            case BuyerDecision.Accept:
                await _db.SaveChangesAsync(ct);
                var settled = await TryExecuteAsync(league, offer, response.Offer, now, ct);
                if (!settled.Success) await CloseUnsettledAsync(offer, now, ct);
                return;

            case BuyerDecision.GiveUp:
                offer.Status = LeagueOfferStatus.Rejected;
                offer.UpdatedUtc = now;
                offer.ResolvedUtc = now;
                await _db.SaveChangesAsync(ct);
                return;

            default:
                offer.Amount = response.Offer;
                offer.ProposedBy = LeagueOfferParty.Buyer;
                offer.UpdatedUtc = now;
                await _db.SaveChangesAsync(ct);
                return;
        }
    }

    /// <summary>A bot said yes but the deal could not actually be settled (a budget or a squad moved under
    /// it). The negotiation must not be left Pending with the bot on the clock — nobody would ever answer
    /// it — so it closes.</summary>
    private async Task CloseUnsettledAsync(LeagueOffer offer, DateTime now, CancellationToken ct)
    {
        if (offer.Status != LeagueOfferStatus.Pending) return;
        offer.Status = LeagueOfferStatus.Rejected;
        offer.UpdatedUtc = now;
        offer.ResolvedUtc = now;
        await _db.SaveChangesAsync(ct);
    }

    // --- settlement --------------------------------------------------------------------------------

    /// <summary>
    /// Moves the player and the money, or explains why it cannot. Re-validates EVERYTHING at the moment the
    /// money actually moves — the figure was agreed earlier, and squads, budgets and prices have all had a
    /// chance to change since. Saves.
    /// </summary>
    private async Task<(bool Success, LeagueError Error, string? Message)> TryExecuteAsync(
        PrivateLeague league, LeagueOffer offer, long fee, DateTime now, CancellationToken ct)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == offer.PlayerId, ct);
        if (player is null || player.ClubId != offer.SellerClubId)
            return (false, LeagueError.PlayerUnavailable, "He is no longer available.");

        var buyerClub = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == offer.BuyerClubId, ct);
        var sellerClub = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == offer.SellerClubId, ct);
        if (buyerClub is null || sellerClub is null)
            return (false, LeagueError.NotFound, "A club in the deal no longer exists.");
        if (buyerClub.TransferBudget < fee)
            return (false, LeagueError.InsufficientBudget, "The buying club can no longer cover that fee.");
        if (buyerClub.Players.Count >= LeagueMarketEngine.MaxSquadSize)
            return (false, LeagueError.SquadFull, "The buying squad is full.");
        if (sellerClub.Players.Count - 1 < _t.MinSquadSize)
            return (false, LeagueError.SquadTooSmall, "The selling squad would drop below the legal minimum.");

        // COLLUSION GUARD, second half (Phase 9.5): re-assess at the moment money moves.
        var assessment = TransferIntegrity.Assess(fee, player.MarketValue, _integrityOpt.Bands());
        if (assessment.IsBlocked)
        {
            offer.Status = LeagueOfferStatus.Rejected;
            offer.UpdatedUtc = now;
            offer.ResolvedUtc = now;
            await _integrity.FlagAsync(
                IntegrityFlagKind.BlockedTransfer, severity: 100, userId: offer.BuyerUserId,
                subjectUserId: offer.SellerUserId, rankedGroupId: null, fee: fee, marketValue: player.MarketValue,
                details: $"{assessment.Reason} at settlement; league deal; fee={assessment.FeePercentOfValue}% of value; "
                         + $"player={player.ExternalId}",
                ct);
            await _db.SaveChangesAsync(ct);
            return (false, LeagueError.IntegrityBlocked, "That fee is outside the allowed band — the deal was refused.");
        }

        player.ClubId = buyerClub.Id;
        player.ContractSeasonsRemaining = _t.SignedContractSeasons;
        buyerClub.TransferBudget = Math.Max(0, buyerClub.TransferBudget - fee);
        sellerClub.TransferBudget += fee;

        offer.Status = LeagueOfferStatus.Accepted;
        offer.Fee = fee;
        offer.Amount = fee;
        offer.UpdatedUtc = now;
        offer.ResolvedUtc = now;

        _db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            WorldId = league.WorldId,
            PlayerId = player.Id,
            FromClubId = sellerClub.Id,
            ToClubId = buyerClub.Id,
            Fee = fee,
            SeasonYear = 0,
            Day = offer.WindowIndex,
            CreatedUtc = now,
        });

        // He is sold: off the list, and every other negotiation over him is over.
        await _db.LeagueListings
            .Where(l => l.PrivateLeagueId == league.Id && l.PlayerId == player.Id)
            .ExecuteDeleteAsync(ct);
        var stale = await _db.LeagueOffers
            .Where(o => o.PrivateLeagueId == league.Id && o.PlayerId == player.Id
                        && o.Id != offer.Id && o.Status == LeagueOfferStatus.Pending)
            .ToListAsync(ct);
        foreach (LeagueOffer other in stale)
        {
            other.Status = LeagueOfferStatus.Rejected;
            other.UpdatedUtc = now;
            other.ResolvedUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        // Allowed, but odd enough to be worth a reviewer's eyes (Phase 9.5).
        if (assessment.IsSuspicious)
        {
            await _integrity.FlagAsync(
                IntegrityFlagKind.SuspiciousTransfer,
                severity: Math.Clamp(Math.Abs(100 - assessment.FeePercentOfValue), 10, 90),
                userId: offer.BuyerUserId, subjectUserId: offer.SellerUserId, rankedGroupId: null,
                fee: fee, marketValue: player.MarketValue,
                details: $"{assessment.Reason}; league deal; fee={assessment.FeePercentOfValue}% of value; "
                         + $"player={player.ExternalId}",
                ct);
        }

        if (offer.BuyerUserId is { } buyerUser)
            await SafeSend(buyerUser, "Affare fatto", $"Hai preso {FullName(player)} per {fee:N0}.",
                new Dictionary<string, string> { ["kind"] = "league_transfer_done" }, ct);
        if (offer.SellerUserId is { } sellerUser)
            await SafeSend(sellerUser, "Affare fatto", $"Hai ceduto {FullName(player)} per {fee:N0}.",
                new Dictionary<string, string> { ["kind"] = "league_transfer_done" }, ct);

        return (true, LeagueError.None, null);
    }

    // --- view --------------------------------------------------------------------------------------

    private async Task<LeagueMarketDto> BuildMarketAsync(PrivateLeague league, Guid userId, CancellationToken ct)
    {
        var window = await WindowAsync(league, ct);

        var members = await _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == league.Id)
            .ToListAsync(ct);
        var me = members.FirstOrDefault(m => m.UserId == userId);
        var humanClubIds = members.Where(m => m.ClubId is not null).Select(m => m.ClubId!.Value).ToHashSet();

        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .OrderBy(c => c.ExternalId)
            .ToListAsync(ct);

        var listings = await _db.LeagueListings
            .Where(l => l.PrivateLeagueId == league.Id)
            .ToDictionaryAsync(l => l.PlayerId, l => l.AskingPrice, ct);

        LeagueMarketPlayerDto ToPlayer(EntPlayer p) => new(
            p.ExternalId, FullName(p), p.Age, p.Role, p.Overall, p.MarketValue, p.WeeklyWage,
            p.ContractSeasonsRemaining,
            listings.ContainsKey(p.Id),
            listings.TryGetValue(p.Id, out long ask) ? ask : 0);

        EntClub? myClub = me?.ClubId is { } myId ? clubs.FirstOrDefault(c => c.Id == myId) : null;

        var mySquad = myClub is null
            ? (IReadOnlyList<LeagueMarketPlayerDto>)Array.Empty<LeagueMarketPlayerDto>()
            : myClub.Players.OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId).Select(ToPlayer).ToList();

        var otherClubs = clubs
            .Where(c => myClub is null || c.Id != myClub.Id)
            .Select(c => new LeagueMarketSquadDto(
                c.ExternalId, c.Name, humanClubIds.Contains(c.Id), c.TransferBudget,
                c.Players.OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId).Select(ToPlayer).ToList()))
            .ToList();

        var freeAgents = await _db.Players
            .Where(p => p.WorldId == league.WorldId && p.ClubId == null)
            .OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId)
            .Take(FreeAgentPageSize)
            .ToListAsync(ct);
        var freeAgentDtos = freeAgents.Select(p =>
        {
            long wage = _engine.DemandedWage(p);
            return new LeagueFreeAgentDto(
                p.ExternalId, FullName(p), p.Age, p.Role, p.Overall, p.MarketValue,
                wage, LeagueMarketEngine.FreeAgentMinSeasons, LeagueMarketEngine.FreeAgentMaxSeasons,
                LeagueMarketEngine.SigningCost(wage));
        }).ToList();

        var offers = await _db.LeagueOffers
            .Where(o => o.PrivateLeagueId == league.Id
                        && (o.BuyerUserId == userId || o.SellerUserId == userId))
            .OrderByDescending(o => o.UpdatedUtc)
            .Take(100)
            .ToListAsync(ct);

        var clubByGuid = clubs.ToDictionary(c => c.Id);
        var playerIds = offers.Select(o => o.PlayerId).Distinct().ToList();
        var playersById = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        LeagueOfferDto ToOffer(LeagueOffer o)
        {
            bool youAreBuyer = o.BuyerUserId == userId;
            bool youAreSeller = o.SellerUserId == userId;
            var awaiting = o.ProposedBy == LeagueOfferParty.Buyer ? LeagueOfferParty.Seller : LeagueOfferParty.Buyer;
            bool awaitingYou = o.Status == LeagueOfferStatus.Pending
                               && (awaiting == LeagueOfferParty.Seller ? youAreSeller : youAreBuyer);
            playersById.TryGetValue(o.PlayerId, out EntPlayer? p);
            return new LeagueOfferDto(
                o.Id, o.WindowIndex, o.PlayerExternalId, p is null ? string.Empty : FullName(p),
                p?.Role ?? 0, p?.Overall ?? 0, p?.MarketValue ?? 0,
                o.BuyerClubExternalId, clubByGuid.TryGetValue(o.BuyerClubId, out EntClub? bc) ? bc.Name : string.Empty,
                o.SellerClubExternalId, clubByGuid.TryGetValue(o.SellerClubId, out EntClub? sc) ? sc.Name : string.Empty,
                o.Amount, o.ProposedBy, o.Rounds, o.Status, youAreBuyer, youAreSeller, awaitingYou, o.UpdatedUtc);
        }

        var offerDtos = offers.Select(ToOffer).ToList();

        var transfers = await _db.Transfers
            .Where(t => t.WorldId == league.WorldId)
            .OrderByDescending(t => t.CreatedUtc)
            .Take(NewsPageSize)
            .ToListAsync(ct);
        var transferPlayerIds = transfers.Select(t => t.PlayerId).Distinct().ToList();
        var transferPlayers = await _db.Players
            .Where(p => transferPlayerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.ExternalId, p.FirstName, p.LastName }, ct);

        var news = transfers.Select(t =>
        {
            transferPlayers.TryGetValue(t.PlayerId, out var tp);
            return new LeagueTransferNewsDto(
                tp?.ExternalId ?? 0,
                tp is null ? string.Empty : Combine(tp.FirstName, tp.LastName),
                t.FromClubId is { } fc && clubByGuid.TryGetValue(fc, out EntClub? fclub) ? fclub.Name : "Svincolato",
                t.ToClubId is { } tc && clubByGuid.TryGetValue(tc, out EntClub? tclub) ? tclub.Name : "Svincolato",
                t.Fee,
                myClub is not null && (t.FromClubId == myClub.Id || t.ToClubId == myClub.Id),
                t.CreatedUtc);
        }).ToList();

        return new LeagueMarketDto(
            window,
            myClub?.ExternalId,
            myClub?.TransferBudget ?? 0,
            myClub?.Players.Count ?? 0,
            mySquad,
            otherClubs,
            freeAgentDtos,
            offerDtos.Where(o => o.AwaitingYou).ToList(),
            offerDtos.Where(o => o.YouAreSeller).ToList(),
            offerDtos.Where(o => o.YouAreBuyer).ToList(),
            news);
    }

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<(PrivateLeague? League, LeagueMember? Me, LeagueError Error, string? Message)>
        ResolveMemberAsync(Guid userId, Guid leagueId, CancellationToken ct)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null) return (null, null, LeagueError.NotFound, "League not found.");

        var me = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (me is null) return (null, null, LeagueError.Forbidden, "You are not a member of this league.");

        return (league, me, LeagueError.None, null);
    }

    private async Task<LeagueMarketWindowDto> WindowAsync(PrivateLeague league, CancellationToken ct)
    {
        var rounds = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == league.Id)
            .Select(f => new { f.Round, f.IsPlayed })
            .ToListAsync(ct);

        int totalRounds = rounds.Count == 0 ? 0 : rounds.Max(f => f.Round);
        int played = 0;
        for (int r = 1; r <= totalRounds; r++)
            if (rounds.Where(f => f.Round == r).All(f => f.IsPlayed)) played++;

        return LeagueMarketWindow.State(league.Status == LeagueStatus.Active, played, totalRounds);
    }

    private Task<long> WorldSeedAsync(PrivateLeague league, CancellationToken ct) =>
        _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);

    private Task<Guid?> UserOfClubAsync(Guid leagueId, Guid clubId, CancellationToken ct) =>
        _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == leagueId && m.ClubId == clubId)
            .Select(m => (Guid?)m.UserId)
            .FirstOrDefaultAsync(ct);

    private async Task NotifyOtherSideAsync(
        LeagueOffer offer, bool callerIsBuyer, string title, string body, CancellationToken ct)
    {
        Guid? other = callerIsBuyer ? offer.SellerUserId : offer.BuyerUserId;
        if (other is { } target)
            await SafeSend(target, title, body,
                new Dictionary<string, string> { ["kind"] = "league_offer_update" }, ct);
    }

    private static string Combine(string first, string last) =>
        string.IsNullOrEmpty(first) ? last : $"{first} {last}";

    private static string FullName(EntPlayer p) => Combine(p.FirstName, p.LastName);

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* best-effort — a push failure must never break a transfer. */ }
    }
}
