using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Ranked;
using Fts.Infrastructure.Ranked;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Pure clock math for the ranked real-time calendar (Phase 9.2) — no DB, no wall-clock waits. Proves the
/// matchday spacing and that BOTH market windows (season start + midpoint) compute at the right instants and
/// that <see cref="RankedCalendar.CurrentWindow"/> flips 0 → shut → 1 as the clock advances.
/// </summary>
[TestFixture]
public class RankedCalendarTests
{
    private static readonly DateTime Start = new(2026, 7, 23, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Kickoffs_AreSpacedByTheInterval()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RankedCalendar.KickoffOf(Start, 1, 3600), Is.EqualTo(Start), "matchday 1 = season start");
            Assert.That(RankedCalendar.KickoffOf(Start, 2, 3600), Is.EqualTo(Start.AddSeconds(3600)));
            Assert.That(RankedCalendar.KickoffOf(Start, 5, 3600), Is.EqualTo(Start.AddSeconds(4 * 3600)));
        });
    }

    [Test]
    public void TwoWindows_OpenAtSeasonStartAndMidpoint()
    {
        const int totalRounds = 14, interval = 3600, dur = 1800;
        var windows = RankedCalendar.Windows(Start, totalRounds, interval, dur);

        int mid = RankedCalendar.MidpointRound(totalRounds); // 8 for a 14-matchday season
        var midKick = RankedCalendar.KickoffOf(Start, mid, interval);

        Assert.Multiple(() =>
        {
            Assert.That(windows, Has.Count.EqualTo(2));
            Assert.That(windows[0].Index, Is.EqualTo(0));
            Assert.That(windows[0].OpensUtc, Is.EqualTo(Start));
            Assert.That(windows[0].ClosesUtc, Is.EqualTo(Start.AddSeconds(dur)));
            Assert.That(windows[1].Index, Is.EqualTo(1));
            Assert.That(windows[1].OpensUtc, Is.EqualTo(midKick));
            Assert.That(windows[1].ClosesUtc, Is.EqualTo(midKick.AddSeconds(dur)));
        });
    }

    [Test]
    public void CurrentWindow_FlipsFromZeroToShutToOne()
    {
        const int totalRounds = 14, interval = 3600, dur = 1800;
        var midKick = RankedCalendar.KickoffOf(Start, RankedCalendar.MidpointRound(totalRounds), interval);

        Assert.Multiple(() =>
        {
            Assert.That(RankedCalendar.CurrentWindow(Start, totalRounds, interval, dur, Start.AddSeconds(60))?.Index,
                Is.EqualTo(0), "the opening window is open just after kickoff of the season");
            Assert.That(RankedCalendar.CurrentWindow(Start, totalRounds, interval, dur, Start.AddSeconds(dur + 60)),
                Is.Null, "between the two windows the market is shut");
            Assert.That(RankedCalendar.CurrentWindow(Start, totalRounds, interval, dur, midKick.AddSeconds(60))?.Index,
                Is.EqualTo(1), "the midpoint window opens around halfway");
        });
    }
}

/// <summary>
/// Integration base for the ranked real-time season (Phase 9.2): a shrunk pyramid (4-club groups) with a
/// COMPRESSED calendar (0s between matchdays ⇒ a whole season resolves in a few ticks; a 1h market window
/// so it is observably open during the test). Each test gets its OWN in-memory SQLite ladder DB (per-test
/// SetUp) so cohorts do not interfere across tests.
/// </summary>
public abstract class RankedSeasonTestBase
{
    protected const string ValidPassword = "Password1";
    protected const int GroupSize = 4;

    protected AuthTestFactory Factory = null!;
    protected HttpClient Client = null!;

