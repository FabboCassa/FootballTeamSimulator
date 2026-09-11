using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Ranked;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The ladder's rating maths (Phase 9.3) — pure, no DB, no HTTP. These pin the PROPERTIES Elo must have
/// (symmetry, upsets worth more, a favourite's win worth less, a monotone finishing bonus), not magic
/// numbers, so re-tuning the K-factor or the swing cannot silently break them.
/// </summary>
[TestFixture]
public class EloModelTests
{
    private const int K = 24;

    [Test]
    public void EqualRatings_ExpectAHalf_AndAWinGainsWhatALossCosts()
    {
        Assert.That(EloModel.Expected(1000, 1000), Is.EqualTo(0.5).Within(0.0001));

        int win = EloModel.MatchDelta(1000, 1000, RankedMatchOutcome.Win, K);
        int loss = EloModel.MatchDelta(1000, 1000, RankedMatchOutcome.Loss, K);
        int draw = EloModel.MatchDelta(1000, 1000, RankedMatchOutcome.Draw, K);

        Assert.Multiple(() =>
        {
            Assert.That(win, Is.EqualTo(K / 2), "an even match is worth half the K-factor");
            Assert.That(loss, Is.EqualTo(-win), "and costs exactly the same when lost");
            Assert.That(draw, Is.EqualTo(0).Or.EqualTo(1).Or.EqualTo(-1), "an even draw barely moves anything");
        });
    }

    [Test]
    public void BeatingAStrongerCoach_IsWorthMoreThanBeatingAWeakerOne()
    {
        int upset = EloModel.MatchDelta(1000, 1400, RankedMatchOutcome.Win, K);
        int expected = EloModel.MatchDelta(1400, 1000, RankedMatchOutcome.Win, K);

        Assert.Multiple(() =>
        {
            Assert.That(upset, Is.GreaterThan(expected), "the underdog gains more for the same win");
            Assert.That(expected, Is.GreaterThan(0), "a favourite still gains something");
            Assert.That(EloModel.MatchDelta(1400, 1000, RankedMatchOutcome.Loss, K),
                Is.LessThan(EloModel.MatchDelta(1000, 1400, RankedMatchOutcome.Loss, K)),
                "and losing as the favourite costs more");
        });
    }

    [Test]
    public void DrawingWithAStrongerCoach_Gains_DrawingWithAWeakerOne_Costs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EloModel.MatchDelta(1000, 1400, RankedMatchOutcome.Draw, K), Is.GreaterThan(0));
            Assert.That(EloModel.MatchDelta(1400, 1000, RankedMatchOutcome.Draw, K), Is.LessThan(0));
        });
    }

    [Test]
    public void SeasonEndBonus_IsMonotonic_PositiveOnTop_NegativeAtTheBottom()
    {
        const int size = 8, swing = 40;
        var deltas = Enumerable.Range(1, size).Select(p => EloModel.SeasonEndDelta(p, size, swing)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(deltas[0], Is.EqualTo(swing), "the winner gets the full swing");
            Assert.That(deltas[^1], Is.EqualTo(-swing), "the last-placed loses it");
            for (int i = 1; i < deltas.Count; i++)
                Assert.That(deltas[i], Is.LessThan(deltas[i - 1]), $"position {i + 1} is worth less than {i}");
            Assert.That(EloModel.SeasonEndDelta(1, 1, swing), Is.EqualTo(0), "a one-club group has nobody to beat");
        });
    }

    [Test]
    public void TierMove_AddsForPromotion_SubtractsForRelegation_AndTheFloorHolds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EloModel.TierMoveDelta(RankedTierMove.Promotion, 60), Is.EqualTo(60));
            Assert.That(EloModel.TierMoveDelta(RankedTierMove.Relegation, 60), Is.EqualTo(-60));
            Assert.That(EloModel.TierMoveDelta(RankedTierMove.Stay, 60), Is.EqualTo(0));
            Assert.That(EloModel.Apply(120, -500, minRating: 100), Is.EqualTo(100), "the rating floor holds");
        });
    }

    [Test]
    public void Outcome_ReadsTheScoreline()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EloModel.OutcomeOf(2, 1), Is.EqualTo(RankedMatchOutcome.Win));
            Assert.That(EloModel.OutcomeOf(1, 1), Is.EqualTo(RankedMatchOutcome.Draw));
            Assert.That(EloModel.OutcomeOf(0, 3), Is.EqualTo(RankedMatchOutcome.Loss));
        });
    }
}

