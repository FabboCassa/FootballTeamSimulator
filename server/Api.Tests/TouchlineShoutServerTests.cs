using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;
using Sim.Core.Random;

namespace Fts.Api.Tests;

/// <summary>
/// Touchline shouts on the server (watchable-match spec R11, issue #36): the online inputs carry
/// them, the stored plan formats round-trip them, a row stored before shouts existed reads as "no
/// shout", and a live shout reaches the authoritative re-simulation.
/// </summary>
[TestFixture]
public class TouchlineShoutServerTests
{
    // The options the league and ranked services store plans and live changes with.
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Test]
    public void ALiveChange_CarriesItsShout_AndAnOlderStoredChangeHasNone()
    {
        var changes = new List<LiveChange> { new(40, LiveSide.Away, null, null, TouchlineShout.PressHigh) };

        var back = JsonSerializer.Deserialize<List<LiveChange>>(JsonSerializer.Serialize(changes, PlanJson), PlanJson)!;
        Assert.That(back.Single().Shout, Is.EqualTo(TouchlineShout.PressHigh));
        Assert.That(back.Single().Side, Is.EqualTo(LiveSide.Away));

        var older = JsonSerializer.Deserialize<List<LiveChange>>(
            "[{\"FromMinute\":30,\"Side\":\"Home\",\"Lineup\":null,\"Tactic\":null}]", PlanJson)!;
        Assert.That(older.Single().Shout, Is.EqualTo(TouchlineShout.None));
    }

    [Test]
    public void TheLiveChangeRequests_BindAShout()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var league = JsonSerializer.Deserialize<SubmitLiveChangeRequest>(
            "{\"fromMinute\":50,\"shout\":4}", web)!;
        var ranked = JsonSerializer.Deserialize<SubmitRankedLiveChangeRequest>(
            "{\"fromMinute\":50,\"shout\":5}", web)!;
        var legacy = JsonSerializer.Deserialize<SubmitLiveChangeRequest>("{\"fromMinute\":50}", web)!;

        Assert.That(league.Shout, Is.EqualTo(TouchlineShout.Encourage));
        Assert.That(ranked.Shout, Is.EqualTo(TouchlineShout.Concentrate));
        Assert.That(legacy.Shout, Is.EqualTo(TouchlineShout.None));
    }

    [Test]
    public void APrematchPlanWithAShout_RoundTripsAsStored()
    {
        var plan = new PrematchPlan
        {
            Rules = { new PrematchRule { FromMinute = 75, When = ScoreSituation.Losing, Shout = TouchlineShout.AllForward } }
        };

        var back = JsonSerializer.Deserialize<PrematchPlan>(JsonSerializer.Serialize(plan, PlanJson), PlanJson)!;
        Assert.That(back.Rules.Single().Shout, Is.EqualTo(TouchlineShout.AllForward));
        Assert.That(back.Rules.Single().When, Is.EqualTo(ScoreSituation.Losing));
    }

    [Test]
    public void ALiveShout_ReachesTheReSimulation_AndLeavesThePrefixAlone()
    {
        var cfg = new BalanceConfig();
        League league = new LeagueGenerator().Generate(new Pcg32(20260826UL));
        Club home = league.Clubs[2];
        Club away = league.Clubs[6];
        ulong seed = FixtureSeed.For(777L, 3, home.Id, away.Id);

        MatchReport before = MatchResolver.ResolveLive(
            home, away, null, null, Array.Empty<LiveChange>(), seed, cfg).Report;
        MatchReport after = MatchResolver.ResolveLive(
            home, away, null, null,
            new[] { new LiveChange(50, LiveSide.Away, null, null, TouchlineShout.KeepBall) }, seed, cfg).Report;

        MatchEvent shout = after.Events.Single(e => e.Type == MatchEventType.Shout);
        Assert.Multiple(() =>
        {
            Assert.That(shout.Minute, Is.EqualTo(50));
            Assert.That(shout.ClubId, Is.EqualTo(away.Id));
            Assert.That(shout.Shout, Is.EqualTo(TouchlineShout.KeepBall));
            Assert.That(before.Events.Any(e => e.Type == MatchEventType.Shout), Is.False);
        });

        var early = (MatchReport r) => r.Events.Where(e => e.Minute < 45).Select(e => $"{e.Minute}:{e.Type}:{e.PlayerId}").ToList();
        Assert.That(early(after), Is.EqualTo(early(before)), "a shout re-rolls only the remainder");
    }
}
