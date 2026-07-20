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

    /// <summary>Cleanup: log in the deterministic bots and leave every league they are in (disbanding a
    /// league when the last member leaves), so repeated dev runs don't pile up worlds.</summary>
    Task<DevResetResult> ResetAsync(int bots, CancellationToken ct = default);
}
