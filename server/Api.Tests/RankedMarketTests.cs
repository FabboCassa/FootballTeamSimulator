using System.Net;
using System.Net.Http.Json;
using Fts.Application.Ranked;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The ranked direct market (Phase 9.2b): budgets are seeded when a season starts, and coaches trade players
/// via direct offers during an open market window. A placement cohort of GroupSize fills ONE group entirely
/// with humans, so the offers can be exercised there (no AI seats to work around). Reuses the compressed
/// calendar from <see cref="RankedSeasonTestBase"/> (a window stays open through the test).
/// </summary>
[TestFixture]
public class RankedMarketTests : RankedSeasonTestBase
{
    private async Task<RankedOffersDto> Offers(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/offers", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedOffersDto>())!;
    }

    private async Task<RankedSquadDto> Squad(string token, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/clubs/{clubExternalId}/squad", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedSquadDto>())!;
    }

    private async Task<HttpResponseMessage> MakeOffer(string token, int playerExternalId, long fee)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/offers", token,
            new MakeRankedOfferRequest(playerExternalId, fee));
        return await Client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Respond(string token, Guid offerId, bool accept)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/offers/{offerId}/{(accept ? "accept" : "reject")}", token);
        return await Client.SendAsync(req);
    }

    /// <summary>Enrol a full cohort and tick once so the placement season starts (budgets seeded + window open).
    /// All GroupSize coaches share the one placement group, so any two can trade.</summary>
    private async Task<List<string>> StartMarket()
    {
        var tokens = await EnrolCohort();
        await Tick();
        Assert.That((await Offers(tokens[0])).MarketOpen, Is.True, "the market window opens when the season starts");
        return tokens;
    }

    [Test]
    public async Task Budgets_AreSeeded_WhenTheSeasonStarts()
    {
        var tokens = await StartMarket();
        foreach (var t in tokens)
            Assert.That((await Offers(t)).YourBudget, Is.EqualTo(25_000_000), "every club gets the starting budget");
    }

    [Test]
    public async Task DirectOffer_MovesThePlayer_AndSettlesBothBudgets()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1];
        int buyerClub = (await GetMine(buyer)).ClubExternalId!.Value;
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;

        // Browse the seller's squad and pick a target.
        var sellerSquad = await Squad(buyer, sellerClub);
        Assert.That(sellerSquad.IsHuman, Is.True);
        Assert.That(sellerSquad.Players, Is.Not.Empty);
        int targetPlayer = sellerSquad.Players[0].ExternalId;

        long buyerBudgetBefore = (await Offers(buyer)).YourBudget;
        long sellerBudgetBefore = (await Offers(seller)).YourBudget;
        const long fee = 1_000_000;

        var madeResp = await MakeOffer(buyer, targetPlayer, fee);
        Assert.That(madeResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Offers(buyer)).Outgoing.Count(o => o.Status == RankedOfferStatus.Pending), Is.EqualTo(1));

        var incoming = (await Offers(seller)).Incoming.Single(o => o.Status == RankedOfferStatus.Pending);
        Assert.That(incoming.PlayerExternalId, Is.EqualTo(targetPlayer));

        var acceptResp = await Respond(seller, incoming.Id, accept: true);
        Assert.That(acceptResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        bool boughtNowInBuyerSquad = (await Squad(buyer, buyerClub)).Players.Any(p => p.ExternalId == targetPlayer);
        bool goneFromSellerSquad = (await Squad(buyer, sellerClub)).Players.All(p => p.ExternalId != targetPlayer);
        long buyerBudgetAfter = (await Offers(buyer)).YourBudget;
        long sellerBudgetAfter = (await Offers(seller)).YourBudget;

        Assert.Multiple(() =>
        {
            Assert.That(boughtNowInBuyerSquad, Is.True, "the player joined the buyer's squad");
            Assert.That(goneFromSellerSquad, Is.True, "the player left the seller's squad");
            Assert.That(buyerBudgetAfter, Is.EqualTo(buyerBudgetBefore - fee), "the buyer is charged the fee");
            Assert.That(sellerBudgetAfter, Is.EqualTo(sellerBudgetBefore + fee), "the seller receives the fee");
        });
    }

    [Test]
    public async Task Offer_OverBudget_IsRejected()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1];
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        int target = (await Squad(buyer, sellerClub)).Players[0].ExternalId;

        var resp = await MakeOffer(buyer, target, 999_000_000); // far over the 25M budget
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Offer_ForYourOwnPlayer_IsRejected()
    {
        var tokens = await StartMarket();
        string me = tokens[0];
        int myClub = (await GetMine(me)).ClubExternalId!.Value;
        int myPlayer = (await Squad(me, myClub)).Players[0].ExternalId;

        var resp = await MakeOffer(me, myPlayer, 1_000_000);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task OnlyTheOwner_CanAcceptAnOffer()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1], bystander = tokens[2];
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        int target = (await Squad(buyer, sellerClub)).Players[0].ExternalId;

        await MakeOffer(buyer, target, 1_000_000);
        var offerId = (await Offers(seller)).Incoming.Single(o => o.Status == RankedOfferStatus.Pending).Id;

        // The buyer cannot accept their own offer, and an unrelated coach cannot either.
        Assert.That((await Respond(buyer, offerId, true)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Respond(bystander, offerId, true)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Buyer_CanWithdrawAPendingOffer()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1];
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        int target = (await Squad(buyer, sellerClub)).Players[0].ExternalId;

        await MakeOffer(buyer, target, 1_000_000);
        var offerId = (await Offers(buyer)).Outgoing.Single(o => o.Status == RankedOfferStatus.Pending).Id;

        using var req = Authed(HttpMethod.Post, $"/ranked/offers/{offerId}/withdraw", buyer);
        Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Offers(seller)).Incoming.Any(o => o.Status == RankedOfferStatus.Pending), Is.False,
            "the withdrawn offer is no longer pending for the seller");
    }
}

/// <summary>The market-closed guard: with a zero-length window the season still runs but no market ever
/// opens, so a direct offer is refused.</summary>
[TestFixture]
public class RankedMarketClosedTests : RankedSeasonTestBase
{
    protected override int WindowSeconds => 0; // the window never opens

    [Test]
    public async Task Offer_WhenMarketClosed_IsRejected()
    {
        var tokens = await EnrolCohort();
        await Tick(); // the placement season starts (budgets seeded) but no window opens

        using var reqOffers = Authed(HttpMethod.Get, "/ranked/offers", tokens[0]);
        var offers = (await (await Client.SendAsync(reqOffers)).Content.ReadFromJsonAsync<RankedOffersDto>())!;
        Assert.That(offers.MarketOpen, Is.False, "a zero-length window is never open");

        // Browse still works (no window needed); making an offer does not.
        int sellerClub = (await GetMine(tokens[1])).ClubExternalId!.Value;
        using var reqSquad = Authed(HttpMethod.Get, $"/ranked/clubs/{sellerClub}/squad", tokens[0]);
        var squad = (await (await Client.SendAsync(reqSquad)).Content.ReadFromJsonAsync<RankedSquadDto>())!;
        int target = squad.Players[0].ExternalId;

        using var reqMake = Authed(HttpMethod.Post, "/ranked/offers", tokens[0],
            new MakeRankedOfferRequest(target, 1_000_000));
        Assert.That((await Client.SendAsync(reqMake)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "no market window is open, so the offer is refused");
    }
}