/// <summary>
/// Integration base for the seasonal loop (Phase 9.3). The pyramid is shrunk to 4-club groups (one per tier)
/// and — the important bit — the whole placement cohort is sorted into tier 2, so the tier-2 division is
/// 100% human: every match rates two real coaches and the group's champion/last-placed are guaranteed to be
/// coaches, which makes promotion and relegation deterministic to assert.
///
/// The calendar is compressed to 0s between matchdays and 0s of between-seasons break, so one tick = one
/// matchday and the reset lands on the tick after a season closes. Each test gets its own in-memory SQLite
/// ladder.
/// </summary>
public abstract class RankedLadderTestBase
{
    protected const string ValidPassword = "Password1";
    protected const int GroupSize = 4;

    private AuthTestFactory _root = null!;
    protected WebApplicationFactory<Program> App = null!;
    protected HttpClient Client = null!;

    /// <summary>Matchday spacing. 0 = every matchday is due at once (one tick resolves one round).</summary>
    protected virtual int MatchdayIntervalSeconds => 0;

    [SetUp]
    public void SetUp()
    {
        _root = new AuthTestFactory();
        App = _root.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ranked:GroupSize"] = GroupSize.ToString(),
                ["Ranked:PlacementGroupSize"] = GroupSize.ToString(),
                ["Ranked:Tier1Groups"] = "1",
                ["Ranked:Tier2Groups"] = "1",
                ["Ranked:Tier3Groups"] = "1",
                // The whole cohort lands in tier 2 → an all-human division, so P/R is deterministic.
                ["Ranked:PlacementTopPositionsToUpperTier"] = GroupSize.ToString(),
                ["Ranked:MatchdayIntervalSeconds"] = MatchdayIntervalSeconds.ToString(),
                ["Ranked:MarketWindowDurationSeconds"] = "3600",
                ["Ranked:SeasonBreakSeconds"] = "0",      // no waiting between seasons in a test
                ["Ranked:PromotionSlots"] = "1",
                ["Ranked:RelegationSlots"] = "1",
            })));
        Client = App.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        Client.Dispose();
        App.Dispose();
        _root.Dispose();
    }

    // --- HTTP helpers ------------------------------------------------------------------------------

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

    protected async Task<RankedStateDto> GetMine(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/me", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
    }

    protected async Task<RankedSeasonDto?> GetSeason(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/season", token);
        var resp = await Client.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.OK) return null;
        return await resp.Content.ReadFromJsonAsync<RankedSeasonDto>();
    }

    protected async Task<RankedLeaderboardDto> GetLeaderboard(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/leaderboard", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedLeaderboardDto>())!;
    }

    protected async Task<RankedPalmaresDto> GetPalmares(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/palmares", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedPalmaresDto>())!;
    }

    protected async Task<RankedTickSummary> Tick()
    {
        var resp = await Client.PostAsync("/internal/ranked/tick", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedTickSummary>())!;
    }

    protected async Task<RankedTickSummary> FastForward(int matchdays)
    {
        var resp = await Client.PostAsync($"/internal/ranked/fast-forward?matchdays={matchdays}", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedTickSummary>())!;
    }

    /// <summary>Enrols a full placement cohort and ticks until every coach has been sorted into a division.</summary>
    protected async Task<List<string>> EnrolAndPlaceCohort(int maxTicks = 40)
    {
        var tokens = new List<string>(GroupSize);
        for (int i = 0; i < GroupSize; i++)
        {
            var token = await RegisterAccount();
            using var req = Authed(HttpMethod.Post, "/ranked/enrol", token);
            var resp = await Client.SendAsync(req);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            tokens.Add(token);
        }

        for (int i = 0; i < maxTicks; i++)
        {
            await Tick();
            if ((await GetMine(tokens[0])).Status == RankedCoachStatus.Placed) break;
        }

        foreach (var t in tokens)
            Assert.That((await GetMine(t)).Status, Is.EqualTo(RankedCoachStatus.Placed),
                "the placement season sorted every coach into a division");
        return tokens;
    }

    /// <summary>Ticks until the caller's current division season is complete, and returns it.</summary>
    protected async Task<RankedSeasonDto> RunSeasonToCompletion(string token, int maxTicks = 40)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            await Tick();
            var season = await GetSeason(token);
            if (season?.State is { SeasonComplete: true, Kind: RankedGroupKind.Division }) return season;
        }
        Assert.Fail("the division season did not complete within the tick budget");
        return null!;
    }

    // --- DB helpers (the ladder's own state, not exposed over HTTP) --------------------------------

    protected IServiceScope Scope() => App.Services.CreateScope();

    /// <summary>The club strengths of the group a coach sits in, and how far apart they are (the fairness
    /// measure: the reset re-equalises the squads, so this spread collapses).</summary>
    protected async Task<SquadShape> GroupSquadShapeAsync(string token)
    {
        var state = await GetMine(token);
        Assert.That(state.GroupId, Is.Not.Null);

        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();

        var group = await db.RankedGroups.FirstAsync(g => g.Id == state.GroupId!.Value);
        var clubs = await db.Clubs.Where(c => c.WorldId == group.WorldId).Include(c => c.Players).ToListAsync();
        int freeAgents = await db.Players.CountAsync(p => p.WorldId == group.WorldId && p.ClubId == null);

        var strengths = clubs.Select(c => c.Strength).OrderByDescending(x => x).ToList();
        return new SquadShape(
            Spread: strengths.Count == 0 ? 0 : strengths[0] - strengths[^1],
            Strengths: strengths,
            Budgets: clubs.Select(c => c.TransferBudget).ToList(),
            SquadSizes: clubs.Select(c => c.Players.Count).ToList(),
            FreeAgents: freeAgents,
            SeasonNumber: group.SeasonNumber);
    }

    /// <summary>What a group's squads look like right now — the fairness picture the reset is judged on.</summary>
    protected sealed record SquadShape(
        int Spread, List<int> Strengths, List<long> Budgets, List<int> SquadSizes, int FreeAgents, int SeasonNumber);
}

