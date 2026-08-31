using Fts.Services.Online;

namespace Fts.Presenters
{
    /// <summary>Maps an online-league API error to its localization key (Phase 8.1b), shared by the
    /// list/create/lobby presenters so the wording stays consistent.</summary>
    public static class LeagueErrorFormat
    {
        public static string Key(LeagueApiError error) => error switch
        {
            LeagueApiError.NotSignedIn => "leagues.error.not_signed_in",
            LeagueApiError.Network => "leagues.error.network",
            LeagueApiError.Validation => "leagues.error.validation",
            LeagueApiError.NotFound => "leagues.error.not_found",
            LeagueApiError.AlreadyMember => "leagues.error.already_member",
            LeagueApiError.LeagueFull => "leagues.error.full",
            LeagueApiError.NotJoinable => "leagues.error.not_joinable",
            LeagueApiError.Forbidden => "leagues.error.forbidden",
            LeagueApiError.WrongPhase => "leagues.error.wrong_phase",
            LeagueApiError.NotYourTurn => "leagues.error.not_your_turn",
            LeagueApiError.ClubUnavailable => "leagues.error.club_unavailable",
            LeagueApiError.TooFewMembers => "leagues.error.too_few_members",
            LeagueApiError.NotAssignedClub => "leagues.error.not_assigned_club",
            LeagueApiError.NothingToResolve => "leagues.error.nothing_to_resolve",
            LeagueApiError.ReplayNotReady => "leagues.error.replay_not_ready",
            LeagueApiError.AuctionNotFound => "leagues.error.auction_not_found",
            LeagueApiError.AuctionClosed => "leagues.error.auction_closed",
            LeagueApiError.BidTooLow => "leagues.error.bid_too_low",
            LeagueApiError.InsufficientBudget => "leagues.error.insufficient_budget",
            LeagueApiError.WindowAlreadyOpen => "leagues.error.window_already_open",
            LeagueApiError.NoAuctionsOpen => "leagues.error.no_auctions_open",
            LeagueApiError.LiveMatchNotFound => "leagues.error.live_match_not_found",
            LeagueApiError.LiveMatchNotJoinable => "leagues.error.live_match_not_joinable",
            LeagueApiError.NotYourSide => "leagues.error.not_your_side",
            LeagueApiError.LiveMatchNotLive => "leagues.error.live_match_not_live",
            LeagueApiError.LiveMatchAlreadyFinished => "leagues.error.live_match_already_finished",
            LeagueApiError.InvalidLiveChange => "leagues.error.invalid_live_change",
            LeagueApiError.MarketClosed => "leagues.error.market_closed",
            LeagueApiError.OfferNotFound => "leagues.error.offer_not_found",
            LeagueApiError.OfferResolved => "leagues.error.offer_resolved",
            LeagueApiError.PlayerUnavailable => "leagues.error.player_unavailable",
            LeagueApiError.SquadTooSmall => "leagues.error.squad_too_small",
            LeagueApiError.SquadFull => "leagues.error.squad_full",
            LeagueApiError.IntegrityBlocked => "leagues.error.integrity_blocked",
            LeagueApiError.ReplayTooOld => "leagues.error.replay_too_old",
            _ => "leagues.error.server",
        };
    }
}
