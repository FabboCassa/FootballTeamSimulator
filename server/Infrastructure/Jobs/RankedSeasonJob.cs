using Fts.Application.Ranked;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Fts.Infrastructure.Jobs;

/// <summary>
/// The recurring Hangfire job that drives the ranked ladder's real-time calendar (Phase 9.2): each run it
/// calls <see cref="IRankedSeasonService.TickAsync"/>, which starts any due season, resolves every group's
/// due matchday, opens the market windows and closes/sorts finished seasons. DI-activated (like
/// <see cref="HeartbeatJob"/>) so it can take the scoped season service. Frequent polling is fine — the
/// per-fixture kickoff times gate what actually fires, so a minutely tick just checks "is anything due yet".
/// </summary>
public sealed class RankedSeasonJob
{
    private readonly IRankedSeasonService _season;
    private readonly ILogger<RankedSeasonJob> _log;

    public RankedSeasonJob(IRankedSeasonService season, ILogger<RankedSeasonJob> log)
    {
        _season = season;
        _log = log;
    }

    public const string RecurringJobId = "fts-ranked-season";

    /// <summary>
    /// A tick must never overlap itself (Phase 9.6). The job is scheduled every minute, but a matchday run
    /// on a large ladder can take longer than that — the load test measured a run of several minutes — and
    /// two concurrent ticks would resolve the same due matchday twice over, or deadlock each other on the
    /// same rows. Hangfire's distributed lock is the right guard because it holds across API instances, not
    /// just inside one process. Waiting rather than skipping is deliberate: the tick is idempotent (nothing
    /// is due twice), so a queued run simply finds nothing left to do.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var s = await _season.TickAsync(ct);
        if (s.SeasonsStarted + s.MatchdaysResolved + s.PlacementsResolved + s.DivisionsCompleted + s.MarketWindowsOpened > 0)
            _log.LogInformation(
                "[ranked-season] tick: started {Started}, matchdays {Matchdays} ({Fixtures} fixtures), " +
                "placements {Placements}, divisions completed {Completed}, windows opened {Windows}.",
                s.SeasonsStarted, s.MatchdaysResolved, s.FixturesResolved,
                s.PlacementsResolved, s.DivisionsCompleted, s.MarketWindowsOpened);
    }
}
