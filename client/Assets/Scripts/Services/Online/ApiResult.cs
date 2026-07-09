namespace Fts.Services.Online
{
    /// <summary>Why an online auth call failed — mapped from the server's HTTP status (Phase 7.2b).</summary>
    public enum ApiError
    {
        None = 0,
        EmailInUse,          // 409
        WeakPassword,        // 400 weak_password
        Validation,          // 400 validation_failed
        InvalidCredentials,  // 401 (login / refresh)
        Network,             // no connection / server unreachable
        Server               // 5xx / unexpected
    }

    /// <summary>Result of an auth call. The presenter shows <see cref="Message"/> (already a loc
    /// key resolved by the presenter, not the raw server text) — this carries only the outcome.</summary>
    public readonly struct ApiResult
    {
        public bool Success { get; }
        public ApiError Error { get; }

        private ApiResult(bool success, ApiError error)
        {
            Success = success;
            Error = error;
        }

        public static ApiResult Ok() => new ApiResult(true, ApiError.None);
        public static ApiResult Fail(ApiError error) => new ApiResult(false, error);
    }
}
