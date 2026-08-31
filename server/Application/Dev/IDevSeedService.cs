namespace Fts.Application.Dev;

/// <summary>
/// Dev-only test-league seeding (dev tooling). Composes the existing auth / league / season / auction use
/// cases to build a ready-to-test online league in one call, drive minimal bot autopilot (bid, ready), and
/// clean up afterwards — so online features can be tested without hand-creating accounts and re-running the
/// draft. The Api maps these behind a dev gate (never in Production). Bots are deterministic accounts
/// (<c>bot{n}@dev.local</c>) reused across runs so the account table doesn't grow unbounded.
/// </summary>
public interface IDevSeedService
{
    /// <summary>Creates (or reuses) bot accounts, makes a league, joins the bots, and — for ToStatus
    /// Active — runs the whole snake draft, returning the league id + every member (bot tokens included).</summary>
    Task<DevSeedResult> SeedTestLeagueAsync(DevSeedRequest request, CancellationToken ct = default);

    /// <summary>Minimal auction autopilot: the league's bot members place legal ascending bids on the open
    /// lots for the given number of rounds (each round every not-currently-leading bot outbids in turn).</summary>
    Task<DevBotBidResult> BotBidAsync(Guid leagueId, DevBotBidRequest request, CancellationToken ct = default);

    /// <summary>Minimal season autopilot: mark every bot member ready (the all-ready round resolves once
    /// everyone — bots + the human — is ready).</summary>
    Task<DevBotReadyResult> BotReadyAsync(Guid leagueId, CancellationToken ct = default);

    /// <summary>Minimal live-match autopilot: the fixture's bot opponent joins the live session (kicking the
    /// match off once the human is present too) and optionally makes a substitution, so a single human can
    /// test live match control solo (Phase 8.6).</summary>
    Task<DevBotLiveResult> BotLiveAsync(
        Guid leagueId, Guid fixtureId, DevBotLiveRequest request, CancellationToken ct = default);

    /// <summary>Cleanup: log in the deterministic bots and leave every league they are in (disbanding a
    /// league when the last member leaves), so repeated dev runs don't pile up worlds.</summary>
    Task<DevResetResult> ResetAsync(int bots, CancellationToken ct = default);

    /// <summary>Fill the caller's forming ranked placement group with fresh bot coaches (Phase 9.2 dev
    /// tooling) so a solo human's placement cohort completes and its season can start on the calendar.</summary>
    Task<DevRankedFillResult> FillRankedAsync(DevRankedFillRequest request, CancellationToken ct = default);

    /// <summary>Seed a whole load-test cohort (Phase 9.6 dev tooling): create N accounts, enrol them all on
    /// the ladder and tick the calendar so their seasons are running with an open market window — then hand
    /// back the accounts WITH access tokens so the load generator can start hitting the API immediately
    /// instead of spending minutes registering and logging in over HTTP.</summary>
    Task<DevLoadSeedResult> SeedLoadCohortAsync(DevLoadSeedRequest request, CancellationToken ct = default);

    /// <summary>Ranked live-match autopilot (task 12.3 dev tooling): the fixture's OPPONENT — a bot coach
    /// holding the other club — opens the live session and optionally makes a substitution and confirms
    /// full-time, so a single human can see the other side of a live ranked match without a second
    /// account/device. Against a vacant AI seat there is nobody to log in as: the call says so and does
    /// nothing, because that side is already playing its stored orders.</summary>
    Task<DevRankedLiveResult> RankedBotLiveAsync(
        Guid fixtureId, DevRankedLiveRequest request, CancellationToken ct = default);

    /// <summary>Ranked market autopilot (Phase 9.2b dev tooling): the bot coaches in the group outbid on the
    /// open auction lots and answer the pending offers sent to them, so a solo human can see the market
    /// react (being outbid, an offer accepted/rejected) without a second account.</summary>
    Task<DevRankedBotMarketResult> RankedBotMarketAsync(
        Guid rankedGroupId, DevRankedBotMarketRequest request, CancellationToken ct = default);
}
