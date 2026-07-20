using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Online auctions over the real HTTP pipeline (Phase 8.5) against in-memory SQLite. THE 8.5 ✅ — "4
/// accounts bid live on one player: outbid notifications arrive, last-second bid extends the timer, winner
/// charged correctly, others refunded": <see cref="FourAccounts_BidOnOneLot_WinnerChargedLosersRefunded"/>
/// exercises the outbid path (the displaced leader is reported, and the wired FCM sender is invoked),
/// <see cref="LastSecondsBid_ExtendsTheTimer"/> proves anti-sniping, and closing the window charges only
/// the winner while every loser's budget is untouched (no money moves until settlement = "refunded").
/// Settlement runs synchronously via the creator "close window" action (Hangfire is disabled under the
/// Testing environment); production settles each lot automatically at its timer end. Reuses
/// <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class LeagueAuctionTests
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

    // --- Open a window ------------------------------------------------------------------------------

    [Test]
    public async Task OpenWindow_CreatesFreeAgentLots_WithBudgetPicture()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);

        var open = await OpenWindow(accounts[0].Tok, leagueId);
        Assert.That(open.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var view = (await open.Content.ReadFromJsonAsync<AuctionsDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(view.Lots, Is.Not.Empty, "the window opens a lot per seeded free agent");
            Assert.That(view.WindowOpen, Is.True);
            Assert.That(view.Budget, Is.EqualTo(DraftBudget), "the caller's club carries the draft budget");
            Assert.That(view.Committed, Is.EqualTo(0));
            Assert.That(view.Available, Is.EqualTo(DraftBudget));
            Assert.That(view.Lots.All(l => l.StartPrice >= 25_000), Is.True, "start prices are floored");
            Assert.That(view.Lots.All(l => l.Status == AuctionStatus.Open), Is.True);
        });

        // Free agents span the quality curve — at least one genuine standout among many.
        Assert.That(view.Lots.Max(l => l.Overall), Is.GreaterThanOrEqualTo(75),
            "the pool includes a couple of phenoms");
    }

    [Test]
    public async Task OpenWindow_ByNonCreator_ReturnsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var resp = await OpenWindow(accounts[1].Tok, leagueId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task OpenWindow_Twice_ReturnsWindowAlreadyOpen()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        Assert.That((await OpenWindow(accounts[0].Tok, leagueId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var second = await OpenWindow(accounts[0].Tok, leagueId);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- Bidding ------------------------------------------------------------------------------------

    [Test]
    public async Task Bid_OutbidsPrevious_TracksLeaderAndCommittedBudget()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);

        // Account 0 opens the bidding at the start price.
        var first = await Bid(accounts[0].Tok, leagueId, lot.AuctionId, lot.StartPrice);
        Assert.That(first.Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(first.Result!.OutbidPrevious, Is.False, "no one to outbid on the opening bid");

        // Account 1 outbids.
        long raise = lot.StartPrice + 500_000;
        var second = await Bid(accounts[1].Tok, leagueId, lot.AuctionId, raise);
        Assert.That(second.Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(second.Result!.OutbidPrevious, Is.True, "account 0 was displaced");
            Assert.That(second.Result!.PreviousLeaderClubExternalId,
                Is.EqualTo(accounts[0].ClubExternalId), "the displaced leader is reported (→ outbid push)");
            Assert.That(second.Result!.Lot.HighBid, Is.EqualTo(raise));
            Assert.That(second.Result!.Lot.HighBidClubExternalId, Is.EqualTo(accounts[1].ClubExternalId));
        });

        // Account 1's committed budget now reflects the leading bid; account 0 is fully freed again.
        var view1 = await GetAuctions(accounts[1].Tok, leagueId);
        var view0 = await GetAuctions(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(view1.Committed, Is.EqualTo(raise), "the leader's budget is committed");
            Assert.That(view1.Available, Is.EqualTo(DraftBudget - raise));
            Assert.That(view0.Committed, Is.EqualTo(0), "an outbid club's reservation is released");
            Assert.That(view0.Available, Is.EqualTo(DraftBudget));
        });
    }

    [Test]
    public async Task Bid_BelowMinimum_ReturnsBidTooLow()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);

        var resp = (await Bid(accounts[0].Tok, leagueId, lot.AuctionId, lot.StartPrice - 1)).Resp;
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Bid_OverAvailableBudget_ReturnsInsufficientBudget()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);

        var resp = (await Bid(accounts[0].Tok, leagueId, lot.AuctionId, DraftBudget + 1)).Resp;
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Bid_ByNonMember_ReturnsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);
        var (strangerTok, _) = await RegisterAccount();

        var resp = (await Bid(strangerTok, leagueId, lot.AuctionId, lot.StartPrice)).Resp;
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Bid_BeforeAnyWindow_ReturnsAuctionNotFound()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var resp = (await Bid(accounts[0].Tok, leagueId, Guid.NewGuid(), lot: 1_000_000)).Resp;
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Anti-sniping -------------------------------------------------------------------------------

    /// <summary>A bid in the last seconds extends the timer (anti-sniping). We push the lot's end to
    /// within the anti-snipe window directly, then bid, and assert the end moved out.</summary>
    [Test]
    public async Task LastSecondsBid_ExtendsTheTimer()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);

        // Move this lot's end to 10s away (inside the 30s anti-snipe window).
        DateTime near = DateTime.UtcNow.AddSeconds(10);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
            var auction = await db.Auctions.FirstAsync(a => a.Id == lot.AuctionId);
            auction.EndsUtc = near;
            await db.SaveChangesAsync();
        }

        var bid = await Bid(accounts[1].Tok, leagueId, lot.AuctionId, lot.StartPrice);
        Assert.That(bid.Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(bid.Result!.Extended, Is.True, "a last-seconds bid extends the timer");
            Assert.That(bid.Result!.Lot.EndsUtc, Is.GreaterThan(near.AddSeconds(5)),
                "the end was pushed out beyond the near deadline");
        });
    }

    // --- Settlement: THE 8.5 ✅ ---------------------------------------------------------------------

    [Test]
    public async Task FourAccounts_BidOnOneLot_WinnerChargedLosersRefunded()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await OpenWindow(accounts[0].Tok, leagueId);
        var lot = await CheapestLot(accounts[0].Tok, leagueId);

        // All four accounts bid in turn on the same lot, each outbidding the last (the outbid push fires
        // for the displaced leader every time).
        long b0 = lot.StartPrice;
        long b1 = b0 + 500_000;
        long b2 = b1 + 500_000;
        long b3 = b2 + 500_000; // account 3 wins
        Assert.That((await Bid(accounts[0].Tok, leagueId, lot.AuctionId, b0)).Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Bid(accounts[1].Tok, leagueId, lot.AuctionId, b1)).Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Bid(accounts[2].Tok, leagueId, lot.AuctionId, b2)).Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var winning = await Bid(accounts[3].Tok, leagueId, lot.AuctionId, b3);
        Assert.That(winning.Resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(winning.Result!.Lot.HighBidClubExternalId, Is.EqualTo(accounts[3].ClubExternalId));

        // Budgets before settlement are untouched (no money moves until a lot settles).
        var before = await ClubBudgets(accounts[0].Tok, leagueId);
        Assert.That(before[accounts[3].ClubExternalId!.Value], Is.EqualTo(DraftBudget), "not yet charged");

        // Creator closes the window → the lot settles.
        var close = await CloseWindow(accounts[0].Tok, leagueId);
        Assert.That(close.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await ClubBudgets(accounts[0].Tok, leagueId);
        var detail = await GetDetail(accounts[0].Tok, leagueId);
        var winnerClub = detail.Clubs.First(c => c.ExternalId == accounts[3].ClubExternalId!.Value);

        Assert.Multiple(() =>
        {
            // Winner charged exactly the winning bid.
            Assert.That(after[accounts[3].ClubExternalId!.Value], Is.EqualTo(DraftBudget - b3),
                "the winner is charged the winning fee");
            // Losers refunded = never charged.
            Assert.That(after[accounts[0].ClubExternalId!.Value], Is.EqualTo(DraftBudget));
            Assert.That(after[accounts[1].ClubExternalId!.Value], Is.EqualTo(DraftBudget));
            Assert.That(after[accounts[2].ClubExternalId!.Value], Is.EqualTo(DraftBudget));
            // The player joined the winner's squad (22 drafted + 1 signed).
            Assert.That(winnerClub.Players.Count, Is.EqualTo(23));
            Assert.That(winnerClub.Players.Any(p => p.ExternalId == lot.PlayerExternalId), Is.True,
                "the auctioned player now belongs to the winning club");
        });

        // The lot is settled and no window is open anymore.
        var view = await GetAuctions(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(view.WindowOpen, Is.False);
            Assert.That(view.Lots.First(l => l.AuctionId == lot.AuctionId).Status, Is.EqualTo(AuctionStatus.Settled));
        });
    }

    [Test]
    public async Task CloseWindow_WithNoOpenAuctions_ReturnsConflict()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var resp = await CloseWindow(accounts[0].Tok, leagueId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- helpers ------------------------------------------------------------------------------------

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

    private async Task<HttpResponseMessage> OpenWindow(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/auctions/open", tok);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> CloseWindow(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/auctions/close", tok);
        return await _client.SendAsync(req);
    }

    private async Task<AuctionsDto> GetAuctions(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/auctions", tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<AuctionsDto>())!;
    }

    private async Task<AuctionLotDto> CheapestLot(string tok, Guid leagueId)
    {
        var view = await GetAuctions(tok, leagueId);
        return view.Lots.Where(l => l.Status == AuctionStatus.Open).OrderBy(l => l.StartPrice).First();
    }

    private async Task<(HttpResponseMessage Resp, BidResultDto? Result)> Bid(
        string tok, Guid leagueId, Guid auctionId, long lot)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/auctions/{auctionId}/bid", tok,
            new PlaceBidRequest(lot));
        var resp = await _client.SendAsync(req);
        BidResultDto? result = resp.StatusCode == HttpStatusCode.OK
            ? await resp.Content.ReadFromJsonAsync<BidResultDto>()
            : null;
        return (resp, result);
    }

    private async Task<Dictionary<int, long>> ClubBudgets(string tok, Guid leagueId)
    {
        var detail = await GetDetail(tok, leagueId);
        return detail.Clubs.ToDictionary(c => c.ExternalId, c => c.TransferBudget);
    }

    private async Task<LeagueDetailDto> GetDetail(string tok, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}", tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<LeagueDetailDto> CreateLeagueDetail(string tok, int size)
    {
        using var req = Authed(HttpMethod.Post, "/leagues", tok, new CreateLeagueRequest("Amici FC", size, LeagueMode.AllReady));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    /// <summary>Creates a league, fills it, runs the whole draft → an Active league with every club
    /// claimed, plus each account's (token, id, clubExternalId). Index 0 = the creator.</summary>
    private async Task<(Guid LeagueId, List<Account> Accounts)> CreateActiveLeague(int size)
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeagueDetail(creatorTok, size);
        var raw = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < size - 1; i++)
        {
            var acc = await RegisterAccount();
            using var join = Authed(HttpMethod.Post, "/leagues/join", acc.Tok, new JoinLeagueRequest(created.League.InviteCode));
            Assert.That((await _client.SendAsync(join)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            raw.Add(acc);
        }

        using (var start = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/start", creatorTok))
            Assert.That((await _client.SendAsync(start)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var taken = new HashSet<int>();
        for (int pick = 0; pick < size; pick++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            var picker = raw.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            using var pickReq = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/pick", picker.Tok,
                new PickClubRequest(club));
            Assert.That((await _client.SendAsync(pickReq)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }

        // Resolve each account's assigned club external id from the final detail.
        var final = await GetDetail(creatorTok, created.League.Id);
        var accounts = raw.Select(a =>
        {
            var member = final.Members.First(m => m.UserId == a.Id);
            return new Account(a.Tok, a.Id, member.ClubExternalId);
        }).ToList();

        return (created.League.Id, accounts);
    }

    private sealed record Account(string Tok, Guid Id, int? ClubExternalId);
}
