using System;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Fts.Services.Online
{
    /// <summary>
    /// The client's gateway to the FTS backend auth API (Phase 7.2b). App-scope singleton: it owns
    /// the token pair, persists it in <see cref="PlayerPrefs"/> so the player stays signed in across
    /// launches, and transparently rotates the short-lived access token using the refresh token
    /// (the "no re-login" decision). Single-player is offline-first — nothing here is required to
    /// play; an account is only for the online phases. Network errors surface as
    /// <see cref="ApiError.Network"/> so the UI can tell "wrong password" from "server unreachable".
    /// </summary>
    public sealed class ApiClient
    {
        private const string BaseUrlKey = "fts.api.base_url";
        private const string AccessKey = "fts.auth.access";
        private const string RefreshKey = "fts.auth.refresh";
        private const string ExpiresKey = "fts.auth.expires_unix";
        private const string UserIdKey = "fts.auth.user_id";
        private const string EmailKey = "fts.auth.email";
        private const string NameKey = "fts.auth.name";

        private const string DefaultBaseUrl = "http://localhost:8080";
        // Refresh a bit before the access token actually expires to avoid edge-of-expiry 401s.
        private const int RefreshMarginSeconds = 30;

        private string _accessToken;
        private string _refreshToken;
        private long _expiresAtUnix;

        /// <summary>Raised when the sign-in state changes (login / logout / refresh failure).</summary>
        public event Action AuthChanged;

        public ApiClient()
        {
            BaseUrl = PlayerPrefs.GetString(BaseUrlKey, DefaultBaseUrl);
            _accessToken = PlayerPrefs.GetString(AccessKey, string.Empty);
            _refreshToken = PlayerPrefs.GetString(RefreshKey, string.Empty);
            _expiresAtUnix = long.TryParse(PlayerPrefs.GetString(ExpiresKey, "0"), out var e) ? e : 0;

            if (!string.IsNullOrEmpty(PlayerPrefs.GetString(EmailKey, string.Empty)))
                Profile = new AuthProfile(
                    PlayerPrefs.GetString(UserIdKey, string.Empty),
                    PlayerPrefs.GetString(EmailKey, string.Empty),
                    PlayerPrefs.GetString(NameKey, string.Empty));
        }

        /// <summary>Base URL of the API. Editable at runtime (e.g. a LAN IP to reach the PC from a
        /// phone); persisted so it survives a relaunch.</summary>
        public string BaseUrl { get; private set; }

        public void SetBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            BaseUrl = url.TrimEnd('/');
            PlayerPrefs.SetString(BaseUrlKey, BaseUrl);
            PlayerPrefs.Save();
        }

        /// <summary>The signed-in account, if any.</summary>
        public AuthProfile? Profile { get; private set; }

        public bool IsSignedIn => !string.IsNullOrEmpty(_refreshToken) && Profile.HasValue;

        // ---------------------------------------------------------------- public use cases

        public async UniTask<ApiResult> RegisterAsync(string email, string password, string displayName)
        {
            var body = new RegisterBody { email = email, password = password, displayName = displayName };
            var (status, text, network) = await SendAsync("POST", "/auth/register", body, bearer: null);
            return HandleAuthResponse(status, text, network);
        }

        public async UniTask<ApiResult> LoginAsync(string email, string password)
        {
            var body = new LoginBody { email = email, password = password };
            var (status, text, network) = await SendAsync("POST", "/auth/login", body, bearer: null);
            return HandleAuthResponse(status, text, network);
        }

        /// <summary>Uses the stored refresh token to get a fresh token pair (rotation). On failure the
        /// session is cleared (the refresh token was revoked/expired) and the UI drops to signed-out.</summary>
        public async UniTask<bool> TryRefreshAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken)) return false;

            var body = new RefreshBody { refreshToken = _refreshToken };
            var (status, text, network) = await SendAsync("POST", "/auth/refresh", body, bearer: null);
            if (network || status is < 200 or >= 300)
            {
                // A definitive rejection (401) means the token is gone — sign out. A transient
                // network error leaves the stored session intact for a later retry.
                if (!network) ClearSession();
                return false;
            }

            return StoreAuth(text);
        }

        /// <summary>Revokes the refresh token server-side (best effort) and clears the local session.</summary>
        public async UniTask LogoutAsync()
        {
            if (!string.IsNullOrEmpty(_refreshToken))
            {
                var body = new RefreshBody { refreshToken = _refreshToken };
                await SendAsync("POST", "/auth/logout", body, bearer: null);
            }
            ClearSession();
        }

        /// <summary>Deletes this account for good (Roadmap 10.2a) and, on success, clears the local
        /// session — there is nothing left to sign back into. The password is sent because the server
        /// re-confirms it; a wrong one comes back as <see cref="ApiError.InvalidCredentials"/> and the
        /// account is untouched. Both mobile stores require this to exist in the app.</summary>
        public async UniTask<ApiResult> DeleteAccountAsync(string password)
        {
            if (!IsSignedIn) return ApiResult.Fail(ApiError.InvalidCredentials);

            var body = new DeleteAccountBody { password = password };
            var (status, _, network) = await SendAuthedAsync("POST", "/auth/account/delete", body);

            if (network) return ApiResult.Fail(ApiError.Network);

            if (status is >= 200 and < 300)
            {
                ClearSession();
                return ApiResult.Ok();
            }

            return ApiResult.Fail(status switch
            {
                401 => ApiError.InvalidCredentials,
                400 => ApiError.Validation,
                _ => ApiError.Server
            });
        }

        /// <summary>Fetches the account profile from a protected endpoint, refreshing the access token
        /// first if it's about to expire. Returns null if not signed in / unreachable.</summary>
        public async UniTask<AuthProfile?> GetMeAsync()
        {
            var token = await EnsureAccessTokenAsync();
            if (token == null) return null;

            var (status, text, network) = await SendAsync("GET", "/auth/me", body: null, bearer: token);
            if (network || status is < 200 or >= 300) return null;

            var dto = TryParse<CoachProfileDto>(text);
            if (dto == null) return null;

            var profile = new AuthProfile(dto.userId, dto.email, dto.displayName);
            Profile = profile;
            PersistProfile(profile);
            return profile;
        }

        /// <summary>Sends an authenticated request, refreshing the access token first if it's about to
        /// expire (Phase 8.1b). Returns (status, body, wasNetworkError). If the caller isn't signed in
        /// (no valid token) it comes back as status 401 with no network error, so callers can tell
        /// "signed out" from "server unreachable". Never throws.</summary>
        public async UniTask<(long status, string body, bool network)> SendAuthedAsync(
            string method, string path, object body = null)
        {
            var token = await EnsureAccessTokenAsync();
            if (token == null) return (401, null, false);
            return await SendAsync(method, path, body, token);
        }

        /// <summary>A valid (refreshed if needed) access token for authenticated calls, or null.</summary>
        public async UniTask<string> EnsureAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken)) return null;

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(_accessToken) && nowUnix < _expiresAtUnix - RefreshMarginSeconds)
                return _accessToken;

            return await TryRefreshAsync() ? _accessToken : null;
        }

        // ---------------------------------------------------------------- helpers

        private ApiResult HandleAuthResponse(long status, string text, bool network)
        {
            if (network) return ApiResult.Fail(ApiError.Network);

            if (status is >= 200 and < 300)
                return StoreAuth(text) ? ApiResult.Ok() : ApiResult.Fail(ApiError.Server);

            return ApiResult.Fail(status switch
            {
                409 => ApiError.EmailInUse,
                400 => text != null && text.Contains("weak_password") ? ApiError.WeakPassword : ApiError.Validation,
                401 => ApiError.InvalidCredentials,
                >= 500 => ApiError.Server,
                _ => ApiError.Server
            });
        }

        private bool StoreAuth(string json)
        {
            var dto = TryParse<AuthResponseDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.accessToken) || string.IsNullOrEmpty(dto.refreshToken))
                return false;

            _accessToken = dto.accessToken;
            _refreshToken = dto.refreshToken;
            _expiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(0, dto.expiresInSeconds);

            PlayerPrefs.SetString(AccessKey, _accessToken);
            PlayerPrefs.SetString(RefreshKey, _refreshToken);
            PlayerPrefs.SetString(ExpiresKey, _expiresAtUnix.ToString());

            var profile = dto.profile != null
                ? new AuthProfile(dto.profile.userId, dto.profile.email, dto.profile.displayName)
                : (Profile ?? default);
            Profile = profile;
            PersistProfile(profile);

            PlayerPrefs.Save();
            AuthChanged?.Invoke();
            return true;
        }

        private void PersistProfile(AuthProfile profile)
        {
            PlayerPrefs.SetString(UserIdKey, profile.UserId ?? string.Empty);
            PlayerPrefs.SetString(EmailKey, profile.Email ?? string.Empty);
            PlayerPrefs.SetString(NameKey, profile.DisplayName ?? string.Empty);
            PlayerPrefs.Save();
        }

        private void ClearSession()
        {
            _accessToken = string.Empty;
            _refreshToken = string.Empty;
            _expiresAtUnix = 0;
            Profile = null;

            PlayerPrefs.DeleteKey(AccessKey);
            PlayerPrefs.DeleteKey(RefreshKey);
            PlayerPrefs.DeleteKey(ExpiresKey);
            PlayerPrefs.DeleteKey(UserIdKey);
            PlayerPrefs.DeleteKey(EmailKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.Save();

            AuthChanged?.Invoke();
        }

        private static T TryParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>Sends a request and returns (status, body, wasNetworkError) without ever throwing
        /// — UniTask's awaiter throws on non-2xx, so we catch and read the code/body back off the
        /// request. A connection failure (no server) comes back as wasNetworkError=true.</summary>
        private async UniTask<(long status, string body, bool network)> SendAsync(
            string method, string path, object body, string bearer)
        {
            var url = BaseUrl + path;
            using var req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };

            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(bearer))
                req.SetRequestHeader("Authorization", "Bearer " + bearer);

            try
            {
                await req.SendWebRequest();
            }
            catch (UnityWebRequestException)
            {
                // Non-2xx (ProtocolError) or a connection error — fall through and inspect the request.
            }

            bool network = req.result == UnityWebRequest.Result.ConnectionError
                        || req.result == UnityWebRequest.Result.DataProcessingError;
            var text = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
            return (req.responseCode, text, network);
        }
    }
}
