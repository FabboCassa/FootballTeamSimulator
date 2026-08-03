using Fts.Application.Auth;
using Fts.Application.Dev;
using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Sim.Core.Match;
using SimClub = Sim.Core.Domain.Club;
using SimPlayer = Sim.Core.Domain.Player;

namespace Fts.Infrastructure.Dev;

/// <summary>
/// Dev-only test-league seeding (dev tooling). Composes the real use cases — <see cref="IAuthService"/>
/// (bot accounts), <see cref="ILeagueService"/> (create/join/draft), <see cref="ILeagueSeasonService"/>
/// (ready), <see cref="IAuctionService"/> (bot bids) — plus a direct read of the shared
/// <see cref="FtsDbContext"/> to find a league's bot members. Everything runs server-side by user id, so
/// no tokens are needed to drive the flow; the bot access tokens are only returned for a caller's
/// convenience. Never mapped in Production (the Api gates the endpoints).
/// </summary>
public sealed class DevSeedService : IDevSeedService
{
    private const string BotPassword = "BotPass1";
    private const string BotDomain = "@dev.local";

    private readonly IAuthService _auth;
    private readonly ILeagueService _leagues;
    private readonly ILeagueSeasonService _season;
    private readonly IAuctionService _auctions;
    private readonly ILiveMatchService _liveMatch;
    private readonly IRankedService _ranked;
    private readonly IRankedSeasonService _rankedSeason;
    private readonly IRankedAuctionService _rankedAuctions;
    private readonly IRankedMarketService _rankedMarket;
    private readonly FtsDbContext _db;

    public DevSeedService(
        IAuthService auth, ILeagueService leagues, ILeagueSeasonService season,
        IAuctionService auctions, ILiveMatchService liveMatch, IRankedService ranked,
        IRankedSeasonService rankedSeason, IRankedAuctionService rankedAuctions,
        IRankedMarketService rankedMarket, FtsDbContext db)
    {
        _auth = auth;
        _leagues = leagues;
        _season = season;
        _auctions = auctions;
        _liveMatch = liveMatch;
        _ranked = ranked;
        _rankedSeason = rankedSeason;
        _rankedAuctions = rankedAuctions;
        _rankedMarket = rankedMarket;
        _db = db;
    }

    // --- seed ------------------------------------------------------------------------------------

    public async Task<DevSeedResult> SeedTestLeagueAsync(DevSeedRequest request, CancellationToken ct = default)
    {
        int size = Clamp(request.Size, 2, 20);
        int members = Clamp(request.Bots, 2, size);
        string target = (request.ToStatus ?? "Active").Trim().ToLowerInvariant();

        var roster = new List<Member>();

        // Creator: the signed-in human (client dev button) or a deterministic bot.
        if (request.CreatorUserId is Guid human)
            roster.Add(new Member(human, null, null, true, false));
        else
        {
            var b1 = await EnsureBotAsync(1, ct);
            roster.Add(new Member(b1.Id, b1.Email, b1.Token, true, true));
        }

        // Fill the remaining seats with bots (indices continue after the creator bot, if any).
        int nextBot = request.CreatorUserId is null ? 2 : 1;
        while (roster.Count < members)
        {
            var b = await EnsureBotAsync(nextBot++, ct);
            roster.Add(new Member(b.Id, b.Email, b.Token, false, true));
        }

        var creator = roster[0];
        string code = System.Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();
        var create = await _leagues.CreateAsync(
            creator.Id, new CreateLeagueRequest($"Dev League {code}", size, LeagueMode.AllReady), ct);
        if (!create.Success)
            throw new InvalidOperationException($"Dev seed: create failed ({create.Error}).");

        Guid leagueId = create.Value!.League.Id;
        string inviteCode = create.Value.League.InviteCode;

        // The others join by code.
        for (int i = 1; i < roster.Count; i++)
        {
            var join = await _leagues.JoinAsync(roster[i].Id, new JoinLeagueRequest(inviteCode), ct);
            if (!join.Success)
                throw new InvalidOperationException($"Dev seed: join failed for member {i} ({join.Error}).");
        }

        if (target is "drafting" or "active")
        {
            var start = await _leagues.StartDraftAsync(creator.Id, leagueId, ct);
            if (!start.Success)
                throw new InvalidOperationException($"Dev seed: start draft failed ({start.Error}).");
        }

        if (target == "active")
            await RunDraftAsync(creator.Id, leagueId, roster, size, ct);

        // Final detail for the assigned clubs + reported status.
        var detail = await _leagues.GetAsync(creator.Id, leagueId, ct);
        var memberDtos = new List<DevSeedMemberDto>();
        foreach (var m in roster)
        {
            int? clubExternalId = null;
            if (detail.Success)
                foreach (var dm in detail.Value!.Members)
                    if (dm.UserId == m.Id) { clubExternalId = dm.ClubExternalId; break; }
            memberDtos.Add(new DevSeedMemberDto(m.Id, m.Email ?? string.Empty, m.Token, clubExternalId, m.IsCreator, m.IsBot));
        }

        string status = detail.Success ? detail.Value!.League.Status.ToString() : target;
        return new DevSeedResult(leagueId, inviteCode, status, memberDtos);
    }