    [SetUp]
    public void SetUp()
    {
        Factory = new AuthTestFactory();
        var shrunk = Factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ranked:GroupSize"] = GroupSize.ToString(),
                ["Ranked:PlacementGroupSize"] = GroupSize.ToString(),
                ["Ranked:Tier1Groups"] = "1",
                ["Ranked:Tier2Groups"] = "1",
                ["Ranked:Tier3Groups"] = "1",
                ["Ranked:PlacementTopPositionsToUpperTier"] = "2",
                ["Ranked:MatchdayIntervalSeconds"] = "0",        // every matchday is due at once → fast season
                ["Ranked:MarketWindowDurationSeconds"] = "3600",  // a window stays observably open during the test
            })));
        Client = shrunk.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    protected async Task<string> RegisterAccount()
    {
        var resp = await Client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    protected HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    protected async Task<RankedStateDto> Enrol(string token)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/enrol", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
    }

    protected async Task<RankedStateDto> GetMine(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/me", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
    }

    protected async Task<(HttpStatusCode Code, RankedSeasonDto? Season)> GetSeason(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/season", token);
        var resp = await Client.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.OK) return (resp.StatusCode, null);
        return (resp.StatusCode, await resp.Content.ReadFromJsonAsync<RankedSeasonDto>());
    }

    /// <summary>Advance the ranked calendar once via the dev-gated internal endpoint.</summary>
    protected async Task<RankedTickSummary> Tick()
    {
        var resp = await Client.PostAsync("/internal/ranked/tick", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the tick endpoint is dev-mapped under Testing");
        return (await resp.Content.ReadFromJsonAsync<RankedTickSummary>())!;
    }

    /// <summary>Enrol a full placement cohort and return their tokens.</summary>
    protected async Task<List<string>> EnrolCohort()
    {
        var tokens = new List<string>(GroupSize);
        for (int i = 0; i < GroupSize; i++)
        {
            var token = await RegisterAccount();
            tokens.Add(token);
            var st = await Enrol(token);
            Assert.That(st.Status, Is.EqualTo(RankedCoachStatus.Placement));
        }
        return tokens;
    }
}

/// <summary>
/// THE 9.2 ✅: a whole ranked season runs unattended on the real-time calendar. A placement cohort's season
/// resolves matchday by matchday, sorts its coaches into divisions, and those division seasons then run to a
/// reconciled final table — all driven purely by repeated calendar ticks, with a market window firing.
/// </summary>
[TestFixture]
public class RankedSeasonTests : RankedSeasonTestBase
{
    [Test]
    public async Task PlacementSeason_RunsUnattended_ResolvesToDivisions_WithAMarketWindowFiring()
    {
        var tokens = await EnrolCohort();

        int matchdays = 0, placements = 0, windows = 0;
        for (int i = 0; i < 40; i++)
        {
            var s = await Tick();
            matchdays += s.MatchdaysResolved;
            placements += s.PlacementsResolved;
            windows += s.MarketWindowsOpened;
            if ((await GetMine(tokens[0])).Status == RankedCoachStatus.Placed) break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(matchdays, Is.GreaterThan(0), "matchdays fired on the calendar");
            Assert.That(placements, Is.EqualTo(1), "the placement season auto-resolved exactly once");
            Assert.That(windows, Is.GreaterThan(0), "at least one market window opened + was announced");
        });

