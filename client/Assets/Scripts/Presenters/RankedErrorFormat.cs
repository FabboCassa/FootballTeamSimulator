using Fts.Services.Online;

namespace Fts.Presenters
{
    /// <summary>Maps a ranked API error to its localization key (Phase 9.2), shared by the ranked
    /// presenters so the wording stays consistent.</summary>
    public static class RankedErrorFormat
    {
        public static string Key(RankedApiError error) => error switch
        {
            RankedApiError.NotSignedIn => "ranked.error.not_signed_in",
            RankedApiError.Network => "ranked.error.network",
            RankedApiError.Validation => "ranked.error.validation",
            RankedApiError.NotEnrolled => "ranked.error.not_enrolled",
            RankedApiError.NotFound => "ranked.error.not_found",
            RankedApiError.Forbidden => "ranked.error.forbidden",
            RankedApiError.WrongPhase => "ranked.error.wrong_phase",
            RankedApiError.NoCapacity => "ranked.error.no_capacity",
            RankedApiError.FixtureNotFound => "ranked.error.fixture_not_found",
            RankedApiError.ReplayNotReady => "ranked.error.replay_not_ready",
            RankedApiError.InsufficientBudget => "ranked.error.insufficient_budget",
            RankedApiError.AuctionNotFound => "ranked.error.auction_not_found",
            RankedApiError.AuctionClosed => "ranked.error.auction_closed",
            RankedApiError.BidTooLow => "ranked.error.bid_too_low",
            // Phase 9.5: the integrity guards a coach can actually run into.
            RankedApiError.IntegrityBlocked => "ranked.error.integrity_blocked",
            RankedApiError.DeadlinePassed => "ranked.error.deadline_passed",
            RankedApiError.RateLimited => "ranked.error.rate_limited",
            // Task 12.2: selling your own players by auction.
            RankedApiError.LotDurationInvalid => "ranked.error.lot_duration",
            RankedApiError.SquadTooSmall => "ranked.error.squad_too_small",
            RankedApiError.PlayerUnavailable => "ranked.error.player_unavailable",
            // Task 12.3: attending your match.
            RankedApiError.LiveNotFound => "ranked.error.live_not_found",
            RankedApiError.LiveNotOpen => "ranked.error.live_not_open",
            RankedApiError.NotYourMatch => "ranked.error.not_your_match",
            RankedApiError.InvalidLiveChange => "ranked.error.invalid_live_change",
            RankedApiError.LiveAlreadyFinished => "ranked.error.live_finished",
            RankedApiError.ReplayTooOld => "ranked.error.replay_too_old",
            _ => "ranked.error.server",
        };
    }
}
