using Fts.Application.Ranked;
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
