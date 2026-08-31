using Fts.Application.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using EntPlayer = Fts.Infrastructure.Persistence.Entities.Player;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;
using SimPlayer = Sim.Core.Domain.Player;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// The world-side half of the private-league market (Phase 12.1): everything that happens WITHOUT a coach
/// pressing a button. It runs the bot clubs' own transfer window between rounds (so the league keeps moving
/// while the friends argue over one striker), turns a bot's interest in a human's player into a real offer
/// the coach has to answer, and expires the negotiations nobody answered when a round resolves.
///
/// Deliberately a plain class, not a DI service: <see cref="LeagueService"/> (season start = window 0) and
/// <see cref="LeagueSeasonService"/> (round resolution = window close + window 1) both drive it, and making
/// it injectable would tangle three scoped services around one DbContext for no gain. It never calls
/// <c>SaveChanges</c> on paths its callers already save — each public method says what it does.
///
/// Nothing here touches the match engine, so golden masters and replays are unaffected.
/// </summary>
public sealed class LeagueMarketEngine
{
    /// <summary>A club may not exceed this many players (the free-agent hoovering guard).</summary>
    public const int MaxSquadSize = 26;

    /// <summary>Signing a free agent prepays this many weeks of his wage out of the transfer budget —
    /// the only price of a free transfer, and what stops a club signing the whole free-agent pool.</summary>
    public const int WagePrepaidWeeks = 52;

    /// <summary>The contract lengths a free agent will sign.</summary>
    public const int FreeAgentMinSeasons = 2;
    public const int FreeAgentMaxSeasons = 5;

    /// <summary>At most this many bot offers land on one human club in a single between-rounds pass —
    /// the world moves, but the coach is not buried under bids he has to answer before he can play.</summary>
    private const int MaxBotOffersPerHumanClubPerPass = 2;

    /// <summary>At most this many bot clubs bid on a freshly listed player.</summary>
    private const int MaxBotOffersPerListing = 3;

    private readonly FtsDbContext _db;
    private readonly BalanceConfig _config;
    private readonly TransferBalance _t;

    public LeagueMarketEngine(FtsDbContext db, BalanceConfig config)
    {
        _db = db;
        _config = config;
        _t = config.Transfer;
    }

    // --- terms -------------------------------------------------------------------------------------

    /// <summary>What a free agent asks per week: the shared <see cref="WageModel"/> at a neutral season
    /// result, so his demand tracks his value exactly as a contracted player's wage does.</summary>
    public long DemandedWage(EntPlayer player) =>
        Math.Max(1, WageModel.WeeklyWage(player.MarketValue, 1000, _config.Finance));

    /// <summary>What agreeing those terms costs the club up front.</summary>
    public static long SigningCost(long weeklyWage) => Math.Max(0, weeklyWage) * WagePrepaidWeeks;

    // --- expiry ------------------------------------------------------------------------------------

