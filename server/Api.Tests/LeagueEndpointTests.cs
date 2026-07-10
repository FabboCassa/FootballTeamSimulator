using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Private-league lifecycle over the real HTTP pipeline (Phase 8.1) against in-memory SQLite: the
/// endpoints are JWT-protected; creating a league server-generates a fresh world (unique players);
/// two accounts joining by invite code see the SAME squads with no duplicate players (the 8.1 ✅);
/// join guards (bad code / already-member / full) and leave/list behave. Reuses
/// <see cref="AuthTestFactory"/> and registers a fresh account per caller to get a bearer token.
/// </summary>
[TestFixture]
public class LeagueEndpointTests
{
    private AuthTestFactory _factory = null!;
    private HttpClient _client = null!;

    private const string ValidPassword = "Password1";

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

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    private async Task<string> RegisterAndGetAccessToken()
    {
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<LeagueDetailDto> CreateLeague(string accessToken, int size, string name = "Amici FC")
    {
        using var req = Authed(HttpMethod.Post, "/leagues", accessToken,
            new CreateLeagueRequest(name, size, LeagueMode.AllReady));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create league should succeed");
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<HttpResponseMessage> Join(string accessToken, string inviteCode)
    {
        using var req = Authed(HttpMethod.Post, "/leagues/join", accessToken,
            new JoinLeagueRequest(inviteCode));
        return await _client.SendAsync(req);
    }

    [Test]
    public async Task CreateLeague_WithoutToken_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/leagues",
            new CreateLeagueRequest("Amici FC", 8, LeagueMode.AllReady));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CreateLeague_GeneratesWorld_AndMakesCreatorTheFirstMember()
    {
        var access = await RegisterAndGetAccessToken();
        var detail = await CreateLeague(access, size: 8);

        Assert.Multiple(() =>
        {
            Assert.That(detail.League.InviteCode, Is.Not.Empty, "an invite code is issued");
            Assert.That(detail.League.Size, Is.EqualTo(8));
            Assert.That(detail.League.Status, Is.EqualTo(LeagueStatus.Forming));
            Assert.That(detail.League.IsCreator, Is.True);
            Assert.That(detail.Clubs, Has.Count.EqualTo(8), "the world has one club per league slot");
            Assert.That(detail.Members, Has.Count.EqualTo(1), "the creator is the only member");
            Assert.That(detail.Members[0].IsCreator, Is.True);
            Assert.That(detail.Members[0].ClubExternalId, Is.Null, "clubs are assigned at the 8.2 draft");
        });

        Assert.That(detail.Clubs.All(c => c.Players.Count > 0), Is.True,
            "every generated club has a squad");
    }

    [Test]
    public async Task CreateLeague_InvalidSize_ReturnsBadRequest()
    {
        var access = await RegisterAndGetAccessToken();
        using var req = Authed(HttpMethod.Post, "/leagues", access,
            new CreateLeagueRequest("Too small", 1, LeagueMode.AllReady));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task TwoAccounts_JoinSameLeague_SeeSameSquads_WithNoDuplicatePlayers()
    {
        var creator = await RegisterAndGetAccessToken();
        var created = await CreateLeague(creator, size: 8);

        var joiner = await RegisterAndGetAccessToken();
        var joinResp = await Join(joiner, created.League.InviteCode);
        Assert.That(joinResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var joinerView = (await joinResp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;

        // Both accounts are members of the same league now.
        Assert.That(joinerView.League.Id, Is.EqualTo(created.League.Id));
        Assert.That(joinerView.Members, Has.Count.EqualTo(2));

        // Both see the SAME generated squads (same clubs, same players, same overall).
        var creatorSquads = Flatten(created);
        var joinerSquads = Flatten(joinerView);
        Assert.That(joinerSquads, Is.EquivalentTo(creatorSquads),
            "every member queries the same world → identical squads");

        // No duplicate players in the world (uniqueness enforced per world).
        var allPlayerIds = created.Clubs.SelectMany(c => c.Players.Select(p => p.ExternalId)).ToList();
        Assert.That(allPlayerIds, Is.Unique, "player ids are unique per world");
        Assert.That(allPlayerIds, Has.Count.EqualTo(allPlayerIds.Distinct().Count()));
    }

    [Test]
    public async Task Join_UnknownCode_ReturnsNotFound()
    {
        var access = await RegisterAndGetAccessToken();
        var resp = await Join(access, "ZZZZZZ");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Join_WhenAlreadyMember_ReturnsConflict()
    {
        var access = await RegisterAndGetAccessToken();
        var created = await CreateLeague(access, size: 8);

        var resp = await Join(access, created.League.InviteCode);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Join_WhenFull_ReturnsConflict()
    {
        var creator = await RegisterAndGetAccessToken();
        var created = await CreateLeague(creator, size: 2); // creator fills 1 of 2

        var second = await RegisterAndGetAccessToken();
        Assert.That((await Join(second, created.League.InviteCode)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "the 2nd of 2 seats");

        var third = await RegisterAndGetAccessToken();
        Assert.That((await Join(third, created.League.InviteCode)).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict), "the league is full");
    }

    [Test]
    public async Task Leave_RemovesMembership()
    {
        var creator = await RegisterAndGetAccessToken();
        var created = await CreateLeague(creator, size: 8);

        var joiner = await RegisterAndGetAccessToken();
        await Join(joiner, created.League.InviteCode);

        using var leave = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/leave", joiner);
        var leaveResp = await _client.SendAsync(leave);
        Assert.That(leaveResp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // The joiner no longer sees the league; the creator sees a single member again.
        var mine = await ListMine(joiner);
        Assert.That(mine.Any(l => l.Id == created.League.Id), Is.False);

        using var get = Authed(HttpMethod.Get, $"/leagues/{created.League.Id}", creator);
        var detail = (await (await _client.SendAsync(get)).Content.ReadFromJsonAsync<LeagueDetailDto>())!;
        Assert.That(detail.Members, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LeaveAsLastMember_DisbandsLeague()
    {
        var creator = await RegisterAndGetAccessToken();
        var created = await CreateLeague(creator, size: 4);

        using var leave = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/leave", creator);
        Assert.That((await _client.SendAsync(leave)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // The league (and its world) is gone.
        using var get = Authed(HttpMethod.Get, $"/leagues/{created.League.Id}", creator);
        Assert.That((await _client.SendAsync(get)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var mine = await ListMine(creator);
        Assert.That(mine.Any(l => l.Id == created.League.Id), Is.False);
    }

    [Test]
    public async Task ListMine_ReturnsTheCallersLeagues()
    {
        var access = await RegisterAndGetAccessToken();
        var a = await CreateLeague(access, size: 8, name: "Lega A");
        var b = await CreateLeague(access, size: 8, name: "Lega B");

        var mine = await ListMine(access);
        var ids = mine.Select(l => l.Id).ToList();
        Assert.That(ids, Does.Contain(a.League.Id));
        Assert.That(ids, Does.Contain(b.League.Id));
    }

    [Test]
    public async Task Get_ByNonMember_ReturnsForbidden()
    {
        var creator = await RegisterAndGetAccessToken();
        var created = await CreateLeague(creator, size: 8);

        var stranger = await RegisterAndGetAccessToken();
        using var get = Authed(HttpMethod.Get, $"/leagues/{created.League.Id}", stranger);
        var resp = await _client.SendAsync(get);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private async Task<List<LeagueSummaryDto>> ListMine(string accessToken)
    {
        using var req = Authed(HttpMethod.Get, "/leagues", accessToken);
        var resp = await _client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<List<LeagueSummaryDto>>())!;
    }

    /// <summary>Flattens a detail into a comparable (clubExternalId, playerExternalId, overall) set.</summary>
    private static List<(int Club, int Player, int Overall)> Flatten(LeagueDetailDto d) =>
        d.Clubs.SelectMany(c => c.Players.Select(p => (c.ExternalId, p.ExternalId, p.Overall)))
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2)
            .ToList();
}
