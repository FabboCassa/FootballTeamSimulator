using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Sim.Core.Development;
using Sim.Core.Match;

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

        /// <summary>Browse a club's squad in the caller's group (the lineup editor loads the caller's own).</summary>
        public async UniTask<RankedApiResult<RankedSquadDto>> GetClubSquadAsync(int clubExternalId)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedSquadDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/clubs/" + clubExternalId + "/squad");
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedSquadDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedSquadDto>(text);
            return dto?.players == null
                ? RankedApiResult<RankedSquadDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedSquadDto>.Ok(dto);
        }

        /// <summary>The caller's currently submitted lineup (the stored Sim.Core <see cref="LineupPlan"/>),
        /// or null when they have not submitted one — so the editor re-opens on the saved XI.</summary>
        public async UniTask<RankedApiResult<LineupPlan>> GetMyLineupAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<LineupPlan>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/lineup");
            if (!IsSuccess(status, network))
                return RankedApiResult<LineupPlan>.Fail(MapError(status, text, network));
            var plan = TryParse<LineupPlan>(text);
            // An empty "{}" body (no submission yet) parses to a plan with no slots → report it as null.
            return RankedApiResult<LineupPlan>.Ok(plan != null && plan.Slots != null && plan.Slots.Count > 0 ? plan : null);
        }

        /// <summary>Submit (or replace) the caller's lineup/tactic/plan for their ranked club. <paramref
        /// name="body"/> is the { lineup, tactic, plan } wrapper the presenter builds from the Sim.Core plans;
        /// on success the server returns the updated season.</summary>
        public async UniTask<RankedApiResult<RankedSeasonDto>> SubmitLineupAsync(object body)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/lineup", body);
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedSeasonDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedSeasonDto>(text);
            return dto == null
                ? RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedSeasonDto>.Ok(dto);
        }

        /// <summary>The stored full MatchReport for a played fixture in the caller's group, deserialized to
        /// the Sim.Core <see cref="MatchReport"/> so the caller renders the replay with the MatchRenderer.
        /// Fails if the report has no position stream (not renderable).</summary>
        public async UniTask<RankedApiResult<MatchReport>> GetReplayReportAsync(string fixtureId)
        {
            if (!_api.IsSignedIn) return RankedApiResult<MatchReport>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "GET", "/ranked/season/fixtures/" + fixtureId + "/replay");
            if (!IsSuccess(status, network))
                return RankedApiResult<MatchReport>.Fail(MapError(status, text, network));
            var report = TryParse<MatchReport>(text);
            return report?.Positions == null
                ? RankedApiResult<MatchReport>.Fail(RankedApiError.Server)
                : RankedApiResult<MatchReport>.Ok(report);
        }

        // ---------------------------------------------------------------- daily digest (9.4)

        /// <summary>The caller's daily digest: everything the ranked day needs in one call (state, next match,
        /// last result, table position, market, whether the inputs are ready) + a prioritised to-do list.
        /// Succeeds for any signed-in account — a coach who never joined gets the "enrol" digest.</summary>
        public async UniTask<RankedApiResult<RankedTodayDto>> GetTodayAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedTodayDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/today");
            return ParseToday(status, text, network);
        }

        /// <summary>One-tap "my day is handled": the server seeds/repairs the stored inputs and stamps the
        /// upcoming matchday as confirmed, then returns the refreshed digest. Idempotent.</summary>
        public async UniTask<RankedApiResult<RankedTodayDto>> ConfirmMatchdayAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedTodayDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/today/confirm");
            return ParseToday(status, text, network);
        }

        /// <summary>The caller's stored training plan (the Sim.Core <see cref="TrainingPlan"/>), or null when
        /// nothing is stored — so the training screen opens on the saved plan, not on a fresh default.</summary>
        public async UniTask<RankedApiResult<TrainingPlan>> GetMyTrainingAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<TrainingPlan>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/training");
            if (!IsSuccess(status, network))
                return RankedApiResult<TrainingPlan>.Fail(MapError(status, text, network));
            // An empty "{}" body (nothing stored) parses to a default plan — indistinguishable from a stored
            // Balanced one, which is exactly what the server would have seeded anyway.
            return RankedApiResult<TrainingPlan>.Ok(TryParse<TrainingPlan>(text));
        }

        /// <summary>Submit (or replace) the training plan the caller's ranked club develops on. <paramref
        /// name="body"/> is the { training } wrapper the presenter builds (shared with the private-league
        /// training screen).</summary>
        public async UniTask<RankedApiResult<RankedSeasonDto>> SubmitTrainingAsync(object body)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/training", body);
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedSeasonDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedSeasonDto>(text);
            return dto == null
                ? RankedApiResult<RankedSeasonDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedSeasonDto>.Ok(dto);
        }

        private static RankedApiResult<RankedTodayDto> ParseToday(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedTodayDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedTodayDto>(text);
            return dto == null
                ? RankedApiResult<RankedTodayDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedTodayDto>.Ok(dto);
        }

        // ---------------------------------------------------------------- market (9.2b)

        /// <summary>The caller's market view: budget + incoming/outgoing offers + window state.</summary>
        public async UniTask<RankedApiResult<RankedOffersDto>> GetOffersAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedOffersDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/offers");
            return ParseOffers(status, text, network);
        }

        /// <summary>Offer a fee for a player on another coach's club (requires an open window + budget).</summary>
        public async UniTask<RankedApiResult<RankedOffersDto>> MakeOfferAsync(int playerExternalId, long fee)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedOffersDto>.Fail(RankedApiError.NotSignedIn);
            var body = new MakeRankedOfferBody { playerExternalId = playerExternalId, fee = fee };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/ranked/offers", body);
            return ParseOffers(status, text, network);
        }

        /// <summary>Accept / reject an incoming offer (only the player's owner may).</summary>
        public async UniTask<RankedApiResult<RankedOffersDto>> RespondOfferAsync(string offerId, bool accept)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedOffersDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/ranked/offers/" + offerId + (accept ? "/accept" : "/reject"));
            return ParseOffers(status, text, network);
        }

        /// <summary>Withdraw an offer the caller made (while still pending).</summary>
        public async UniTask<RankedApiResult<RankedOffersDto>> WithdrawOfferAsync(string offerId)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedOffersDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/ranked/offers/" + offerId + "/withdraw");
            return ParseOffers(status, text, network);
        }

        /// <summary>The open free-agent auction lots + the caller's budget picture.</summary>
        public async UniTask<RankedApiResult<RankedAuctionsDto>> GetAuctionsAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedAuctionsDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/auctions");
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedAuctionsDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedAuctionsDto>(text);
            return dto?.lots == null
                ? RankedApiResult<RankedAuctionsDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedAuctionsDto>.Ok(dto);
        }

        /// <summary>Place an ascending bid on a lot (requires an open window + available budget).</summary>
        public async UniTask<RankedApiResult<bool>> PlaceBidAsync(string auctionId, long amount)
        {
            if (!_api.IsSignedIn) return RankedApiResult<bool>.Fail(RankedApiError.NotSignedIn);
            var body = new PlaceRankedBidBody { amount = amount };
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/ranked/auctions/" + auctionId + "/bid", body);
            return IsSuccess(status, network)
                ? RankedApiResult<bool>.Ok(true)
                : RankedApiResult<bool>.Fail(MapError(status, text, network));
        }

        private static RankedApiResult<RankedOffersDto> ParseOffers(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedOffersDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedOffersDto>(text);
            return dto == null
                ? RankedApiResult<RankedOffersDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedOffersDto>.Ok(dto);
        }

        // ---------------------------------------------------------------- ranking (9.3)

        /// <summary>The global ladder: the top coaches by rating, plus the caller's own row when they sit
        /// outside the slice. <paramref name="top"/> 0 = the server's default page size.</summary>
        public async UniTask<RankedApiResult<RankedLeaderboardDto>> GetLeaderboardAsync(int top = 0)
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedLeaderboardDto>.Fail(RankedApiError.NotSignedIn);
            string path = top > 0 ? "/ranked/leaderboard?top=" + top : "/ranked/leaderboard";
            var (status, text, network) = await _api.SendAuthedAsync("GET", path);
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedLeaderboardDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedLeaderboardDto>(text);
            return dto?.entries == null
                ? RankedApiResult<RankedLeaderboardDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedLeaderboardDto>.Ok(dto);
        }

        /// <summary>The caller's persistent record: rating, career best, seasons played and the award history
        /// (titles, promotions, relegations). NotEnrolled when the account never joined the ladder.</summary>
        public async UniTask<RankedApiResult<RankedPalmaresDto>> GetPalmaresAsync()
        {
            if (!_api.IsSignedIn) return RankedApiResult<RankedPalmaresDto>.Fail(RankedApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/ranked/palmares");
            if (!IsSuccess(status, network))
                return RankedApiResult<RankedPalmaresDto>.Fail(MapError(status, text, network));
            var dto = TryParse<RankedPalmaresDto>(text);
            return dto?.awards == null
                ? RankedApiResult<RankedPalmaresDto>.Fail(RankedApiError.Server)
                : RankedApiResult<RankedPalmaresDto>.Ok(dto);
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

        /// <summary>Dev-only: drive the group's bot coaches through a round of market activity (they outbid
        /// on the open lots and answer the offers you sent them), so the market is solo-testable.</summary>
        public async UniTask<bool> BotMarketDevAsync(string groupId, int rounds = 1, bool accept = true)
        {
            if (!_api.IsSignedIn || string.IsNullOrEmpty(groupId)) return false;
            string path = "/internal/dev/ranked/" + groupId + "/market/bot?rounds=" + rounds
                          + "&accept=" + (accept ? "true" : "false");
            var (status, _, network) = await _api.SendAuthedAsync("POST", path);
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

        /// <summary>Dev-only: time-travel the ladder forward <paramref name="matchdays"/> matchdays (server
        /// /internal/ranked/fast-forward). A single tick only resolves what the REAL calendar has made due —
        /// one matchday a day — so this is what actually lets a solo tester watch a season, the seasonal reset
        /// and the season after it play out (Phase 9.3).</summary>
        public async UniTask<bool> FastForwardDevAsync(int matchdays = 1)
        {
            if (!_api.IsSignedIn) return false;
            var (status, _, network) = await _api.SendAuthedAsync(
                "POST", "/internal/ranked/fast-forward?matchdays=" + matchdays);
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
