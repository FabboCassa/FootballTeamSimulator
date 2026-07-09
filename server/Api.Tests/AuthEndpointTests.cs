using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// End-to-end auth flow over the real HTTP pipeline (Phase 7.2) against an in-memory SQLite DB:
/// register → login → /auth/me → refresh (rotation) → logout. Each test uses a unique email so
/// the shared in-memory database doesn't leak state between cases.
/// </summary>
[TestFixture]
public class AuthEndpointTests
{
    private AuthTestFactory _factory = null!;
    private HttpClient _client = null!;

    private const string ValidPassword = "Password1";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new AuthTestFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> Register(string email, string password = ValidPassword, string name = "Mister") =>
        _client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, password, name));

    [Test]
    public async Task Register_ReturnsTokensAndProfile()
    {
        var email = UniqueEmail();

        var response = await Register(email);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.That(auth, Is.Not.Null);
        Assert.That(auth!.AccessToken, Is.Not.Empty);
        Assert.That(auth.RefreshToken, Is.Not.Empty);
        Assert.That(auth.ExpiresInSeconds, Is.GreaterThan(0));
        Assert.That(auth.Profile.Email, Is.EqualTo(email));
        Assert.That(auth.Profile.DisplayName, Is.EqualTo("Mister"));
    }

    [Test]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var email = UniqueEmail();
        await Register(email);

        var second = await Register(email);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Register_WeakPassword_ReturnsBadRequest()
    {
        var response = await Register(UniqueEmail(), password: "weak");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_WithCorrectPassword_Succeeds()
    {
        var email = UniqueEmail();
        await Register(email);

        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.That(auth!.AccessToken, Is.Not.Empty);
    }

    [Test]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var email = UniqueEmail();
        await Register(email);

        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "Wrongpass1"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Me_WithAccessToken_ReturnsProfile()
    {
        var email = UniqueEmail();
        var auth = await (await Register(email)).Content.ReadFromJsonAsync<AuthResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var profile = await response.Content.ReadFromJsonAsync<CoachProfileDto>();
        Assert.That(profile!.Email, Is.EqualTo(email));
        Assert.That(profile.UserId, Is.EqualTo(auth.Profile.UserId));
    }

    [Test]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Refresh_RotatesToken_OldOneIsRejected()
    {
        var auth = await (await Register(UniqueEmail())).Content.ReadFromJsonAsync<AuthResponse>();

        // First refresh succeeds and returns a NEW refresh token.
        var refreshed = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(auth!.RefreshToken));
        Assert.That(refreshed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var next = await refreshed.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.That(next!.RefreshToken, Is.Not.EqualTo(auth.RefreshToken));

        // Reusing the OLD (rotated) token must fail.
        var reused = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // The NEW token still works.
        var again = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(next.RefreshToken));
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Refresh_WithGarbageToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest("not-a-real-token"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logout_RevokesRefreshToken()
    {
        var auth = await (await Register(UniqueEmail())).Content.ReadFromJsonAsync<AuthResponse>();

        var logout = await _client.PostAsJsonAsync("/auth/logout", new RefreshRequest(auth!.RefreshToken));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var refreshAfterLogout = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.That(refreshAfterLogout.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
