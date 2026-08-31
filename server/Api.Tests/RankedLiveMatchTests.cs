using System.Net;
using System.Net.Http.Json;
using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Ranked;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;
using Sim.Core.Tactics;

namespace Fts.Api.Tests;

/// <summary>
/// TASK 12.3, THE CLOCK — pure calendar math, no DB and no wall-clock waits. A ranked match is now an
/// appointment, so these pin the two things an appointment has to get right: it happens at 21:00 of the
/// WORLD's own evening, and it stays at 21:00 when the clocks change.
/// </summary>
[TestFixture]
public class RankedKickoffClockTests
{
    private const int Day = 86_400;

    private static TimeZoneInfo Rome => TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static TimeZoneInfo London => TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static RankedCalendar.KickoffClock RomeAt21 => new(Day, Rome, 21);

    [Test]
    public void EveryMatchday_KicksOffAt2100_InTheWorldsOwnZone()
    {
        // A season that STARTS at an inconvenient hour must not condemn its coaches to that hour: the first
        // matchday is the next 21:00, not "now".
        var start = new DateTime(2026, 4, 7, 4, 30, 0, DateTimeKind.Utc);

        for (int round = 1; round <= 14; round++)
        {
            DateTime kickoff = RankedCalendar.KickoffOf(start, round, RomeAt21);
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(kickoff, Rome);
            Assert.That(local.Hour, Is.EqualTo(21), $"matchday {round} kicks off at 21:00 Italian time");
            Assert.That(local.Minute, Is.EqualTo(0));
        }

        DateTime first = RankedCalendar.KickoffOf(start, 1, RomeAt21);
        Assert.That(first, Is.GreaterThan(start), "matchday 1 is the FIRST 21:00 after the season starts");
        Assert.That(RankedCalendar.KickoffOf(start, 2, RomeAt21) - first, Is.EqualTo(TimeSpan.FromDays(1)));
    }

    [Test]
    public void ADeviceInLondon_Reads2000_ForTheItalianWorlds2100()
    {
        // The ✅'s own wording. One instant, two coaches, two correct answers — which is exactly why the
        // fixture list carries a UTC instant and every client renders it locally.
        var start = new DateTime(2026, 4, 7, 4, 30, 0, DateTimeKind.Utc);
        DateTime kickoff = RankedCalendar.KickoffOf(start, 3, RomeAt21);

        Assert.Multiple(() =>
        {
            Assert.That(TimeZoneInfo.ConvertTimeFromUtc(kickoff, Rome).Hour, Is.EqualTo(21));
            Assert.That(TimeZoneInfo.ConvertTimeFromUtc(kickoff, London).Hour, Is.EqualTo(20));
        });
    }

    [Test]
    public void AcrossADstChange_TheUtcInstantMoves_AndTheLocalHourDoesNot()
    {
        // Italy leaves summer time on the last Sunday of October. THIS is why the world stores a zone and not
        // an offset: 21:00 Rome is 19:00 UTC in CEST and 20:00 UTC in CET, and a coach's evening must not
        // shift by an hour because the calendar did arithmetic in seconds.
        var start = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

        DateTime before = RankedCalendar.KickoffOf(start, 2, RomeAt21);  // 21 Oct, still CEST
        DateTime after = RankedCalendar.KickoffOf(start, 8, RomeAt21);   // 27 Oct, CET

        Assert.Multiple(() =>
        {
            Assert.That(before.Hour, Is.EqualTo(19), "21:00 CEST is 19:00 UTC");
            Assert.That(after.Hour, Is.EqualTo(20), "21:00 CET is 20:00 UTC");
            Assert.That(TimeZoneInfo.ConvertTimeFromUtc(before, Rome).Hour, Is.EqualTo(21));
            Assert.That(TimeZoneInfo.ConvertTimeFromUtc(after, Rome).Hour, Is.EqualTo(21));
        });

        TestContext.Out.WriteLine($"[ranked-kickoff] pre-DST {before:o} · post-DST {after:o}");
    }

