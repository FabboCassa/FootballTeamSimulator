using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Sim.Core.Match;

namespace Fts.Services.Online
{
    /// <summary>
    /// The client's gateway to the private-league API (Phase 8.1b): create / join by invite code /
    /// leave / list-mine / detail. App-scope singleton built on <see cref="ApiClient"/> (which owns the
    /// rotating token store and does the authed sends). Every call needs a signed-in account — if the
    /// user isn't signed in it returns <see cref="LeagueApiError.NotSignedIn"/> without hitting the
    /// network. Failures come back as <see cref="LeagueApiResult{T}"/>; the presenter maps them to loc.
    /// </summary>
    public sealed class LeagueApiService
    {
        private readonly ApiClient _api;

        public LeagueApiService(ApiClient api) => _api = api;

        public bool IsSignedIn => _api.IsSignedIn;

        /// <summary>The signed-in account's id (or null), so a draft screen can tell whose turn it is (8.2b).</summary>
        public string CurrentUserId => _api.Profile?.UserId;

        public async UniTask<LeagueApiResult<LeagueDetailDto>> CreateAsync(string name, int size, LeagueMode mode)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new CreateLeagueBody { name = name, size = size, mode = (int)mode };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues", body);
            return ParseDetail(status, text, network);
        }

        public async UniTask<LeagueApiResult<LeagueDetailDto>> JoinAsync(string inviteCode)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new JoinLeagueBody { inviteCode = inviteCode };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/join", body);
            return ParseDetail(status, text, network);
        }

        public async UniTask<LeagueApiResult<LeagueDetailDto>> GetAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/leagues/" + leagueId);
            return ParseDetail(status, text, network);
        }

        public async UniTask<LeagueApiResult<List<LeagueSummaryDto>>> ListMineAsync()
        {
            if (!_api.IsSignedIn) return LeagueApiResult<List<LeagueSummaryDto>>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/leagues");
            if (!IsSuccess(status, network))
                return LeagueApiResult<List<LeagueSummaryDto>>.Fail(MapError(status, text, network));

            var list = TryParse<List<LeagueSummaryDto>>(text);
            return list == null
                ? LeagueApiResult<List<LeagueSummaryDto>>.Fail(LeagueApiError.Server)
                : LeagueApiResult<List<LeagueSummaryDto>>.Ok(list);
        }

        /// <summary>Starts the season snake draft (creator only) — equalises squads + budgets and opens
        /// the pick order (8.2b).</summary>
        public async UniTask<LeagueApiResult<LeagueDetailDto>> StartDraftAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/draft/start");
            return ParseDetail(status, text, network);
        }

        /// <summary>Claims a club during the draft (only on your turn, only an unclaimed club) (8.2b).</summary>
        public async UniTask<LeagueApiResult<LeagueDetailDto>> PickClubAsync(string leagueId, int clubExternalId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new PickClubBody { clubExternalId = clubExternalId };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/draft/pick", body);
            return ParseDetail(status, text, network);
        }

        public async UniTask<LeagueApiResult<bool>> LeaveAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<bool>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/leave");
            return IsSuccess(status, network)
                ? LeagueApiResult<bool>.Ok(true)
                : LeagueApiResult<bool>.Fail(MapError(status, text, network));
        }

        // ---------------------------------------------------------------- season (8.3b)

        /// <summary>The season view: state + fixtures + standings (members only).</summary>
        public async UniTask<LeagueApiResult<LeagueSeasonDto>> GetSeasonAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueSeasonDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/leagues/" + leagueId + "/season");
            return ParseSeason(status, text, network);
        }

        /// <summary>Mark (or clear) ready. When everyone is ready the next round resolves server-side.</summary>
        public async UniTask<LeagueApiResult<LeagueSeasonDto>> SetReadyAsync(string leagueId, bool ready)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueSeasonDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new SetReadyBody { ready = ready };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/ready", body);
            return ParseSeason(status, text, network);
        }

        /// <summary>Force the next round to resolve now (creator only).</summary>
        public async UniTask<LeagueApiResult<LeagueSeasonDto>> AdvanceAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LeagueSeasonDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/advance");
            return ParseSeason(status, text, network);
        }

        /// <summary>Submit (or replace) the caller's match inputs for their club. <paramref name="body"/>
        /// is the { lineup, tactic, plan } wrapper the presenter builds from the Sim.Core plans (8.3b);
        /// on success the server returns the updated season state.</summary>
        public async UniTask<LeagueApiResult<SeasonStateDto>> SubmitInputsAsync(string leagueId, object body)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<SeasonStateDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/lineup", body);
            if (!IsSuccess(status, network))
                return LeagueApiResult<SeasonStateDto>.Fail(MapError(status, text, network));
            var dto = TryParse<SeasonStateDto>(text);
            return dto == null
                ? LeagueApiResult<SeasonStateDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<SeasonStateDto>.Ok(dto);
        }

        /// <summary>The stored full MatchReport for a played fixture (served identically to every member),
        /// deserialized into the Sim.Core <see cref="MatchReport"/> so the caller renders the replay with
        /// the existing MatchRenderer. Fails if the report has no position stream (not renderable).</summary>
        public async UniTask<LeagueApiResult<MatchReport>> GetReplayReportAsync(string leagueId, string fixtureId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<MatchReport>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "GET", "/leagues/" + leagueId + "/fixtures/" + fixtureId + "/replay");
            if (!IsSuccess(status, network))
                return LeagueApiResult<MatchReport>.Fail(MapError(status, text, network));

            var report = TryParse<MatchReport>(text);
            return report?.Positions == null
                ? LeagueApiResult<MatchReport>.Fail(LeagueApiError.Server)
                : LeagueApiResult<MatchReport>.Ok(report);
        }

        // ---------------------------------------------------------------- helpers

        private static LeagueApiResult<LeagueSeasonDto> ParseSeason(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return LeagueApiResult<LeagueSeasonDto>.Fail(MapError(status, text, network));
            var dto = TryParse<LeagueSeasonDto>(text);
            return dto?.season == null
                ? LeagueApiResult<LeagueSeasonDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<LeagueSeasonDto>.Ok(dto);
        }

        private static LeagueApiResult<LeagueDetailDto> ParseDetail(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return LeagueApiResult<LeagueDetailDto>.Fail(MapError(status, text, network));

            var dto = TryParse<LeagueDetailDto>(text);
            return dto?.league == null
                ? LeagueApiResult<LeagueDetailDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<LeagueDetailDto>.Ok(dto);
        }

        private static bool IsSuccess(long status, bool network) => !network && status is >= 200 and < 300;

        private static LeagueApiError MapError(long status, string body, bool network)
        {
            if (network) return LeagueApiError.Network;
            return status switch
            {
                401 => LeagueApiError.NotSignedIn,
                403 => LeagueApiError.Forbidden,
                404 => LeagueApiError.NotFound,
                400 => body != null && body.Contains("too_few_members") ? LeagueApiError.TooFewMembers
                     : LeagueApiError.Validation,
                409 => body != null && body.Contains("league_full") ? LeagueApiError.LeagueFull
                     : body != null && body.Contains("not_joinable") ? LeagueApiError.NotJoinable
                     : body != null && body.Contains("wrong_phase") ? LeagueApiError.WrongPhase
                     : body != null && body.Contains("not_your_turn") ? LeagueApiError.NotYourTurn
                     : body != null && body.Contains("club_unavailable") ? LeagueApiError.ClubUnavailable
                     : body != null && body.Contains("not_assigned_club") ? LeagueApiError.NotAssignedClub
                     : body != null && body.Contains("nothing_to_resolve") ? LeagueApiError.NothingToResolve
                     : body != null && body.Contains("replay_not_ready") ? LeagueApiError.ReplayNotReady
                     : LeagueApiError.AlreadyMember,
                >= 500 => LeagueApiError.Server,
                _ => LeagueApiError.Server,
            };
        }

        private static T TryParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (JsonException) { return null; }
        }
    }
}
