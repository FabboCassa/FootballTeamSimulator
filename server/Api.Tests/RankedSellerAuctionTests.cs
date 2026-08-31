using System.Net;
using System.Net.Http.Json;
using Fts.Application.Ranked;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Shared plumbing for task 12.2 — selling your OWN players in the ladder by auction, with a timer you
/// choose. Everything here goes through the HTTP surface, like the rest of the ranked suite.
/// </summary>
public abstract class RankedSellerAuctionTestBase : RankedSeasonTestBase
{
    /// <summary><c>RankedOptions.MinSquadSizeForSale</c>'s default — the floor a sale may not break.</summary>
    protected const int SquadFloor = 16;

    protected const int OneHour = 3_600;

    /// <summary>
    /// TAKE THE DICE OUT OF THE MONEY. A ranked world is a top flight, and after the 10.1 economy rescale a
    /// top-flight squad is worth hundreds of millions — so whether the CHEAPEST player in a squad fits
    /// inside the seeded 25M kitty depends on the world's random seed. That is a coin flip, and this project
    /// has already paid for it once: `RankedIntegrityTests` went red on CI with "no cheap player" for
    /// exactly this reason, and the cure taken then is the cure taken here — make sure the budget can NEVER
    /// be what refuses a bid, and assert budgets RELATIVE to what a club actually holds rather than against
    /// a hard-coded 25M. What these tests are about is the auction, not the kitty.
    /// </summary>
    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        settings["Ranked:StartingTransferBudget"] = "2000000000";
    }

    /// <summary>Assert a call went through, and when it did not, SAY WHY. A bare status code turns every
    /// refusal in this suite into the same red; the server's message is the whole diagnosis.</summary>
    protected static async Task AssertOk(HttpResponseMessage resp, string what)
    {
        if (resp.StatusCode == HttpStatusCode.OK) return;
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Fail($"{what}: expected OK but got {(int)resp.StatusCode} {resp.StatusCode} - {body}");
    }

    protected async Task<long> Budget(string token) => (await Auctions(token)).Budget;

    protected async Task<RankedAuctionsDto> Auctions(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/auctions", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedAuctionsDto>())!;
    }

    protected async Task<HttpResponseMessage> ListLot(string token, int playerExternalId, long reserve, int seconds)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/auctions/list", token,
            new ListRankedLotRequest(playerExternalId, reserve, seconds));
        return await Client.SendAsync(req);
    }

    protected async Task<HttpResponseMessage> Unlist(string token, Guid auctionId)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/auctions/{auctionId}/unlist", token);
        return await Client.SendAsync(req);
    }

    protected async Task<HttpResponseMessage> Bid(string token, Guid auctionId, long amount)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/auctions/{auctionId}/bid", token,
            new PlaceRankedBidRequest(amount));
        return await Client.SendAsync(req);
    }

    protected async Task<RankedSquadDto> Squad(string token, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/clubs/{clubExternalId}/squad", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedSquadDto>())!;
    }

    protected async Task ForceSettle()
    {
        var resp = await Client.PostAsync("/internal/ranked/auctions/settle", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>Enrol a full cohort and tick once: the placement season starts and its opening window puts
    /// the free agents up, which is the board a seller lot then has to coexist with.</summary>
    protected async Task<List<string>> StartSeason()
    {
        var tokens = await EnrolCohort();
        await Tick();
        Assert.That((await Auctions(tokens[0])).Lots, Is.Not.Empty, "a window opening creates free-agent lots");
        return tokens;
    }

    protected async Task<int> MyClub(string token) => (await GetMine(token)).ClubExternalId!.Value;

    /// <summary>The caller's own cheapest player — cheap enough that a rival's seeded budget covers him
    /// several times over, so a test never fails on affordability.</summary>
    protected async Task<RankedPlayerDto> CheapestOfMine(string token)
    {
        var squad = await Squad(token, await MyClub(token));
        return squad.Players.OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId).First();
    }

    protected static RankedAuctionLotDto LotFor(RankedAuctionsDto board, int playerExternalId) =>
        board.Lots.Single(l => l.PlayerExternalId == playerExternalId);
}