    [Test]
    public void ACompressedCalendar_KeepsThePre123RelativeKickoffs()
    {
        // The load-bearing compatibility claim: anchoring engages only at a whole-day cadence, so every
        // ranked test written before 12.3 — all of which compress the interval — measures what it always did.
        var start = new DateTime(2026, 7, 23, 18, 0, 0, DateTimeKind.Utc);

        var hourly = new RankedCalendar.KickoffClock(3600, Rome, 21);
        var instant = new RankedCalendar.KickoffClock(0, Rome, 21);

        Assert.Multiple(() =>
        {
            Assert.That(hourly.AnchorsToLocalHour, Is.False, "an hourly cadence has no 'same hour every day'");
            Assert.That(instant.AnchorsToLocalHour, Is.False);
            Assert.That(RomeAt21.AnchorsToLocalHour, Is.True);
            Assert.That(RankedCalendar.KickoffOf(start, 1, hourly), Is.EqualTo(start));
            Assert.That(RankedCalendar.KickoffOf(start, 5, hourly), Is.EqualTo(start.AddHours(4)));
            Assert.That(RankedCalendar.KickoffOf(start, 9, instant), Is.EqualTo(start));
        });
    }

    [Test]
    public void AnUnknownZone_DegradesToTheRelativeCalendar_RatherThanThrowing()
    {
        // A bad zone id in configuration must not be able to take a ladder down.
        Assert.That(RankedCalendar.ZoneOrNull("Middle/Earth"), Is.Null);
        Assert.That(RankedCalendar.ZoneOrNull(""), Is.Null);
        Assert.That(RankedCalendar.ZoneOrNull("Europe/Rome"), Is.Not.Null);

        var start = new DateTime(2026, 7, 23, 18, 0, 0, DateTimeKind.Utc);
        var broken = new RankedCalendar.KickoffClock(Day, RankedCalendar.ZoneOrNull("Middle/Earth"), 21);
        Assert.That(RankedCalendar.KickoffOf(start, 3, broken), Is.EqualTo(start.AddDays(2)));
    }
}

/// <summary>
/// TASK 12.3, THE DETERMINISM CLAIM — the single most important assertion in the task, and it needs no
/// database: attending a match cannot change it unless you actually change something. A live session with no
/// pause-point inputs must produce the byte-identical <c>MatchReport</c> the headless matchday produces from
/// the same clubs, the same stored orders and the same seed. Everything else in 12.3 rests on this: it is
/// what lets the calendar consume a live report verbatim, and what makes "a fixture nobody attends still
/// resolves with the identical scoreline and replay" true by construction rather than by luck.
/// </summary>
[TestFixture]
public class RankedLiveDeterminismTests
{
    [Test]
    public void ALiveSessionWithNoChanges_IsByteIdenticalToTheHeadlessResolve()
    {
        var cfg = new BalanceConfig();
        League league = new LeagueGenerator().Generate(new Pcg32(20260826UL));
        Club home = league.Clubs[0];
        Club away = league.Clubs[5];
        ulong seed = FixtureSeed.For(worldSeed: 987654321L, round: 4, homeExternalId: home.Id, awayExternalId: away.Id);

        MatchResolver.ResolveResult headless = MatchResolver.Resolve(home, away, null, null, seed, cfg);
        MatchResolver.ResolveResult live = MatchResolver.ResolveLive(
            home, away, null, null, Array.Empty<LiveChange>(), seed, cfg);

        string headlessJson = System.Text.Json.JsonSerializer.Serialize(headless.Report);
        string liveJson = System.Text.Json.JsonSerializer.Serialize(live.Report);

        Assert.Multiple(() =>
        {
            Assert.That(live.Report.HomeGoals, Is.EqualTo(headless.Report.HomeGoals));
            Assert.That(live.Report.AwayGoals, Is.EqualTo(headless.Report.AwayGoals));
            Assert.That(liveJson, Is.EqualTo(headlessJson),
                "a live session that changed nothing IS the scheduled match, byte for byte");
            Assert.That(live.HomeStarterIds, Is.EquivalentTo(headless.HomeStarterIds));
            Assert.That(live.AwayStarterIds, Is.EquivalentTo(headless.AwayStarterIds));
        });

        TestContext.Out.WriteLine(
            $"[ranked-live-determinism] {home.Name} {headless.Report.HomeGoals}-{headless.Report.AwayGoals} " +
            $"{away.Name} · report {headlessJson.Length} bytes, identical live and headless");
    }

