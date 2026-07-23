using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace Fts.Services.Online
{
    /// <summary>
    /// The client's gateway to the public ranked ladder API (Phase 9.2): enrol, read your ladder state,
    /// and read your current season (schedule + standings). App-scope singleton built on
    /// <see cref="ApiClient"/> (rotating token store + authed sends). Every call needs a signed-in account
    /// — otherwise it returns <see cref="RankedApiError.NotSignedIn"/> without hitting the network. Failures
    /// come back as <see cref="RankedApiResult{T}"/>; the presenter maps them to loc via RankedErrorFormat.
    /// </summary>
    public sealed class RankedApiService
    {
        private readonly ApiClient _api;

        public RankedApiService(ApiClient api) => _api = api;

        public bool IsSignedIn => _api.IsSignedIn;

        /// <summary>Join the ladder → a placement seat (idempotent: returns the current state if enrolled).</summary>
        public async UniTask<RankedApiResult<RankedStateDto>> EnrolAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedStateDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/enrol");
            return ParseState(status, text, network);
        }

        /// <summary>The caller's ladder state (Enrolled=false when they never joined).</summary>
        public async UniTask<RankedApiResult<RankedStateDto>> GetMineAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedStateDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/me");
            return ParseState(status, text, network);
        }

        /// <summary>Opt in/out of automatic re-enrolment next season (ranking is kept either way).</summary>
        public async UniTask<RankedApiResult<RankedStateDto>> SetAutoEnrolAsync(bool autoEnrol)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedStateDto>.Fail(RankedApiError.NotSignedIn);
            var body = new AutoEnrolBody { autoEnrol = autoEnrol };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/auto-enrol", body);
            return ParseState(status, text, network);
        }

        /// <summary>The caller's current ranked season (schedule + standings + state), or InSeason=false
        /// when they are enrolled but their group has not kicked off yet.</summary>
        public async UniTask<RankedApiResult<RankedSeasonDto>> GetSeasonAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/season");
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedSeasonDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedSeasonDto>(text);
            return dto == null
                ? RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedSeasonDto>.Ok(dto);
        }

        // ---------------------------------------------------------------- dev tooling (gated by DevFlags)

        /// <summary>Dev-only: fill the caller's forming ranked placement group with bot coaches so a solo
        /// human's cohort completes and its season can start (server /internal/dev/ranked/fill).</summary>
        public async UniTask<bool> FillDevAsync(int? count = null)
        {
            if (!_api.IsSignedIn) return false;
            object body = count.HasValue ? new RankedFillBody { count = count.Value } : null;
            var (status, _, network) = await _api.SendAuthedAsync("POST", "/internal/dev/ranked/fill", body);
            return IsSuccess(status, network);
        }

        /// <summary>Dev-only: advance the ranked real-time calendar once (server /internal/ranked/tick) —
        /// starts due seasons, resolves due matchdays, opens windows, settles auctions.</summary>
        public async UniTask<bool> TickDevAsync()
        {
            if (!_api.IsSignedIn) return false;
            var (status, _, network) = await _api.SendAuthedAsync("POST", "/internal/ranked/tick");
            return IsSuccess(status, network);
        }

        // ---------------------------------------------------------------- helpers

        private static RankedApiResult<RankedStateDto> ParseState(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedStateDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedStateDto>(text);
            return dto == null
                ? RankedApiResult<RankedStateDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedStateDto>.Ok(dto);
        }

        private static bool IsSuccess(long status, bool network) => !network && status is >= 200 and < 300;

        private static RankedApiError MapError(long status, string body, bool network)
        {
            if (network) return RankedApiError.Network;
            return status switch
            {
                401 => RankedApiError.NotSignedIn,
                403 => RankedApiError.Forbidden,
                404 => body != null && body.Contains("not_enrolled") ? RankedApiError.NotEnrolled
                     : body != null && body.Contains("fixture_not_found") ? RankedApiError.FixtureNotFound
                     : body != null && body.Contains("auction_not_found") ? RankedApiError.AuctionNotFound
                     : RankedApiError.NotFound,
                400 => body != null && body.Contains("insufficient_budget") ? RankedApiError.InsufficientBudget
                     : body != null && body.Contains("bid_too_low") ? RankedApiError.BidTooLow
                     : RankedApiError.Validation,
                409 => body != null && body.Contains("replay_not_ready") ? RankedApiError.ReplayNotReady
                     : body != null && body.Contains("auction_closed") ? RankedApiError.AuctionClosed
                     : body != null && body.Contains("no_capacity") ? RankedApiError.NoCapacity
                     : RankedApiError.WrongPhase,
                >= 500 => RankedApiError.Server,
                _ => RankedApiError.Server,
            };
        }

        private static T TryParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (JsonException) { return null; }
        }
    }

    [System.Serializable]
    public sealed class AutoEnrolBody { public bool autoEnrol; }

    [System.Serializable]
    public sealed class RankedFillBody { public int count; }
}
