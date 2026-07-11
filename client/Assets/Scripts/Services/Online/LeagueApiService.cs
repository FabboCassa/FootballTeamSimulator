using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

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

        // ---------------------------------------------------------------- helpers

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