        foreach (var t in tokens)
        {
            var st = await GetMine(t);
            Assert.That(st.Status, Is.EqualTo(RankedCoachStatus.Placed), "every coach is sorted into a division");
            Assert.That(st.Tier, Is.AnyOf(2, 3), "placement sorts into tier 2 or 3, never tier 1");
        }
    }

    [Test]
    public async Task DivisionSeason_StartsAfterPlacement_AndRunsToAReconciledTable()
    {
        var tokens = await EnrolCohort();

        // Run the whole cycle: placement → division → a placed coach's division season complete.
        RankedSeasonDto? finished = null;
        for (int i = 0; i < 80; i++)
        {
            await Tick();
            var (code, season) = await GetSeason(tokens[0]);
            if (code == HttpStatusCode.OK && season?.State is { SeasonComplete: true, Kind: RankedGroupKind.Division })
            {
                finished = season;
                break;
            }
        }

        Assert.That(finished, Is.Not.Null, "the placed coach's division season ran to completion");
        var standings = finished!.Standings;
        Assert.Multiple(() =>
        {
            Assert.That(standings, Has.Count.EqualTo(GroupSize), "the division always has its fixed number of clubs");
            Assert.That(standings.Sum(x => x.Won), Is.EqualTo(standings.Sum(x => x.Lost)), "wins reconcile with losses");
            Assert.That(standings.Sum(x => x.GoalDifference), Is.EqualTo(0), "goal differences net to zero");
            Assert.That(standings.All(x => x.Played == 2 * (GroupSize - 1)), Is.True, "every club played a double round-robin");
        });
    }

    [Test]
    public async Task Replay_IsServedForAPlayedFixture_AndGuardsUnplayedAndUnknown()
    {
        var tokens = await EnrolCohort();

        await Tick(); // starts the placement season + resolves matchday 1
        var (_, season) = await GetSeason(tokens[0]);
        Assert.That(season, Is.Not.Null);

        var played = season!.Fixtures.FirstOrDefault(f => f.Played);
        var unplayed = season.Fixtures.FirstOrDefault(f => !f.Played);
        Assert.That(played, Is.Not.Null, "matchday 1 resolved on the first tick");
        Assert.That(unplayed, Is.Not.Null, "later matchdays are still pending");

        using (var req = Authed(HttpMethod.Get, $"/ranked/season/fixtures/{played!.Id}/replay", tokens[0]))
        {
            var resp = await Client.SendAsync(req);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a played fixture serves its replay");
            Assert.That(await resp.Content.ReadAsStringAsync(), Does.Contain("Events").Or.Contain("events"));
        }

        using (var req = Authed(HttpMethod.Get, $"/ranked/season/fixtures/{unplayed!.Id}/replay", tokens[0]))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
                "an unplayed fixture has no replay yet");

        using (var req = Authed(HttpMethod.Get, $"/ranked/season/fixtures/{Guid.NewGuid()}/replay", tokens[0]))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                "an unknown fixture is not found");
    }

    [Test]
    public async Task SubmitLineup_GuardsNotEnrolledAndMissingLineup()
    {
        // Not enrolled → 404 not_enrolled.
        var stranger = await RegisterAccount();
        using (var req = Authed(HttpMethod.Post, "/ranked/lineup", stranger, new { tactic = (object?)null }))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // Enrolled but no lineup in the body → 400 validation.
        await Enrol(stranger);
        using (var req = Authed(HttpMethod.Post, "/ranked/lineup", stranger, new { tactic = (object?)null }))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetSeason_BeforeAnySeasonStarts_ReportsNotInSeason()
    {
        var token = await RegisterAccount();
        await Enrol(token); // enrolled, but the cohort is not full → no season yet

        var (code, season) = await GetSeason(token);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(season!.InSeason, Is.False, "a coach whose group has not kicked off is not in a season");
    }

    [Test]
    public async Task GetSeason_WithoutToken_IsUnauthorized()
    {
        var resp = await Client.GetAsync("/ranked/season");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Condition_EvolvesAcrossMatchdays_StateHashChanges_AndIsDeterministic()
    {
        var tokens = await EnrolCohort();

        await Tick(); // start the placement season + resolve matchday 1 (+ its weekly condition/dev tick)
        var (_, s1) = await GetSeason(tokens[0]);
        string hashAfterRound1 = s1!.State!.StateHashHex;
        Assert.That(hashAfterRound1, Is.Not.Empty, "the world-state hash is reported");

        // Reading the season again does not mutate anything → the hash is stable.
        var (_, s1Again) = await GetSeason(tokens[0]);
        Assert.That(s1Again!.State!.StateHashHex, Is.EqualTo(hashAfterRound1), "the state hash is deterministic across reads");

        await Tick(); // resolve matchday 2 → another week of condition drain/recovery + development
        var (_, s2) = await GetSeason(tokens[0]);
        Assert.That(s2!.State!.StateHashHex, Is.Not.EqualTo(hashAfterRound1),
            "a played matchday evolves the whole world's condition + development, so the state hash moves");
    }
}