/// <summary>
/// THE 9.3 ✅: two consecutive seasons on the ladder — ratings track results, promotion and relegation are
/// applied, and season 2 starts from fresh, fair squads. Everything is driven purely by calendar ticks, over
/// HTTP, exactly as the live scheduler drives it.
/// </summary>
[TestFixture]
public class RankedLadderSeasonTests : RankedLadderTestBase
{
    [Test]
    public async Task TwoSeasons_RatingsFollowResults_PromotionAndRelegationApply_AndSeason2IsFairAgain()
    {
        var tokens = await EnrolAndPlaceCohort();

        // Everyone landed in the same all-human tier-2 division.
        var placed = new List<RankedStateDto>();
        foreach (var t in tokens) placed.Add(await GetMine(t));
        Assert.That(placed.Select(p => p.GroupId).Distinct().Count(), Is.EqualTo(1),
            "the shrunk pyramid puts the whole cohort in one division");
        Assert.That(placed.All(p => p.Tier == 2), Is.True);

        var ratingBefore = new Dictionary<string, int>();
        var clubOf = new Dictionary<string, int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            ratingBefore[tokens[i]] = placed[i].Rating;
            clubOf[tokens[i]] = placed[i].ClubExternalId ?? 0;
        }

        // --- season 1 -----------------------------------------------------------------------------
        var finished = await RunSeasonToCompletion(tokens[0]);
        var table = finished.Standings;
        Assert.That(table, Has.Count.EqualTo(GroupSize));

        var before = await GroupSquadShapeAsync(tokens[0]);
        Assert.That(before.SeasonNumber, Is.EqualTo(1), "that was the group's first season");

        // Who finished where (every club in this group is held by one of our coaches).
        string championToken = TokenAt(table, clubOf, tokens, position: 1);
        string lastToken = TokenAt(table, clubOf, tokens, position: GroupSize);

        var ratingAfter = new Dictionary<string, int>();
        foreach (var t in tokens) ratingAfter[t] = (await GetMine(t)).Rating;

        int championDelta = ratingAfter[championToken] - ratingBefore[championToken];
        int lastDelta = ratingAfter[lastToken] - ratingBefore[lastToken];
        TestContext.Out.WriteLine(
            $"[ladder-rating] champion {ratingBefore[championToken]}→{ratingAfter[championToken]} ({championDelta:+#;-#;0}) | " +
            $"last {ratingBefore[lastToken]}→{ratingAfter[lastToken]} ({lastDelta:+#;-#;0})");

        Assert.Multiple(() =>
        {
            Assert.That(championDelta, Is.GreaterThan(lastDelta),
                "rating follows results: the group winner gains more than the bottom club");
            Assert.That(championDelta, Is.GreaterThan(0), "winning your group is worth rating");
            Assert.That(lastDelta, Is.LessThan(0), "finishing last costs rating");
        });