    [Test]
    public void APausePointChange_MovesOnlyTheRemainder()
    {
        // The 8.6 mechanic, re-pinned for the ladder: the minutes BEFORE a change are untouched, so two
        // clients rendering from a shared kickoff never have to rewind what they already showed.
        var cfg = new BalanceConfig();
        League league = new LeagueGenerator().Generate(new Pcg32(20260826UL));
        Club home = league.Clubs[1];
        Club away = league.Clubs[7];
        ulong seed = FixtureSeed.For(555L, 2, home.Id, away.Id);

        var attacking = new TacticPlan { Mentality = Mentality.Attacking, Pressing = Pressing.High };
        MatchReport before = MatchResolver.ResolveLive(
            home, away, null, null, Array.Empty<LiveChange>(), seed, cfg).Report;
        MatchReport after = MatchResolver.ResolveLive(
            home, away, null, null,
            new[] { new LiveChange(60, LiveSide.Home, null, attacking) }, seed, cfg).Report;

        var earlyBefore = before.Events.Where(e => e.Minute <= 55).Select(e => $"{e.Minute}:{e.Type}").ToList();
        var earlyAfter = after.Events.Where(e => e.Minute <= 55).Select(e => $"{e.Minute}:{e.Type}").ToList();

        Assert.That(earlyAfter, Is.EqualTo(earlyBefore),
            "everything before the pause point is the same match — only the remainder re-rolls");
    }
}

/// <summary>
/// Integration base for LIVE RANKED MATCHES (task 12.3). The shape is the one the feature needs and the
/// 9.2 base cannot give: matchdays an HOUR apart rather than all at once, so a round exists that has not been
/// resolved yet and can therefore be attended, plus <c>/internal/ranked/kickoff-now</c> to pull that round's
/// kick-off to this second instead of waiting for it — the same dev tooling a solo human uses at 15:40 to
/// test a 21:00 appointment, which is the point of shipping it.
///
/// The door is held wide open (two hours before, ten minutes of grace) so the tests measure the LIVE
/// MECHANISM and not the clock arithmetic — that is pinned separately, without a database, in
/// <see cref="RankedKickoffClockTests"/>.
/// </summary>
public abstract class RankedLiveTestBase : RankedSeasonTestBase
{
    /// <summary>An hour apart: matchday 1 is due at season start (so a tick starts AND plays it, as ever),
    /// and everything after it is in the future, waiting to be attended.</summary>
    protected override int MatchdayIntervalSeconds => 3600;

    /// <summary>How much slack the server gives a pause-point change beyond the minute the clock says has
    /// been played. 0 = off, which is what a test that cannot spend twenty real seconds reaching minute 10
    /// needs; the fixture that actually tests the guard turns it back on.</summary>
    protected virtual int ChangeMinuteTolerance => 0;

    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        settings["Ranked:LiveMatchesEnabled"] = "true";
        settings["Ranked:LiveOpensBeforeSeconds"] = "7200";
        settings["Ranked:LiveGraceSeconds"] = "600";
        settings["Ranked:LiveSecondsPerMatchMinute"] = "2";
        settings["Ranked:LiveChangeMinuteTolerance"] = ChangeMinuteTolerance.ToString();
        // No between-seasons pause: a fixture that has to reach a DIVISION should not sit out a week for it.
        settings["Ranked:SeasonBreakSeconds"] = "0";
    }

    // --- the live surface ---------------------------------------------------------------------------

    protected async Task<(HttpStatusCode Code, RankedLiveStateDto? State, string Body)> OpenLive(
        string token, Guid fixtureId)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/live/{fixtureId}/open", token);
        var resp = await Client.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        return resp.StatusCode != HttpStatusCode.OK
            ? (resp.StatusCode, null, body)
            : (resp.StatusCode, await resp.Content.ReadFromJsonAsync<RankedLiveStateDto>(), body);
    }

    protected async Task<(HttpStatusCode Code, RankedLiveStateDto? State, string Body)> GetLive(
        string token, Guid fixtureId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/live/{fixtureId}", token);
        var resp = await Client.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        return resp.StatusCode != HttpStatusCode.OK
            ? (resp.StatusCode, null, body)
            : (resp.StatusCode, await resp.Content.ReadFromJsonAsync<RankedLiveStateDto>(), body);
    }

    protected async Task<(HttpStatusCode Code, RankedLiveStateDto? State, string Body)> ChangeLive(
        string token, Guid fixtureId, int minute, TacticPlan? tactic)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/live/{fixtureId}/change", token,
            new SubmitRankedLiveChangeRequest(minute, null, tactic));
        var resp = await Client.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        return resp.StatusCode != HttpStatusCode.OK
            ? (resp.StatusCode, null, body)
            : (resp.StatusCode, await resp.Content.ReadFromJsonAsync<RankedLiveStateDto>(), body);
    }

    protected async Task<HttpStatusCode> FinishLive(string token, Guid fixtureId)
    {
        using var req = Authed(HttpMethod.Post, $"/ranked/live/{fixtureId}/finish", token);
        var resp = await Client.SendAsync(req);
        return resp.StatusCode;
    }

    /// <summary>Pull the whole running ladder's next kick-off to (about) now — the dev tooling under test.</summary>
    protected async Task KickoffNow(int seconds = 0)
    {
        var resp = await Client.PostAsync($"/internal/ranked/kickoff-now?seconds={seconds}", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the kickoff-now endpoint is dev-mapped under Testing");
    }

    protected async Task<RankedSeasonDto> Season(string token)
    {
        var (code, season) = await GetSeason(token);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(season, Is.Not.Null);
        return season!;
    }

    /// <summary>
    /// The fixture where two given coaches meet, brought to the front of the queue: rounds before it are
    /// played off, and its own kick-off is pulled to now so it can actually be attended. Returns the fixture
    /// as the FIRST coach sees it.
    /// </summary>
    protected async Task<RankedFixtureDto> AdvanceToTheirMatch(string tokenA, string tokenB)
    {
        RankedSeasonDto a = await Season(tokenA);
        RankedSeasonDto b = await Season(tokenB);
        var hisIds = b.Fixtures.Where(f => f.IsYours).Select(f => f.Id).ToHashSet();

        RankedFixtureDto? target = a.Fixtures
            .Where(f => f.IsYours && !f.Played && hisIds.Contains(f.Id))
            .OrderBy(f => f.Round)
            .FirstOrDefault();
        Assert.That(target, Is.Not.Null, "the two coaches meet somewhere in a double round-robin");

        for (int guard = 0; guard < 20; guard++)
        {
            RankedSeasonDto season = await Season(tokenA);
            RankedFixtureDto now = season.Fixtures.First(f => f.Id == target!.Id);
            Assert.That(now.Played, Is.False, "the match we were waiting for was resolved underneath us");

            int nextRound = season.Fixtures.Where(f => !f.Played).Min(f => f.Round);
            if (nextRound == now.Round)
            {
                await KickoffNow();
                RankedSeasonDto refreshed = await Season(tokenA);
                return refreshed.Fixtures.First(f => f.Id == target!.Id);
            }

            await KickoffNow();
            await Tick();
        }

        Assert.Fail("could not reach the two coaches' shared fixture");
        return null!;
    }
}

