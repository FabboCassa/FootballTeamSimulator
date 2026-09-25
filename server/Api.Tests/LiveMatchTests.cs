using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fts.Application.Auth;
using Fts.Application.Dev;
using Fts.Application.Leagues;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Sim.Core.Match;
using Sim.Core.Match.Broadcast;

namespace Fts.Api.Tests;

/// <summary>
/// Live match control over the real HTTP pipeline (Phase 8.6) against in-memory SQLite. The chosen model
/// is "deterministic re-sim + broadcast": both members connect to a fixture, then a pause-point change
/// (a sub / instruction change) is processed server-side by re-simulating the deterministic engine from
/// the fixture seed and returning the new state. The 8.6 ✅: "a sub made by user A is reflected in B's
/// view; disconnect mid-match falls back to plan/AI gracefully." Because the engine is a pure function of
/// (plan, seed), the minutes before a change stay byte-identical — proven here without a live clock, and
/// the opponent's GET returns the same updated report. Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class LiveMatchTests
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

    // --- kickoff -----------------------------------------------------------------------------------

    [Test]
    public async Task OpenAndJoin_ByBothMembers_GoesLive_WithAnInitialReport()
    {
        var fx = await SetUpLiveFixture();

        var opened = await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.Multiple(() =>
        {
            Assert.That(opened.Status, Is.EqualTo(LiveMatchStatus.Pending), "one side present ⇒ not yet live");
            Assert.That(opened.YourSide, Is.EqualTo(LiveSide.Home));
            Assert.That(opened.HomePresent, Is.True);
            Assert.That(opened.AwayPresent, Is.False);
            Assert.That(opened.ReportJson, Is.Null, "no report until kickoff");
        });

        var live = await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.Multiple(() =>
        {
            Assert.That(live.Status, Is.EqualTo(LiveMatchStatus.Live), "both present ⇒ kicks off");
            Assert.That(live.YourSide, Is.EqualTo(LiveSide.Away));
            Assert.That(live.HomePresent, Is.True);
            Assert.That(live.AwayPresent, Is.True);
            Assert.That(live.KickoffUtc, Is.Not.Null);
            Assert.That(live.ReportJson, Is.Not.Null.And.Not.Empty, "a baseline report exists at kickoff");
        });
    }

    /// <summary>THE 8.6 ✅: a sub by the home user is processed server-side (re-sim), reflected in the away
    /// user's view (same report + the change in the timeline), and the minutes before the change stay
    /// byte-identical (the deterministic prefix guarantee, so both clients render in sync).</summary>
    [Test]
    public async Task Change_ByHome_IsReflectedToAway_AndPreservesThePrefix()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        var baseline = await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        var baselineEvents = EventsOf(baseline.ReportJson!);

        // The home user makes a substitution at minute 45 (a different, still-legal XI).
        const int changeMinute = 45;
        LineupPlan subbedXi = BuildLineupPlan(fx.Detail, fx.HomeClubExternalId, skip: 1);
        var afterChange = await Change(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(changeMinute, subbedXi, null));

        // The away user's independent read sees the same updated report + the applied change.
        var awayView = await GetLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);

        Assert.Multiple(() =>
        {
            Assert.That(afterChange.Changes, Has.Count.EqualTo(1));
            Assert.That(afterChange.Changes[0].FromMinute, Is.EqualTo(changeMinute));
            Assert.That(afterChange.Changes[0].Side, Is.EqualTo(LiveSide.Home));
            Assert.That(awayView.ReportJson, Is.EqualTo(afterChange.ReportJson),
                "the opponent's view is the same authoritative report (reflected)");
            Assert.That(awayView.Changes, Has.Count.EqualTo(1), "the sub shows in B's timeline");
        });

        // The engine re-rolls only from the change minute — the earlier timeline is byte-identical.
        var newEvents = EventsOf(afterChange.ReportJson!);
        var basePrefix = baselineEvents.Where(e => e.Minute < changeMinute).ToList();
        var newPrefix = newEvents.Where(e => e.Minute < changeMinute).ToList();
        Assert.That(newPrefix, Is.EqualTo(basePrefix), "minutes before the change are unchanged (determinism)");
    }

    // --- guards ------------------------------------------------------------------------------------

    [Test]
    public async Task Change_ByAMemberNotInTheFixture_ReturnsForbidden()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);

        // A league member who is not one of the two side owners cannot change the match.
        var plan = BuildLineupPlan(fx.Detail, fx.HomeClubExternalId, skip: 1);
        var resp = await ChangeRaw(fx.OtherAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(30, plan, null));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "not_your_side");
    }

    [Test]
    public async Task Change_BeforeKickoff_ReturnsConflict()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId); // Pending — away has not joined

        var plan = BuildLineupPlan(fx.Detail, fx.HomeClubExternalId, skip: 1);
        var resp = await ChangeRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(20, plan, null));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "live_match_not_live");
    }

    [Test]
    public async Task Change_MovingBackwards_OrEmpty_OrOutOfRange_ReturnsBadRequest()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);

        var plan = BuildLineupPlan(fx.Detail, fx.HomeClubExternalId, skip: 1);

        // Apply a change at 60, then a backwards change at 30 must fail.
        await Change(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId, new SubmitLiveChangeRequest(60, plan, null));
        var backwards = await ChangeRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(30, plan, null));
        Assert.That(backwards.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "cannot move behind an applied change");

        // A change carrying neither a lineup nor a tactic is invalid.
        var empty = await ChangeRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(70, null, null));
        Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "no lineup and no tactic");

        // A minute out of the 1..90 range is invalid.
        var outOfRange = await ChangeRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId,
            new SubmitLiveChangeRequest(0, plan, null));
        Assert.That(outOfRange.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "minute out of range");
    }

    [Test]
    public async Task Open_ByAMemberNotInTheFixture_ReturnsConflict()
    {
        var fx = await SetUpLiveFixture();
        var resp = await OpenRaw(fx.OtherAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "live_match_not_joinable (not playing)");
    }

    [Test]
    public async Task Open_OnAnAlreadyPlayedFixture_ReturnsConflict()
    {
        var fx = await SetUpLiveFixture();
        // Resolve round 1 instantly (Advance is creator-only), then the fixture can no longer be opened live.
        await Advance(fx.CreatorTok, fx.LeagueId);
        var resp = await OpenRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "already played / not current round");
    }

    // --- engine version (spec watchable-match-engine R16) -------------------------------------------

    /// <summary>Every client derives the shown moment from the director timeline of the report, so a build
    /// on another engine would show another moment: it is refused, and told why.</summary>
    [Test]
    public async Task Open_WithADifferentEngineVersion_IsRefusedWithAClearMessage()
    {
        var fx = await SetUpLiveFixture();

        var resp = await OpenRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId, MatchEngine.Version - 1);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("engine_version_mismatch"));
        string message = doc.RootElement.GetProperty("message").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain($"v{MatchEngine.Version - 1}"), "names the client's engine");
            Assert.That(message, Does.Contain($"v{MatchEngine.Version}"), "names the match's engine");
            Assert.That(message, Does.Contain("Update"), "says what to do");
        });

        // Refused means nothing happened: the home side was not marked present.
        await OpenLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        var state = await GetLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(state.HomePresent, Is.False, "a refused client never joined the match");
    }

    [Test]
    public async Task OpenAndJoin_WithoutAnEngineVersion_AreRefused()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);

        var open = await OpenRaw(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId, engineVersion: null);
        var join = await JoinRaw(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId, engineVersion: null);

        string openBody = await open.Content.ReadAsStringAsync();
        string joinBody = await join.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(open.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "an old build says no version");
            Assert.That(openBody, Does.Contain("engine_version_mismatch"));
            Assert.That(join.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(joinBody, Does.Contain("engine_version_mismatch"));
        });
    }

    /// <summary>The state names the engine the STORED report was simulated with, not the server's constant: a
    /// report from another engine is announced as such, so a client can refuse to build its timeline.</summary>
    [Test]
    public async Task State_CarriesTheEngineVersionOfTheStoredReport()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        var live = await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(live.EngineVersion, Is.EqualTo(MatchEngine.Version), "a fresh kickoff runs on this engine");

        const int olderEngine = MatchEngine.Version - 1;
        string current = $"{{\"EngineVersion\":{MatchEngine.Version},";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
            var row = await db.LiveMatches.SingleAsync(l => l.FixtureId == fx.FixtureId);
            Assert.That(row.ReportJson, Does.StartWith(current), "the stored report opens with its engine version");
            row.ReportJson = $"{{\"EngineVersion\":{olderEngine}," + row.ReportJson![current.Length..];
            await db.SaveChangesAsync();
        }

        var state = await GetLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(state.EngineVersion, Is.EqualTo(olderEngine), "the version is read from the stored report");
    }

    // --- dev tooling (spec watchable-match-engine R16: bot autopilot + fast-forward keep working) ---------

    /// <summary>The solo tester's opponent: the private-league bot endpoint still takes the fixture Live and
    /// substitutes, through the real (engine-version-checked) open.</summary>
    [Test]
    public async Task DevBot_TakesTheFixtureLive_AndSubstitutes()
    {
        var solo = await SetUpSoloLiveFixture();
        await OpenLive(solo.Tok, solo.LeagueId, solo.FixtureId);

        var resp = await _client.PostAsJsonAsync(
            $"/internal/dev/leagues/{solo.LeagueId}/live/{solo.FixtureId}/bot", new { sub = true, minute = 30 });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bot = (await resp.Content.ReadFromJsonAsync<DevBotLiveResult>())!;

        var state = await GetLive(solo.Tok, solo.LeagueId, solo.FixtureId);
        Assert.Multiple(() =>
        {
            Assert.That(bot.WentLive, Is.True, bot.Status);
            Assert.That(bot.SubMade, Is.True, bot.Status);
            Assert.That(state.Status, Is.EqualTo(LiveMatchStatus.Live));
            Assert.That(state.HomePresent && state.AwayPresent, Is.True, "the bot turned up");
            Assert.That(state.Changes, Has.Count.EqualTo(1));
            Assert.That(state.Changes[0].FromMinute, Is.EqualTo(30));
            Assert.That(state.Changes[0].Side, Is.Not.EqualTo(state.YourSide), "on the bot's side");
        });
    }

    /// <summary>A live match now lasts the director timeline (~10 real minutes at 1x), so the dev fast-forward
    /// moves the shared kickoff back by exactly the timeline's playback time to the asked minute — and never
    /// forward, so no screen has to rewind.</summary>
    [Test]
    public async Task DevFastForward_MovesTheSharedKickoff_AlongTheDirectorTimeline()
    {
        var solo = await SetUpSoloLiveFixture();
        await OpenLive(solo.Tok, solo.LeagueId, solo.FixtureId);
        await _client.PostAsJsonAsync($"/internal/dev/leagues/{solo.LeagueId}/live/{solo.FixtureId}/bot", new { });
        var before = await GetLive(solo.Tok, solo.LeagueId, solo.FixtureId);
        Assert.That(before.Status, Is.EqualTo(LiveMatchStatus.Live));

        var ff = await FastForward(solo.LeagueId, solo.FixtureId, 60);
        DateTime now = DateTime.UtcNow;
        var after = await GetLive(solo.Tok, solo.LeagueId, solo.FixtureId);

        var clock = new LiveBroadcastClock(
            new BroadcastDirector().Build(ReplayStore.Read(after.ReportJson!)!), after.KickoffUtc!.Value);
        double elapsed = (now - after.KickoffUtc.Value).TotalSeconds;
        Assert.Multiple(() =>
        {
            Assert.That(ff.Status, Is.EqualTo("ok"));
            Assert.That(ff.Minute, Is.GreaterThanOrEqualTo(60));
            Assert.That(after.KickoffUtc, Is.LessThan(before.KickoffUtc), "the kickoff moved back");
            Assert.That(elapsed, Is.EqualTo(clock.SecondsToReach(60)).Within(5.0),
                "moved by the director timeline's playback time to minute 60");
            Assert.That(clock.MinuteAt(now), Is.GreaterThanOrEqualTo(ff.Minute), "every screen now shows minute 60");
        });

        var back = await FastForward(solo.LeagueId, solo.FixtureId, 10);
        var unchanged = await GetLive(solo.Tok, solo.LeagueId, solo.FixtureId);
        Assert.Multiple(() =>
        {
            Assert.That(back.Status, Is.EqualTo("already_past"));
            Assert.That(unchanged.KickoffUtc, Is.EqualTo(after.KickoffUtc), "the clock never runs backwards");
        });

        TestContext.Out.WriteLine(
            $"[live-fast-forward] minute 60 is {clock.SecondsToReach(60):F1} s after kickoff; full time at " +
            $"{clock.SecondsToReach(90):F1} s");
    }

    [Test]
    public async Task Get_WithNoSession_ReturnsNotFound_AndByNonMember_ReturnsForbidden()
    {
        var fx = await SetUpLiveFixture();

        // A member reading a fixture that was never opened live.
        var noSession = await GetRaw(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        Assert.That(noSession.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "live_match_not_found");

        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        var (strangerTok, _) = await RegisterAccount();
        var byStranger = await GetRaw(strangerTok, fx.LeagueId, fx.FixtureId);
        Assert.That(byStranger.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- disconnect / season integration -----------------------------------------------------------

    /// <summary>THE 8.6 ✅ (fallback): the away user disconnects mid-match; with no changes from either
    /// side the match plays out unchanged on the kickoff plan (the "plan/AI fallback") — the finished
    /// report equals the kickoff baseline.</summary>
    [Test]
    public async Task Disconnect_MidMatch_FallsBackToPlanAI_Gracefully()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        var kickoff = await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);

        // The away user drops out; neither side sends a change.
        await LeaveLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        var finished = await Finish(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);

        Assert.Multiple(() =>
        {
            Assert.That(finished.Status, Is.EqualTo(LiveMatchStatus.Finished));
            Assert.That(finished.AwayPresent, Is.False, "the away user is marked absent");
            Assert.That(finished.ReportJson, Is.EqualTo(kickoff.ReportJson),
                "no changes ⇒ the match played out on the plan/AI, unchanged by the disconnect");
        });
    }

    /// <summary>THE 8.6 ✅ (season integration): a finished live match becomes the fixture's official
    /// result when the round resolves — the score feeds the schedule and standings.</summary>
    [Test]
    public async Task FinishedLiveResult_IsConsumedByRoundResolution()
    {
        var fx = await SetUpLiveFixture();
        await OpenLive(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);
        var live = await JoinLive(fx.AwayAcc.Tok, fx.LeagueId, fx.FixtureId);
        var finished = await Finish(fx.HomeAcc.Tok, fx.LeagueId, fx.FixtureId);

        // The live match is finished, but the fixture is not yet official until the round resolves.
        var before = await GetSeason(fx.CreatorTok, fx.LeagueId);
        Assert.That(before.Fixtures.First(f => f.Id == fx.FixtureId).Played, Is.False,
            "the live result is pending until the round resolves");

        // Force the round: the live fixture uses its stored result verbatim; the other fixture resolves instantly.
        await Advance(fx.CreatorTok, fx.LeagueId);
        var after = await GetSeason(fx.CreatorTok, fx.LeagueId);
        var liveFixture = after.Fixtures.First(f => f.Id == fx.FixtureId);

        Assert.Multiple(() =>
        {
            Assert.That(after.Season.RoundsPlayed, Is.EqualTo(1), "round 1 resolved");
            Assert.That(liveFixture.Played, Is.True);
            Assert.That(liveFixture.HomeGoals, Is.EqualTo(finished.HomeGoals), "the live score is the official score");
            Assert.That(liveFixture.AwayGoals, Is.EqualTo(finished.AwayGoals));
            Assert.That(after.Standings.Sum(s => s.Played), Is.EqualTo(4), "both round-1 matches are in the table");
        });
    }

    // --- helpers -----------------------------------------------------------------------------------

    private sealed record LiveFixture(
        Guid LeagueId,
        Guid FixtureId,
        int HomeClubExternalId,
        int AwayClubExternalId,
        (string Tok, Guid Id) HomeAcc,
        (string Tok, Guid Id) AwayAcc,
        (string Tok, Guid Id) OtherAcc,
        string CreatorTok,
        LeagueDetailDto Detail);

    private sealed record SoloFixture(string Tok, Guid LeagueId, Guid FixtureId);

    /// <summary>The solo tester's shape: one human among dev bots in an Active league, and his round-1
    /// fixture — whose opponent is therefore a bot the dev autopilot can drive.</summary>
    private async Task<SoloFixture> SetUpSoloLiveFixture()
    {
        var (tok, id) = await RegisterAccount();
        var seedResp = await _client.PostAsJsonAsync("/internal/dev/test-league",
            new { size = 4, bots = 4, toStatus = "Active", creatorUserId = id });
        Assert.That(seedResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the dev seed is mapped under Testing");
        var seed = (await seedResp.Content.ReadFromJsonAsync<DevSeedResult>())!;
        int myClub = seed.Members.Single(m => m.UserId == id).ClubExternalId!.Value;

        var season = await GetSeason(tok, seed.LeagueId);
        var fixture = season.Fixtures.First(f =>
            f.Round == 1 && (f.HomeClubExternalId == myClub || f.AwayClubExternalId == myClub));
        return new SoloFixture(tok, seed.LeagueId, fixture.Id);
    }

    private async Task<DevLiveFastForwardResult> FastForward(Guid leagueId, Guid fixtureId, int minute)
    {
        var resp = await _client.PostAsync(
            $"/internal/dev/leagues/{leagueId}/live/{fixtureId}/fast-forward?minute={minute}", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<DevLiveFastForwardResult>())!;
    }

    /// <summary>Draft a size-4 league to Active and pick the round-1 human-vs-human fixture, returning the
    /// two side owners plus a third member (a non-participant) and the creator token.</summary>
    private async Task<LiveFixture> SetUpLiveFixture()
    {
        var (leagueId, accounts) = await CreateActiveLeague(size: 4);
        var detail = await GetDetail(accounts[0].Tok, leagueId);
        var season = await GetSeason(accounts[0].Tok, leagueId);

        var fixture = season.Fixtures.First(f => f.Round == 1);
        var homeAcc = AccountForClub(detail, accounts, fixture.HomeClubExternalId);
        var awayAcc = AccountForClub(detail, accounts, fixture.AwayClubExternalId);
        var otherAcc = accounts.First(a => a.Id != homeAcc.Id && a.Id != awayAcc.Id);

        return new LiveFixture(
            leagueId, fixture.Id, fixture.HomeClubExternalId, fixture.AwayClubExternalId,
            homeAcc, awayAcc, otherAcc, accounts[0].Tok, detail);
    }

    private static (string Tok, Guid Id) AccountForClub(
        LeagueDetailDto detail, List<(string Tok, Guid Id)> accounts, int clubExternalId)
    {
        Guid ownerId = detail.Members.First(m => m.ClubExternalId == clubExternalId).UserId;
        return accounts.First(a => a.Id == ownerId);
    }

    private static List<(int Minute, int Type, int ClubId, int PlayerId)> EventsOf(string reportJson)
    {
        using var doc = JsonDocument.Parse(reportJson);
        var list = new List<(int, int, int, int)>();
        foreach (var e in doc.RootElement.GetProperty("Events").EnumerateArray())
            list.Add((
                e.GetProperty("Minute").GetInt32(),
                e.GetProperty("Type").GetInt32(),
                e.GetProperty("ClubId").GetInt32(),
                e.GetProperty("PlayerId").GetInt32()));
        return list;
    }

    // --- live-match HTTP helpers -------------------------------------------------------------------

    private async Task<LiveMatchStateDto> OpenLive(string tok, Guid leagueId, Guid fixtureId)
    {
        var resp = await OpenRaw(tok, leagueId, fixtureId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "open live should succeed");
        return (await resp.Content.ReadFromJsonAsync<LiveMatchStateDto>())!;
    }

    /// <summary>A client announces the match engine it will build the director timeline with; null is an
    /// old build that does not say.</summary>
    private Task<HttpResponseMessage> OpenRaw(
        string tok, Guid leagueId, Guid fixtureId, int? engineVersion = MatchEngine.Version) =>
        _client.SendAsync(Authed(HttpMethod.Post,
            $"/leagues/{leagueId}/live/{fixtureId}/open{VersionQuery(engineVersion)}", tok));

    private Task<HttpResponseMessage> JoinRaw(
        string tok, Guid leagueId, Guid fixtureId, int? engineVersion = MatchEngine.Version) =>
        _client.SendAsync(Authed(HttpMethod.Post,
            $"/leagues/{leagueId}/live/{fixtureId}/join{VersionQuery(engineVersion)}", tok));

    private static string VersionQuery(int? engineVersion) =>
        engineVersion.HasValue ? $"?engineVersion={engineVersion.Value}" : string.Empty;

    private async Task<LiveMatchStateDto> JoinLive(string tok, Guid leagueId, Guid fixtureId)
    {
        var resp = await JoinRaw(tok, leagueId, fixtureId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "join live should succeed");
        return (await resp.Content.ReadFromJsonAsync<LiveMatchStateDto>())!;
    }

    private async Task<LiveMatchStateDto> GetLive(string tok, Guid leagueId, Guid fixtureId)
    {
        var resp = await GetRaw(tok, leagueId, fixtureId);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "get live should succeed");
        return (await resp.Content.ReadFromJsonAsync<LiveMatchStateDto>())!;
    }

    private Task<HttpResponseMessage> GetRaw(string tok, Guid leagueId, Guid fixtureId) =>
        _client.SendAsync(Authed(HttpMethod.Get, $"/leagues/{leagueId}/live/{fixtureId}", tok));

    private async Task<LiveMatchStateDto> Change(string tok, Guid leagueId, Guid fixtureId, SubmitLiveChangeRequest req)
    {
        var resp = await ChangeRaw(tok, leagueId, fixtureId, req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a valid change should succeed");
        return (await resp.Content.ReadFromJsonAsync<LiveMatchStateDto>())!;
    }

    private Task<HttpResponseMessage> ChangeRaw(string tok, Guid leagueId, Guid fixtureId, SubmitLiveChangeRequest req) =>
        _client.SendAsync(Authed(HttpMethod.Post, $"/leagues/{leagueId}/live/{fixtureId}/change", tok, req));

    private async Task<LiveMatchStateDto> Finish(string tok, Guid leagueId, Guid fixtureId)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, $"/leagues/{leagueId}/live/{fixtureId}/finish", tok));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "finish should succeed");
        return (await resp.Content.ReadFromJsonAsync<LiveMatchStateDto>())!;
    }

    private async Task LeaveLive(string tok, Guid leagueId, Guid fixtureId)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post, $"/leagues/{leagueId}/live/{fixtureId}/leave", tok));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "leave should succeed");
    }

    // --- league / season HTTP helpers (mirrors LeagueSeasonEndpointTests) --------------------------

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
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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

    /// <summary>Creates a league, fills it, runs the whole draft → an Active season with every club
    /// claimed, plus the (token, id) of each account (index 0 = the creator).</summary>
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

    /// <summary>A legal 4-3-3 lineup plan from the club's squad, starting at <paramref name="skip"/> so a
    /// live "sub" can field a different (still valid) XI (squads have 22 players).</summary>
    private static LineupPlan BuildLineupPlan(LeagueDetailDto detail, int clubExternalId, int skip = 0)
    {
        var club = detail.Clubs.First(c => c.ExternalId == clubExternalId);
        var players = club.Players.Skip(skip).Take(11).ToList();
        var roles = LineupSelector.DefaultFormation;

        var plan = new LineupPlan { ClubId = clubExternalId };
        for (int i = 0; i < 11; i++)
            plan.Slots.Add(new LineupPlanSlot { Role = roles[i], PlayerId = players[i].ExternalId });
        return plan;
    }
}