        // The palmarès recorded it (append-only history).
        var championPalmares = await GetPalmares(championToken);
        Assert.Multiple(() =>
        {
            Assert.That(championPalmares.SeasonsPlayed, Is.EqualTo(1));
            Assert.That(championPalmares.Titles, Is.EqualTo(1), "the group winner has a title");
            Assert.That(championPalmares.Awards.Any(a => a.Kind == RankedAwardKind.SeasonPlayed), Is.True);
            Assert.That(championPalmares.Awards.Any(a => a.Kind == RankedAwardKind.Promotion), Is.True,
                "winning tier 2 earns a promotion");
            Assert.That(championPalmares.Rating, Is.EqualTo(ratingAfter[championToken]));
        });

        // --- the seasonal reset -------------------------------------------------------------------
        var reset = await Tick();   // the break is 0s, so the reset lands on the tick after the season closed
        Assert.That(reset.SeasonsReset, Is.GreaterThan(0), "the finished group was reset");

        var championState = await GetMine(championToken);
        var lastState = await GetMine(lastToken);
        Assert.Multiple(() =>
        {
            Assert.That(championState.Tier, Is.EqualTo(1), "the winner is promoted to the tier above");
            Assert.That(lastState.Tier, Is.EqualTo(3), "the bottom club is relegated to the tier below");
            Assert.That(championState.GroupId, Is.Not.EqualTo(lastState.GroupId));
        });

        // Divisions never change size, however many coaches move through them (the 9.1 invariant).
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
            var groups = await db.RankedGroups.Where(g => g.Kind == RankedGroupKind.Division).ToListAsync();
            foreach (var g in groups)
            {
                int seats = await db.RankedSeats.CountAsync(s => s.RankedGroupId == g.Id);
                Assert.That(seats, Is.EqualTo(g.Capacity), $"{g.Name} still has exactly {g.Capacity} seats");
            }
        }

        // --- season 2 -----------------------------------------------------------------------------
        // A coach who stayed put sees their group reopen with a brand-new season.
        string stayedToken = tokens.First(t => t != championToken && t != lastToken);

        // The group's fixtures were wiped by the reset, so the first tick that reports a season again IS
        // season 2 (with a compressed calendar its opening matchday resolves on that same tick).
        RankedSeasonDto? season2 = null;
        for (int i = 0; i < 5; i++)
        {
            await Tick();
            var s = await GetSeason(stayedToken);
            if (s is { InSeason: true, State: { SeasonComplete: false } }) { season2 = s; break; }
        }
        Assert.That(season2, Is.Not.Null, "the group reopened and the next season started on the calendar");

        var after = await GroupSquadShapeAsync(stayedToken);
        TestContext.Out.WriteLine(
            $"[ladder-reset] strength spread {before.Spread} → {after.Spread} | strengths [{string.Join(",", after.Strengths)}] " +
            $"| squads [{string.Join(",", after.SquadSizes)}] | free agents {after.FreeAgents}");

        Assert.Multiple(() =>
        {
            Assert.That(after.SeasonNumber, Is.EqualTo(2), "the group is on its second season");
            Assert.That(after.Spread, Is.LessThanOrEqualTo(Math.Max(before.Spread, 3)),
                "the reset re-equalises the squads — season 2 starts at least as fair as season 1 did");
            // The world is generated at RANDOM per run (the coaches are fresh GUID accounts), so the
            // exact residual spread varies: the serpentine equaliser minimises it but cannot zero it,
            // because integer-truncated 22-man averages and role-tier gaps leave a few points. The
            // bound only has to prove equalisation WORKED - an un-equalised world spans four times
            // wider, and the relative check above is the one that carries the meaning. ≤3 flaked on
            // 2026-09-11 with a reading of 4 (it started from a wider pool that run: 22 against 16);
            // this is the same lesson, and the same number, the draft test already learned in
            // LeagueEndpointTests ("[draft-equal] ... ≤2 was too tight and flaked on CI"). See
            // SquadEqualizer.
            Assert.That(after.Spread, Is.LessThanOrEqualTo(6),
                "equal-strength squads: the serpentine redistribution leaves only a few points between clubs");
            Assert.That(after.SquadSizes.Distinct().Count(), Is.EqualTo(1), "and every club fields the same number of players");
            Assert.That(after.Budgets.Distinct().Count(), Is.EqualTo(1), "everyone starts the season on the same budget");
            Assert.That(after.FreeAgents, Is.GreaterThan(0), "there is a free-agent pool for the new season's auction");
            Assert.That(season2!.State!.TotalRounds, Is.EqualTo(2 * (GroupSize - 1)),
                "a full double round-robin was scheduled again");
            Assert.That(season2.State.RoundsPlayed, Is.LessThanOrEqualTo(1), "the new season has only just kicked off");
        });
    }

    [Test]
    public async Task Leaderboard_RanksEveryCoach_AndMarksTheCaller()
    {
        var tokens = await EnrolAndPlaceCohort();
        await RunSeasonToCompletion(tokens[0]);

        var board = await GetLeaderboard(tokens[0]);

        Assert.Multiple(() =>
        {
            Assert.That(board.TotalCoaches, Is.EqualTo(GroupSize));
            Assert.That(board.Entries, Has.Count.EqualTo(GroupSize));
            Assert.That(board.Entries.Select(e => e.Rating), Is.Ordered.Descending, "best rating first");
            Assert.That(board.Entries.Select(e => e.Rank), Is.EqualTo(Enumerable.Range(1, GroupSize)));
            Assert.That(board.Entries.Count(e => e.IsYou), Is.EqualTo(1), "the caller's own row is marked");
            Assert.That(board.You, Is.Not.Null);
            Assert.That(board.Entries.All(e => e.SeasonsPlayed == 1), Is.True);
        });
    }

    [Test]
    public async Task Palmares_RequiresEnrolment_AndLeaderboardRequiresAToken()
    {
        var stranger = await RegisterAccount();

        using (var req = Authed(HttpMethod.Get, "/ranked/palmares", stranger))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                "an account that never joined has no palmarès");

        var resp = await Client.GetAsync("/ranked/leaderboard");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task OptingOutOfAutoEnrol_LeavesTheLadderAtTheReset_KeepingRatingAndPalmares()
    {
        var tokens = await EnrolAndPlaceCohort();
        string quitter = tokens[0];

        using (var req = Authed(HttpMethod.Post, "/ranked/auto-enrol", quitter, new SetAutoEnrolRequest(false)))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await RunSeasonToCompletion(tokens[1]);
        int ratingAtSeasonEnd = (await GetMine(quitter)).Rating;

        await Tick();   // the reset releases coaches who opted out

        var after = await GetMine(quitter);
        var palmares = await GetPalmares(quitter);

        Assert.Multiple(() =>
        {
            Assert.That(after.Status, Is.EqualTo(RankedCoachStatus.Retired), "they left the ladder");
            Assert.That(after.Rating, Is.EqualTo(ratingAtSeasonEnd), "their rating is untouched");
            Assert.That(palmares.SeasonsPlayed, Is.EqualTo(1), "and their record stands");
            Assert.That(palmares.Awards, Is.Not.Empty);
        });
    }

    /// <summary>The token of the coach whose club sits at <paramref name="position"/> of the final table.</summary>
    private static string TokenAt(
        IReadOnlyList<RankedStandingDto> table, IReadOnlyDictionary<string, int> clubOf,
        IReadOnlyList<string> tokens, int position)
    {
        int clubExternalId = table[position - 1].ClubExternalId;
        var token = tokens.FirstOrDefault(t => clubOf[t] == clubExternalId);
        Assert.That(token, Is.Not.Null, $"position {position} is held by one of the cohort's coaches");
        return token!;
    }
}

