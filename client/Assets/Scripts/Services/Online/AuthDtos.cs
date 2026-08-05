using System;

namespace Fts.Services.Online
{
    // Wire DTOs mirroring the server's /auth payloads (Phase 7.2). Public fields named to match the
    // server's camelCase JSON so Newtonsoft round-trips them exactly (requests serialize to the
    // names the API binds, responses deserialize from the names the API emits).

    [Serializable]
    public sealed class RegisterBody
    {
        public string email;
        public string password;
        public string displayName;
    }

    [Serializable]
    public sealed class LoginBody
    {
        public string email;
        public string password;
    }

    [Serializable]
    public sealed class RefreshBody
    {
        public string refreshToken;
    }

    /// <summary>Deleting your own account (Roadmap 10.2a). The password travels again because the
    /// server re-checks it: an access token can be minutes old on an unlocked phone.</summary>
    [Serializable]
    public sealed class DeleteAccountBody
    {
        public string password;
    }

    [Serializable]
    public sealed class AuthResponseDto
    {
        public string accessToken;
        public int expiresInSeconds;
        public string refreshToken;
        public string refreshTokenExpiresUtc;
        public CoachProfileDto profile;
    }

    [Serializable]
    public sealed class CoachProfileDto
    {
        public string userId;
        public string email;
        public string displayName;
    }

    /// <summary>The account identity the client keeps after signing in (from the token pair).</summary>
    public readonly struct AuthProfile
    {
        public readonly string UserId;
        public readonly string Email;
        public readonly string DisplayName;

        public AuthProfile(string userId, string email, string displayName)
        {
            UserId = userId;
            Email = email;
            DisplayName = displayName;
        }
    }
}