    /// <summary>Drives the snake draft to completion: whoever's turn it is claims the first unclaimed club.</summary>
    private async Task RunDraftAsync(Guid readerId, Guid leagueId, List<Member> roster, int size, CancellationToken ct)
    {
        for (int guard = 0; guard <= size; guard++)
        {
            var detail = await _leagues.GetAsync(readerId, leagueId, ct);
            if (!detail.Success) return;
            var draft = detail.Value!.Draft;
            if (draft is null || !draft.InProgress) return;

            Guid? pick = draft.CurrentPickUserId;
            if (pick is null) return;
            Guid pickUser = pick.Value;

            var taken = new HashSet<int>();
            foreach (var dm in detail.Value.Members)
                if (dm.ClubExternalId is int cid) taken.Add(cid);

            int? club = null;
            foreach (var c in detail.Value.Clubs)
                if (!taken.Contains(c.ExternalId)) { club = c.ExternalId; break; }
            if (club is null) return;

            var res = await _leagues.PickClubAsync(pickUser, leagueId, new PickClubRequest(club.Value), ct);
            if (!res.Success)
                throw new InvalidOperationException($"Dev seed: pick failed ({res.Error}).");
        }
    }

    // --- bot autopilot ---------------------------------------------------------------------------

    public async Task<DevBotBidResult> BotBidAsync(Guid leagueId, DevBotBidRequest request, CancellationToken ct = default)
    {
        var bots = await BotMembersAsync(leagueId, ct);
        if (bots.Count == 0) return new DevBotBidResult(0);

        int rounds = Clamp(request.Rounds, 1, 20);
        int placed = 0;
        for (int r = 0; r < rounds; r++)
        {
            var view = await _auctions.GetAuctionsAsync(bots[0].UserId, leagueId, ct);
            if (!view.Success) break;

            foreach (var lot in view.Value!.Lots)
            {
                if (lot.Status != AuctionStatus.Open) continue;
                // A bot that isn't already the leader outbids the current high (minimum legal raise).
                var bidder = bots.FirstOrDefault(b => lot.HighBidClubExternalId != b.ClubExternalId);
                if (bidder is null) continue;

                long min = lot.HighBid <= 0
                    ? lot.StartPrice
                    : lot.HighBid + System.Math.Max(25_000, lot.HighBid / 20);
                var res = await _auctions.PlaceBidAsync(
                    bidder.UserId, leagueId, lot.AuctionId, new PlaceBidRequest(min), ct);
                if (res.Success) placed++;
            }
        }
        return new DevBotBidResult(placed);
    }

