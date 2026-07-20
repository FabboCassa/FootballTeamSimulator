using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Dev;
using Fts.Application.Leagues;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Dev-only test-league seeding (dev tooling) over the real HTTP pipeline against in-memory SQLite. Proves
/// the one-call shortcut equals the manual create→join→draft flow (a ready Active league with drafted
/// clubs + working bot tokens), the minimal bot autopilot (bids), and that the endpoints are gated by the
/// <c>Dev:ExposeSeedEndpoints</c> flag (which, together with the never-in-Production guard, keeps these
/// unauthenticated endpoints out of a real deployment). Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class DevSeedTests
{
    private AuthTestFactory _factory = null!;
    private HttpClient _client = null!;

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

    [Test]
    public async Task SeedTestLeague_Size4Active_ReturnsFourDraftedMembers_WithWorkingTokens()
    {
        var seed = await Seed(new { size = 4, bots = 4, toStatus = "Active" });

        Assert.Multiple(() =>
        {
            Assert.That(seed.Members, Has.Count.EqualTo(4));
            Assert.That(seed.Status, Is.EqualTo("Active"));
            Assert.That(seed.Members.All(m => m.ClubExternalId.HasValue), Is.True, "every member drafted a club");
            Assert.That(seed.Members.Select(m => m.ClubExternalId).ToList(), Is.Unique, "distinct clubs");
            Assert.That(seed.Members.Count(m => m.IsCreator), Is.EqualTo(1));
            Assert.That(seed.Members.All(m => !string.IsNullOrEmpty(m.AccessToken)), Is.True, "bot tokens returned");
        });

        // Each returned bot token can actually read the league (the shortcut produced real, usable members).
        foreach (var m in seed.Members)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/leagues/{seed.LeagueId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m.AccessToken);
            var resp = await _client.SendAsync(req);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a seeded bot token is valid + a member");
        }
    }

    [Test]
    public async Task SeedTestLeague_Forming_JoinsButDoesNotDraft()
    {
        var seed = await Seed(new { size = 4, bots = 3, toStatus = "Forming" });
        Assert.Multiple(() =>
        {
            Assert.That(seed.Members, Has.Count.EqualTo(3));
            Assert.That(seed.Status, Is.EqualTo("Forming"));
            Assert.That(seed.Members.All(m => m.ClubExternalId == null), Is.True, "no clubs before the draft");
        });
    }

    [Test]
    public async Task BotBid_PlacesBids_OnOpenLots()
    {
        var seed = await Seed(new { size = 4, bots = 4, toStatus = "Active" });
        string creatorTok = seed.Members.First(m => m.IsCreator).AccessToken!;

        // The creator opens the auction window, then the dev autopilot has the bots bid.
        using (var open = new HttpRequestMessage(HttpMethod.Post, $"/leagues/{seed.LeagueId}/auctions/open"))
        {
            open.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creatorTok);
            Assert.That((await _client.SendAsync(open)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var botbid = await _client.PostAsJsonAsync(
            $"/internal/dev/leagues/{seed.LeagueId}/auctions/botbid", new { rounds = 2 });
        Assert.That(botbid.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bidResult = (await botbid.Content.ReadFromJsonAsync<DevBotBidResult>())!;
        Assert.That(bidResult.BidsPlaced, Is.GreaterThan(0), "bots placed at least one legal bid");

        // At least one lot now carries a high bid.
        using var view = new HttpRequestMessage(HttpMethod.Get, $"/leagues/{seed.LeagueId}/auctions");
        view.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creatorTok);
        var resp = await _client.SendAsync(view);
        var auctions = (await resp.Content.ReadFromJsonAsync<AuctionsDto>())!;
        Assert.That(auctions.Lots.Any(l => l.HighBid > 0), Is.True, "a bot bid landed on a lot");
    }

    [Test]
    public async Task Reset_LeavesTheBotsLeagues()
    {
        await Seed(new { size = 4, bots = 4, toStatus = "Drafting" });

        var reset = await _client.PostAsJsonAsync("/internal/dev/reset", new { bots = 8 });
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = (await reset.Content.ReadFromJsonAsync<DevResetResult>())!;
        Assert.That(result.LeaguesLeft, Is.GreaterThanOrEqualTo(1), "the bots left the seeded league");
    }

    [Test]
    public async Task SeedEndpoints_AreGated_WhenFlagIsOff()
    {
        // AuthTestFactory is sealed → layer the flag override on via WithWebHostBuilder instead of subclassing.
        using var disabled = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Dev:ExposeSeedEndpoints"] = "false" })));
        using var client = disabled.CreateClient();

        var resp = await client.PostAsJsonAsync("/internal/dev/test-league", new { size = 4 });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "with Dev:ExposeSeedEndpoints=false the dev endpoints are not mapped");
    }

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<DevSeedResult> Seed(object body)
    {
        var resp = await _client.PostAsJsonAsync("/internal/dev/test-league", body);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "seed should succeed");
        return (await resp.Content.ReadFromJsonAsync<DevSeedResult>())!;
    }
}
