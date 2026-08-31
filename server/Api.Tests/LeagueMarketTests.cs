using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The private-league transfer market over the real HTTP pipeline (Phase 12.1) against in-memory SQLite.
///
/// THE 12.1 ✅, point by point: <see cref="TwoCoaches_ListAndHaggle_UntilTheySettle"/> is "A lists a player
/// and offers for one of B's; B counters and they settle"; <see cref="Offer_ToABotClub_IsAnsweredAtOnce"/>
/// is "A also buys from a bot club"; <see cref="UnansweredOffer_ExpiresWhenTheRoundResolves"/> is the rule
/// the user chose INSTEAD of the roadmap's AI auto-answer — a private league is played together, so nobody
/// answers in your place and silence simply means no; <see cref="TwoCoaches_ChaseTheSameFreeAgent_OnlyOneSignsHim"/>
/// is the free-agent race; and the budget/squad bookkeeping is asserted on both clients' views after each
/// deal.
///
/// Every league here is created LARGER than its membership, so the unclaimed clubs are bots — that is what
/// makes "a human club is treated exactly like a bot club" testable in one world.
/// </summary>
[TestFixture]
public class LeagueMarketTests
{
    private AuthTestFactory _factory = null!;
    private HttpClient _client = null!;

    private const string ValidPassword = "Password1";
    private const long DraftBudget = 25_000_000; // 8.2 DraftTransferBudget — every club after the draft.

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new AuthTestFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // --- the window --------------------------------------------------------------------------------