    public async Task<DevBotReadyResult> BotReadyAsync(Guid leagueId, CancellationToken ct = default)
    {
        var bots = await BotMembersAsync(leagueId, ct);
        int n = 0;
        foreach (var b in bots)
        {
            var res = await _season.SetReadyAsync(b.UserId, leagueId, new SetReadyRequest(true), ct);
            if (res.Success) n++;
        }
        return new DevBotReadyResult(n);
    }

    public async Task<DevBotLiveResult> BotLiveAsync(
        Guid leagueId, Guid fixtureId, DevBotLiveRequest request, CancellationToken ct = default)
    {
        var fixture = await _db.LeagueFixtures.FirstOrDefaultAsync(
            f => f.Id == fixtureId && f.PrivateLeagueId == leagueId, ct);
        if (fixture is null) return new DevBotLiveResult("fixture_not_found", false, false, 0);

        // The bot on one side of the fixture (a @dev.local member holding one of the two clubs).
        var side = await (
            from m in _db.LeagueMembers
            join u in _db.Users on m.UserId equals u.Id
            where m.PrivateLeagueId == leagueId && m.ClubId != null
                  && (m.ClubId == fixture.HomeClubId || m.ClubId == fixture.AwayClubId)
                  && u.Email!.EndsWith(BotDomain)
            select new { m.UserId, m.ClubId }).FirstOrDefaultAsync(ct);
        if (side is null || side.ClubId is null) return new DevBotLiveResult("no_bot_side", false, false, 0);

        Guid botUserId = side.UserId;
        Guid botClubId = side.ClubId.Value;

        // The bot joins (marks itself present); the match goes Live once the human is present too.
        var open = await _liveMatch.OpenAsync(botUserId, leagueId, fixtureId, ct);
        if (!open.Success) return new DevBotLiveResult(open.Error.ToString(), false, false, 0);

        LiveMatchStateDto state = open.Value!;
        bool wentLive = state.Status == LiveMatchStatus.Live;

        bool subMade = false;
        int usedMinute = 0;
        if (request.Sub && wentLive)
        {
            var club = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == botClubId, ct);
            if (club != null)
            {
                LineupPlan plan = BuildBotSubPlan(club);

                // Not before an already-applied change (the server enforces monotonic minutes).
                int last = 0;
                foreach (LiveChangeDto ch in state.Changes) if (ch.FromMinute > last) last = ch.FromMinute;
                usedMinute = Clamp(System.Math.Max(request.Minute, System.Math.Max(1, last)), 1, 90);

                var res = await _liveMatch.SubmitChangeAsync(
                    botUserId, leagueId, fixtureId,
                    new SubmitLiveChangeRequest(usedMinute, plan, null), ct);
                subMade = res.Success;
                if (!subMade) return new DevBotLiveResult(res.Error.ToString(), wentLive, false, usedMinute);
            }
        }