    /// <summary>
    /// "No answer means not accepted" (decision taken with the user, 2026-08-25). Every negotiation still
    /// waiting on somebody when a round resolves expires — no AI ever answers in a human's place, because a
    /// private league is meant to be played together. Saves nothing: the caller is mid-transaction.
    /// </summary>
    public Task<int> ExpirePendingOffersAsync(Guid leagueId, DateTime now, CancellationToken ct) =>
        _db.LeagueOffers
            .Where(o => o.PrivateLeagueId == leagueId && o.Status == LeagueOfferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, LeagueOfferStatus.Expired)
                .SetProperty(o => o.ResolvedUtc, (DateTime?)now), ct);

    // --- the bots' own window ----------------------------------------------------------------------

    /// <summary>
    /// Runs one market window for the clubs no member holds, then lets them come shopping at the humans'
    /// doors. Two distinct halves:
    /// <list type="number">
    ///   <item>BOT ↔ BOT — the shared <see cref="TransferMarket"/> over a league containing only the bot
    ///   clubs, exactly as the single-player career runs its window. Deterministic from (world seed,
    ///   window index), so the same league replays the same window.</item>
    ///   <item>BOT → HUMAN — a bot that needs what a human has makes a real, pending OFFER instead of
    ///   helping itself: the coach accepts, counters or lets it expire.</item>
    /// </list>
    /// Prices the whole world first (the humans' squads included) so every valuation on the market screen
    /// is the one the negotiations use. Calls <c>SaveChanges</c> itself.
    /// </summary>
    public async Task RunWindowAsync(PrivateLeague league, int windowIndex, CancellationToken ct)
    {
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .OrderBy(c => c.ExternalId)
            .ToListAsync(ct);
        if (clubs.Count == 0) return;

        var humanClubIds = (await _db.LeagueMembers
                .Where(m => m.PrivateLeagueId == league.Id && m.ClubId != null)
                .Select(m => m.ClubId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        long worldSeed = await _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);

        // Rebuild the world into Sim.Core, carrying the budgets and contracts the negotiations read.
        var simByClubId = new Dictionary<Guid, SimClub>();
        var entByExternal = new Dictionary<int, EntClub>();
        foreach (EntClub c in clubs)
        {
            SimClub sim = WorldSquadReader.ToSimClub(c);
            sim.TransferBudget = c.TransferBudget;
            var contractByExternal = c.Players.ToDictionary(p => p.ExternalId, p => p.ContractSeasonsRemaining);
            foreach (SimPlayer sp in sim.Squad.Players)
                if (contractByExternal.TryGetValue(sp.Id, out int seasons)) sp.Contract.SeasonsRemaining = seasons;
            simByClubId[c.Id] = sim;
            entByExternal[c.ExternalId] = c;
        }

        // One price list for everybody, before anyone trades.
        var wholeWorld = new SimLeague { Division = 1 };
        foreach (SimClub sim in simByClubId.Values) wholeWorld.Clubs.Add(sim);
        new ValuationProgressor(_config).Reprice(new[] { wholeWorld });

        var entPlayerByExternal = new Dictionary<int, EntPlayer>();
        foreach (EntClub c in clubs)
            foreach (EntPlayer p in c.Players)
                entPlayerByExternal[p.ExternalId] = p;
        foreach (SimClub sim in simByClubId.Values)
            foreach (SimPlayer sp in sim.Squad.Players)
                if (entPlayerByExternal.TryGetValue(sp.Id, out EntPlayer? ep)) ep.MarketValue = sp.MarketValue;

        // 1) BOT ↔ BOT: the shared window over the bot clubs only.
        var botLeague = new SimLeague { Division = 1 };
        foreach (EntClub c in clubs)
            if (!humanClubIds.Contains(c.Id)) botLeague.Clubs.Add(simByClubId[c.Id]);

        if (botLeague.Clubs.Count >= 2)
        {
            List<TransferRecord> records =
                new TransferMarket(_config).RunWindow(new[] { botLeague }, unchecked((ulong)worldSeed), windowIndex);

            var now = DateTime.UtcNow;
            foreach (TransferRecord r in records)
            {
                if (!entPlayerByExternal.TryGetValue(r.PlayerId, out EntPlayer? player)) continue;
                if (!entByExternal.TryGetValue(r.FromClubId, out EntClub? from)) continue;
                if (!entByExternal.TryGetValue(r.ToClubId, out EntClub? to)) continue;

                player.ClubId = to.Id;
                player.ContractSeasonsRemaining = _t.SignedContractSeasons;
                _db.Transfers.Add(new Transfer
                {
                    Id = Guid.NewGuid(),
                    WorldId = league.WorldId,
                    PlayerId = player.Id,
                    FromClubId = from.Id,
                    ToClubId = to.Id,
                    Fee = r.Fee,
                    SeasonYear = 0,
                    Day = windowIndex,
                    CreatedUtc = now,
                });
                // A sold player is off the market.
                await _db.LeagueListings
                    .Where(l => l.PrivateLeagueId == league.Id && l.PlayerId == player.Id)
                    .ExecuteDeleteAsync(ct);
            }

            // Budgets moved on the Sim.Core clubs — read them back rather than replaying the arithmetic.
            foreach (EntClub c in clubs)
                if (!humanClubIds.Contains(c.Id)) c.TransferBudget = simByClubId[c.Id].TransferBudget;
        }

        // 2) BOT → HUMAN: a bot that needs a human's player asks for him properly.
        await CreateBotOffersAsync(league, clubs, humanClubIds, simByClubId, entByExternal,
            unchecked((ulong)worldSeed), windowIndex, target: null, ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Brings the bot clubs to a freshly listed player (Phase 12.1). Listing is the "sell" half of the
    /// market, and it must not depend on a friend being online — so the bots that actually need him bid
    /// straight away. Calls <c>SaveChanges</c> itself. Returns how many offers were created.
    /// </summary>
    public async Task<int> InviteBotOffersForListingAsync(
        PrivateLeague league, EntPlayer listed, int windowIndex, CancellationToken ct)
    {
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .OrderBy(c => c.ExternalId)
            .ToListAsync(ct);
        if (clubs.Count == 0) return 0;

        var humanClubIds = (await _db.LeagueMembers
                .Where(m => m.PrivateLeagueId == league.Id && m.ClubId != null)
                .Select(m => m.ClubId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        long worldSeed = await _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);

        var simByClubId = new Dictionary<Guid, SimClub>();
        var entByExternal = new Dictionary<int, EntClub>();
        foreach (EntClub c in clubs)
        {
            SimClub sim = WorldSquadReader.ToSimClub(c);
            sim.TransferBudget = c.TransferBudget;
            simByClubId[c.Id] = sim;
            entByExternal[c.ExternalId] = c;
        }

        int created = await CreateBotOffersAsync(league, clubs, humanClubIds, simByClubId, entByExternal,
            unchecked((ulong)worldSeed), windowIndex, target: listed, ct);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// The shared bot-buyer scan. With <paramref name="target"/> null it sweeps every human squad once per
    /// bot club (the between-rounds pass); with a target it only considers that one player (a fresh
    /// listing). Never saves — the callers do.
    /// </summary>
    private async Task<int> CreateBotOffersAsync(
        PrivateLeague league,
        List<EntClub> clubs,
        HashSet<Guid> humanClubIds,
        Dictionary<Guid, SimClub> simByClubId,
        Dictionary<int, EntClub> entByExternal,
        ulong worldSeed,
        int windowIndex,
        EntPlayer? target,
        CancellationToken ct)
    {
        if (humanClubIds.Count == 0) return 0;

        var memberByClub = await _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == league.Id && m.ClubId != null)
            .ToDictionaryAsync(m => m.ClubId!.Value, m => m.UserId, ct);

        // Who is already being haggled over — one live negotiation per (buyer club, player).
        var live = await _db.LeagueOffers
            .Where(o => o.PrivateLeagueId == league.Id && o.Status == LeagueOfferStatus.Pending)
            .Select(o => new { o.BuyerClubId, o.PlayerId, o.SellerClubId })
            .ToListAsync(ct);
        var alreadyBidding = live.Select(x => (x.BuyerClubId, x.PlayerId)).ToHashSet();
        var pendingPerSeller = live.GroupBy(x => x.SellerClubId).ToDictionary(g => g.Key, g => g.Count());

        // A LISTING IS AN INVITATION, and it gets its own budget of bidders. Without this the "don't bury
        // the coach" cap below would silently swallow it: two unsolicited bids already sitting on his club
        // would mean that putting a player in the shop window brought nobody at all — precisely the case
        // where he ASKED for offers. So the sweep is capped per club, a listing per player.
        int pendingOnTarget = target is null ? 0 : live.Count(x => x.PlayerId == target.Id);

        // Listings are the shop window: they say WHO is for sale and at what price.
        var askingByPlayer = await _db.LeagueListings
            .Where(l => l.PrivateLeagueId == league.Id)
            .ToDictionaryAsync(l => l.PlayerId, l => l.AskingPrice, ct);

        var analysisCache = new Dictionary<Guid, SquadAnalysis>();
        SquadAnalysis Analyze(Guid clubId)
        {
            if (!analysisCache.TryGetValue(clubId, out SquadAnalysis? a))
            {
                a = SquadAnalysis.Analyze(simByClubId[clubId], _t);
                analysisCache[clubId] = a;
            }
            return a!;
        }

        var now = DateTime.UtcNow;
        int created = 0;

        foreach (EntClub buyer in clubs)
        {
            if (humanClubIds.Contains(buyer.Id)) continue;      // bots only — humans do their own shopping
            if (buyer.TransferBudget <= 0) continue;
            if (buyer.Players.Count >= MaxSquadSize) continue;
            if (target is not null && pendingOnTarget >= MaxBotOffersPerListing) break;

            PersonalityProfile buyerProfile =
                ClubPersonalities.Profile(ClubPersonalities.For(buyer.ExternalId, worldSeed));
            SquadAnalysis buyerAnalysis = Analyze(buyer.Id);

            Candidate? best = null;
            foreach (RoleNeed need in buyerAnalysis.Needs)
            {
                int bar = need.CurrentBest + _t.UpgradeMinPoints;

                foreach (EntClub seller in clubs)
                {
                    if (!humanClubIds.Contains(seller.Id)) continue;   // only the humans' doors
                    if (seller.Id == buyer.Id) continue;
                    // The unsolicited sweep is the one that has to stay polite; an advertised player is not.
                    if (target is null
                        && pendingPerSeller.TryGetValue(seller.Id, out int open)
                        && open >= MaxBotOffersPerHumanClubPerPass) continue;

                    SquadAnalysis sellerAnalysis = Analyze(seller.Id);
                    SimClub simSeller = simByClubId[seller.Id];
                    var simByExternal = simSeller.Squad.Players.ToDictionary(p => p.Id);

                    foreach (EntPlayer player in seller.Players)
                    {
                        if (target is not null && player.Id != target.Id) continue;
                        if (alreadyBidding.Contains((buyer.Id, player.Id))) continue;
                        if (!simByExternal.TryGetValue(player.ExternalId, out SimPlayer? sp)) continue;

                        int rating = PlayerRating.OverallFor(sp, need.Role);
                        // A listed player is a shop window: the bots look at him even if he is not an
                        // upgrade on paper, which is what makes "list him and offers arrive" true.
                        bool listed = askingByPlayer.ContainsKey(player.Id);
                        if (!listed && rating < bar) continue;
                        if (!sellerAnalysis.CanSell(sp, _t)) continue;

                        PlayerImportance importance = sellerAnalysis.ImportanceOf(sp);
                        long value = player.MarketValue > 0 ? player.MarketValue : sp.MarketValue;
                        if (value <= 0) continue;

                        long minSale = NegotiationModel.MinSalePrice(value, importance, _t);
                        long buyerMax = NegotiationModel.BuyerMaxPrice(value, buyerProfile, buyer.TransferBudget, _t);
                        if (buyerMax < minSale) continue;

                        // What the bot thinks it will have to pay. A listed player advertises his own
                        // price; otherwise the bot assumes the ask a club of that character would make.
                        PersonalityProfile sellerProfile =
                            ClubPersonalities.Profile(ClubPersonalities.For(seller.ExternalId, worldSeed));
                        long ask = askingByPlayer.TryGetValue(player.Id, out long advertised) && advertised > 0
                            ? advertised
                            : NegotiationModel.AskingPrice(value, importance, sellerProfile, _t);
                        long opening = NegotiationModel.OpeningOffer(ask, buyerMax, _t);
                        if (opening <= 0) continue;

                        var candidate = new Candidate(player, seller, opening, rating, listed);
                        if (best is null || candidate.Beats(best.Value)) best = candidate;
                    }
                }
            }

            if (best is not { } pick) continue;

            _db.LeagueOffers.Add(new LeagueOffer
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = league.Id,
                WorldId = league.WorldId,
                WindowIndex = windowIndex,
                PlayerId = pick.Player.Id,
                PlayerExternalId = pick.Player.ExternalId,
                BuyerClubId = buyer.Id,
                BuyerClubExternalId = buyer.ExternalId,
                BuyerUserId = null,
                SellerClubId = pick.Seller.Id,
                SellerClubExternalId = pick.Seller.ExternalId,
                SellerUserId = memberByClub.TryGetValue(pick.Seller.Id, out Guid sellerUser) ? sellerUser : null,
                Amount = pick.Amount,
                ProposedBy = LeagueOfferParty.Buyer,
                Rounds = 1,
                Status = LeagueOfferStatus.Pending,
                CreatedUtc = now,
                UpdatedUtc = now,
            });

            alreadyBidding.Add((buyer.Id, pick.Player.Id));
            pendingPerSeller[pick.Seller.Id] =
                (pendingPerSeller.TryGetValue(pick.Seller.Id, out int c2) ? c2 : 0) + 1;
            if (target is not null) pendingOnTarget++;
            created++;
        }

        return created;
    }

    /// <summary>A bot's shortlist entry: a listed player always beats an unlisted one, then the better
    /// rating, then the lower id so the pass is deterministic.</summary>
    private readonly record struct Candidate(EntPlayer Player, EntClub Seller, long Amount, int Rating, bool Listed)
    {
        public bool Beats(Candidate other)
        {
            if (Listed != other.Listed) return Listed;
            if (Rating != other.Rating) return Rating > other.Rating;
            return Player.ExternalId < other.Player.ExternalId;
        }
    }
}
