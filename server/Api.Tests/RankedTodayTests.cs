using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Ranked;
using NUnit.Framework;
using Sim.Core.Development;
using Sim.Core.Match;

namespace Fts.Api.Tests;

/// <summary>
/// The ranked DAILY LOOP (Phase 9.4): the one-screen digest, the one-tap matchday confirmation, and the two
/// smart defaults that keep a coach competitive without visiting a screen (a seeded + repaired lineup, and a
/// stored training plan). Reuses <see cref="RankedSeasonTestBase"/>'s shrunk pyramid with the compressed
/// calendar, so a placement cohort fills ONE all-human group and a single tick starts the season and resolves
/// its first matchday.
/// </summary>
[TestFixture]
public class RankedTodayTests : RankedSeasonTestBase
{
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<RankedTodayDto> Today(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/today", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedTodayDto>())!;
    }

    private async Task<HttpResponseMessage> Confirm(string token)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/today/confirm", token);
        return await Client.SendAsync(req);
    }

    private async Task<LineupPlan?> StoredLineup(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/lineup", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string json = await resp.Content.ReadAsStringAsync();
        var plan = JsonSerializer.Deserialize<LineupPlan>(json, PlanJson);
        return plan is { Slots.Count: > 0 } ? plan : null;
    }

    private async Task<string> StoredTrainingJson(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/training", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<RankedSquadDto> Squad(string token, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/clubs/{clubExternalId}/squad", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedSquadDto>())!;
    }

    private static bool HasTodo(RankedTodayDto d, RankedTodoKind kind) => d.Todo.Any(t => t.Kind == kind);

    /// <summary>A fee the 9.5 collusion guard is happy with: the player's own market value, floored at the
    /// minimum a transfer can be. These tests used to offer a flat 1M for whoever they needed, which is
    /// exactly the "gifted star" the guard now refuses — the offers here are incidental to what is being
    /// tested (the digest and the lineup repair), so they simply pay the going rate.</summary>
    private static long FairFee(RankedPlayerDto player) => Math.Max(25_000, player.MarketValue);

    /// <summary>A full cohort + one tick: the season starts and its first matchday resolves.</summary>
    private async Task<List<string>> StartSeason()
    {
        var tokens = await EnrolCohort();
        await Tick();
        return tokens;
    }

    // --- the digest --------------------------------------------------------------------------------

    [Test]
    public async Task Digest_ForAnAccountThatNeverJoined_JustAsksThemToEnrol()
    {
        var stranger = await RegisterAccount();

        var today = await Today(stranger);

        Assert.Multiple(() =>
        {
            Assert.That(today.Enrolled, Is.False, "the digest answers for a signed-in account that never joined");
            Assert.That(today.InSeason, Is.False);
            Assert.That(today.ActionCount, Is.EqualTo(1));
            Assert.That(today.Todo.Single().Kind, Is.EqualTo(RankedTodoKind.Enrol));
        });
    }

    [Test]
    public async Task Digest_WhenEnrolledButTheCohortIsNotFull_HasNothingToDo()
    {
        var token = await RegisterAccount();
        await Enrol(token);

        var today = await Today(token);

        Assert.Multiple(() =>
        {
            Assert.That(today.Enrolled, Is.True);
            Assert.That(today.InSeason, Is.False, "the placement group has not kicked off yet");
            Assert.That(today.ActionCount, Is.EqualTo(0), "waiting for a cohort is not a chore");
        });
    }

    /// <summary>
    /// THE 9.4 ✅: an in-season coach opens ONE screen and sees their whole day — where they stand, the last
    /// result, the next kickoff, the market — and a single tap clears the only thing they had to do.
    /// </summary>
    [Test]
    public async Task Digest_InSeason_ShowsTheWholeDay_AndOneTapConfirmClearsTheChore()
    {
        var tokens = await StartSeason();
        string me = tokens[0];

        var before = await Today(me);
        Assert.Multiple(() =>
        {
            Assert.That(before.InSeason, Is.True);
            Assert.That(before.RoundsPlayed, Is.GreaterThanOrEqualTo(1), "the first matchday resolved on the tick");
            Assert.That(before.LastResult, Is.Not.Null, "you can see how the last match went");
            Assert.That(before.NextMatch, Is.Not.Null, "and who you play next");
            Assert.That(before.NextMatch!.OpponentClubName, Is.Not.Empty);
            Assert.That(before.YourPosition, Is.Not.Null, "and where that leaves you in the table");
            Assert.That(before.YourPosition, Is.InRange(1, GroupSize));
            Assert.That(before.ClubName, Is.Not.Null.And.Not.Empty);
            Assert.That(before.LineupReady, Is.True, "the smart default means your team is always ready to play");
            Assert.That(before.LineupConfirmed, Is.False, "…but the seeded default still asks for a nod");
            Assert.That(HasTodo(before, RankedTodoKind.ConfirmMatchday), Is.True);
        });

        var resp = await Confirm(me);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = (await resp.Content.ReadFromJsonAsync<RankedTodayDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(after.LineupConfirmed, Is.True, "one tap confirms the upcoming matchday");
            Assert.That(HasTodo(after, RankedTodoKind.ConfirmMatchday), Is.False, "…and the chore leaves the list");
            Assert.That(after.LineupReady, Is.True);
            Assert.That(after.TrainingSet, Is.True);
        });

        // Idempotent: confirming again changes nothing.
        var again = (await (await Confirm(me)).Content.ReadFromJsonAsync<RankedTodayDto>())!;
        Assert.That(again.LineupConfirmed, Is.True);
        Assert.That(again.ActionCount, Is.EqualTo(after.ActionCount), "a second confirm is a no-op");
    }

    [Test]
    public async Task Digest_ListsPendingOffersAsTheMostUrgentChore()
    {
        var tokens = await StartSeason();
        string buyer = tokens[0], seller = tokens[1];

        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        // Who is bought does not matter here — only that an offer is pending — so take the cheapest player
        // and pay what he is worth (see FairFee).
        var target = (await Squad(buyer, sellerClub)).Players
            .OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId).First();

        using (var req = Authed(HttpMethod.Post, "/ranked/offers", buyer,
                   new MakeRankedOfferRequest(target.ExternalId, FairFee(target))))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sellerDay = await Today(seller);
        var buyerDay = await Today(buyer);

        Assert.Multiple(() =>
        {
            Assert.That(sellerDay.IncomingOffers, Is.EqualTo(1));
            Assert.That(HasTodo(sellerDay, RankedTodoKind.RespondOffer), Is.True);
            // The top CHORE, which is what this test is about. Since task 12.3 a matchday being played right
            // now can sit above every chore — it is the one item on the list that EXPIRES — and on this
            // fixture's compressed calendar the kick-off is always moments away, so the live door is always
            // open. Skipping past it keeps the assertion measuring what it was written to measure.
            Assert.That(sellerDay.Todo.First(t => t.Kind != RankedTodoKind.WatchLive).Kind,
                Is.EqualTo(RankedTodoKind.RespondOffer),
                "answering another coach outranks your own housekeeping");
            Assert.That(buyerDay.OutgoingOffers, Is.EqualTo(1));
            Assert.That(HasTodo(buyerDay, RankedTodoKind.RespondOffer), Is.False, "your own offer is not your chore");
        });
    }

    // --- smart defaults ----------------------------------------------------------------------------

    [Test]
    public async Task SmartDefaults_SeedALineupAndABalancedTrainingPlan_WhenTheSeasonStarts()
    {
        var tokens = await StartSeason();
        string me = tokens[0];

        var lineup = await StoredLineup(me);
        string trainingJson = await StoredTrainingJson(me);
        var training = JsonSerializer.Deserialize<TrainingPlan>(trainingJson, PlanJson);
        var today = await Today(me);

        Assert.Multiple(() =>
        {
            Assert.That(lineup, Is.Not.Null, "a best-XI lineup is stored without the coach doing anything");
            Assert.That(lineup!.Slots, Has.Count.EqualTo(11));
            Assert.That(lineup.Slots.Select(s => s.PlayerId).Distinct().Count(), Is.EqualTo(11), "11 distinct players");
            Assert.That(training, Is.Not.Null, "and a training plan too");
            Assert.That(training!.TeamFocus, Is.EqualTo(TeamTrainingFocus.Balanced), "seeded balanced by default");
            Assert.That(today.TrainingSet, Is.True);
            Assert.That(today.TrainingTeamFocus, Is.EqualTo((int)TeamTrainingFocus.Balanced));
        });
    }

    /// <summary>
    /// THE OTHER 9.4 ✅: selling a starter mid-window does not leave a broken lineup behind. The server
    /// rebuilds it from the best available squad, so the coach's next matchday is never resolved by a silent
    /// fallback they did not choose.
    /// </summary>
    [Test]
    public async Task SmartDefault_RepairsTheStoredLineup_WhenAStarterIsSold()
    {
        var tokens = await StartSeason();
        string buyer = tokens[0], seller = tokens[1];

        var before = await StoredLineup(seller);
        Assert.That(before, Is.Not.Null, "the seller starts with the seeded best XI");

        // An outfield STARTER (never the goalkeeper slot) — the cheapest one, so that paying his full market
        // value both satisfies the 9.5 collusion guard and stays inside the seeded budget.
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        var squad = (await Squad(buyer, sellerClub)).Players.ToDictionary(p => p.ExternalId);
        var sold = before!.Slots.Skip(1)
            .Where(s => squad.ContainsKey(s.PlayerId))
            .Select(s => squad[s.PlayerId])
            .OrderBy(p => p.MarketValue).ThenBy(p => p.ExternalId)
            .First();
        int soldPlayer = sold.ExternalId;

        using (var req = Authed(HttpMethod.Post, "/ranked/offers", buyer,
                   new MakeRankedOfferRequest(soldPlayer, FairFee(sold))))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var req = Authed(HttpMethod.Get, "/ranked/offers", seller))
        {
            var offers = (await (await Client.SendAsync(req)).Content.ReadFromJsonAsync<RankedOffersDto>())!;
            var pending = offers.Incoming.Single(o => o.Status == RankedOfferStatus.Pending);
            using var accept = Authed(HttpMethod.Post, $"/ranked/offers/{pending.Id}/accept", seller);
            Assert.That((await Client.SendAsync(accept)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var after = await StoredLineup(seller);
        var today = await Today(seller);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Not.Null, "the seller still has a stored lineup");
            Assert.That(after!.Slots, Has.Count.EqualTo(11), "still a full XI");
            Assert.That(after.Slots.Any(s => s.PlayerId == soldPlayer), Is.False,
                "the sold player is gone from the stored lineup");
            Assert.That(after.Slots.Select(s => s.PlayerId).Distinct().Count(), Is.EqualTo(11));
            Assert.That(today.LineupReady, Is.True, "and the digest still reports the team as ready");
        });
    }

    [Test]
    public async Task Training_Submitted_ReplacesTheSeededDefault_AndSurvivesAMatchday()
    {
        var tokens = await StartSeason();
        string me = tokens[0];

        var plan = new TrainingPlan { TeamFocus = TeamTrainingFocus.Attacking };
        using (var req = Authed(HttpMethod.Post, "/ranked/training", me, new SubmitRankedTrainingRequest(plan)))
            Assert.That((await Client.SendAsync(req)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = JsonSerializer.Deserialize<TrainingPlan>(await StoredTrainingJson(me), PlanJson);
        Assert.That(stored!.TeamFocus, Is.EqualTo(TeamTrainingFocus.Attacking));

        await Tick();   // another matchday-week develops on the submitted plan

        var afterTick = await Today(me);
        Assert.Multiple(() =>
        {
            Assert.That(afterTick.TrainingSet, Is.True);
            Assert.That(afterTick.TrainingTeamFocus, Is.EqualTo((int)TeamTrainingFocus.Attacking),
                "the plan is reused week after week until the coach changes it");
        });
    }

    // --- guards ------------------------------------------------------------------------------------

    [Test]
    public async Task Confirm_GuardsNotEnrolledAndNoSeason()
    {
        var stranger = await RegisterAccount();
        Assert.That((await Confirm(stranger)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "not on the ladder → not_enrolled");

        await Enrol(stranger);
        Assert.That((await Confirm(stranger)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "enrolled but no season under way → wrong_phase");
    }

    [Test]
    public async Task Today_WithoutToken_IsUnauthorized()
    {
        Assert.That((await Client.GetAsync("/ranked/today")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Client.PostAsync("/ranked/today/confirm", content: null)).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