        return new DevBotLiveResult(state.Status.ToString(), wentLive, subMade, usedMinute);
    }

    /// <summary>A legal substitution for the bot's club: its best XI with one bench player brought on for
    /// the last outfield slot, as a serializable <see cref="LineupPlan"/> the live change accepts.</summary>
    private static LineupPlan BuildBotSubPlan(Persistence.Entities.Club club)
    {
        SimClub sim = WorldSquadReader.ToSimClub(club);
        Lineup xi = LineupSelector.BestEleven(sim);

        var inXi = new HashSet<int>();
        foreach (LineupSlot slot in xi.Slots)
            if (slot.Player != null) inXi.Add(slot.Player.Id);

        foreach (SimPlayer p in sim.Squad.Players)
        {
            if (inXi.Contains(p.Id)) continue;
            xi.Slots[xi.Slots.Count - 1].Player = p; // bring a bench player on (keeps the GK slot intact)
            break;
        }

        return LineupPlan.From(xi);
    }

    public async Task<DevResetResult> ResetAsync(int bots, CancellationToken ct = default)
    {
        int count = Clamp(bots, 1, 50);
        int left = 0;
        for (int n = 1; n <= count; n++)
        {
            var login = await _auth.LoginAsync(new LoginRequest(BotEmail(n), BotPassword), ct);
            if (!login.Success) continue;
            Guid botId = login.Value!.Profile.UserId;

            var mine = await _leagues.ListMineAsync(botId, ct);
            foreach (var l in mine)
            {
                var res = await _leagues.LeaveAsync(botId, l.Id, ct);
                if (res.Success) left++;
            }
        }
        return new DevResetResult(left, count);
    }

    // --- ranked fill (Phase 9.2 dev tooling) -----------------------------------------------------

    public async Task<DevRankedFillResult> FillRankedAsync(DevRankedFillRequest request, CancellationToken ct = default)
    {
        // ALL forming placement groups (oldest first). Enrol fills the earliest forming group first, so to
        // guarantee the caller's group completes we top up every forming group's free seats — an older,
        // partially-filled group would otherwise soak up the bots and leave the caller's group short.
        var groups = await _db.RankedGroups
            .Where(g => g.Kind == RankedGroupKind.Placement && g.Status == RankedGroupStatus.Forming)
            .OrderBy(g => g.CreatedUtc).ThenBy(g => g.Id)
            .ToListAsync(ct);

        int capacity = 0, free = 0;
        foreach (var g in groups)
        {
            int occ = await _db.RankedSeats.CountAsync(s => s.RankedGroupId == g.Id && s.UserId != null, ct);
            capacity += g.Capacity;
            free += System.Math.Max(0, g.Capacity - occ);
        }

        // How many to enrol: an explicit override, else exactly enough to fill every forming group.
        int needed = Clamp(request.Count ?? free, 0, 128);

        // Fresh accounts every call — a ranked coach is one row per account ever, so bots can't be reused
        // across ranked runs (unlike the league bots).
        string batch = System.Guid.NewGuid().ToString("N").Substring(0, 6);
        int enrolled = 0;
        for (int i = 0; i < needed; i++)
        {
            Guid botId = await EnsureRankedBotAsync(batch, i, ct);
            var res = await _ranked.EnrolAsync(botId, ct);
            if (res.Success) enrolled++;
        }

        // Occupancy of the groups we targeted, after filling (most/all are now Active).
        var groupIds = groups.Select(g => g.Id).ToList();
        int nowOccupied = groupIds.Count == 0
            ? enrolled
            : await _db.RankedSeats.CountAsync(s => groupIds.Contains(s.RankedGroupId) && s.UserId != null, ct);
        return new DevRankedFillResult(enrolled, nowOccupied, capacity);
    }

    public async Task<DevRankedBotMarketResult> RankedBotMarketAsync(
        Guid rankedGroupId, DevRankedBotMarketRequest request, CancellationToken ct = default)
    {
        // The group's BOT coaches (a @dev.local account holding one of its seats).
        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == rankedGroupId && s.UserId != null)
            .Select(s => s.UserId!.Value)
            .ToListAsync(ct);
        if (seats.Count == 0) return new DevRankedBotMarketResult(0, 0);

        var botIds = await _db.Users
            .Where(u => seats.Contains(u.Id) && u.Email!.EndsWith(BotDomain))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (botIds.Count == 0) return new DevRankedBotMarketResult(0, 0);

        int bids = 0, answered = 0;

        // (a) Outbid on the open auction lots: each round, a bot that isn't already leading raises to the
        // lot's minimum next bid (the service enforces budget/min-increment, so an illegal try just fails).
        int rounds = Clamp(request.Rounds, 1, 20);
        for (int r = 0; r < rounds; r++)
        {
            var view = await _rankedAuctions.GetAuctionsAsync(botIds[0], ct);
            if (!view.Success || view.Value is null || !view.Value.WindowOpen) break;

            foreach (var lot in view.Value.Lots)
            {
                if (lot.Status != RankedAuctionStatus.Open) continue;
                foreach (var botId in botIds)
                {
                    var res = await _rankedAuctions.PlaceBidAsync(
                        botId, lot.Id, new PlaceRankedBidRequest(lot.MinNextBid), ct);
                    if (res.Success) { bids++; break; } // one raise per lot per round
                }
            }
        }

        // (b) Answer the pending offers the human sent to a bot-held club.
        foreach (var botId in botIds)
        {
            var offers = await _rankedMarket.GetOffersAsync(botId, ct);
            if (!offers.Success || offers.Value is null) continue;

            foreach (var offer in offers.Value.Incoming)
            {
                if (offer.Status != RankedOfferStatus.Pending) continue;
                var res = await _rankedMarket.RespondAsync(botId, offer.Id, request.AcceptOffers, ct);
                if (res.Success) answered++;
            }
        }

        return new DevRankedBotMarketResult(bids, answered);
    }

    // --- load-test cohort (Phase 9.6 dev tooling) ------------------------------------------------

    public async Task<DevLoadSeedResult> SeedLoadCohortAsync(
        DevLoadSeedRequest request, CancellationToken ct = default)
    {
        int coaches = Clamp(request.Coaches, 1, 2000);
        int ticks = Clamp(request.Ticks, 0, 20);
        var started = System.Diagnostics.Stopwatch.StartNew();

        // Fresh accounts every run: a ranked coach is one row per account ever, so a load cohort cannot be
        // reused (same reason FillRankedAsync mints new bots). The batch tag also gives us a cheap way to
        // count the groups these coaches landed in without shipping a thousand ids into a WHERE IN.
        string batch = System.Guid.NewGuid().ToString("N").Substring(0, 6);
        string prefix = LoadBotPrefix(batch);

        var accounts = new List<DevLoadAccountDto>(coaches);
        int enrolled = 0;
        for (int i = 0; i < coaches; i++)
        {
            ct.ThrowIfCancellationRequested();
            var bot = await EnsureLoadBotAsync(batch, i, ct);
            accounts.Add(new DevLoadAccountDto(
                bot.Id, bot.Email, bot.Token, bot.RefreshToken, bot.ExpiresInSeconds));

            // The REAL enrolment path: it fills the earliest forming placement group and opens a fresh
            // world when the existing ones run out of placeable room — exactly what a launch-day rush does.
            var res = await _ranked.EnrolAsync(bot.Id, ct);
            if (res.Success) enrolled++;

            // One request seeds hundreds of coaches, and every full placement group materialises a whole
            // generated world (clubs + squads + free agents) through this same scoped DbContext. Left alone
            // the change tracker would end up holding tens of thousands of entities and the identity-fixup
            // cost would grow with every account. Each use case above saves and re-queries what it needs, so
            // dropping the tracked graph between coaches is safe — and keeps the seeding linear.
            _db.ChangeTracker.Clear();
        }

        // Start the seasons the now-full placement cohorts are waiting for, and open the first market
        // window (its free-agent lots are what the auction-spike scenario bids on).
        int seasonsStarted = 0, fixturesResolved = 0, windowsOpened = 0;
        for (int t = 0; t < ticks; t++)
        {
            var summary = await _rankedSeason.TickAsync(ct);
            seasonsStarted += summary.SeasonsStarted;
            fixturesResolved += summary.FixturesResolved;
            windowsOpened += summary.MarketWindowsOpened;
            _db.ChangeTracker.Clear();
        }

        int groups = await (
            from s in _db.RankedSeats
            join u in _db.Users on s.UserId equals (Guid?)u.Id
            where u.Email!.StartsWith(prefix)
            select s.RankedGroupId).Distinct().CountAsync(ct);

        started.Stop();
        return new DevLoadSeedResult(
            coaches, accounts.Count, enrolled, groups,
            seasonsStarted, fixturesResolved, windowsOpened,
            (long)started.Elapsed.TotalMilliseconds, accounts);
    }

    private static string LoadBotPrefix(string batch) => $"loadbot{batch}x";

    /// <summary>A fresh load-test account WITH its token pair — the generator then needs no login round trip
    /// (Identity password hashing is deliberately expensive and would dominate the ramp-up) and can keep its
    /// session alive through a long run instead of drifting into 401s.</summary>
    private async Task<(Guid Id, string Email, string Token, string RefreshToken, int ExpiresInSeconds)>
        EnsureLoadBotAsync(string batch, int i, CancellationToken ct)
    {
        string email = $"{LoadBotPrefix(batch)}{i}{BotDomain}";
        var reg = await _auth.RegisterAsync(new RegisterRequest(email, BotPassword, $"LoadBot {i}"), ct);
        if (reg.Success)
            return (reg.Value!.Profile.UserId, email, reg.Value.AccessToken,
                    reg.Value.RefreshToken, reg.Value.ExpiresInSeconds);

        var login = await _auth.LoginAsync(new LoginRequest(email, BotPassword), ct);
        if (login.Success)
            return (login.Value!.Profile.UserId, email, login.Value.AccessToken,
                    login.Value.RefreshToken, login.Value.ExpiresInSeconds);

        throw new InvalidOperationException($"Dev load seed: could not create/login {email}.");
    }

    private async Task<Guid> EnsureRankedBotAsync(string batch, int i, CancellationToken ct)
    {
        // Alphanumeric local part only (like the league bots) — a fresh GUID batch keeps it unique.
        string email = $"rankedbot{batch}x{i}{BotDomain}";
        var reg = await _auth.RegisterAsync(new RegisterRequest(email, BotPassword, $"RankedBot {i}"), ct);
        if (reg.Success) return reg.Value!.Profile.UserId;

        var login = await _auth.LoginAsync(new LoginRequest(email, BotPassword), ct);
        if (login.Success) return login.Value!.Profile.UserId;

        throw new InvalidOperationException($"Dev ranked fill: could not create/login {email}.");
    }

    // --- helpers ---------------------------------------------------------------------------------

    /// <summary>The league's bot members (email under the dev domain) that hold a club, with the club's
    /// external id — read straight from the DB (dev tooling), so no membership token is needed.</summary>
    private async Task<List<BotMember>> BotMembersAsync(Guid leagueId, CancellationToken ct)
    {
        var rows = await (
            from m in _db.LeagueMembers
            join c in _db.Clubs on m.ClubId equals (Guid?)c.Id
            join u in _db.Users on m.UserId equals u.Id
            where m.PrivateLeagueId == leagueId && m.ClubId != null && u.Email!.EndsWith(BotDomain)
            select new { m.UserId, c.ExternalId }).ToListAsync(ct);

        var list = new List<BotMember>(rows.Count);
        foreach (var r in rows) list.Add(new BotMember(r.UserId, r.ExternalId));
        return list;
    }

    private async Task<(Guid Id, string Email, string Token)> EnsureBotAsync(int n, CancellationToken ct)
    {
        string email = BotEmail(n);
        var reg = await _auth.RegisterAsync(new RegisterRequest(email, BotPassword, $"Bot {n}"), ct);
        if (reg.Success) return (reg.Value!.Profile.UserId, email, reg.Value.AccessToken);

        var login = await _auth.LoginAsync(new LoginRequest(email, BotPassword), ct);
        if (login.Success) return (login.Value!.Profile.UserId, email, login.Value.AccessToken);

        throw new InvalidOperationException($"Dev seed: could not create/login {email} ({reg.Error}/{login.Error}).");
    }

    private static string BotEmail(int n) => $"bot{n}{BotDomain}";

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    private readonly record struct Member(Guid Id, string? Email, string? Token, bool IsCreator, bool IsBot);

    // Reference type so FirstOrDefault can return null (a "no eligible bot" sentinel) in BotBidAsync.
    private sealed record BotMember(Guid UserId, int ClubExternalId);
}