/// <summary>
/// THE 12.3 ✅, the half that needs two accounts: both coaches enter at kick-off and see ONE live match, a
/// change by one is visible to the other, and what they watched is what the table ends up saying.
/// </summary>
[TestFixture]
public class RankedLiveMatchTests : RankedLiveTestBase
{
    [Test]
    public async Task TwoCoaches_AttendTheSameMatch_AndOnesChangeIsVisibleToTheOther()
    {
        var tokens = await EnrolCohort();
        await Tick(); // the season starts and plays its first matchday, as it always has

        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        var (codeA, stateA, bodyA) = await OpenLive(tokens[0], match.Id);
        Assert.That(codeA, Is.EqualTo(HttpStatusCode.OK), bodyA);
        var (codeB, stateB, bodyB) = await OpenLive(tokens[1], match.Id);
        Assert.That(codeB, Is.EqualTo(HttpStatusCode.OK), bodyB);

        Assert.Multiple(() =>
        {
            Assert.That(stateA!.Status, Is.EqualTo(LiveMatchStatus.Live),
                "the calendar's kick-off has arrived, so the match is under way");
            Assert.That(stateA.YourSide, Is.Not.Null, "the coach is one of the two sides");
            Assert.That(stateB!.YourSide, Is.Not.Null);
            Assert.That(stateB.YourSide, Is.Not.EqualTo(stateA.YourSide), "opposite sides of one match");
            Assert.That(stateA.ReportJson, Is.Not.Null.And.Not.Empty, "the 90' exists to render from");
            Assert.That(stateA.KickoffUtc, Is.EqualTo(stateB.KickoffUtc),
                "one appointment — both clients render the same minute at the same instant");
            Assert.That(stateB.HomePresent && stateB.AwayPresent, Is.True, "each sees the other arrive");
            Assert.That(stateA.HomeIsAi || stateA.AwayIsAi, Is.False, "this one is coach against coach");
        });

        // One coach shouts an instruction from the touchline at minute 20.
        var attacking = new TacticPlan { Mentality = Mentality.Attacking, Pressing = Pressing.High };
        var (codeChange, changed, bodyChange) = await ChangeLive(tokens[0], match.Id, 20, attacking);
        Assert.That(codeChange, Is.EqualTo(HttpStatusCode.OK), bodyChange);

        // …and the OTHER coach sees it, on his next read, on the same side he is playing against.
        var (codeSeen, seen, bodySeen) = await GetLive(tokens[1], match.Id);
        Assert.That(codeSeen, Is.EqualTo(HttpStatusCode.OK), bodySeen);
        Assert.Multiple(() =>
        {
            Assert.That(seen!.Changes, Has.Count.EqualTo(1), "the opponent's change is on his timeline");
            Assert.That(seen.Changes[0].FromMinute, Is.EqualTo(20));
            Assert.That(seen.Changes[0].Side, Is.EqualTo(stateA!.YourSide));
            Assert.That(seen.HomeGoals, Is.EqualTo(changed!.HomeGoals), "both clients hold the same match");
            Assert.That(seen.AwayGoals, Is.EqualTo(changed.AwayGoals));
        });

        // Full time, confirmed by both — the calendar may now consume what they watched.
        Assert.That(await FinishLive(tokens[0], match.Id), Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await FinishLive(tokens[1], match.Id), Is.EqualTo(HttpStatusCode.OK));

        int liveHome = seen!.HomeGoals, liveAway = seen.AwayGoals;
        await Tick();

        RankedSeasonDto after = await Season(tokens[0]);
        RankedFixtureDto played = after.Fixtures.First(f => f.Id == match.Id);
        Assert.Multiple(() =>
        {
            Assert.That(played.Played, Is.True, "the matchday resolved once the live match was over");
            Assert.That(played.HomeGoals, Is.EqualTo(liveHome), "the table says what the two coaches watched");
            Assert.That(played.AwayGoals, Is.EqualTo(liveAway));
        });

        // The stored replay is the live report — one match, one set of bytes, for everyone.
        using var replayReq = Authed(HttpMethod.Get, $"/ranked/season/fixtures/{match.Id}/replay", tokens[1]);
        var replayResp = await Client.SendAsync(replayReq);
        Assert.That(replayResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string replay = await replayResp.Content.ReadAsStringAsync();
        Assert.That(replay, Is.EqualTo(seen.ReportJson), "the replay IS the live report, verbatim");

        TestContext.Out.WriteLine(
            $"[ranked-live] {stateA!.HomeClubName} {liveHome}-{liveAway} {stateA.AwayClubName} " +
            $"· kickoff {stateA.KickoffUtc:o} · changes {seen.Changes.Count}");
    }

    [Test]
    public async Task AMatchdayNobodyAttends_ResolvesAtItsKickoff_WithoutWaitingOutTheGrace()
    {
        // The compatibility half of the ✅: 12.3 must not have made every ladder matchday ten minutes late.
        // A round with no live session resolves the moment its kick-off passes, exactly as in 9.2.
        var tokens = await EnrolCohort();
        await Tick();

        await KickoffNow();
        RankedTickSummary summary = await Tick();

        Assert.That(summary.MatchdaysResolved, Is.GreaterThan(0),
            "no session exists for this round, so the calendar does not wait for one");

        RankedSeasonDto season = await Season(tokens[0]);
        Assert.That(season.State!.RoundsPlayed, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task AnOpenLiveMatch_HoldsItsWholeMatchday_UntilItIsFinished()
    {
        // A round resolves in ONE piece (one EvolveWeek, one coherent table), so a match still being played
        // holds the whole matchday — and releases it the moment it is over.
        var tokens = await EnrolCohort();
        await Tick();

        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);
        var (code, state, body) = await OpenLive(tokens[0], match.Id);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(state!.Status, Is.EqualTo(LiveMatchStatus.Live));

        RankedTickSummary held = await Tick();
        Assert.That(held.MatchdaysResolved, Is.EqualTo(0),
            "the matchday waits while one of its fixtures is being played");

        RankedSeasonDto during = await Season(tokens[0]);
        Assert.That(during.Fixtures.First(f => f.Id == match.Id).Played, Is.False);

        Assert.That(await FinishLive(tokens[0], match.Id), Is.EqualTo(HttpStatusCode.OK));

        RankedTickSummary released = await Tick();
        Assert.That(released.MatchdaysResolved, Is.EqualTo(1), "full time releases the matchday");
        RankedSeasonDto after = await Season(tokens[0]);
        Assert.That(after.Fixtures.First(f => f.Id == match.Id).Played, Is.True);
    }

    [Test]
    public async Task TheDailyDigest_PutsTheAppointmentAtTheTopOfTheList()
    {
        // The ≤10-minute daily loop (9.4) is where a coach looks first, and an appointment is the one item on
        // it that EXPIRES — an unanswered offer is still there tomorrow, a 21:00 kick-off is not. So it goes
        // above everything else, and the digest carries the door with it.
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        using var req = Authed(HttpMethod.Get, "/ranked/today", tokens[0]);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var today = await resp.Content.ReadFromJsonAsync<RankedTodayDto>();

        Assert.Multiple(() =>
        {
            Assert.That(today!.NextMatch, Is.Not.Null);
            Assert.That(today.NextMatch!.FixtureId, Is.EqualTo(match.Id));
            Assert.That(today.NextMatch.LiveOpen, Is.True, "the door is open, and the digest says so");
            Assert.That(today.NextMatch.SecondsToLiveOpen, Is.EqualTo(0), "no countdown left to run");
            Assert.That(today.NextMatch.LiveStatus, Is.Null, "nobody has opened a session yet");
            Assert.That(today.Todo.Select(t => t.Kind), Does.Contain(RankedTodoKind.WatchLive));
            Assert.That(today.Todo[0].Kind, Is.EqualTo(RankedTodoKind.WatchLive),
                "the match being played now outranks every other thing to do today");
        });
    }

    [Test]
    public async Task TheSeasonScreen_KnowsWhenTheLiveDoorIsOpen_AndWhoseMatchItIs()
    {
        // The client half of the ✅: the row that becomes "guardala dal vivo" is decided by the SERVER's
        // clock, so a device a few minutes out of step never offers it at the wrong moment.
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        RankedSeasonDto season = await Season(tokens[0]);
        RankedFixtureDto row = season.Fixtures.First(f => f.Id == match.Id);

        Assert.Multiple(() =>
        {
            Assert.That(row.LiveOpen, Is.True, "his own next match, at its kick-off, is attendable");
            Assert.That(row.LiveStatus, Is.Null, "nobody has opened a session yet");
            Assert.That(season.State!.LiveEnabled, Is.True);
            Assert.That(season.State.LiveSecondsPerMatchMinute, Is.EqualTo(2),
                "the client renders on the server's playback rate, not on a constant of its own");
            Assert.That(season.State.ServerUtc, Is.Not.EqualTo(default(DateTime)),
                "a countdown needs the server's clock, not the device's");
            Assert.That(season.Fixtures.Where(f => !f.IsYours).All(f => !f.LiveOpen), Is.True,
                "other people's matches are never offered as yours to play");
        });

        await OpenLive(tokens[0], match.Id);
        RankedSeasonDto afterOpen = await Season(tokens[0]);
        Assert.That(afterOpen.Fixtures.First(f => f.Id == match.Id).LiveStatus,
            Is.EqualTo(LiveMatchStatus.Live), "the row now knows a session is under way");
    }
}

/// <summary>
/// The refusals — the guards that make a LADDER match different from a lobby of friends. Each one is a way
/// the live door could be abused if it were only the client that policed it.
/// </summary>
[TestFixture]
public class RankedLiveGuardTests : RankedLiveTestBase
{
    /// <summary>The future-minute check ON, and tight: one minute of slack, so a change asking for the 90th
    /// minute a second after kick-off is exactly the hindsight the guard exists to refuse.</summary>
    protected override int ChangeMinuteTolerance => 1;

    [Test]
    public async Task AChangeInTheMatchsFuture_IsRefused()
    {
        // The whole 90' is in the report the client holds. Without this check a doctored client could read
        // the ending and then "substitute" with hindsight — which is cheating, in a ranked competition.
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        var (code, state, body) = await OpenLive(tokens[0], match.Id);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(state!.Status, Is.EqualTo(LiveMatchStatus.Live));

        var (late, _, lateBody) = await ChangeLive(
            tokens[0], match.Id, 90, new TacticPlan { Mentality = Mentality.Defensive });
        Assert.That(late, Is.EqualTo(HttpStatusCode.BadRequest),
            "the 90th minute has not been played yet, whatever the client says");
        Assert.That(lateBody, Does.Contain("invalid_live_change"));

        // …while a change at the minute actually being played goes through.
        var (nowOk, _, nowBody) = await ChangeLive(
            tokens[0], match.Id, 1, new TacticPlan { Mentality = Mentality.Attacking });
        Assert.That(nowOk, Is.EqualTo(HttpStatusCode.OK), nowBody);
    }

    [Test]
    public async Task SomebodyElsesMatch_CanBeWatched_ButNotChanged()
    {
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);
        await OpenLive(tokens[0], match.Id);

        // A third coach in the same group: he may look (it is his group's match) …
        var (watchCode, watched, watchBody) = await GetLive(tokens[2], match.Id);
        Assert.That(watchCode, Is.EqualTo(HttpStatusCode.OK), watchBody);
        Assert.That(watched!.YourSide, Is.Null, "a spectator controls neither side");

        // … but he may not open it as a player, nor touch either team.
        var (openCode, _, openBody) = await OpenLive(tokens[2], match.Id);
        Assert.That(openCode, Is.EqualTo(HttpStatusCode.Forbidden), openBody);
        Assert.That(openBody, Does.Contain("not_your_match"));

        var (changeCode, _, changeBody) = await ChangeLive(
            tokens[2], match.Id, 5, new TacticPlan { Mentality = Mentality.Defensive });
        Assert.That(changeCode, Is.EqualTo(HttpStatusCode.Forbidden), changeBody);
    }

    [Test]
    public async Task ACoachFromAnotherGroup_LearnsNothing()
    {
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        // A coach who enrolled after the cohort filled lands in a DIFFERENT placement group.
        string outsider = await RegisterAccount();
        await Enrol(outsider);

        var (code, _, body) = await GetLive(outsider, match.Id);
        Assert.That(code, Is.EqualTo(HttpStatusCode.Forbidden), body);
    }

    [Test]
    public async Task AMatchThatHasAlreadyBeenPlayed_CannotBeAttended()
    {
        var tokens = await EnrolCohort();
        await Tick(); // matchday 1 resolves in the same tick that starts the season

        RankedSeasonDto season = await Season(tokens[0]);
        RankedFixtureDto done = season.Fixtures.First(f => f.IsYours && f.Played);

        var (code, _, body) = await OpenLive(tokens[0], done.Id);
        Assert.That(code, Is.EqualTo(HttpStatusCode.Conflict), body);
        Assert.That(body, Does.Contain("live_not_open"));
        Assert.That(done.LiveOpen, Is.False, "and the row never offered it in the first place");
    }
}

/// <summary>
/// The decision that makes live ranked matches worth building at all (taken with the user, 2026-08-25): you
/// can attend a match against an AI seat. A ladder group is mostly vacant seats until the pyramid fills, so a
/// live mode that needed two humans would almost never fire — and "there is exactly one match a day and
/// therefore one appointment to keep" would be false for most of the ladder.
///
/// Reaching an AI opponent costs a whole placement season (a placement cohort is all-human by construction),
/// so this fixture fast-forwards into a DIVISION first — the same shape 12.2's bot-bidding test uses.
/// </summary>
[TestFixture]
public class RankedLiveVsAiTests : RankedLiveTestBase
{
    private async Task<RankedTickSummary> FastForward(int matchdays)
    {
        var resp = await Client.PostAsync($"/internal/ranked/fast-forward?matchdays={matchdays}", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedTickSummary>())!;
    }

