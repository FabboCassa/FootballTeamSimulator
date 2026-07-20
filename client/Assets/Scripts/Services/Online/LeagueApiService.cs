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

        /// <summary>Submit (or replace) the caller's training plan for their league club (Phase 8.4b).
        /// <paramref name="body"/> is the { training: { teamFocus, individualFocuses } } wrapper the
        /// presenter builds from the Sim.Core <c>TrainingPlan</c>; on success the server returns the
        /// updated season state. Requires the league Active and the caller to have a drafted club.</summary>
        public async UniTask<LeagueApiResult<SeasonStateDto>> SubmitTrainingAsync(string leagueId, object body)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<SeasonStateDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/training", body);
            if (!IsSuccess(status, network))
                return LeagueApiResult<SeasonStateDto>.Fail(MapError(status, text, network));
            var dto = TryParse<SeasonStateDto>(text);
            return dto == null
                ? LeagueApiResult<SeasonStateDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<SeasonStateDto>.Ok(dto);
        }

        /// <summary>The server's canonical whole-world state hash (condition + attributes) — the 8.4 ✅
        /// agreement value. Deterministic across reads and CHANGES after a round of play. Members only.</summary>
        public async UniTask<LeagueApiResult<StateHashDto>> GetStateHashAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<StateHashDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/leagues/" + leagueId + "/state-hash");
            if (!IsSuccess(status, network))
                return LeagueApiResult<StateHashDto>.Fail(MapError(status, text, network));
            var dto = TryParse<StateHashDto>(text);
            return dto?.hashHex == null
                ? LeagueApiResult<StateHashDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<StateHashDto>.Ok(dto);
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

        // ---------------------------------------------------------------- auctions (8.5b)

        /// <summary>The current auction view: the window's lots + the caller's budget picture (members only).</summary>
        public async UniTask<LeagueApiResult<AuctionsDto>> GetAuctionsAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<AuctionsDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("GET", "/leagues/" + leagueId + "/auctions");
            return ParseAuctions(status, text, network);
        }

        /// <summary>Opens the next auction window (creator only): a lot per free agent.</summary>
        public async UniTask<LeagueApiResult<AuctionsDto>> OpenWindowAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<AuctionsDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/auctions/open");
            return ParseAuctions(status, text, network);
        }

        /// <summary>Closes the window now (creator only): settles every open lot immediately.</summary>
        public async UniTask<LeagueApiResult<AuctionsDto>> CloseWindowAsync(string leagueId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<AuctionsDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/leagues/" + leagueId + "/auctions/close");
            return ParseAuctions(status, text, network);
        }

        /// <summary>Places a bid on a lot. On success returns the updated lot + whether it outbid a
        /// previous leader / extended the timer; on failure a mapped error (too low / over budget / closed).</summary>
        public async UniTask<LeagueApiResult<BidResultDto>> PlaceBidAsync(string leagueId, string auctionId, long amount)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<BidResultDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new PlaceBidBody { amount = amount };
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/leagues/" + leagueId + "/auctions/" + auctionId + "/bid", body);
            if (!IsSuccess(status, network))
                return LeagueApiResult<BidResultDto>.Fail(MapError(status, text, network));
            var dto = TryParse<BidResultDto>(text);
            return dto?.lot == null
                ? LeagueApiResult<BidResultDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<BidResultDto>.Ok(dto);
        }

        private static LeagueApiResult<AuctionsDto> ParseAuctions(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return LeagueApiResult<AuctionsDto>.Fail(MapError(status, text, network));
            var dto = TryParse<AuctionsDto>(text);
            return dto?.lots == null
                ? LeagueApiResult<AuctionsDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<AuctionsDto>.Ok(dto);
        }

        // ---------------------------------------------------------------- live match control (8.6b)

        /// <summary>Open (or return) the live session for a fixture — marks the caller present. Both members
        /// opening the same fixture kicks it off (Live).</summary>
        public UniTask<LeagueApiResult<LiveMatchStateDto>> OpenLiveAsync(string leagueId, string fixtureId) =>
            LivePostAsync(leagueId, fixtureId, "/open");

        /// <summary>Join a session (mark present). Open already does this — kept for completeness.</summary>
        public UniTask<LeagueApiResult<LiveMatchStateDto>> JoinLiveAsync(string leagueId, string fixtureId) =>
            LivePostAsync(leagueId, fixtureId, "/join");

        /// <summary>The current live-match state (members only). Polled ~1s while the screen is open.</summary>
        public async UniTask<LeagueApiResult<LiveMatchStateDto>> GetLiveAsync(string leagueId, string fixtureId)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LiveMatchStateDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "GET", "/leagues/" + leagueId + "/live/" + fixtureId);
            return ParseLive(status, text, network);
        }

        /// <summary>Submit a pause-point change for the caller's own side (sub / instruction change). The
        /// server re-simulates from the fixture seed and returns the new state (with the updated report).
        /// <paramref name="body"/> is a <see cref="SubmitLiveChangeBody"/> carrying the Sim.Core plans.</summary>
        public async UniTask<LeagueApiResult<LiveMatchStateDto>> SubmitLiveChangeAsync(
            string leagueId, string fixtureId, object body)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LiveMatchStateDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/leagues/" + leagueId + "/live/" + fixtureId + "/change", body);
            return ParseLive(status, text, network);
        }

        /// <summary>Mark the caller absent (disconnect) — the match keeps running on the accumulated plan.</summary>
        public UniTask<LeagueApiResult<LiveMatchStateDto>> LeaveLiveAsync(string leagueId, string fixtureId) =>
            LivePostAsync(leagueId, fixtureId, "/leave");

        /// <summary>Confirm full-time — the stored report becomes the fixture's official result at round
        /// resolution.</summary>
        public UniTask<LeagueApiResult<LiveMatchStateDto>> FinishLiveAsync(string leagueId, string fixtureId) =>
            LivePostAsync(leagueId, fixtureId, "/finish");

        private async UniTask<LeagueApiResult<LiveMatchStateDto>> LivePostAsync(
            string leagueId, string fixtureId, string action)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<LiveMatchStateDto>.Fail(LeagueApiError.NotSignedIn);
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/leagues/" + leagueId + "/live/" + fixtureId + action);
            return ParseLive(status, text, network);
        }

        private static LeagueApiResult<LiveMatchStateDto> ParseLive(long status, string text, bool network)
        {
            if (!IsSuccess(status, network))
                return LeagueApiResult<LiveMatchStateDto>.Fail(MapError(status, text, network));
            var dto = TryParse<LiveMatchStateDto>(text);
            return dto?.fixtureId == null
                ? LeagueApiResult<LiveMatchStateDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<LiveMatchStateDto>.Ok(dto);
        }

        // ---------------------------------------------------------------- dev tooling (gated by DevFlags)

        /// <summary>Dev-only: seed a ready test league (the signed-in account as creator + bots, drafted to
        /// Active) via the server's /internal/dev endpoint, so online features can be tested solo. Returns
        /// the new league id to open its lobby.</summary>
        public async UniTask<LeagueApiResult<DevSeedResultDto>> SeedTestLeagueAsync(int size, int bots)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<DevSeedResultDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new DevSeedBody { size = size, bots = bots, toStatus = "Active", creatorUserId = _api.Profile?.UserId };
            var (status, text, network) = await _api.SendAuthedAsync("POST", "/internal/dev/test-league", body);
            if (!IsSuccess(status, network))
                return LeagueApiResult<DevSeedResultDto>.Fail(MapError(status, text, network));
            var dto = TryParse<DevSeedResultDto>(text);
            return dto?.leagueId == null
                ? LeagueApiResult<DevSeedResultDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<DevSeedResultDto>.Ok(dto);
        }

        /// <summary>Dev-only: have the league's bot members place a round of legal bids on the open lots, so
        /// a single human sees live outbidding.</summary>
        public async UniTask<LeagueApiResult<bool>> BotBidAsync(string leagueId, int rounds)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<bool>.Fail(LeagueApiError.NotSignedIn);
            var body = new DevBotBidBody { rounds = rounds };
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/internal/dev/leagues/" + leagueId + "/auctions/botbid", body);
            return IsSuccess(status, network)
                ? LeagueApiResult<bool>.Ok(true)
                : LeagueApiResult<bool>.Fail(MapError(status, text, network));
        }

        /// <summary>Dev-only: the fixture's bot opponent joins the live match (kicking it off once you're
        /// present) and, if <paramref name="sub"/>, makes a substitution at <paramref name="minute"/> — so a
        /// single human can test live match control solo (8.6).</summary>
        public async UniTask<LeagueApiResult<DevBotLiveResultDto>> BotLiveAsync(
            string leagueId, string fixtureId, bool sub, int minute)
        {
            if (!_api.IsSignedIn) return LeagueApiResult<DevBotLiveResultDto>.Fail(LeagueApiError.NotSignedIn);
            var body = new DevBotLiveBody { sub = sub, minute = minute };
            var (status, text, network) = await _api.SendAuthedAsync(
                "POST", "/internal/dev/leagues/" + leagueId + "/live/" + fixtureId + "/bot", body);
            if (!IsSuccess(status, network))
                return LeagueApiResult<DevBotLiveResultDto>.Fail(MapError(status, text, network));
            var dto = TryParse<DevBotLiveResultDto>(text);
            return dto == null
                ? LeagueApiResult<DevBotLiveResultDto>.Fail(LeagueApiError.Server)
                : LeagueApiResult<DevBotLiveResultDto>.Ok(dto);
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
                403 => body != null && body.Contains("not_your_side") ? LeagueApiError.NotYourSide
                     : LeagueApiError.Forbidden,
                404 => body != null && body.Contains("auction_not_found") ? LeagueApiError.AuctionNotFound
                     : body != null && body.Contains("live_match_not_found") ? LeagueApiError.LiveMatchNotFound
                     : LeagueApiError.NotFound,
                400 => body != null && body.Contains("too_few_members") ? LeagueApiError.TooFewMembers
                     : body != null && body.Contains("bid_too_low") ? LeagueApiError.BidTooLow
                     : body != null && body.Contains("insufficient_budget") ? LeagueApiError.InsufficientBudget
                     : body != null && body.Contains("invalid_live_change") ? LeagueApiError.InvalidLiveChange
                     : LeagueApiError.Validation,
                409 => body != null && body.Contains("live_match_not_joinable") ? LeagueApiError.LiveMatchNotJoinable
                     : body != null && body.Contains("live_match_already_finished") ? LeagueApiError.LiveMatchAlreadyFinished
                     : body != null && body.Contains("live_match_not_live") ? LeagueApiError.LiveMatchNotLive
                     : body != null && body.Contains("league_full") ? LeagueApiError.LeagueFull
                     : body != null && body.Contains("not_joinable") ? LeagueApiError.NotJoinable
                     : body != null && body.Contains("wrong_phase") ? LeagueApiError.WrongPhase
                     : body != null && body.Contains("not_your_turn") ? LeagueApiError.NotYourTurn
                     : body != null && body.Contains("club_unavailable") ? LeagueApiError.ClubUnavailable
                     : body != null && body.Contains("not_assigned_club") ? LeagueApiError.NotAssignedClub
                     : body != null && body.Contains("nothing_to_resolve") ? LeagueApiError.NothingToResolve
                     : body != null && body.Contains("replay_not_ready") ? LeagueApiError.ReplayNotReady
                     : body != null && body.Contains("auction_closed") ? LeagueApiError.AuctionClosed
                     : body != null && body.Contains("window_already_open") ? LeagueApiError.WindowAlreadyOpen
                     : body != null && body.Contains("no_auctions_open") ? LeagueApiError.NoAuctionsOpen
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