/// <summary>
/// Task 12.2, the main body: a coach puts one of his own players up, the board carries both kinds of lot at
/// once, the money ends up with the seller, and the guards (duration range, window clamp, squad floor,
/// ownership) all bite. Settlement is FORCED here — the per-lot timer running out on its own is what
/// <see cref="RankedSellerLotTimerTests"/> exercises.
/// </summary>
[TestFixture]
public class RankedSellerAuctionTests : RankedSellerAuctionTestBase
{
    [Test]
    public async Task ListingAPlayer_PutsHimOnTheSameBoardAsTheFreeAgents_AsASellerLot()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var player = await CheapestOfMine(a);
        int freeAgentsBefore = (await Auctions(a)).Lots.Count;

        await AssertOk(await ListLot(a, player.ExternalId, player.MarketValue, OneHour), "listing");

        var board = await Auctions(a);
        var lot = LotFor(board, player.ExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(board.Lots, Has.Count.EqualTo(freeAgentsBefore + 1), "the lot joins the existing board");
            Assert.That(lot.Kind, Is.EqualTo(RankedLotKind.Seller));
            Assert.That(lot.SellerClubExternalId, Is.EqualTo(board.YourClubExternalId));
            Assert.That(lot.YouAreSeller, Is.True);
            Assert.That(lot.StartPrice, Is.EqualTo(player.MarketValue), "the reserve is what the seller asked");
            Assert.That(lot.MinNextBid, Is.EqualTo(player.MarketValue), "the first bid has to meet the reserve");
            Assert.That(board.WindowClosesUtc, Is.Not.Null, "the screen can say when the window shuts");
        });
    }

    /// <summary>THE ✅, money half: B wins the lot, the player changes club and the FEE GOES TO THE SELLER —
    /// which is what a ranked auction could not do before this task.</summary>
    [Test]
    public async Task ACoachSellsHisOwnPlayer_AndIsPaidAtSettlement()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var player = await CheapestOfMine(a);

        long budgetABefore = await Budget(a), budgetBBefore = await Budget(b);
        await AssertOk(await ListLot(a, player.ExternalId, player.MarketValue, OneHour), "listing");

        var lot = LotFor(await Auctions(b), player.ExternalId);
        long price = lot.MinNextBid;
        await AssertOk(await Bid(b, lot.Id, price), "b opens on the lot");

        await ForceSettle();

        var squadA = await Squad(a, await MyClub(a));
        var squadB = await Squad(b, await MyClub(b));
        long budgetA = await Budget(a), budgetB = await Budget(b);
        Assert.Multiple(() =>
        {
            Assert.That(squadB.Players.Any(p => p.ExternalId == player.ExternalId), Is.True, "the buyer gets him");
            Assert.That(squadA.Players.Any(p => p.ExternalId == player.ExternalId), Is.False, "the seller loses him");
            Assert.That(budgetB, Is.EqualTo(budgetBBefore - price), "the buyer pays");
            Assert.That(budgetA, Is.EqualTo(budgetABefore + price), "THE SELLER IS PAID");
        });
    }

    /// <summary>THE ✅, refusal half: the duration is a real range, and a value outside it is refused rather
    /// than quietly rounded into shape.</summary>
    [Test]
    public async Task ADurationOutsideOneToTwentyFourHours_IsRefused()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var player = await CheapestOfMine(a);

        var tooLong = await ListLot(a, player.ExternalId, player.MarketValue, 25 * OneHour);
        var tooShort = await ListLot(a, player.ExternalId, player.MarketValue, OneHour / 2);
        bool onTheBoard = (await Auctions(a)).Lots.Any(l => l.PlayerExternalId == player.ExternalId);

        Assert.Multiple(() =>
        {
            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "25h is refused");
            Assert.That(tooShort.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "30 minutes is refused");
            Assert.That(onTheBoard, Is.False, "nothing was listed on either attempt");
        });
    }

    /// <summary>A lot never outlives the market that allowed it (decided with the user): 24h asked for
    /// inside a one-hour window comes back cut down to the window's close.</summary>
    [Test]
    public async Task ALotIsClampedToTheWindowClose()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var player = await CheapestOfMine(a);

        await AssertOk(await ListLot(a, player.ExternalId, player.MarketValue, 24 * OneHour),
            "24h is inside the allowed range");

        var board = await Auctions(a);
        var lot = LotFor(board, player.ExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(lot.EndsUtc, Is.LessThanOrEqualTo(board.WindowClosesUtc!.Value), "…but not past the window");
            Assert.That(lot.SecondsRemaining, Is.LessThanOrEqualTo(WindowSeconds));
            Assert.That(board.MaxLotSeconds, Is.LessThanOrEqualTo(WindowSeconds),
                "the duration picker is told what it may still offer");
        });
    }

    [Test]
    public async Task ListingSomeoneElsesPlayer_IsRefused()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var his = await CheapestOfMine(b);

        var resp = await ListLot(a, his.ExternalId, his.MarketValue, OneHour);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "you can only auction your own");
    }

    [Test]
    public async Task ListingTheSamePlayerTwice_IsRefused()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var player = await CheapestOfMine(a);

        Assert.That((await ListLot(a, player.ExternalId, player.MarketValue, OneHour)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await ListLot(a, player.ExternalId, player.MarketValue, OneHour)).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict), "he is already on the board");
    }

    [Test]
    public async Task TheSeller_CannotBidOnHisOwnLot()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var player = await CheapestOfMine(a);
        await ListLot(a, player.ExternalId, player.MarketValue, OneHour);

        var lot = LotFor(await Auctions(a), player.ExternalId);
        Assert.That((await Bid(a, lot.Id, lot.MinNextBid)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "bidding against yourself with money that comes back to you is not an auction");
    }

    /// <summary>The floor counts the BOARD, not just the squad — otherwise a coach lists his way under it
    /// one lot at a time and only finds out when the money is already promised.</summary>
    [Test]
    public async Task TheSquadFloor_CountsThePlayersAlreadyOnTheBoard()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        var squad = await Squad(a, await MyClub(a));
        int listable = squad.Players.Count - SquadFloor;
        Assert.That(listable, Is.GreaterThan(0), "a ranked squad starts above the floor");

        var cheapestFirst = squad.Players.OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId).ToList();
        for (int i = 0; i < listable; i++)
        {
            var resp = await ListLot(a, cheapestFirst[i].ExternalId, cheapestFirst[i].MarketValue, OneHour);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"listing #{i + 1} is still above the floor");
        }

        var overTheLine = cheapestFirst[listable];
        Assert.That((await ListLot(a, overTheLine.ExternalId, overTheLine.MarketValue, OneHour)).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict), "the one that would break the floor is refused");
    }

    [Test]
    public async Task AReserveOutsideTheIntegrityBand_IsRefused()
    {
        var tokens = await StartSeason();
        string a = tokens[0];
        // Expensive enough that the band's "don't police the cheap stuff" floor cannot swallow the check.
        var squad = await Squad(a, await MyClub(a));
        var player = squad.Players.OrderByDescending(p => p.MarketValue).First();

        var resp = await ListLot(a, player.ExternalId, Math.Max(25_000, player.MarketValue / 100), OneHour);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "a star opened at 1% of his value is a gift with extra steps");
    }

    [Test]
    public async Task ASellerCanPullHisLotBack_UntilSomeoneBids()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var player = await CheapestOfMine(a);
        await ListLot(a, player.ExternalId, player.MarketValue, OneHour);

        var lot = LotFor(await Auctions(a), player.ExternalId);
        Assert.That((await Unlist(a, lot.Id)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Auctions(a)).Lots.Any(l => l.Id == lot.Id), Is.False, "the lot is off the board");

        // List him again, and this time let someone bid: now the timer is the only way out.
        await ListLot(a, player.ExternalId, player.MarketValue, OneHour);
        var relisted = LotFor(await Auctions(a), player.ExternalId);
        await AssertOk(await Bid(b, relisted.Id, relisted.MinNextBid), "someone bids on the relisted lot");
        Assert.That((await Unlist(a, relisted.Id)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "a seller who could withdraw after seeing the bidding is running a fake auction");
    }

    /// <summary>The ✅'s last clause: listing is an ADDITION to the 9.2b market, not a replacement — a
    /// direct offer for a player who is not on the board still works exactly as before.</summary>
    [Test]
    public async Task ADirectOffer_StillWorks_WhileALotIsOnTheBoard()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var squad = (await Squad(a, await MyClub(a))).Players
            .OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId).ToList();
        var listed = squad[0];
        var wanted = squad[1];

        await ListLot(a, listed.ExternalId, listed.MarketValue, OneHour);

        using var offer = Authed(HttpMethod.Post, "/ranked/offers", b,
            new MakeRankedOfferRequest(wanted.ExternalId, wanted.MarketValue));
        var offerResp = await Client.SendAsync(offer);
        Assert.That(offerResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var incoming = (await offerResp.Content.ReadFromJsonAsync<RankedOffersDto>())!;
        var pending = (await Offers(a)).Incoming.Single(o => o.PlayerExternalId == wanted.ExternalId);

        using var accept = Authed(HttpMethod.Post, $"/ranked/offers/{pending.Id}/accept", a);
        Assert.That((await Client.SendAsync(accept)).StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the direct route is untouched by the auction board");
        Assert.That(incoming.Outgoing, Is.Not.Empty);

        async Task<RankedOffersDto> Offers(string token)
        {
            using var req = Authed(HttpMethod.Get, "/ranked/offers", token);
            var resp = await Client.SendAsync(req);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            return (await resp.Content.ReadFromJsonAsync<RankedOffersDto>())!;
        }
    }
}