    [Test]
    public async Task ACoachAttendsHisMatch_AgainstAVacantAiSeat()
    {
        var tokens = await EnrolCohort();

        // Placement first — its cohort is all human, so there is no AI seat to meet until it is over.
        RankedSeasonDto? division = null;
        for (int i = 0; i < 60 && division is null; i++)
        {
            await FastForward(1);
            if ((await GetMine(tokens[0])).Status != RankedCoachStatus.Placed) continue;

            var (code, season) = await GetSeason(tokens[0]);
            if (code == HttpStatusCode.OK
                && season?.State is { Kind: RankedGroupKind.Division, SeasonComplete: false }
                && season.Fixtures.Any(f => f.IsYours && !f.Played))
                division = season;
        }

        Assert.That(division, Is.Not.Null,
            "the placement cohort sorted into divisions and a division season is under way");

        RankedLiveStateDto? vsAi = null;
        for (int guard = 0; guard < 8 && vsAi is null; guard++)
        {
            RankedSeasonDto season = await Season(tokens[0]);
            var mine = season.Fixtures.Where(f => f.IsYours && !f.Played).OrderBy(f => f.Round).ToList();
            if (mine.Count == 0) break;

            int nextRound = season.Fixtures.Where(f => !f.Played).Min(f => f.Round);
            if (mine[0].Round != nextRound) { await KickoffNow(); await Tick(); continue; }

            await KickoffNow();
            var (code, state, body) = await OpenLive(tokens[0], mine[0].Id);
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK), body);

