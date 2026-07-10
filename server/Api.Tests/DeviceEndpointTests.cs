using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Notifications;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Device-registration flow over the real HTTP pipeline (Phase 7.4) against in-memory SQLite:
/// the endpoints are JWT-protected, register is an idempotent upsert per token, and a token can
/// be listed and unregistered. Reuses <see cref="AuthTestFactory"/> and registers a fresh account
/// per test to get a bearer token. Under the Testing environment Hangfire is disabled, but the
/// device endpoints are always mapped, so this exercises the real device store.
/// </summary>
[TestFixture]
public class DeviceEndpointTests
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
    private static string UniqueToken() => $"fcm_token_{Guid.NewGuid():N}";

    private async Task<string> RegisterAndGetAccessToken()
    {
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Test]
    public async Task RegisterDevice_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/notifications/devices",
            new RegisterDeviceRequest(UniqueToken(), DevicePlatform.Android));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RegisterDevice_WithToken_RegistersAndLists()
    {
        var access = await RegisterAndGetAccessToken();
        var deviceToken = UniqueToken();

        using var reg = Authed(HttpMethod.Post, "/notifications/devices", access,
            new RegisterDeviceRequest(deviceToken, DevicePlatform.Web));
        var regResp = await _client.SendAsync(reg);
        Assert.That(regResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dto = await regResp.Content.ReadFromJsonAsync<DeviceRegistrationDto>();
        Assert.That(dto!.Platform, Is.EqualTo(DevicePlatform.Web));

        using var list = Authed(HttpMethod.Get, "/notifications/devices", access);
        var listResp = await _client.SendAsync(list);
        var devices = await listResp.Content.ReadFromJsonAsync<List<DeviceRegistrationDto>>();
        Assert.That(devices!.Select(d => d.Id), Does.Contain(dto.Id));
    }

    [Test]
    public async Task RegisterDevice_SameTokenTwice_IsIdempotent()
    {
        var access = await RegisterAndGetAccessToken();
        var deviceToken = UniqueToken();
        var body = new RegisterDeviceRequest(deviceToken, DevicePlatform.Ios);

        using (var first = Authed(HttpMethod.Post, "/notifications/devices", access, body))
            await _client.SendAsync(first);
        using (var second = Authed(HttpMethod.Post, "/notifications/devices", access, body))
            await _client.SendAsync(second);

        using var list = Authed(HttpMethod.Get, "/notifications/devices", access);
        var devices = await (await _client.SendAsync(list)).Content.ReadFromJsonAsync<List<DeviceRegistrationDto>>();
        Assert.That(devices!.Count, Is.EqualTo(1), "re-registering the same token must not duplicate it");
    }

    [Test]
    public async Task UnregisterDevice_RemovesIt_ThenNotFound()
    {
        var access = await RegisterAndGetAccessToken();
        var deviceToken = UniqueToken();

        using (var reg = Authed(HttpMethod.Post, "/notifications/devices", access,
            new RegisterDeviceRequest(deviceToken, DevicePlatform.Android)))
            await _client.SendAsync(reg);

        using var del1 = Authed(HttpMethod.Delete, $"/notifications/devices/{deviceToken}", access);
        var del1Resp = await _client.SendAsync(del1);
        Assert.That(del1Resp.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var del2 = Authed(HttpMethod.Delete, $"/notifications/devices/{deviceToken}", access);
        var del2Resp = await _client.SendAsync(del2);
        Assert.That(del2Resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task JobAndDashboardEndpoints_AreNotMappedUnderTesting()
    {
        // Testing disables Hangfire, so neither the dashboard nor the dev enqueue endpoint exist.
        var dashboard = await _client.GetAsync("/hangfire");
        Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var enqueue = await _client.PostAsync("/internal/jobs/heartbeat", null);
        Assert.That(enqueue.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