/// <summary>
/// THE ✅'s hardest half: a lot closes on ITS OWN timer while the rest of the board is still bidding. The
/// seller-lot floor is compressed to a second so the timer can actually elapse inside a test, and the
/// anti-snipe extension is switched off so nothing pushes that end back.
/// </summary>
[TestFixture]
public class RankedSellerLotTimerTests : RankedSellerAuctionTestBase
{
    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        base.ConfigureExtra(settings);   // keep the base's "money is never the reason" budget
        settings["Ranked:SellerLotMinSeconds"] = "1";
        settings["Ranked:AuctionAntiSnipeSeconds"] = "0";
    }

    [Test]
    public async Task ALotSettlesOnItsOwnTimer_WhileTheRestOfTheBoardStaysOpen()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var player = await CheapestOfMine(a);
        long budgetABefore = await Budget(a), budgetBBefore = await Budget(b);

        // Three seconds, not one: the lot has to outlive a couple of HTTP round-trips or the test is racing
        // its own timer, which is a different thing from proving the timer works.
        await AssertOk(await ListLot(a, player.ExternalId, player.MarketValue, 3), "a three-second lot");

        var lot = LotFor(await Auctions(b), player.ExternalId);
        long price = lot.MinNextBid;
        TestContext.Out.WriteLine(
            $"[ranked-lot-timer] {player.Name} value {player.MarketValue:N0}, reserve {lot.StartPrice:N0}, "
            + $"buyer budget {budgetBBefore:N0}");
        await AssertOk(await Bid(b, lot.Id, price), "b bids the reserve");

        var freeAgentsBefore = (await Auctions(b)).Lots
            .Where(l => l.Kind == RankedLotKind.FreeAgent).Select(l => l.Id).ToHashSet();
        Assert.That(freeAgentsBefore, Is.Not.Empty);

        await Task.Delay(3_500);
        await Tick();   // the tick settles whatever is DUE — one lot, not the board

        var boardAfter = await Auctions(b);
        var stillOpen = boardAfter.Lots.Where(l => l.Kind == RankedLotKind.FreeAgent).Select(l => l.Id).ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(boardAfter.Lots.Any(l => l.PlayerExternalId == player.ExternalId), Is.False,
                "the seller's lot closed at its own end");
            Assert.That(stillOpen.SetEquals(freeAgentsBefore), Is.True,
                "THE POINT OF THE TASK: every other lot is still open, on its own clock");
        });

        var squadB = await Squad(b, await MyClub(b));
        long sellerBudget = await Budget(a);
        Assert.Multiple(() =>
        {
            Assert.That(squadB.Players.Any(p => p.ExternalId == player.ExternalId), Is.True, "the buyer got him");
            Assert.That(boardAfter.Budget, Is.EqualTo(budgetBBefore - price), "the buyer paid");
            Assert.That(sellerBudget, Is.EqualTo(budgetABefore + price), "the seller was paid");
        });
    }
}