            if (state!.HomeIsAi || state.AwayIsAi) { vsAi = state; break; }

            // A human opponent this round — play it off and try the next one.
            await FinishLive(tokens[0], mine[0].Id);
            await Tick();
        }

        Assert.That(vsAi, Is.Not.Null, "a division of 4 with 2 coaches puts an AI seat on the fixture list");
        Assert.Multiple(() =>
        {
            Assert.That(vsAi!.Status, Is.EqualTo(LiveMatchStatus.Live),
                "the appointment is the calendar's: an absent (or non-existent) opponent does not delay it");
            Assert.That(vsAi.YourSide, Is.Not.Null, "the coach still controls his own side");
            Assert.That(vsAi.HomeIsAi ^ vsAi.AwayIsAi, Is.True, "exactly one side has no account behind it");
            Assert.That(vsAi.ReportJson, Is.Not.Null.And.Not.Empty,
                "the AI side plays its stored orders — the same thing an absent human's side does");
        });

        // And he can coach it: a touchline instruction lands against an AI opponent just as it would
        // against a coach.
        var (changeCode, changed, changeBody) = await ChangeLive(
            tokens[0], vsAi!.FixtureId, 15, new TacticPlan { Mentality = Mentality.Attacking });
        Assert.That(changeCode, Is.EqualTo(HttpStatusCode.OK), changeBody);
        Assert.That(changed!.Changes, Has.Count.EqualTo(1));

