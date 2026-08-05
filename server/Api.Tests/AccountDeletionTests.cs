using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Account deletion end-to-end (Phase 10.2a) — the store-blocking requirement: an account can delete
/// itself from inside the app, and what it leaves behind is either gone or anonymous.
///
/// The ranked pyramid is shrunk through configuration (4-club groups, one group per tier) exactly as the
/// other ladder fixtures do, so enrolling a coach generates a small world instead of a full one.
/// </summary>
[TestFixture]
public class AccountDeletionTests
{
    private const string ValidPassword = "Password1";

    private AuthTestFactory _root = null!;
    private WebApplicationFactory<Program> _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new AuthTestFactory();
        _app = _root.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ranked:GroupSize"] = "4",
                ["Ranked:PlacementGroupSize"] = "4",
                ["Ranked:Tier1Groups"] = "1",
                ["Ranked:Tier2Groups"] = "1",
                ["Ranked:Tier3Groups"] = "1",
            })));
        _client = _app.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app.Dispose();
        _root.Dispose();
    }

    // --- helpers -----------------------------------------------------------------------------------

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(string Email, string Token, string Refresh, Guid UserId)> RegisterAccount()
    {
        var email = UniqueEmail();
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(email, ValidPassword, "Mister"));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        return (email, auth.AccessToken, auth.RefreshToken, auth.Profile.UserId);
    }

    private async Task<HttpResponseMessage> Delete(string token, string password) =>
        await _client.SendAsync(Authed(HttpMethod.Post, "/auth/account/delete", token,
            new DeleteAccountRequest(password)));

    private IServiceScope Scope() => _app.Services.CreateScope();

    // --- the ✅ ------------------------------------------------------------------------------------

    [Test]
    public async Task DeletingAnAccount_WipesThePersonalData_AnonymisesTheLadderHistory_AndTheCredentialsStopWorking()
    {
        var (email, token, refresh, userId) = await RegisterAccount();

        // Give the account something in every corner of the system.
        using (var device = Authed(HttpMethod.Post, "/notifications/devices", token,
                   new RegisterDeviceRequest($"dev_{Guid.NewGuid():N}", DevicePlatform.Android)))
            Assert.That((await _client.SendAsync(device)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var league = Authed(HttpMethod.Post, "/leagues", token,
                   new CreateLeagueRequest("Amici FC", 4, LeagueMode.AllReady)))
            Assert.That((await _client.SendAsync(league)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var enrol = Authed(HttpMethod.Post, "/ranked/enrol", token))
            Assert.That((await _client.SendAsync(enrol)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // A palmarès line, as a completed season would have written it.
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
            db.RankedAwards.Add(new RankedAward
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = RankedAwardKind.SeasonPlayed,
                WorldName = "Mondo 1",
                GroupName = "Divisione 2A",
                Tier = 2,
                Position = 3,
                SeasonNumber = 1,
                RatingAfter = 1010,
                RatingDelta = 10,
                AwardedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            Assert.That(await db.RankedSeats.CountAsync(s => s.UserId == userId), Is.EqualTo(1),
                "the coach holds a ladder seat before the deletion");
        }

        var response = await Delete(token, ValidPassword);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var summary = (await response.Content.ReadFromJsonAsync<DeleteAccountResult>())!;
        Assert.That(summary.PrivateLeaguesLeft, Is.EqualTo(1));
        Assert.That(summary.SessionsRevoked, Is.GreaterThan(0));
        Assert.That(summary.DevicesRemoved, Is.EqualTo(1));
        Assert.That(summary.RankedHistoryAnonymised, Is.True);
        Assert.That(summary.RankedAwardsAnonymised, Is.EqualTo(1));

        // 1. The credentials are dead: the old access token, the password and the refresh token.
        using (var me = Authed(HttpMethod.Get, "/auth/me", token))
            Assert.That((await _client.SendAsync(me)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "the access token no longer resolves to an account");

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var refreshed = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(refresh));
        Assert.That(refreshed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // 2. Nothing that names the person is left; the ladder history survives, belonging to nobody.
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();

            Assert.That(await db.CoachProfiles.AnyAsync(p => p.UserId == userId), Is.False);
            Assert.That(await db.RefreshTokens.AnyAsync(t => t.UserId == userId), Is.False);
            Assert.That(await db.DeviceRegistrations.AnyAsync(d => d.UserId == userId), Is.False);
            Assert.That(await db.AccountSignals.AnyAsync(s => s.UserId == userId), Is.False);
            Assert.That(await db.LeagueMembers.AnyAsync(m => m.UserId == userId), Is.False);
            Assert.That(await db.PrivateLeagues.AnyAsync(), Is.False,
                "the only member left, so the league and its world were torn down");

            Assert.That(await db.RankedSeats.AnyAsync(s => s.UserId == userId), Is.False,
                "the ladder seat was freed and plays on as AI");
            Assert.That(await db.RankedCoaches.AnyAsync(c => c.UserId == userId), Is.False);
            Assert.That(await db.RankedAwards.AnyAsync(a => a.UserId == userId), Is.False);

            var coach = await db.RankedCoaches.SingleAsync();
            var award = await db.RankedAwards.SingleAsync();
            Assert.That(coach.Status, Is.EqualTo(RankedCoachStatus.Retired));
            Assert.That(coach.SeatId, Is.Null);
            Assert.That(coach.AutoEnrol, Is.False);
            Assert.That(award.UserId, Is.EqualTo(coach.UserId),
                "the coach row and its palmarès were re-stamped with the same anonymous id");
            Assert.That(award.GroupName, Is.EqualTo("Divisione 2A"), "the season record itself is intact");
        }
    }

    // --- guards ------------------------------------------------------------------------------------

    [Test]
    public async Task Delete_WithTheWrongPassword_IsRefused_AndTheAccountStillWorks()
    {
        var (_, token, _, userId) = await RegisterAccount();

        var response = await Delete(token, "NotMyPassword1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var me = Authed(HttpMethod.Get, "/auth/me", token);
        Assert.That((await _client.SendAsync(me)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        Assert.That(await db.CoachProfiles.AnyAsync(p => p.UserId == userId), Is.True);
    }

    [Test]
    public async Task Delete_WithoutAToken_IsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/account/delete",
            new DeleteAccountRequest(ValidPassword));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Delete_Twice_IsHarmless()
    {
        var (_, token, _, _) = await RegisterAccount();

        Assert.That((await Delete(token, ValidPassword)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The token still parses, but there is no account behind it any more.
        Assert.That((await Delete(token, ValidPassword)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Delete_LeavesTheLeagueRunning_ForTheMembersWhoStay()
    {
        var creator = await RegisterAccount();
        var friend = await RegisterAccount();

        using var create = Authed(HttpMethod.Post, "/leagues", creator.Token,
            new CreateLeagueRequest("Amici FC", 4, LeagueMode.AllReady));
        var detail = (await (await _client.SendAsync(create)).Content.ReadFromJsonAsync<LeagueDetailDto>())!;

        using (var join = Authed(HttpMethod.Post, "/leagues/join", friend.Token,
                   new JoinLeagueRequest(detail.League.InviteCode)))
            Assert.That((await _client.SendAsync(join)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await Delete(creator.Token, ValidPassword)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var get = Authed(HttpMethod.Get, $"/leagues/{detail.League.Id}", friend.Token);
        var resp = await _client.SendAsync(get);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the league outlives the account that made it");

        var after = (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
        Assert.That(after.Members.Count, Is.EqualTo(1));
        Assert.That(after.Members.Single().UserId, Is.EqualTo(friend.UserId));
        Assert.That(after.League.IsCreator, Is.True, "ownership passed to the member who stayed");
    }
}
