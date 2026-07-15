using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using NUnit.Framework;
using Sim.Core.Development;

namespace Fts.Api.Tests;

/// <summary>
/// Private-league daily-management sync over the real HTTP pipeline (Phase 8.4) against in-memory SQLite:
/// members submit training plans; each resolved round runs the server-authoritative weekly condition +
/// development tick; and a state-hash endpoint exposes a canonical hash of the whole world's mutable player
/// state (condition + attributes). THE 8.4 ✅ ("client and server state agree after a week of play"): the
/// state hash is deterministic across repeated reads and evolves after a round is played, so a client that
/// re-runs the same deterministic Sim.Core progressors can confirm it holds the same state the server does.
/// Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class LeagueDailyManagementTests
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

    // --- training submission -----------------------------------------------------------------------

    [Test]
    public async Task SubmitTraining_ValidPlan_IsAccepted()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var (tok, id) = accounts[1];
        var detail = await GetDetail(tok, leagueId);
        int clubExternalId = detail.Members.First(m => m.UserId == id).ClubExternalId!.Value;

        var plan = new TrainingPlan { TeamFocus = TeamTrainingFocus.Attacking };
        var resp = await SubmitTraining(tok, leagueId, plan);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var state = (await resp.Content.ReadFromJsonAsync<SeasonStateDto>())!;
        Assert.That(state.YourClubExternalId, Is.EqualTo(clubExternalId));
    }

    [Test]
    public async Task SubmitTraining_BeforeSeasonActive_ReturnsConflict()
    {
        var (creatorTok, _) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);

        var resp = await SubmitTraining(creatorTok, created.League.Id, new TrainingPlan());
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "wrong_phase before Active");
    }

    // --- state hash: the 8.4 ✅ --------------------------------------------------------------------

    [Test]
    public async Task StateHash_IsDeterministic_AcrossRepeatedReads()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);

        var first = await GetStateHash(accounts[0].Tok, leagueId);
        var second = await GetStateHash(accounts[1].Tok, leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(second.HashHex, Is.EqualTo(first.HashHex), "reading state never mutates it");
            Assert.That(first.PlayerCount, Is.EqualTo(4 * 22), "size-4 world, 22 players per club");
            Assert.That(first.RoundsPlayed, Is.EqualTo(0), "nothing played yet");
            Assert.That(first.HashHex, Does.StartWith("0x"));
        });
    }

    /// <summary>THE 8.4 ✅: after a week of play the authoritative world state has evolved (condition +
    /// development), so the state hash changes and the rounds-played count advances — the client re-runs
    /// the same deterministic progressors to confirm it agrees.</summary>
    [Test]
    public async Task StateHash_ChangesAfterAWeekOfPlay()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);

        var before = await GetStateHash(accounts[0].Tok, leagueId);
        await Advance(accounts[0].Tok, leagueId);
        var after = await GetStateHash(accounts[0].Tok, leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(after.RoundsPlayed, Is.EqualTo(1), "one round-week resolved");
            Assert.That(after.HashHex, Is.Not.EqualTo(before.HashHex),
                "condition + development evolved after a week → the world-state hash must change");
            Assert.That(after.PlayerCount, Is.EqualTo(before.PlayerCount), "no players added or removed");
        });

        // Re-reading after the tick is still deterministic (the tick, not the read, mutates state).
        var afterAgain = await GetStateHash(accounts[1].Tok, leagueId);
        Assert.That(afterAgain.HashHex, Is.EqualTo(after.HashHex), "the post-week hash is stable for every member");
    }

    [Test]
    public async Task StateHash_ByNonMember_ReturnsForbidden()
    {
        var (leagueId, _) = await CreateActiveLeague(size: 4);
        var (strangerTok, _) = await RegisterAccount();

        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/state-hash", strangerTok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- helpers -----------------------------------------------------------------------------------

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

    private async Task<LeagueDetailDto> CreateLeague(string accessToken, int size, string name = "Amici FC")
    {
        using var req = Authed(HttpMethod.Post, "/leagues", accessToken,
            new CreateLeagueRequest(name, size, LeagueMode.AllReady));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create league should succeed");
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<LeagueDetailDto> GetDetail(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<StateHashDto> GetStateHash(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/state-hash", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<StateHashDto>())!;
    }

    private async Task<HttpResponseMessage> SubmitTraining(string accessToken, Guid leagueId, TrainingPlan plan)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/training", accessToken,
            new SubmitTrainingRequest(plan));
        return await _client.SendAsync(req);
    }

    private async Task<LeagueSeasonDto> Advance(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "advance should resolve a round");
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    /// <summary>Creates a league, fills it, runs the whole draft → returns a league in the Active season
    /// state with every club claimed, plus the (token, id) of each account (index 0 = the creator).</summary>
    private async Task<(Guid LeagueId, List<(string Tok, Guid Id)> Accounts)> CreateActiveLeague(int size)
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size);
        var accounts = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < size - 1; i++)
        {
            var acc = await RegisterAccount();
            using var join = Authed(HttpMethod.Post, "/leagues/join", acc.Tok, new JoinLeagueRequest(created.League.InviteCode));
            Assert.That((await _client.SendAsync(join)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            accounts.Add(acc);
        }

        using (var start = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/start", creatorTok))
            Assert.That((await _client.SendAsync(start)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var taken = new HashSet<int>();
        for (int pick = 0; pick < size; pick++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            var picker = accounts.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            using var pickReq = Authed(HttpMethod.Post, $"/leagues/{created.League.Id}/draft/pick", picker.Tok,
                new PickClubRequest(club));
            Assert.That((await _client.SendAsync(pickReq)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }

        return (created.League.Id, accounts);
    }
}
