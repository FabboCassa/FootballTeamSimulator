using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using NUnit.Framework;
using Sim.Core.Match;

namespace Fts.Api.Tests;

/// <summary>
/// Private-league season over the real HTTP pipeline (Phase 8.3) against in-memory SQLite: completing
/// the draft generates the double round-robin; the creator (or an all-ready quorum) advances rounds,
/// which resolve every fixture through the shared Sim.Core engine with a best-XI AI fallback for any
/// club without a submitted lineup; and the stored full MatchReport is served identically to every
/// member (the 8.3 ✅: "matches fire on schedule, absent player's AI fields a sensible XI, replays
/// render identically for both users"). Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class LeagueSeasonEndpointTests
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

    // --- the 8.3 checks ----------------------------------------------------------------------------

    [Test]
    public async Task Season_StartsWhenDraftCompletes_WithAFullSchedule()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var season = await GetSeason(accounts[0].Tok, leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(season.Season.Started, Is.True, "fixtures are generated when the draft completes");
            Assert.That(season.Season.TotalRounds, Is.EqualTo(6), "n=4 → 2*(n-1) rounds");
            Assert.That(season.Fixtures, Has.Count.EqualTo(12), "6 rounds × 2 matches");
            Assert.That(season.Fixtures.All(f => !f.Played), Is.True, "nothing played yet");
            Assert.That(season.Season.RoundsPlayed, Is.EqualTo(0));
            Assert.That(season.Season.NextRound, Is.EqualTo(1));
            Assert.That(season.Season.SeasonComplete, Is.False);
            Assert.That(season.Standings, Has.Count.EqualTo(4), "one table row per club");
            Assert.That(season.Standings.All(s => s.Played == 0 && s.Points == 0), Is.True);
        });

        // Every club appears exactly twice per round pairing; the schedule is a valid round-robin
        // (each club plays 2*(n-1)=6 matches total).
        var appearances = season.Fixtures
            .SelectMany(f => new[] { f.HomeClubExternalId, f.AwayClubExternalId })
            .GroupBy(x => x).Select(g => g.Count()).ToList();
        Assert.That(appearances, Has.All.EqualTo(6), "each club is scheduled for 6 matches");
    }

    /// <summary>THE 8.3 ✅ (schedule): a 4-account league plays 3 rounds; matches fire in order and the
    /// standings stay internally consistent (points = 3·decisive + 1·1·2 for draws, appearances add up).</summary>
    [Test]
    public async Task Advance_PlaysRoundsInOrder_AndKeepsStandingsConsistent()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var creator = accounts[0].Tok;

        for (int round = 1; round <= 3; round++)
        {
            var afterAdvance = await Advance(creator, leagueId);
            Assert.That(afterAdvance.Season.RoundsPlayed, Is.EqualTo(round), $"round {round} resolved");
        }

        var season = await GetSeason(creator, leagueId);
        var played = season.Fixtures.Where(f => f.Played).ToList();

        int decisive = played.Count(f => f.HomeGoals != f.AwayGoals);
        int draws = played.Count - decisive;

        Assert.Multiple(() =>
        {
            Assert.That(played, Has.Count.EqualTo(6), "3 rounds × 2 matches played");
            Assert.That(season.Fixtures.Where(f => f.Round <= 3).All(f => f.Played), Is.True, "rounds 1-3 done");
            Assert.That(season.Fixtures.Where(f => f.Round > 3).All(f => !f.Played), Is.True, "rounds 4-6 pending");
            Assert.That(season.Standings.Sum(s => s.Played), Is.EqualTo(played.Count * 2), "two clubs per match");
            Assert.That(season.Standings.Sum(s => s.Points), Is.EqualTo(3 * decisive + 2 * draws),
                "points reconcile with results (3 for a win, 1 each for a draw)");
            Assert.That(season.Standings.Sum(s => s.Won), Is.EqualTo(season.Standings.Sum(s => s.Lost)),
                "every decisive match is one win and one loss");
        });
    }

    /// <summary>THE 8.3 ✅ (AI fallback): with NO lineups submitted, advancing still resolves every fixture
    /// — each club's AI fields a legal best XI, so the round completes with valid scores.</summary>
    [Test]
    public async Task Advance_WithNoSubmittedLineups_ResolvesViaBestElevenFallback()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var season = await Advance(accounts[0].Tok, leagueId);

        var round1 = season.Fixtures.Where(f => f.Round == 1).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(season.Season.RoundsPlayed, Is.EqualTo(1));
            Assert.That(round1, Has.Count.EqualTo(2));
            Assert.That(round1.All(f => f.Played), Is.True, "AI fielded XIs → both matches resolved");
            Assert.That(round1.All(f => f.HomeGoals >= 0 && f.AwayGoals >= 0), Is.True, "valid scores");
        });
    }

    /// <summary>THE 8.3 ✅ (replays): the stored full MatchReport is served byte-identically to both members
    /// and its embedded score matches the fixture columns.</summary>
    [Test]
    public async Task Replay_IsIdenticalForBothMembers_AndEmbedsTheScore()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await Advance(accounts[0].Tok, leagueId);

        var season = await GetSeason(accounts[0].Tok, leagueId);
        var fixture = season.Fixtures.First(f => f.Played);

        string replayA = await GetReplayRaw(accounts[0].Tok, leagueId, fixture.Id);
        string replayB = await GetReplayRaw(accounts[1].Tok, leagueId, fixture.Id);

        Assert.That(replayB, Is.EqualTo(replayA), "both members get the exact same stored report");

        using var doc = JsonDocument.Parse(replayA);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("HomeGoals").GetInt32(), Is.EqualTo(fixture.HomeGoals));
            Assert.That(root.GetProperty("AwayGoals").GetInt32(), Is.EqualTo(fixture.AwayGoals));
            Assert.That(root.TryGetProperty("Positions", out _), Is.True, "the replay carries the position stream");
            Assert.That(root.GetProperty("EngineVersion").GetInt32(), Is.GreaterThan(0));
        });
    }

    // --- ready flow --------------------------------------------------------------------------------

    [Test]
    public async Task Ready_WhenEveryoneReady_ResolvesTheRound_AndClearsFlags()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);

        LeagueSeasonDto? last = null;
        foreach (var acc in accounts)
            last = await SetReady(acc.Tok, leagueId, true);

        Assert.Multiple(() =>
        {
            Assert.That(last!.Season.RoundsPlayed, Is.EqualTo(1), "the last ready-up resolved the round");
            Assert.That(last.Season.MembersReady, Is.EqualTo(0), "ready flags reset for the next matchday");
            Assert.That(last.Season.YouAreReady, Is.False);
        });
    }

    // --- lineup submission -------------------------------------------------------------------------

    [Test]
    public async Task SubmitLineup_ValidLineup_IsAcceptedAndMarksSubmitted()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var (tok, id) = accounts[1];

        var detail = await GetDetail(tok, leagueId);
        int clubExternalId = MyClubExternalId(detail, id);
        var plan = BuildLineupPlan(detail, clubExternalId);

        var resp = await SubmitLineup(tok, leagueId, plan);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var state = (await resp.Content.ReadFromJsonAsync<SeasonStateDto>())!;
        Assert.Multiple(() =>
        {
            Assert.That(state.YouSubmittedLineup, Is.True);
            Assert.That(state.YourClubExternalId, Is.EqualTo(clubExternalId));
        });
    }

    [Test]
    public async Task SubmitLineup_InvalidLineup_ReturnsBadRequest()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var (tok, id) = accounts[1];
        var detail = await GetDetail(tok, leagueId);
        int clubExternalId = MyClubExternalId(detail, id);

        // A 5-man "lineup" cannot materialise into a legal XI.
        var shortPlan = BuildLineupPlan(detail, clubExternalId);
        shortPlan.Slots.RemoveRange(5, shortPlan.Slots.Count - 5);

        var resp = await SubmitLineup(tok, leagueId, shortPlan);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SubmitLineup_BeforeSeasonActive_ReturnsConflict()
    {
        // A freshly-created league is Forming, not Active — no season to submit into.
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size: 4);
        var detail = await GetDetail(creatorTok, created.League.Id);

        // No club yet either; the phase check fires first.
        var plan = new LineupPlan { ClubId = detail.Clubs[0].ExternalId };
        var resp = await SubmitLineup(creatorTok, created.League.Id, plan);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "wrong_phase before Active");
    }

    // --- guards ------------------------------------------------------------------------------------

    [Test]
    public async Task Advance_ByNonCreator_ReturnsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", accounts[1].Tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Replay_OfUnplayedFixture_ReturnsConflict()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var season = await GetSeason(accounts[0].Tok, leagueId);
        var unplayed = season.Fixtures.First(f => !f.Played);

        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/fixtures/{unplayed.Id}/replay", accounts[0].Tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "replay_not_ready");
    }

    [Test]
    public async Task Replay_ByNonMember_ReturnsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await Advance(accounts[0].Tok, leagueId);
        var season = await GetSeason(accounts[0].Tok, leagueId);
        var played = season.Fixtures.First(f => f.Played);

        var (strangerTok, _) = await RegisterAccount();
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/fixtures/{played.Id}/replay", strangerTok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Advance_WhenSeasonComplete_ReturnsConflict()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var creator = accounts[0].Tok;

        // Play all six rounds.
        for (int round = 1; round <= 6; round++)
            await Advance(creator, leagueId);

        var season = await GetSeason(creator, leagueId);
        Assert.That(season.Season.SeasonComplete, Is.True, "every fixture played");

        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", creator);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "nothing_to_resolve");
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

    private async Task<HttpResponseMessage> Join(string accessToken, string inviteCode)
    {
        using var req = Authed(HttpMethod.Post, "/leagues/join", accessToken, new JoinLeagueRequest(inviteCode));
        return await _client.SendAsync(req);
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

    private async Task<LeagueSeasonDto> GetSeason(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/season", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    private async Task<LeagueSeasonDto> Advance(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "advance should resolve a round");
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    private async Task<LeagueSeasonDto> SetReady(string accessToken, Guid leagueId, bool ready)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/ready", accessToken, new SetReadyRequest(ready));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    private async Task<HttpResponseMessage> SubmitLineup(string accessToken, Guid leagueId, LineupPlan plan)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/lineup", accessToken,
            new SubmitLineupRequest(plan, null, null));
        return await _client.SendAsync(req);
    }

    private async Task<string> GetReplayRaw(string accessToken, Guid leagueId, Guid fixtureId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/fixtures/{fixtureId}/replay", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "replay should be available");
        return await resp.Content.ReadAsStringAsync();
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
            Assert.That((await Join(acc.Tok, created.League.InviteCode)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            accounts.Add(acc);
        }

        Assert.That((await StartDraft(creatorTok, created.League.Id)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var taken = new HashSet<int>();
        for (int pick = 0; pick < size; pick++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            var picker = accounts.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            Assert.That((await Pick(picker.Tok, created.League.Id, club)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }

        return (created.League.Id, accounts);
    }

    private static int MyClubExternalId(LeagueDetailDto detail, Guid userId) =>
        detail.Members.First(m => m.UserId == userId).ClubExternalId!.Value;

    /// <summary>A legal 4-3-3 lineup plan from the club's first 11 squad players (players may be deployed
    /// out of their natural role; the plan only needs 11 distinct players and exactly one GK slot).</summary>
    private static LineupPlan BuildLineupPlan(LeagueDetailDto detail, int clubExternalId)
    {
        var club = detail.Clubs.First(c => c.ExternalId == clubExternalId);
        var players = club.Players.Take(11).ToList();
        var roles = LineupSelector.DefaultFormation;

        var plan = new LineupPlan { ClubId = clubExternalId };
        for (int i = 0; i < 11; i++)
            plan.Slots.Add(new LineupPlanSlot { Role = roles[i], PlayerId = players[i].ExternalId });
        return plan;
    }
}
