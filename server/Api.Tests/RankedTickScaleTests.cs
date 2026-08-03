using System.Net;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The calendar tick under LOAD-SHAPED conditions (Phase 9.6). The load test found that a tick's cost per
/// fixture grew with the number of groups on the ladder — 44ms per fixture with 25 groups, 1.16s with 125 —
/// because every group's work (a reconstructed world, a simulated matchday, every player written back, a
/// full match report serialized) piled up in one change tracker. The fix gives each group its own unit of
/// work; these tests pin the behaviour that must survive it.
///
/// They deliberately do NOT assert timings: a wall-clock threshold on a shared machine is a coin flip. What
/// they assert is that the work is still CORRECT once the tracked graph is dropped between groups — which is
/// the only way that optimisation could have broken anything.
/// </summary>
[TestFixture]
public class RankedTickScaleTests : RankedSeasonTestBase
{
    /// <summary>
    /// Several cohorts on the ladder at once: one tick has to resolve EVERY group's matchday and persist
    /// each one's evolved world. If clearing the change tracker between groups dropped a write, a later
    /// group would come back with an unplayed matchday or an unchanged world-state hash.
    /// </summary>
    [Test]
    public async Task OneTick_ResolvesEveryGroupsMatchday_AndPersistsEachWorld()
    {
        const int cohorts = 3;

        var leadTokens = new List<string>(cohorts);
        for (int c = 0; c < cohorts; c++)
            leadTokens.Add((await EnrolCohort())[0]);

        // The first tick starts every season (and, with the compressed calendar, resolves matchday 1).
        var first = await Tick();
        Assert.That(first.SeasonsStarted, Is.EqualTo(cohorts), "every full cohort started its season");

        var hashesBefore = new List<string>(cohorts);
        foreach (var token in leadTokens)
        {
            var (code, season) = await GetSeason(token);
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(season!.InSeason, Is.True, "each cohort is in season after the first tick");
            hashesBefore.Add(season.State!.StateHashHex);
        }

        var second = await Tick();
        Assert.That(second.MatchdaysResolved, Is.EqualTo(cohorts),
            "one tick resolves the due matchday of EVERY group, not just the first");

        for (int c = 0; c < cohorts; c++)
        {
            var (code, season) = await GetSeason(leadTokens[c]);
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));

            var state = season!.State!;
            int playedFixtures = season.Fixtures.Count(f => f.Played);

            Assert.Multiple(() =>
            {
                Assert.That(state.RoundsPlayed, Is.GreaterThanOrEqualTo(2),
                    $"cohort {c}: both matchdays were stored");
                Assert.That(playedFixtures, Is.EqualTo(state.RoundsPlayed * (GroupSize / 2)),
                    $"cohort {c}: every fixture of every played round was written");
                Assert.That(state.StateHashHex, Is.Not.EqualTo(hashesBefore[c]),
                    $"cohort {c}: the evolved condition/development was persisted, not lost with the tracker");
                Assert.That(season.Standings.Sum(s => s.Played), Is.EqualTo(playedFixtures * 2),
                    $"cohort {c}: the table reconciles with the played fixtures");
            });
        }
    }

    /// <summary>
    /// Two ticks at once must not resolve the same matchday twice. Before the 9.6 gate the recurring job and
    /// the dev endpoints were separate paths into the same engine, and the load run had them overlapping — a
    /// double resolution double-counts a coach's rating and evolves a world twice. The invariant is
    /// arithmetic rather than timing-based: the matchdays REPORTED across the concurrent ticks must match the
    /// fixtures actually stored.
    /// </summary>
    [Test]
    public async Task ConcurrentTicks_DoNotResolveTheSameMatchdayTwice()
    {
        var tokens = await EnrolCohort();
        var first = await Tick(); // starts the season and resolves matchday 1

        var runs = await Task.WhenAll(Tick(), Tick(), Tick());
        int reported = runs.Sum(r => r.MatchdaysResolved) + first.MatchdaysResolved;

        var (code, season) = await GetSeason(tokens[0]);
        Assert.That(code, Is.EqualTo(HttpStatusCode.OK));

        int playedFixtures = season!.Fixtures.Count(f => f.Played);
        int roundsPlayed = season.State!.RoundsPlayed;

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.EqualTo(roundsPlayed),
                "every matchday a tick reported is a matchday that actually exists in the schedule");
            Assert.That(playedFixtures, Is.EqualTo(roundsPlayed * (GroupSize / 2)),
                "no round was resolved twice or half-written");
        });
    }
}

/// <summary>
/// The optional per-tick cap (Phase 9.6). It is 0 = off everywhere else — the calendar's original behaviour,
/// which the rest of the ranked suite relies on — so it gets its own fixture.
/// </summary>
[TestFixture]
public class RankedCappedTickTests : RankedSeasonTestBase
{
    protected override void ConfigureExtra(IDictionary<string, string?> settings) =>
        settings["Ranked:MaxMatchdaysPerTick"] = "1";

    /// <summary>A capped tick bounds one run without losing work OR starving anyone: each run takes the
    /// group that has waited longest, so three runs serve three groups rather than the same one three
    /// times.</summary>
    [Test]
    public async Task ACappedTick_ServesTheGroupsInTurn_InsteadOfStarvingThem()
    {
        const int cohorts = 3;

        var leadTokens = new List<string>(cohorts);
        for (int c = 0; c < cohorts; c++)
            leadTokens.Add((await EnrolCohort())[0]);

        // The first run starts every season and, being capped, resolves exactly one group's matchday.
        var first = await Tick();
        Assert.Multiple(() =>
        {
            Assert.That(first.SeasonsStarted, Is.EqualTo(cohorts), "the cap never holds back a season start");
            Assert.That(first.MatchdaysResolved, Is.EqualTo(1), "the cap holds a single run to one matchday");
        });

        var second = await Tick();
        var third = await Tick();
        Assert.That(second.MatchdaysResolved + third.MatchdaysResolved, Is.EqualTo(2),
            "each further run resolves exactly one more matchday");

        foreach (var token in leadTokens)
        {
            var (code, season) = await GetSeason(token);
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(season!.State!.RoundsPlayed, Is.EqualTo(1),
                "three capped runs served the three groups in turn, not one group three times");
        }
    }
}