    [Test]
    public async Task Market_OpensPreSeason_AndShutsOnceTheRoundResolves()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);

        var open = await GetMarket(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(open.Window.Open, Is.True, "window 0 opens the moment the draft closes");
            Assert.That(open.Window.WindowIndex, Is.EqualTo(0));
            Assert.That(open.Window.RoundsPlayed, Is.EqualTo(0));
            Assert.That(open.Window.TotalRounds, Is.EqualTo(10), "6 clubs, double round-robin");
            Assert.That(open.Window.ClosesAfterRound, Is.EqualTo(1));
            Assert.That(open.YourBudget, Is.EqualTo(DraftBudget));
            Assert.That(open.YourSquad, Has.Count.EqualTo(22));
            Assert.That(open.Clubs, Has.Count.EqualTo(5), "every other club in the world, bots included");
            Assert.That(open.Clubs.Count(c => c.IsHuman), Is.EqualTo(1), "one friend, four bots");
            Assert.That(open.FreeAgents, Is.Not.Empty, "the seeded free agents are signable, not auctioned");
        });

        await Advance(accounts[0].Tok, leagueId);

        var shut = await GetMarket(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(shut.Window.Open, Is.False, "one round played and the pre-season window is gone");
            Assert.That(shut.Window.RoundsPlayed, Is.EqualTo(1));
            Assert.That(shut.Window.NextOpensAfterRound, Is.EqualTo(5), "mid-season is halfway through");
        });

        var refused = await Offer(accounts[0].Tok, leagueId, FirstBotPlayer(shut).ExternalId, 1_000_000);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "no trading with the market shut");
    }

    [Test]
    public async Task Market_ReopensAtTheMidSeasonRound()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        for (int round = 1; round <= 5; round++) await Advance(accounts[0].Tok, leagueId);

        var market = await GetMarket(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(market.Window.Open, Is.True, "the mid-season window is open");
            Assert.That(market.Window.WindowIndex, Is.EqualTo(1));
            Assert.That(market.Window.RoundsPlayed, Is.EqualTo(5));
        });
    }

    // --- buying from a bot -------------------------------------------------------------------------

    /// <summary>"A also buys from a bot club": a bot answers inside the request, with the same
    /// <c>NegotiationModel</c> the single-player career uses.</summary>
    [Test]
    public async Task Offer_ToABotClub_IsAnsweredAtOnce()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var bot = FullBotClub(market);
        var target = Sellable(bot.Players);
        // A bot's budget is NOT the draft budget any more — it did its own business at window 0 — so the
        // assertion has to be relative to what it actually holds now.
        long botBudgetBefore = bot.TransferBudget;

        // Twice market value clears any asking price the personality table can produce, and stays inside
        // the 9.5 integrity band (40%–250%).
        long fee = Math.Max(50_000, target.MarketValue * 2);
        var resp = await Offer(accounts[0].Tok, leagueId, target.ExternalId, fee);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = (await resp.Content.ReadFromJsonAsync<LeagueMarketDto>())!;

        var deal = after.Outgoing.First(o => o.PlayerExternalId == target.ExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(deal.Status, Is.EqualTo(LeagueOfferStatus.Accepted), "a bot does not sit on an offer");
            Assert.That(after.YourSquad.Any(p => p.ExternalId == target.ExternalId), Is.True, "he is yours");
            Assert.That(after.YourSquad, Has.Count.EqualTo(23));
            Assert.That(after.YourBudget, Is.EqualTo(DraftBudget - deal.Amount), "the fee left your budget");
            Assert.That(after.News.Any(n => n.PlayerExternalId == target.ExternalId && n.InvolvesYou), Is.True,
                "the deal is in the news feed");
        });

        // The selling bot banked the money.
        var seller = (await GetMarket(accounts[0].Tok, leagueId)).Clubs.First(c => c.ClubExternalId == bot.ClubExternalId);
        Assert.That(seller.TransferBudget, Is.EqualTo(botBudgetBefore + deal.Amount));
        Assert.That(seller.Players.Any(p => p.ExternalId == target.ExternalId), Is.False);
    }

    [Test]
    public async Task Offer_AtAGiftPrice_IsRefusedByTheIntegrityBand()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var friend = market.Clubs.First(c => c.IsHuman);
        var star = friend.Players.OrderByDescending(p => p.MarketValue).First();
        Assert.That(star.MarketValue, Is.GreaterThan(250_000), "the guard only polices players worth policing");

        var resp = await Offer(accounts[0].Tok, leagueId, star.ExternalId, star.MarketValue / 100);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "a star for pocket change is refused");
    }

    [Test]
    public async Task Offer_ForYourOwnPlayer_IsRejected()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var mine = market.YourSquad.First();

        var resp = await Offer(accounts[0].Tok, leagueId, mine.ExternalId, 1_000_000);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Market_ByNonMember_ReturnsForbidden()
    {
        var (leagueId, _) = await CreateActiveLeague(size: 6, humans: 2);
        var (strangerTok, _) = await RegisterAccount();

        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/market", strangerTok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- two coaches, one negotiation: THE 12.1 ✅ --------------------------------------------------

    /// <summary>"B lists a player, A offers for one of B's, B counters and they settle" — with both
    /// clients' budgets, squads and news agreeing afterwards.</summary>
    [Test]
    public async Task TwoCoaches_ListAndHaggle_UntilTheySettle()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var a = accounts[0];
        var b = accounts[1];

        // B puts a player in the shop window at his own price.
        var bMarket = await GetMarket(b.Tok, leagueId);
        var forSale = Sellable(bMarket.YourSquad);
        long asking = forSale.MarketValue * 3 / 2;
        var listed = await SetListing(b.Tok, leagueId, forSale.ExternalId, true, asking);
        Assert.That(listed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var aSees = await GetMarket(a.Tok, leagueId);
        var shopWindow = aSees.Clubs.First(c => c.ClubExternalId == bMarket.YourClubExternalId)
            .Players.First(p => p.ExternalId == forSale.ExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(shopWindow.Listed, Is.True, "A can see he is for sale");
            Assert.That(shopWindow.AskingPrice, Is.EqualTo(asking));
        });

        // A offers under the asking price.
        long opening = forSale.MarketValue;
        Assert.That((await Offer(a.Tok, leagueId, forSale.ExternalId, opening)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        // The offer is waiting on B — and his league summary says so, which is what the home screen badges.
        // Note the BASELINE: the bot clubs bid on human squads too (12.1's "anche i bot provano a comprare"),
        // so B's inbox is not empty to begin with and every count here is relative.
        var bAfterOffer = await GetMarket(b.Tok, leagueId);
        var negotiation = Negotiation(bAfterOffer.AwaitingYou, forSale.ExternalId, a.ClubExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(negotiation.PlayerExternalId, Is.EqualTo(forSale.ExternalId));
            Assert.That(negotiation.Status, Is.EqualTo(LeagueOfferStatus.Pending));
            Assert.That(negotiation.Amount, Is.EqualTo(opening));
            Assert.That(negotiation.ProposedBy, Is.EqualTo(LeagueOfferParty.Buyer));
            Assert.That(negotiation.YouAreSeller, Is.True);
        });
        int badgeWithOffer = (await ListMine(b.Tok)).Single(l => l.Id == leagueId).OffersAwaitingYou;
        Assert.That(badgeWithOffer, Is.EqualTo(bAfterOffer.AwaitingYou.Count),
            "the home-screen badge counts exactly the negotiations waiting on him");
        Assert.That(badgeWithOffer, Is.GreaterThanOrEqualTo(1), "the home screen has a number to shout");

        // B counters; the ball is back in A's court.
        long counter = asking;
        Assert.That((await Respond(b.Tok, leagueId, negotiation.Id, LeagueOfferAction.Counter, counter)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var aAfterCounter = await GetMarket(a.Tok, leagueId);
        var onATable = Negotiation(aAfterCounter.AwaitingYou, forSale.ExternalId, a.ClubExternalId);
        Assert.Multiple(() =>
        {
            Assert.That(onATable.Amount, Is.EqualTo(counter));
            Assert.That(onATable.ProposedBy, Is.EqualTo(LeagueOfferParty.Seller));
            Assert.That(onATable.YouAreBuyer, Is.True);
        });
        Assert.That((await ListMine(b.Tok)).Single(l => l.Id == leagueId).OffersAwaitingYou,
            Is.EqualTo(badgeWithOffer - 1), "this one is not B's move any more");

        // Only the side being waited on may answer.
        Assert.That((await Respond(b.Tok, leagueId, onATable.Id, LeagueOfferAction.Accept, 0)).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict), "B cannot accept his own counter");

        // A accepts. Money and player move on both sides.
        var settle = await Respond(a.Tok, leagueId, onATable.Id, LeagueOfferAction.Accept, 0);
        Assert.That(settle.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var aFinal = (await settle.Content.ReadFromJsonAsync<LeagueMarketDto>())!;
        var bFinal = await GetMarket(b.Tok, leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(aFinal.YourSquad.Any(p => p.ExternalId == forSale.ExternalId), Is.True, "A signed him");
            Assert.That(aFinal.YourSquad, Has.Count.EqualTo(23));
            Assert.That(aFinal.YourBudget, Is.EqualTo(DraftBudget - counter));
            Assert.That(bFinal.YourSquad.Any(p => p.ExternalId == forSale.ExternalId), Is.False, "B sold him");
            Assert.That(bFinal.YourSquad, Has.Count.EqualTo(21));
            Assert.That(bFinal.YourBudget, Is.EqualTo(DraftBudget + counter));
            Assert.That(bFinal.YourSquad.Any(p => p.ExternalId == forSale.ExternalId && p.Listed), Is.False,
                "a sold player leaves the transfer list");
            Assert.That(aFinal.News.First(n => n.PlayerExternalId == forSale.ExternalId).Fee, Is.EqualTo(counter));
            Assert.That(bFinal.AwaitingYou.Any(o => o.PlayerExternalId == forSale.ExternalId), Is.False,
                "the negotiation over him is closed on both sides");
        });
    }

    [Test]
    public async Task RejectedOffer_EndsTheNegotiation()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var (a, b) = (accounts[0], accounts[1]);

        var bMarket = await GetMarket(b.Tok, leagueId);
        var target = Sellable(bMarket.YourSquad);
        await Offer(a.Tok, leagueId, target.ExternalId, target.MarketValue);

        var pending = Negotiation((await GetMarket(b.Tok, leagueId)).AwaitingYou, target.ExternalId, a.ClubExternalId);
        Assert.That((await Respond(b.Tok, leagueId, pending.Id, LeagueOfferAction.Reject, 0)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var aAfter = await GetMarket(a.Tok, leagueId);
        var bAfter = await GetMarket(b.Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(aAfter.Outgoing.Single(o => o.Id == pending.Id).Status,
                Is.EqualTo(LeagueOfferStatus.Rejected));
            Assert.That(aAfter.YourBudget, Is.EqualTo(DraftBudget), "nothing moved");
            Assert.That(bAfter.YourSquad, Has.Count.EqualTo(22));
        });

        // A dead negotiation cannot be answered again.
        Assert.That((await Respond(b.Tok, leagueId, pending.Id, LeagueOfferAction.Accept, 0)).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Buyer_CanWithdrawWhileTheOfferIsStillPending()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var (a, b) = (accounts[0], accounts[1]);

        var bMarket = await GetMarket(b.Tok, leagueId);
        var target = Sellable(bMarket.YourSquad);
        await Offer(a.Tok, leagueId, target.ExternalId, target.MarketValue);

        var pending = Negotiation((await GetMarket(a.Tok, leagueId)).Outgoing, target.ExternalId, a.ClubExternalId);
        Assert.That((await Respond(a.Tok, leagueId, pending.Id, LeagueOfferAction.Withdraw, 0)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        // NOT "his table is empty": the bots are bidding on his club too. What must be gone is A's offer.
        Assert.That((await GetMarket(b.Tok, leagueId)).AwaitingYou.Any(o => o.Id == pending.Id), Is.False,
            "the withdrawn offer is off the seller's table");
    }

    /// <summary>THE RULE THE USER CHOSE over the roadmap's AI auto-answer: a private league is played
    /// together, so nobody answers in your place — an unanswered offer just expires when the round
    /// resolves, and "no answer" means "not accepted".</summary>
    [Test]
    public async Task UnansweredOffer_ExpiresWhenTheRoundResolves()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var (a, b) = (accounts[0], accounts[1]);

        var bMarket = await GetMarket(b.Tok, leagueId);
        var target = Sellable(bMarket.YourSquad);
        await Offer(a.Tok, leagueId, target.ExternalId, target.MarketValue);
        Assert.That((await GetMarket(b.Tok, leagueId)).AwaitingYou.Any(o => o.PlayerExternalId == target.ExternalId),
            Is.True, "A's offer is on B's table");

        await Advance(a.Tok, leagueId);

        var aAfter = await GetMarket(a.Tok, leagueId);
        var bAfter = await GetMarket(b.Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(aAfter.Outgoing.Single(o => o.PlayerExternalId == target.ExternalId).Status,
                Is.EqualTo(LeagueOfferStatus.Expired),
                "silence is not consent — and it is not a deal either");
            Assert.That(bAfter.AwaitingYou, Is.Empty,
                "the round wipes the table clean — the bots' own bids expire the same way");
            Assert.That(bAfter.YourSquad.Any(p => p.ExternalId == target.ExternalId), Is.True, "he stayed");
            Assert.That(aAfter.YourBudget, Is.EqualTo(DraftBudget));
        });
        Assert.That((await ListMine(b.Tok)).Single(l => l.Id == leagueId).OffersAwaitingYou, Is.EqualTo(0));
    }

    // --- selling to the world ----------------------------------------------------------------------

    /// <summary>Listing must not depend on a friend being online: the bots that need him bid at once.</summary>
    [Test]
    public async Task ListingAPlayer_BringsBotOffersIn()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 8, humans: 2);
        var seller = accounts[1];

        var market = await GetMarket(seller.Tok, leagueId);
        var forSale = Sellable(market.YourSquad);
        var resp = await SetListing(seller.Tok, leagueId, forSale.ExternalId, true, askingPrice: 0);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = (await resp.Content.ReadFromJsonAsync<LeagueMarketDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(after.YourSquad.Single(p => p.ExternalId == forSale.ExternalId).AskingPrice,
                Is.GreaterThan(0), "an asking price of 0 means 'price him for me'");
            Assert.That(after.AwaitingYou.Any(o => o.PlayerExternalId == forSale.ExternalId && o.YouAreSeller),
                Is.True, "the bot clubs came knocking straight away");
        });

        // Accepting a bot's bid moves the money and the player.
        var bid = after.AwaitingYou.First(o => o.PlayerExternalId == forSale.ExternalId && o.YouAreSeller);
        var sold = await Respond(seller.Tok, leagueId, bid.Id, LeagueOfferAction.Accept, 0);
        Assert.That(sold.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var final = (await sold.Content.ReadFromJsonAsync<LeagueMarketDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(final.YourSquad.Any(p => p.ExternalId == forSale.ExternalId), Is.False);
            Assert.That(final.YourSquad, Has.Count.EqualTo(21));
            Assert.That(final.YourBudget, Is.EqualTo(DraftBudget + bid.Amount));
        });
    }

    [Test]
    public async Task Listing_SomeoneElsesPlayer_IsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var theirs = market.Clubs.First(c => c.IsHuman).Players.First();

        var resp = await SetListing(accounts[0].Tok, leagueId, theirs.ExternalId, true, 1_000_000);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- free agents: the race ---------------------------------------------------------------------

    /// <summary>THE 12.1 ✅ free-agent half: "A and B chase the SAME free agent and only the one who agrees
    /// terms first gets him, the other is told he is gone."</summary>
    [Test]
    public async Task TwoCoaches_ChaseTheSameFreeAgent_OnlyOneSignsHim()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var (a, b) = (accounts[0], accounts[1]);

        var market = await GetMarket(a.Tok, leagueId);
        var wanted = market.FreeAgents
            .Where(f => f.SigningCostAtDemand <= DraftBudget)
            .OrderByDescending(f => f.Overall)
            .First();

        var first = await SignFreeAgent(a.Tok, leagueId, wanted.ExternalId, wanted.DemandedWeeklyWage, seasons: 3);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var signed = (await first.Content.ReadFromJsonAsync<FreeAgentSigningDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(signed.Signed, Is.True, "A agreed terms first");
            Assert.That(signed.Market.YourSquad.Any(p => p.ExternalId == wanted.ExternalId), Is.True);
            Assert.That(signed.Market.YourSquad, Has.Count.EqualTo(23));
            Assert.That(signed.Market.YourBudget, Is.EqualTo(DraftBudget - signed.SigningCost),
                "a free transfer still costs the first season's wages up front");
            Assert.That(signed.Market.FreeAgents.Any(f => f.ExternalId == wanted.ExternalId), Is.False,
                "he is off the free-agent list");
        });

        var second = await SignFreeAgent(b.Tok, leagueId, wanted.ExternalId, wanted.DemandedWeeklyWage * 5, seasons: 3);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "first come, first served — money cannot beat being first");

        var bAfter = await GetMarket(b.Tok, leagueId);
        Assert.That(bAfter.YourSquad, Has.Count.EqualTo(22), "B's squad is untouched");
    }

    [Test]
    public async Task FreeAgent_BelowHisDemand_SaysWhatHeWantsInsteadOfFailing()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var wanted = market.FreeAgents.First(f => f.DemandedWeeklyWage > 1);

        var resp = await SignFreeAgent(accounts[0].Tok, leagueId, wanted.ExternalId, wanted.DemandedWeeklyWage - 1, 3);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var outcome = (await resp.Content.ReadFromJsonAsync<FreeAgentSigningDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Signed, Is.False);
            Assert.That(outcome.DemandedWeeklyWage, Is.EqualTo(wanted.DemandedWeeklyWage));
            Assert.That(outcome.Message, Is.Not.Null, "he tells you what he wants");
            Assert.That(outcome.Market.YourSquad, Has.Count.EqualTo(22));
            Assert.That(outcome.Market.YourBudget, Is.EqualTo(DraftBudget));
        });
    }

    [Test]
    public async Task FreeAgent_WithAContractLengthHeWillNotSign_IsRefused()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 6, humans: 2);
        var market = await GetMarket(accounts[0].Tok, leagueId);
        var wanted = market.FreeAgents.First(f => f.SigningCostAtDemand <= DraftBudget);

        var resp = await SignFreeAgent(
            accounts[0].Tok, leagueId, wanted.ExternalId, wanted.DemandedWeeklyWage, wanted.MaxSeasons + 1);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var outcome = (await resp.Content.ReadFromJsonAsync<FreeAgentSigningDto>())!;
        Assert.That(outcome.Signed, Is.False);
    }

    // --- the bots keep trading between rounds ------------------------------------------------------

    /// <summary>"The AI-to-AI market keeps running between rounds so the world still moves" — the bots
    /// trade among themselves at each window, and never help themselves to a human's player.</summary>
    [Test]
    public async Task BotClubs_TradeAmongThemselves_ButNeverHelpThemselvesToAHumanSquad()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 8, humans: 2);

        var before = await GetMarket(accounts[0].Tok, leagueId);
        int botDeals = before.News.Count(n => !n.InvolvesYou);
        TestContext.WriteLine($"[league-market] bot deals in window 0: {botDeals}");

        Assert.Multiple(() =>
        {
            Assert.That(botDeals, Is.GreaterThan(0), "the bot clubs did their own business at the window");
            Assert.That(before.YourSquad, Has.Count.EqualTo(22), "nobody took a player off a human club");
            Assert.That(before.YourBudget, Is.EqualTo(DraftBudget), "and no money left it either");
            Assert.That(before.Clubs.Where(c => c.IsHuman).All(c => c.Players.Count == 22), Is.True);
        });
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>A player the market's own guards will let move: cheap enough for any budget, worth enough
    /// for the integrity band to police, and with cover in his role so a bot seller's squad rules allow the
    /// sale. Picking blindly makes a test fail for the wrong reason.</summary>
    private static LeagueMarketPlayerDto Sellable(IReadOnlyList<LeagueMarketPlayerDto> squad)
    {
        var depth = squad.GroupBy(p => p.Role).ToDictionary(g => g.Key, g => g.Count());
        var pick = squad
            .Where(p => p.MarketValue > 250_000 && depth[p.Role] > 1)
            .OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId)
            .FirstOrDefault();
        Assert.That(pick, Is.Not.Null,
            "the squad holds a covered, properly priced player — otherwise the guards, not the market, decide the test");
        return pick!;
    }

    /// <summary>The negotiation for this player STARTED BY THIS CLUB. Player alone is not enough: a bot may
    /// be bidding on the same man at the same time, which is the whole point of 12.1's living market.</summary>
    private static LeagueOfferDto Negotiation(
        IReadOnlyList<LeagueOfferDto> offers, int playerExternalId, int? buyerClubExternalId) =>
        offers.Single(o => o.PlayerExternalId == playerExternalId
                           && o.BuyerClubExternalId == buyerClubExternalId);

    private static LeagueMarketSquadDto FullBotClub(LeagueMarketDto market) =>
        market.Clubs.First(c => !c.IsHuman && c.Players.Count >= 20);

    private static LeagueMarketPlayerDto FirstBotPlayer(LeagueMarketDto market) =>
        Sellable(FullBotClub(market).Players);

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    private async Task<(string Tok, Guid Id)> RegisterAccount()
    {
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.Profile.UserId);
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<LeagueMarketDto> GetMarket(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/market", tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueMarketDto>())!;
    }

    private async Task<HttpResponseMessage> Offer(string tok, Guid leagueId, int playerExternalId, long fee)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/market/offers", tok,
            new MakeLeagueOfferRequest(playerExternalId, fee));
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Respond(
        string tok, Guid leagueId, Guid offerId, LeagueOfferAction action, long amount)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/market/offers/{offerId}", tok,
            new RespondLeagueOfferRequest(action, amount));
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> SetListing(
        string tok, Guid leagueId, int playerExternalId, bool listed, long askingPrice)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/market/listings", tok,
            new ListPlayerRequest(playerExternalId, listed, askingPrice));
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> SignFreeAgent(
        string tok, Guid leagueId, int playerExternalId, long weeklyWage, int seasons)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/market/free-agents", tok,
            new SignFreeAgentRequest(playerExternalId, weeklyWage, seasons));
        return await _client.SendAsync(req);
    }

    private async Task Advance(string creatorTok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", creatorTok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the creator forces the round");
    }

    private async Task<List<LeagueSummaryDto>> ListMine(string tok)
    {
        using var req = Authed(HttpMethod.Get, "/leagues", tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<List<LeagueSummaryDto>>())!;
    }

    private async Task<LeagueDetailDto> GetDetail(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}", tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    /// <summary>Creates a league of <paramref name="size"/> clubs with only <paramref name="humans"/>
    /// members, runs the draft, and returns an Active league where the unclaimed clubs are bots — the
    /// shape 12.1 is about. Index 0 = the creator.</summary>
    private async Task<(Guid LeagueId, List<Account> Accounts)> CreateActiveLeague(int size, int humans)
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        using var createReq = Authed(HttpMethod.Post, "/leagues", creatorTok,
            new CreateLeagueRequest("Amici FC", size, LeagueMode.AllReady));
        var createResp = await _client.SendAsync(createReq);
        Assert.That(createResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var created = (await createResp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;

        var raw = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < humans - 1; i++)
        {
            var acc = await RegisterAccount();
            using var join = Authed(HttpMethod.Post, "/leagues/join", acc.Tok,
                new JoinLeagueRequest(created.League.InviteCode));
            Assert.That((await _client.SendAsync(join)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            raw.Add(acc);
        }

        using (var start = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/start", creatorTok))
            Assert.That((await _client.SendAsync(start)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var taken = new HashSet<int>();
        for (int pick = 0; pick < humans; pick++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            var picker = raw.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            using var pickReq = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/pick", picker.Tok,
                new PickClubRequest(club));
            Assert.That((await _client.SendAsync(pickReq)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }

        var final = await GetDetail(creatorTok, created.League.Id);
        Assert.That(final.League.Status, Is.EqualTo(LeagueStatus.Active), "the season starts with bots in it");

        var accounts = raw.Select(a =>
        {
            var member = final.Members.First(m => m.UserId == a.Id);
            return new Account(a.Tok, a.Id, member.ClubExternalId);
        }).ToList();

        return (created.League.Id, accounts);
    }

    private sealed record Account(string Tok, Guid Id, int? ClubExternalId);
}
