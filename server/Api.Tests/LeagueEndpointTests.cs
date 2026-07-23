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

    /// <summary>Registers a fresh account and returns both its bearer token and its account id (so a draft
    /// test can map "whose turn it is" back to the right token without relying on join-order timing).</summary>
    private async Task<(string Token, Guid UserId)> RegisterAccount()
    {
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.Profile.UserId);
    }

    private async Task<HttpResponseMessage> StartDraft(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/draft/start", accessToken);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Pick(string accessToken, Guid leagueId, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/draft/pick", accessToken,
            new PickClubRequest(clubExternalId));
        return await _client.SendAsync(req);
    }

    private async Task<LeagueDetailDto> GetDetail(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
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

    // --- 8.2 draft ---------------------------------------------------------------------------------

    /// <summary>THE 8.2 ✅: a 4-account league drafts into equal-strength, legal squads with an equal
    /// budget and no duplicate players, then everyone snake-picks a distinct club in turn.</summary>
    [Test]
    public async Task Draft_GivesEqualStrengthLegalSquads_EqualBudget_NoDuplicates_ThenSnakePickAssignsClubs()
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var code = created.League.InviteCode;

        var accounts = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < 3; i++)
        {
            var acc = await RegisterAccount();
            Assert.That((await Join(acc.Token, code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            accounts.Add((acc.Token, acc.UserId));
        }

        // Start the draft (creator only): squads are equalised + budgets set, the pick order opens.
        var startResp = await StartDraft(creatorTok, created.League.Id);
        Assert.That(startResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the creator starts the draft");
        var afterStart = (await startResp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(afterStart.League.Status, Is.EqualTo(LeagueStatus.Drafting));
            Assert.That(afterStart.Draft.InProgress, Is.True);
            Assert.That(afterStart.Draft.TotalPicks, Is.EqualTo(4));
            Assert.That(afterStart.Draft.PicksMade, Is.EqualTo(0));
            // Legal squads: every club keeps the full 22-man template composition.
            Assert.That(afterStart.Clubs.All(c => c.Players.Count == 22), Is.True,
                "every club has a legal, full squad after equalisation");
            // "Pari budget a tutti": one budget shared by all clubs.
            var budgets = afterStart.Clubs.Select(c => c.TransferBudget).Distinct().ToList();
            Assert.That(budgets, Has.Count.EqualTo(1), "all clubs share one starting budget");
            Assert.That(budgets[0], Is.EqualTo(25_000_000L));
        });

        // "Rose di pari forza": the club-strength spread is small after equalisation. The world seed is
        // server-generated at random per league, so the exact spread varies run to run (the serpentine
        // equaliser minimises but can't zero it — integer-truncated 22-man averages + role-tier gaps leave
        // a few points). The bound only has to prove equalisation WORKED — an un-equalised world spans far
        // wider — so keep it generous enough to be seed-stable (observed 0–4 across runs; ≤2 was too tight
        // and flaked on CI). See SquadEqualizer.
        var strengths = afterStart.Clubs.Select(c => c.Strength).OrderBy(x => x).ToList();
        int spread = strengths[^1] - strengths[0];
        TestContext.WriteLine($"[draft-equal] strengths=[{string.Join(",", strengths)}] spread={spread}");
        Assert.That(spread, Is.LessThanOrEqualTo(6), "equalised squads are near-identical in strength");

        // No duplicate players across the whole world (4 clubs × 22 = 88, none lost/duplicated).
        var worldPlayerIds = afterStart.Clubs.SelectMany(c => c.Players.Select(p => p.ExternalId)).ToList();
        Assert.That(worldPlayerIds, Is.Unique);
        Assert.That(worldPlayerIds, Has.Count.EqualTo(88));

        // Snake pick: each member on the clock claims the first still-available club, in turn.
        var taken = new HashSet<int>();
        for (int pickNo = 0; pickNo < 4; pickNo++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            Assert.That(state.Draft.InProgress, Is.True, "the draft is still running mid-picks");
            Assert.That(state.Draft.CurrentPickUserId, Is.Not.Null, "someone is on the clock");

            var picker = accounts.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int clubExternalId = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;

            var pickResp = await Pick(picker.Tok, created.League.Id, clubExternalId);
            Assert.That(pickResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"pick #{pickNo} succeeds on turn");
            taken.Add(clubExternalId);
        }

        // Draft done → league Active, every member on a distinct club, squads still full + unique.
        var final = await GetDetail(creatorTok, created.League.Id);
        Assert.Multiple(() =>
        {
            Assert.That(final.League.Status, Is.EqualTo(LeagueStatus.Active));
            Assert.That(final.Draft.InProgress, Is.False);
            Assert.That(final.Draft.PicksMade, Is.EqualTo(4));
            Assert.That(final.Members.All(m => m.ClubExternalId != null), Is.True, "everyone got a club");
            Assert.That(final.Members.Select(m => m.ClubExternalId).ToList(), Is.Unique,
                "no two members share a club");
            Assert.That(final.Clubs.All(c => c.Players.Count == 22), Is.True);
            Assert.That(final.Clubs.SelectMany(c => c.Players.Select(p => p.ExternalId)).ToList(), Is.Unique);
        });
    }

    [Test]
    public async Task StartDraft_ByNonCreator_ReturnsForbidden()
    {
        var (creatorTok, _) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var joiner = await RegisterAccount();
        Assert.That((await Join(joiner.Token, created.League.InviteCode)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var resp = await StartDraft(joiner.Token, created.League.Id);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task StartDraft_WithOnlyOneMember_ReturnsBadRequest()
    {
        var (creatorTok, _) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);

        var resp = await StartDraft(creatorTok, created.League.Id);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "need at least two members");
    }

    [Test]
    public async Task StartDraft_Twice_ReturnsConflict()
    {
        var (creatorTok, _) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var joiner = await RegisterAccount();
        await Join(joiner.Token, created.League.InviteCode);

        Assert.That((await StartDraft(creatorTok, created.League.Id)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await StartDraft(creatorTok, created.League.Id)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "the draft has already started");
    }

    [Test]
    public async Task Pick_OutOfTurn_ReturnsConflict()
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var accounts = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < 2; i++)
        {
            var acc = await RegisterAccount();
            await Join(acc.Token, created.League.InviteCode);
            accounts.Add((acc.Token, acc.UserId));
        }
        await StartDraft(creatorTok, created.League.Id);

        var state = await GetDetail(creatorTok, created.League.Id);
        // Someone who is NOT on the clock tries to pick → 409 not_your_turn.
        var offTurn = accounts.First(a => a.Id != state.Draft.CurrentPickUserId!.Value);
        int anyClub = state.Clubs.First().ExternalId;
        var resp = await Pick(offTurn.Tok, created.League.Id, anyClub);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Pick_ClubAlreadyTaken_ReturnsConflict()
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var accounts = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < 2; i++)
        {
            var acc = await RegisterAccount();
            await Join(acc.Token, created.League.InviteCode);
            accounts.Add((acc.Token, acc.UserId));
        }
        await StartDraft(creatorTok, created.League.Id);

        // First picker takes a club.
        var s1 = await GetDetail(creatorTok, created.League.Id);
        var p1 = accounts.First(a => a.Id == s1.Draft.CurrentPickUserId!.Value);
        int takenClub = s1.Clubs.First().ExternalId;
        Assert.That((await Pick(p1.Tok, created.League.Id, takenClub)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The next picker tries the SAME club → 409 club_unavailable.
        var s2 = await GetDetail(creatorTok, created.League.Id);
        var p2 = accounts.First(a => a.Id == s2.Draft.CurrentPickUserId!.Value);
        Assert.That((await Pick(p2.Tok, created.League.Id, takenClub)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Pick_NonexistentClub_ReturnsConflict()
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var joiner = await RegisterAccount();
        await Join(joiner.Token, created.League.InviteCode);
        await StartDraft(creatorTok, created.League.Id);

        var state = await GetDetail(creatorTok, created.League.Id);
        var picker = state.Draft.CurrentPickUserId!.Value == creatorId ? creatorTok : joiner.Token;
        var resp = await Pick(picker, created.League.Id, clubExternalId: 999_999);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "no such club → club_unavailable");
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