/// <summary>
/// The dev-sim fast-forward (Phase 9.3 tooling): with a REAL calendar spacing, a plain tick resolves at most
/// the one matchday that is due, while the fast-forward time-travels the ladder so a solo tester can watch a
/// whole season — and the season after the reset — play out in seconds. Without this, testing the seasonal
/// loop alone would take the real two weeks.
/// </summary>
[TestFixture]
public class RankedFastForwardTests : RankedLadderTestBase
{
    /// <summary>An hour between matchdays: nothing but matchday 1 is due on a plain tick.</summary>
    protected override int MatchdayIntervalSeconds => 3600;

    [Test]
    public async Task FastForward_AdvancesSeveralMatchdays_WhereAPlainTickWouldOnlyResolveTheDueOne()
    {
        for (int i = 0; i < GroupSize; i++)
        {
            var token = await RegisterAccount();
            using var req = Authed(HttpMethod.Post, "/ranked/enrol", token);
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var first = await Tick();                 // starts the season + resolves the one due matchday
        var second = await Tick();                // nothing else is due for another hour
        var forwarded = await FastForward(4);     // four hours of ladder in one call

        Assert.Multiple(() =>
        {
            Assert.That(first.MatchdaysResolved, Is.EqualTo(1), "matchday 1 kicks off at season start");
            Assert.That(second.MatchdaysResolved, Is.EqualTo(0), "the real calendar holds the next one back");
            Assert.That(forwarded.MatchdaysResolved, Is.GreaterThanOrEqualTo(3),
                "the fast-forward pulls the clock along, matchday by matchday");
        });
    }
}
