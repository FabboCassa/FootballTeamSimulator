using System.Net;
using System.Net.Http.Json;
using Fts.Application.Ranked;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Ranked free-agent auctions (Phase 9.2b): when a season's market window opens, a lot is created per free
/// agent; coaches place ascending bids and the season tick settles them at the window close (the winner gets
/// the player + is charged). Exercised over the compressed calendar with the dev force-settle endpoint (the
/// window is a full hour long in the test, so settlement is forced rather than waited on — the same pattern as
/// the 8.5 "close window" testable path). All GroupSize coaches share the one placement group, so they bid
/// against each other there.
/// </summary>
[TestFixture]
public class RankedAuctionTests : RankedSeasonTestBase
{
    private async Task<RankedAuctionsDto> Auctions(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/auctions", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedAuctionsDto>())!;
    }

    private async Task<HttpResponseMessage> Bid(string token, Guid auctionId, long amount)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/auctions/{auctionId}/bid", token,
            new PlaceRankedBidRequest(amount));
        return await Client.SendAsync(req);
    }

    private async Task<RankedSquadDto> Squad(string token, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/clubs/{clubExternalId}/squad", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedSquadDto>())!;
    }

    private async Task ForceSettle()
    {
        var resp = await Client.PostAsync("/internal/ranked/auctions/settle", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>Enrol a full cohort and tick once so the placement season starts and its opening market
    /// window creates the free-agent lots.</summary>
    private async Task<List<string>> StartAuctions()
    {
        var tokens = await EnrolCohort();
        await Tick();
        Assert.That((await Auctions(tokens[0])).Lots, Is.Not.Empty, "a window opening creates free-agent lots");
        return tokens;
    }

    /// <summary>The cheapest lot — its start price is affordable within the seeded budget.</summary>
    private static RankedAuctionLotDto Cheapest(RankedAuctionsDto a) =>
        a.Lots.OrderBy(l => l.StartPrice).First();

    [Test]
    public async Task Window_OpensFreeAgentLots_WithBudgetPicture()
    {
        var tokens = await StartAuctions();
        var a = await Auctions(tokens[0]);
        Assert.Multiple(() =>
        {
            Assert.That(a.Lots, Is.Not.Empty);
            Assert.That(a.WindowOpen, Is.True);
            Assert.That(a.Budget, Is.EqualTo(25_000_000), "the club is seeded its transfer budget");
            Assert.That(a.Available, Is.EqualTo(a.Budget), "nothing is committed before bidding");
            Assert.That(a.Lots.All(l => l.StartPrice >= 25_000), Is.True);
        });
    }

    [Test]
    public async Task Bidding_ThenSettling_GivesThePlayerToTheTopBidder_AndChargesOnlyThem()
    {
        var tokens = await StartAuctions();
        string a = tokens[0], b = tokens[1];

        var lot = Cheapest(await Auctions(a));
        Assert.That((await Bid(a, lot.Id, lot.StartPrice)).StatusCode, Is.EqualTo(HttpStatusCode.OK), "a opens the bidding");

        // b outbids at the lot's minimum next bid.
        var lotForB = (await Auctions(b)).Lots.Single(l => l.Id == lot.Id);
        long winningBid = lotForB.MinNextBid;
        Assert.That((await Bid(b, lot.Id, winningBid)).StatusCode, Is.EqualTo(HttpStatusCode.OK), "b outbids a");

        await ForceSettle();

        int bClub = (await GetMine(b)).ClubExternalId!.Value;
        bool wonByB = (await Squad(a, bClub)).Players.Any(p => p.ExternalId == lot.PlayerExternalId);
        long budgetB = (await Auctions(b)).Budget;
        long budgetA = (await Auctions(a)).Budget;
        bool lotStillOpen = (await Auctions(a)).Lots.Any(l => l.Id == lot.Id);

        Assert.Multiple(() =>
        {
            Assert.That(wonByB, Is.True, "the top bidder receives the player");
            Assert.That(budgetB, Is.EqualTo(25_000_000 - winningBid), "only the winner is charged");
            Assert.That(budgetA, Is.EqualTo(25_000_000), "the outbid coach is not charged");
            Assert.That(lotStillOpen, Is.False, "a settled lot is no longer open");
        });
    }

    [Test]
    public async Task Bid_BelowMinimum_IsRejected()
    {
        var tokens = await StartAuctions();
        var lot = Cheapest(await Auctions(tokens[0]));
        Assert.That((await Bid(tokens[0], lot.Id, 1)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Bid_OverAvailableBudget_IsRejected()
    {
        var tokens = await StartAuctions();
        var lot = Cheapest(await Auctions(tokens[0]));
        Assert.That((await Bid(tokens[0], lot.Id, 999_000_000)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Bid_OnUnknownLot_IsNotFound()
    {
        var tokens = await StartAuctions();
        Assert.That((await Bid(tokens[0], Guid.NewGuid(), 100_000)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