/// <summary>
/// The anti-snipe rule, brought to the ladder now that lots have their own timers. The window is blown up to
/// two hours so ANY bid lands inside it — waiting out a real 30-second snipe window would be a test that
/// sleeps for half a minute and still races the clock.
/// </summary>
[TestFixture]
public class RankedSellerLotSnipeTests : RankedSellerAuctionTestBase
{
    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        base.ConfigureExtra(settings);
        settings["Ranked:AuctionAntiSnipeSeconds"] = "7200";
    }

    [Test]
    public async Task ABidInTheLastSeconds_ExtendsThatLotOnly()
    {
        var tokens = await StartSeason();
        string a = tokens[0], b = tokens[1];
        var player = await CheapestOfMine(a);
        await ListLot(a, player.ExternalId, player.MarketValue, OneHour);

        var before = await Auctions(b);
        var lot = LotFor(before, player.ExternalId);
        var freeAgentBefore = before.Lots.First(l => l.Kind == RankedLotKind.FreeAgent);

        using var req = Authed(HttpMethod.Post, $"/ranked/auctions/{lot.Id}/bid", b,
            new PlaceRankedBidRequest(lot.MinNextBid));
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = (await resp.Content.ReadFromJsonAsync<RankedBidResultDto>())!;

        var after = await Auctions(b);
        var lotAfter = LotFor(after, player.ExternalId);
        var freeAgentAfter = after.Lots.Single(l => l.Id == freeAgentBefore.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Extended, Is.True, "the bid landed inside the anti-snipe window");
            Assert.That(lotAfter.EndsUtc, Is.GreaterThan(lot.EndsUtc), "that lot's end moved back");
            Assert.That(freeAgentAfter.EndsUtc, Is.EqualTo(freeAgentBefore.EndsUtc),
                "and nothing else on the board moved with it");
        });
    }
}

