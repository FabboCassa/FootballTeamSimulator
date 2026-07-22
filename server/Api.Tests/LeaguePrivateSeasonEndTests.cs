using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Private-league SEASON END (Phase 8.7) over the real HTTP pipeline against in-memory SQLite: a full
/// season flips the league to <see cref="LeagueStatus.Completed"/>, the summary presents the final table
/// plus awards (champion / best defence / wooden spoon / top scorer aggregated from the stored replays),
/// and the creator can start a fresh season (full reset → new draft) that plays again. The 8.7 ✅: "a
/// complete private season finishes cleanly". Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class LeaguePrivateSeasonEndTests
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

    // --- the 8.7 checks ----------------------------------------------------------------------------

    /// <summary>Playing every round flips the league to Completed and freezes the season as complete.</summary>
    [Test]
    public async Task Season_FlipsToCompleted_WhenTheLastRoundResolves()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var creator = accounts[0].Tok;

        // Before the last round the league is still Active.
        for (int round = 1; round <= 5; round++) await Advance(creator, leagueId);
        Assert.That((await GetDetail(creator, leagueId)).League.Status, Is.EqualTo(LeagueStatus.Active));

        await Advance(creator, leagueId); // round 6 — the last one

        var season = await GetSeason(creator, leagueId);
        var detail = await GetDetail(creator, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(season.Season.SeasonComplete, Is.True, "every fixture played");
            Assert.That(season.Season.RoundsPlayed, Is.EqualTo(6));
            Assert.That(detail.League.Status, Is.EqualTo(LeagueStatus.Completed), "league flips to Completed");
        });
    }

    /// <summary>THE 8.7 ✅ (summary): a finished season presents a full final table + awards — champion is
    /// the table leader, the top scorer is aggregated from the replays, best defence conceded fewest, and
    /// the wooden spoon is last.</summary>
    [Test]
    public async Task Summary_AfterAFullSeason_HasFinalTableAndAwards()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var creator = accounts[0].Tok;
        for (int round = 1; round <= 6; round++) await Advance(creator, leagueId);

        var summary = await GetSummary(creator, leagueId);
        var season = await GetSeason(creator, leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(summary.SeasonComplete, Is.True);
            Assert.That(summary.FinalStandings, Has.Count.EqualTo(4), "one row per club");
            Assert.That(summary.MatchesPlayed, Is.EqualTo(12), "6 rounds × 2 matches");

            Assert.That(summary.Champion, Is.Not.Null);
            Assert.That(summary.Champion!.ClubExternalId, Is.EqualTo(summary.FinalStandings[0].ClubExternalId),
                "champion = the table leader");
            Assert.That(summary.Champion.Value, Is.EqualTo(summary.FinalStandings[0].Points), "champion carries points");
            Assert.That(summary.FinalStandings[0].Points, Is.EqualTo(summary.FinalStandings.Max(s => s.Points)),
                "the leader has the most points");

            Assert.That(summary.WoodenSpoon, Is.Not.Null);
            Assert.That(summary.WoodenSpoon!.ClubExternalId, Is.EqualTo(summary.FinalStandings[^1].ClubExternalId),
                "wooden spoon = last");

            Assert.That(summary.BestDefence, Is.Not.Null);
            Assert.That(summary.BestDefence!.Value, Is.EqualTo(summary.FinalStandings.Min(s => s.GoalsAgainst)),
                "best defence conceded the fewest goals");

            // The summary's totals reconcile with the season fixtures.
            int seasonGoals = season.Fixtures.Where(f => f.Played).Sum(f => f.HomeGoals + f.AwayGoals);
            Assert.That(summary.TotalGoals, Is.EqualTo(seasonGoals));

            // A double round-robin over four clubs realistically scores goals; when it does, the top scorer
            // is present with a positive tally on one of the league's clubs.
            if (summary.TotalGoals > 0)
            {
                Assert.That(summary.TopScorer, Is.Not.Null, "goals were scored → a top scorer exists");
                Assert.That(summary.TopScorer!.Goals, Is.GreaterThan(0));
                Assert.That(summary.FinalStandings.Any(s => s.ClubExternalId == summary.TopScorer.ClubExternalId),
                    Is.True, "the top scorer plays for a league club");
            }
        });
    }

    /// <summary>The summary before completion is provisional (SeasonComplete false) but still reads.</summary>
    [Test]
    public async Task Summary_BeforeCompletion_IsProvisional()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await Advance(accounts[0].Tok, leagueId); // one round only

        var summary = await GetSummary(accounts[0].Tok, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(summary.SeasonComplete, Is.False);
            Assert.That(summary.MatchesPlayed, Is.EqualTo(2), "round 1 only");
            Assert.That(summary.FinalStandings, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task Summary_ByNonMember_ReturnsForbidden()
    {
        var (leagueId, _) = await CreateActiveLeague(size: 4);
        var (strangerTok, _) = await RegisterAccount();

        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/season/summary", strangerTok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- new season (full reset) -------------------------------------------------------------------

    /// <summary>THE 8.7 ✅ (rematch): once complete, the creator starts a new season — a full reset that
    /// re-opens the draft (clubs un-assigned, equal budgets), and after re-drafting the season plays again
    /// from a clean slate.</summary>
    [Test]
    public async Task NewSeason_ResetsToDraft_ThenPlaysAgain()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var creator = accounts[0].Tok;
        for (int round = 1; round <= 6; round++) await Advance(creator, leagueId);

        // Start a new season → full reset back into the draft.
        var reset = await StartNewSeason(creator, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(reset.League.Status, Is.EqualTo(LeagueStatus.Drafting), "back to the draft");
            Assert.That(reset.Members.All(m => m.ClubExternalId is null), Is.True, "every club un-assigned");
            Assert.That(reset.Draft.InProgress, Is.True, "the snake draft is open again");
            Assert.That(reset.Clubs.All(c => c.TransferBudget == 25_000_000), Is.True, "equal budgets re-seeded");
        });

        // Re-draft, then confirm a brand-new season is scheduled and can advance.
        await DraftAllClubs(creator, leagueId, accounts, size: 4);

        var season = await GetSeason(creator, leagueId);
        Assert.Multiple(() =>
        {
            Assert.That(season.Season.SeasonComplete, Is.False, "fresh season, not complete");
            Assert.That(season.Season.RoundsPlayed, Is.EqualTo(0));
            Assert.That(season.Fixtures, Has.Count.EqualTo(12));
            Assert.That(season.Fixtures.All(f => !f.Played), Is.True, "nothing played in the new season");
        });

        var afterAdvance = await Advance(creator, leagueId);
        Assert.That(afterAdvance.Season.RoundsPlayed, Is.EqualTo(1), "the new season plays");
    }

    [Test]
    public async Task NewSeason_ByNonCreator_ReturnsForbidden()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        for (int round = 1; round <= 6; round++) await Advance(accounts[0].Tok, leagueId);

        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/season/new", accounts[1].Tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task NewSeason_BeforeSeasonComplete_ReturnsConflict()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        await Advance(accounts[0].Tok, leagueId); // still Active, not complete

        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/season/new", accounts[0].Tok);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "wrong_phase before Completed");
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

    private async Task<SeasonSummaryDto> GetSummary(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}/season/summary", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<SeasonSummaryDto>())!;
    }

    private async Task<LeagueSeasonDto> Advance(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "advance should resolve a round");
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    private async Task<LeagueDetailDto> StartNewSeason(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/season/new", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "new season should reset the league");
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    /// <summary>Drives the snake draft to completion (each account picks the first unclaimed club on its
    /// turn), leaving the league Active with a fresh schedule.</summary>
    private async Task DraftAllClubs(
        string creatorTok, Guid leagueId, List<(string Tok, Guid Id)> accounts, int size)
    {
        var taken = new HashSet<int>();
        for (int pick = 0; pick < size; pick++)
        {
            var state = await GetDetail(creatorTok, leagueId);
            var picker = accounts.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            Assert.That((await Pick(picker.Tok, leagueId, club)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }
    }

    /// <summary>Creates a league, fills it, and runs the whole draft → returns a league in the Active season
    /// state, plus the (token, id) of each account (index 0 = the creator).</summary>
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
        await DraftAllClubs(creatorTok, created.League.Id, accounts, size);
        return (created.League.Id, accounts);
    }
}