        Assert.That(await FinishLive(tokens[0], vsAi.FixtureId), Is.EqualTo(HttpStatusCode.OK));
        await Tick();

        RankedSeasonDto after = await Season(tokens[0]);
        RankedFixtureDto played = after.Fixtures.First(f => f.Id == vsAi.FixtureId);
        Assert.Multiple(() =>
        {
            Assert.That(played.Played, Is.True);
            Assert.That(played.HomeGoals, Is.EqualTo(changed.HomeGoals));
            Assert.That(played.AwayGoals, Is.EqualTo(changed.AwayGoals));
        });

        TestContext.Out.WriteLine(
            $"[ranked-live-ai] {vsAi.HomeClubName}{(vsAi.HomeIsAi ? " (AI)" : "")} " +
            $"{played.HomeGoals}-{played.AwayGoals} " +
            $"{vsAi.AwayClubName}{(vsAi.AwayIsAi ? " (AI)" : "")}");
    }
}

/// <summary>
/// THE DEV-SIM TOOLING (the project's standing rule: every online feature ships a way to test it SOLO).
/// A live match needs an opponent and a kick-off; a solo tester at 15:40 has neither. These two endpoints are
/// what he uses instead — <c>/internal/ranked/kickoff-now</c> to bring the appointment forward, and
/// <c>/internal/dev/ranked/live/{fixtureId}/bot</c> to make the other coach turn up, substitute and shake
/// hands. This fixture is the proof the tooling actually drives the real use cases, because tooling that
/// bypasses the guards proves nothing about the feature.
/// </summary>
[TestFixture]
public class RankedLiveDevToolingTests : RankedLiveTestBase
{
    [Test]
    public async Task TheBotOpponent_TurnsUp_Substitutes_AndShakesHands()
    {
        var tokens = await EnrolCohort();
        await Tick();
        RankedFixtureDto match = await AdvanceToTheirMatch(tokens[0], tokens[1]);

        var (code, mine, body) = await OpenLive(tokens[0], match.Id);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(mine!.Changes, Is.Empty);

        var resp = await Client.PostAsync(
            $"/internal/dev/ranked/live/{match.Id}/bot?sub=true&minute=30&finish=true", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await resp.Content.ReadFromJsonAsync<Fts.Application.Dev.DevRankedLiveResult>();

        Assert.Multiple(() =>
        {
            Assert.That(result!.OpponentJoined, Is.True, result.Note ?? result.Status);
            Assert.That(result.SubMade, Is.True, result.Note ?? result.Status);
            Assert.That(result.Finished, Is.True, result.Note ?? result.Status);
        });

        // The solo tester's screen sees all of it without a second device.
        var (seenCode, seen, seenBody) = await GetLive(tokens[0], match.Id);
        Assert.That(seenCode, Is.EqualTo(HttpStatusCode.OK), seenBody);
        Assert.Multiple(() =>
        {
            Assert.That(seen!.HomePresent && seen.AwayPresent, Is.True, "the opponent turned up");
            Assert.That(seen.Changes, Has.Count.EqualTo(1), "and made his substitution");
            Assert.That(seen.Changes[0].Side, Is.Not.EqualTo(mine.YourSide), "on HIS side, not the tester's");
            Assert.That(seen.Status, Is.EqualTo(LiveMatchStatus.Finished));
        });

        TestContext.Out.WriteLine(
            $"[ranked-live-dev] bot joined, subbed at {result!.Minute}' and confirmed full time " +
            $"— {seen!.HomeClubName} {seen.HomeGoals}-{seen.AwayGoals} {seen.AwayClubName}");
    }
}
