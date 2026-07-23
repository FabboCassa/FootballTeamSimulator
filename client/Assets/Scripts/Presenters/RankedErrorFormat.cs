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
            _ => "ranked.error.server",
        };
    }
}
