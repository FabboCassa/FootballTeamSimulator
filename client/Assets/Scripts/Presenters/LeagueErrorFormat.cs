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
            _ => "leagues.error.server",
        };
    }
}