/// <summary>
/// The user's decision, tested: the AI clubs come shopping on the coaches' board, so selling works in a
/// group that is mostly vacant seats. That needs a DIVISION — a placement group is all human by
/// construction — so the fixture runs the placement season out first, exactly as the 9.2 season test does.
/// </summary>
[TestFixture]
public class RankedSellerLotBotTests : RankedSellerAuctionTestBase
{
    [Test]
    public async Task TheAiClubs_BidOnAPlayerACoachPutsUp()
    {
        var tokens = await EnrolCohort();
        string a = tokens[0];

        // Placement → division, with the division's opening market window live.
        bool ready = false;
        for (int i = 0; i < 60 && !ready; i++)
        {
            await Tick();
            if ((await GetMine(a)).Status != RankedCoachStatus.Placed) continue;
            var (code, season) = await GetSeason(a);
            ready = code == HttpStatusCode.OK
                    && season?.State is { Kind: RankedGroupKind.Division, Started: true }
                    && (await Auctions(a)).WindowOpen;
        }
        Assert.That(ready, Is.True, "the placed coach's division season is running with its window open");

        // Something a bot would actually want, priced inside the seeded budget: the reserve is the asking
        // price, so a player worth more than the whole kitty could never draw a bid from anyone.
        var squad = await Squad(a, await MyClub(a));
        var affordable = squad.Players.Where(p => p.MarketValue > 0 && p.MarketValue <= 20_000_000).ToList();
        if (affordable.Count == 0)
            affordable = squad.Players.OrderBy(p => p.MarketValue).Take(5).ToList();
        var offerUp = affordable
            .OrderByDescending(p => p.Overall).ThenBy(p => p.ExternalId)
            .Take(5)
            .ToList();
        Assert.That(offerUp, Is.Not.Empty, "the squad has players inside a bot's reach");

        foreach (var p in offerUp)
            await AssertOk(await ListLot(a, p.ExternalId, p.MarketValue, OneHour), $"listing {p.Name}");

        await Tick();   // the tick is when the bots look at the board

        var board = await Auctions(a);
        var mine = board.Lots.Where(l => l.YouAreSeller).ToList();
        int bidOn = mine.Count(l => l.HighBid > 0);
        TestContext.Out.WriteLine(
            $"[ranked-lot] listed {mine.Count}, bot bids on {bidOn}: "
            + string.Join(", ", mine.Where(l => l.HighBid > 0).Select(l => $"{l.PlayerName} {l.HighBid:N0}")));

        Assert.Multiple(() =>
        {
            Assert.That(bidOn, Is.GreaterThan(0),
                "an AI club bid on a player a coach put up — a sell flow must not need another human online");
            Assert.That(mine.Where(l => l.HighBid > 0).All(l => l.HighBid >= l.StartPrice), Is.True,
                "a bot still has to meet the reserve");
            Assert.That(mine.All(l => !l.YouAreLeading), Is.True, "and the seller is never the leader on his own lot");
        });
    }
}
