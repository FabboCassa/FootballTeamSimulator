using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fts.Application.Admin;
using Fts.Application.Auth;
using Fts.Application.Balance;
using Fts.Infrastructure.Admin;
using Fts.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The live-ops surface end to end (Phase 10.3): who may reach <c>/admin/*</c>, what a lock actually does
/// to an account, and that a balance push takes effect on the running server and can be rolled back.
///
/// The pyramid is shrunk through configuration (4-club groups, one group per tier) like every other ladder
/// fixture here, so enrolling a coach generates a small world instead of a full one.
///
/// The first admin is granted through the DI container rather than over HTTP, because that is exactly how
/// it happens in production: <c>Admin:BootstrapEmail</c> puts one account in the role at startup, since
/// granting the role otherwise requires an admin. Nothing in this fixture can create an admin over the
/// wire — that is the property being relied on, not a shortcut around it.
/// </summary>
[TestFixture]
public class AdminEndpointTests
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

    private static string UniqueEmail() => $"ops_{Guid.NewGuid():N}@example.com";

    private async Task<(string Token, Guid UserId, string Email)> Register(string? displayName = null)
    {
        string email = UniqueEmail();
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(email, ValidPassword, displayName ?? "Mister"));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "registration should succeed");
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.Profile.UserId, email);
    }

    /// <summary>Puts an account in the admin role the way the bootstrap does — through Identity, not
    /// through the API (there is no API path to it, by design).</summary>
    private async Task GrantAdmin(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        if (!await roles.RoleExistsAsync(AdminService.AdminRole))
            await roles.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = AdminService.AdminRole });

        var user = await users.FindByIdAsync(userId.ToString());
        await users.AddToRoleAsync(user!, AdminService.AdminRole);
    }

    private async Task<(string Token, Guid UserId, string Email)> RegisterAdmin()
    {
        var account = await Register("Operator");
        await GrantAdmin(account.UserId);
        return account;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<T> GetAs<T>(string url, string token)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, url, token));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"GET {url}");
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private IBalanceProvider Balance => _app.Services.GetRequiredService<IBalanceProvider>();

    // --- who may get in ----------------------------------------------------------------------------

    [Test]
    public async Task Admin_surface_rejects_anonymous_callers()
    {
        var resp = await _client.GetAsync("/admin/metrics");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Admin_surface_hides_itself_from_ordinary_accounts()
    {
        var player = await Register();

        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/admin/metrics", player.Token));

        // 404, not 403: a signed-in player should not learn that an admin API lives at this path.
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Admin_reads_the_metrics()
    {
        var admin = await RegisterAdmin();

        var metrics = await GetAs<AdminMetricsDto>("/admin/metrics", admin.Token);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.Environment, Is.EqualTo("Testing"));
            Assert.That(metrics.Version, Is.Not.Empty);
            Assert.That(metrics.SimCoreVersion, Is.Not.Empty);
            Assert.That(metrics.Accounts, Is.GreaterThanOrEqualTo(1));
            Assert.That(metrics.UptimeSeconds, Is.GreaterThanOrEqualTo(0));
            // Nothing pushed yet, so the instance is on the balance embedded in the build.
            Assert.That(metrics.BalanceRevision, Is.EqualTo(0));
            // Hangfire is not running under Testing — the metrics degrade to nulls rather than failing.
            Assert.That(metrics.FailedJobs, Is.Null);
        });
    }

    // --- accounts ----------------------------------------------------------------------------------

    [Test]
    public async Task User_search_matches_email_and_display_name()
    {
        var admin = await RegisterAdmin();
        var player = await Register("Zlatan Ibrahimovic");

        var byEmail = await GetAs<List<AdminUserDto>>($"/admin/users?q={player.Email}", admin.Token);
        // Upper-cased on purpose: the search must be case-insensitive on both PostgreSQL and SQLite.
        var byName = await GetAs<List<AdminUserDto>>("/admin/users?q=ZLATAN", admin.Token);

        Assert.Multiple(() =>
        {
            Assert.That(byEmail.Select(u => u.UserId), Does.Contain(player.UserId));
            Assert.That(byName.Select(u => u.UserId), Does.Contain(player.UserId));
            Assert.That(byEmail.Single(u => u.UserId == player.UserId).IsAdmin, Is.False);
            Assert.That(byEmail.Single(u => u.UserId == player.UserId).IsLocked, Is.False);
        });
    }

    [Test]
    public async Task Locking_an_account_stops_it_signing_in_and_refreshing()
    {
        var admin = await RegisterAdmin();

        // A fresh player with a live session (an access token AND a refresh token).
        string email = UniqueEmail();
        var registered = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(email, ValidPassword, "Banned"));
        var session = (await registered.Content.ReadFromJsonAsync<AuthResponse>())!;

        var lockResp = await _client.SendAsync(Authed(HttpMethod.Post,
            $"/admin/users/{session.Profile.UserId}/lock", admin.Token,
            new SetUserLockRequest(true, "collusion")));
        Assert.That(lockResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var locked = (await lockResp.Content.ReadFromJsonAsync<AdminUserDto>())!;
        Assert.That(locked.IsLocked, Is.True);

        // The password is still right — the account is simply not allowed in. 403, not 401, so the client
        // does not go into a refresh-and-retry loop that can never succeed.
        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        // And the session they were already holding cannot be extended: the lock revoked the refresh token.
        var refresh = await _client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.That(refresh.StatusCode, Is.AnyOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Unlocking_an_account_lets_it_back_in()
    {
        var admin = await RegisterAdmin();

        string email = UniqueEmail();
        var registered = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(email, ValidPassword, "Reprieved"));
        var session = (await registered.Content.ReadFromJsonAsync<AuthResponse>())!;
        var userId = session.Profile.UserId;

        await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/users/{userId}/lock", admin.Token,
            new SetUserLockRequest(true, "mistake")));
        await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/users/{userId}/lock", admin.Token,
            new SetUserLockRequest(false, "cleared")));

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task An_admin_cannot_lock_or_demote_themselves()
    {
        var admin = await RegisterAdmin();

        var selfLock = await _client.SendAsync(Authed(HttpMethod.Post,
            $"/admin/users/{admin.UserId}/lock", admin.Token, new SetUserLockRequest(true, "oops")));
        var selfDemote = await _client.SendAsync(Authed(HttpMethod.Post,
            $"/admin/users/{admin.UserId}/admin", admin.Token, new SetUserAdminRequest(false)));

        Assert.Multiple(() =>
        {
            Assert.That(selfLock.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(selfDemote.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Granting_and_revoking_the_admin_role_takes_effect_immediately()
    {
        var admin = await RegisterAdmin();
        var player = await Register();

        // Before: the surface does not exist as far as this account is concerned.
        var before = await _client.SendAsync(Authed(HttpMethod.Get, "/admin/metrics", player.Token));
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/users/{player.UserId}/admin",
            admin.Token, new SetUserAdminRequest(true)));

        // Immediately after — on the SAME access token. The role is checked against the database on every
        // request precisely so a grant (and a revoke) does not wait for a token to expire.
        var after = await _client.SendAsync(Authed(HttpMethod.Get, "/admin/metrics", player.Token));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/users/{player.UserId}/admin",
            admin.Token, new SetUserAdminRequest(false)));

        var revoked = await _client.SendAsync(Authed(HttpMethod.Get, "/admin/metrics", player.Token));
        Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- balance push ------------------------------------------------------------------------------

    [Test]
    public async Task Balance_starts_on_the_build_baseline()
    {
        var admin = await RegisterAdmin();

        var balance = await GetAs<AdminBalanceDto>("/admin/balance", admin.Token);

        Assert.Multiple(() =>
        {
            Assert.That(balance.IsBaseline, Is.True);
            Assert.That(balance.Revision, Is.EqualTo(0));
            Assert.That(balance.Json, Does.Contain("\"Match\""));
            Assert.That(Balance.Revision, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task A_partial_balance_document_is_refused()
    {
        var admin = await RegisterAdmin();

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest("{\"Match\":{}}", "just the match section")));

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "a fragment must be refused — deserialising it would silently reset every section it omits");
        Assert.That(Balance.Revision, Is.EqualTo(0), "a refused push must not move the live balance");
    }

    [Test]
    public async Task Rubbish_is_refused_without_touching_the_live_balance()
    {
        var admin = await RegisterAdmin();

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest("not json at all", null)));

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(Balance.Revision, Is.EqualTo(0));
    }

    [Test]
    public async Task A_pushed_balance_becomes_the_one_the_server_simulates_with()
    {
        var admin = await RegisterAdmin();

        var baseline = await GetAs<AdminBalanceDto>("/admin/balance", admin.Token);
        int baselineVersion = Balance.Current.Version;

        // Edit the document the dashboard hands the operator, exactly as the dashboard does.
        var doc = JsonNode.Parse(baseline.Json)!.AsObject();
        doc["Version"] = baselineVersion + 1;

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest(doc.ToJsonString(), "bump the config version")));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pushed = (await resp.Content.ReadFromJsonAsync<AdminBalanceDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(pushed.Revision, Is.EqualTo(1));
            Assert.That(pushed.IsBaseline, Is.False);
            Assert.That(pushed.Note, Is.EqualTo("bump the config version"));
            Assert.That(pushed.CreatedByEmail, Is.EqualTo(admin.Email));
            // The live provider — this is the whole point of the phase.
            Assert.That(Balance.Revision, Is.EqualTo(1));
            Assert.That(Balance.Current.Version, Is.EqualTo(baselineVersion + 1));
        });

        var metrics = await GetAs<AdminMetricsDto>("/admin/metrics", admin.Token);
        Assert.That(metrics.BalanceRevision, Is.EqualTo(1));
    }

    [Test]
    public async Task A_rollback_moves_forward_rather_than_rewriting_history()
    {
        var admin = await RegisterAdmin();

        var baseline = await GetAs<AdminBalanceDto>("/admin/balance", admin.Token);
        int baseVersion = Balance.Current.Version;

        var first = JsonNode.Parse(baseline.Json)!.AsObject();
        first["Version"] = baseVersion + 1;
        await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest(first.ToJsonString(), "first")));

        var second = JsonNode.Parse(baseline.Json)!.AsObject();
        second["Version"] = baseVersion + 2;
        await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest(second.ToJsonString(), "second")));

        Assert.That(Balance.Current.Version, Is.EqualTo(baseVersion + 2));

        var rollback = await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance/rollback/1",
            admin.Token));
        Assert.That(rollback.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var history = await GetAs<List<AdminBalanceRevisionDto>>("/admin/balance/history", admin.Token);

        Assert.Multiple(() =>
        {
            // Three rows, not two: the rollback is revision 3 carrying revision 1's payload.
            Assert.That(history, Has.Count.EqualTo(3));
            Assert.That(history[0].Revision, Is.EqualTo(3));
            Assert.That(history[0].RolledBackFrom, Is.EqualTo(1));
            Assert.That(history[0].IsActive, Is.True);
            Assert.That(history.Count(r => r.IsActive), Is.EqualTo(1));
            Assert.That(Balance.Revision, Is.EqualTo(3));
            Assert.That(Balance.Current.Version, Is.EqualTo(baseVersion + 1), "revision 1's numbers are back");
        });
    }

    [Test]
    public async Task Rolling_back_to_a_revision_that_does_not_exist_is_a_404()
    {
        var admin = await RegisterAdmin();

        var resp = await _client.SendAsync(
            Authed(HttpMethod.Post, "/admin/balance/rollback/99", admin.Token));

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- worlds + audit ----------------------------------------------------------------------------

    [Test]
    public async Task A_world_can_be_closed_to_enrolment_and_reopened()
    {
        var admin = await RegisterAdmin();

        // Enrolling one coach is what brings a ranked world into existence.
        var player = await Register();
        var enrol = await _client.SendAsync(Authed(HttpMethod.Post, "/ranked/enrol", player.Token));
        Assert.That(enrol.StatusCode, Is.EqualTo(HttpStatusCode.OK), "enrolment should open a world");

        var worlds = await GetAs<List<AdminWorldDto>>("/admin/worlds", admin.Token);
        Assert.That(worlds, Is.Not.Empty);
        var world = worlds[0];

        Assert.Multiple(() =>
        {
            Assert.That(world.Status, Is.EqualTo("Open"));
            Assert.That(world.Groups, Is.GreaterThan(0));
            Assert.That(world.HumanCoaches, Is.EqualTo(1));
            Assert.That(world.Seats, Is.GreaterThan(0));
        });

        var closed = await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/worlds/{world.Id}/open",
            admin.Token, new SetWorldOpenRequest(false, "misbehaving")));
        Assert.That(closed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await closed.Content.ReadFromJsonAsync<AdminWorldDto>())!.Status, Is.EqualTo("Closed"));

        var reopened = await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/worlds/{world.Id}/open",
            admin.Token, new SetWorldOpenRequest(true, "fixed")));
        Assert.That((await reopened.Content.ReadFromJsonAsync<AdminWorldDto>())!.Status, Is.EqualTo("Open"));
    }

    [Test]
    public async Task Every_admin_action_leaves_an_audit_line()
    {
        var admin = await RegisterAdmin();
        var player = await Register();

        var empty = await GetAs<List<AdminAuditDto>>("/admin/audit", admin.Token);
        Assert.That(empty, Is.Empty, "reads must not be audited — only actions");

        await _client.SendAsync(Authed(HttpMethod.Post, $"/admin/users/{player.UserId}/lock",
            admin.Token, new SetUserLockRequest(true, "spam")));

        var baseline = await GetAs<AdminBalanceDto>("/admin/balance", admin.Token);
        var doc = JsonNode.Parse(baseline.Json)!.AsObject();
        doc["Version"] = Balance.Current.Version + 1;
        await _client.SendAsync(Authed(HttpMethod.Post, "/admin/balance", admin.Token,
            new PushBalanceRequest(doc.ToJsonString(), "tuning")));

        var audit = await GetAs<List<AdminAuditDto>>("/admin/audit", admin.Token);

        Assert.Multiple(() =>
        {
            Assert.That(audit, Has.Count.EqualTo(2));
            Assert.That(audit.Select(a => a.Action), Does.Contain(AdminAction.UserLock));
            Assert.That(audit.Select(a => a.Action), Does.Contain(AdminAction.BalancePush));
            Assert.That(audit.All(a => a.ActorUserId == admin.UserId), Is.True);
            Assert.That(audit.All(a => a.ActorEmail == admin.Email), Is.True);
            // The operator's reason survives — it is the only thing anyone reads afterwards.
            Assert.That(audit.Single(a => a.Action == AdminAction.UserLock).Details, Does.Contain("spam"));
        });
    }
}
