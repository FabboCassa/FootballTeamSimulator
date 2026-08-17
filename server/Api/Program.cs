using System.Reflection;
using System.Text;
using Fts.Api.Admin;
using Fts.Api.Auctions;
using Fts.Api.Matches;
using Fts.Api.Auth;
using Fts.Api.Dev;
using Fts.Api.Integrity;
using Fts.Api.Jobs;
using Fts.Api.Leagues;
using Fts.Api.Notifications;
using Fts.Api.Ranked;
using Fts.Api.Simulation;
using Fts.Application.Admin;
using Fts.Application.Balance;
using Fts.Application.Leagues;
using Fts.Application.Simulation;
using Fts.Infrastructure;
using Fts.Infrastructure.Admin;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Balance;
using Fts.Infrastructure.Jobs;
using Fts.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sim.Core;

var builder = WebApplication.CreateBuilder(args);

// Background jobs (Hangfire, Phase 7.4) need durable Postgres storage; the Testing environment runs
// unit tests on SQLite with no live Postgres, so the scheduler is disabled there (the rest wires up).
var backgroundJobsEnabled = !builder.Environment.IsEnvironment("Testing");

// EF Core (PostgreSQL) + Redis + readiness health checks + Identity/auth services (Phase 7.2) +
// notifications & Hangfire (Phase 7.4).
builder.Services.AddFtsInfrastructure(builder.Configuration, backgroundJobsEnabled);

// Online auctions (Phase 8.5): SignalR for live bid pushes + the real broadcaster (replaces the
// Infrastructure no-op). The REST endpoints stay authoritative; this is the live push layer, reused for
// live match control in 8.6.
builder.Services.AddSignalR();
builder.Services.AddScoped<IAuctionBroadcaster, SignalRAuctionBroadcaster>();

// Live match control (Phase 8.6): the real MatchHub broadcaster (replaces the Infrastructure no-op). The
// REST endpoints stay authoritative; this is the live push layer over the second SignalR hub.
builder.Services.AddScoped<ILiveMatchBroadcaster, SignalRMatchBroadcaster>();

// JWT bearer authentication — validation parameters mirror the JwtTokenService signing settings.
var jwt = builder.Configuration.GetSection("Jwt");
var signingKey = jwt["SigningKey"]
    ?? throw new InvalidOperationException("Missing Jwt:SigningKey.");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"] ?? "fts",
            ValidateAudience = true,
            ValidAudience = jwt["Audience"] ?? "fts-client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SignalR WebSockets can't send an Authorization header on the handshake, so the AuctionHub
        // (Phase 8.5) passes the access token via the access_token query string — pull it for /hubs paths.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Match simulation runs the same Sim.Core as the client (Phase 7.3). Stateless + no I/O → singleton.
builder.Services.AddSingleton<ISimulationService, SimulationService>();

// Abuse & integrity (Phase 9.5): per-account rate limits on the ranked write surface. Framework-native
// (no new dependency); the permit counts live in the Integrity configuration section.
builder.Services.AddFtsRateLimiting();

// Browser origins allowed to call this API (Phase 10.2a): the hosted WebGL build and the public
// account-deletion page. Empty by default, so nothing changes for the native clients — a phone or a
// desktop player is not a browser and never sends an Origin. Configure per environment, e.g.
// Cors__AllowedOrigins__0=https://footballteamsimulator.pages.dev
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").GetChildren()
    .Select(c => c.Value)
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .Select(v => v!.Trim())
    .ToArray();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

// The release version this build carries (Roadmap 10.2). InformationalVersion carries the
// SourceRevisionId suffix ("0.1.0+<sha>") when built in a repo; only the marketing part is reported.
// Computed BEFORE the container is built because live ops (10.3) needs it as a registered singleton, not
// just as a closure over the /health handler.
var apiVersion = (typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0")
    .Split('+')[0];

// The three facts about this instance that Infrastructure cannot discover for itself (Phase 10.3). The
// Api knows them all already — they are what /health reports — so it hands them over rather than making a
// class library take a dependency on the hosting abstractions.
builder.Services.AddSingleton(new AdminRuntimeInfo(
    Version: apiVersion,
    SimCoreVersion: SimCoreInfo.Version,
    Environment: builder.Environment.EnvironmentName,
    StartedUtc: DateTime.UtcNow));

var app = builder.Build();

// Before authentication so a rejected pre-flight still carries the CORS headers.
if (allowedOrigins.Length > 0) app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Rate limiting AFTER authentication so the buckets partition per ACCOUNT rather than per address —
// see IntegrityRateLimits for why that distinction matters behind a shared NAT (Phase 9.5).
app.UseRateLimiter();

// Record where authenticated ranked requests come from (hashed, never raw) so the multi-account
// heuristics have evidence at enrolment time (Phase 9.5). Runs after the response, never fails a request.
app.UseFtsIntegritySignals();

// Apply pending EF migrations on startup (dev convenience so `docker compose up` ⇒ DB
// migrated). Skipped under the Testing environment (no live DB) and gated by a config flag
// so production can switch to an explicit migrator later.
if (!app.Environment.IsEnvironment("Testing")
    && app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
    db.Database.Migrate();
}

// Live ops bootstrap (Phase 10.3), in this order and for these reasons:
//   1. the "admin" role must exist before anyone can be put in it;
//   2. Admin:BootstrapEmail promotes ONE account by email — this is how the first admin comes into
//      existence on a fresh deployment, since there is no admin to grant the role. It is idempotent and
//      config-driven (Admin__BootstrapEmail as an env var), so the answer to "how do I get in?" is a
//      deployment setting rather than a hard-coded account or a seeded password;
//   3. the active balance revision is loaded, so the first request already simulates on the pushed
//      numbers instead of the build's defaults for however long the reload job takes to fire.
// Skipped under Testing for the same reason as the migration above: the test host creates its SQLite
// schema AFTER the app has started, so anything that touches a table here would run against a database
// that does not have one yet. The pieces that matter to a test are reachable without this block —
// AdminService creates the role on the grant path, and the balance provider starts on the build's
// defaults, which is exactly what a test wants as a baseline.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;

    var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync(AdminService.AdminRole))
        await roles.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = AdminService.AdminRole });

    var bootstrapEmail = app.Configuration["Admin:BootstrapEmail"]?.Trim();
    if (!string.IsNullOrWhiteSpace(bootstrapEmail))
    {
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var bootstrapUser = await users.FindByEmailAsync(bootstrapEmail);
        if (bootstrapUser is null)
        {
            app.Logger.LogWarning(
                "Admin:BootstrapEmail is set to {Email} but no such account exists yet — register it and "
                + "restart, or grant the role from an existing admin.", bootstrapEmail);
        }
        else if (!await users.IsInRoleAsync(bootstrapUser, AdminService.AdminRole))
        {
            await users.AddToRoleAsync(bootstrapUser, AdminService.AdminRole);
            app.Logger.LogWarning("Granted the admin role to {Email} from Admin:BootstrapEmail.", bootstrapEmail);
        }
    }

    await BalanceStore.LoadActiveAsync(
        sp.GetRequiredService<FtsDbContext>(),
        sp.GetRequiredService<IBalanceProvider>(),
        app.Logger);
}

// Liveness - also proves the server runs the same Sim.Core DLL as the client, and reports the
// release version (Roadmap 10.2) so a deployed instance can be identified without shell access.
// `apiVersion` is computed above, before the container is built (live ops registers it as a singleton).
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    version = apiVersion,
    simCore = SimCoreInfo.Version,
    utc = DateTime.UtcNow
}));

// Readiness - PostgreSQL reachable + migrated and Redis reachable.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(DependencyInjection.ReadyTag)
});

// Auth: /auth/register, /auth/login, /auth/refresh, /auth/logout, /auth/me (Phase 7.2).
app.MapAuthEndpoints();

// Device registration for push notifications (Phase 7.4): a real, JWT-protected API. Always mapped.
app.MapNotificationEndpoints();

// Private-league lifecycle (Phase 8.1): create/join/leave/list, JWT-protected. Always mapped.
app.MapLeagueEndpoints();

// Online auctions (Phase 8.5): open/close a window, read lots, bid — JWT-protected. Plus the live
// AuctionHub for real-time bid pushes. Always mapped.
app.MapAuctionEndpoints();
app.MapHub<AuctionHub>("/hubs/auction");

// Live match control (Phase 8.6): open/join/change/finish a live human-vs-human fixture, JWT-protected.
// Plus the MatchHub for real-time state pushes. Always mapped.
app.MapLiveMatchEndpoints();
app.MapHub<MatchHub>("/hubs/match");

// Public ranked ladder (Phase 9.1): enrol, read your ladder state / a group's fixed-size seat list,
// toggle auto re-enrolment — JWT-protected. Always mapped.
app.MapRankedEndpoints();

// Live ops (Phase 10.3): metrics, worlds, accounts and the balance push. Mapped in EVERY environment,
// Production included — this is the surface an operator needs precisely when things are live. It is gated
// by the admin ROLE (AdminOnlyFilter), not by the environment flags the dev endpoints use, and a
// signed-in non-admin gets a 404 rather than a 403 so its existence is not advertised.
app.MapAdminEndpoints();

// Ranked lifecycle (Phase 9.1): closing a placement season and sorting its coaches into divisions is a
// SERVER action, not a player one — exposed as a dev-only internal endpoint until the 9.2 real-time
// season scheduler drives it. Never mapped in Production, and behind a config flag.
if (!app.Environment.IsProduction()
    && app.Configuration.GetValue("Ranked:ExposeInternalEndpoints", true))
{
    app.MapRankedInternalEndpoints();
}

// Internal match-simulation endpoints (Phase 7.3): dev-only — never mapped in Production, and
// behind a config flag (default on outside prod) so a deployment can also switch them off.
if (!app.Environment.IsProduction()
    && app.Configuration.GetValue("Simulation:ExposeInternalEndpoints", true))
{
    app.MapSimulationEndpoints();
}

// Dev-only test-league seeding (dev tooling): spin up a ready online league (bots + draft) in one call
// so online features can be tested without hand-creating accounts. Never mapped in Production, and behind
// the Dev:ExposeSeedEndpoints flag. These endpoints are UNAUTHENTICATED — they must never reach prod.
if (!app.Environment.IsProduction()
    && app.Configuration.GetValue("Dev:ExposeSeedEndpoints", true))
{
    app.MapDevEndpoints();
}

// Background jobs (Phase 7.4): register the recurring heartbeat that proves the scheduler fires,
// and expose the dev-only dashboard + job-enqueue diagnostics (gated like the sim endpoints).
if (backgroundJobsEnabled)
{
    // Use the DI-resolved manager, NOT the static RecurringJob API: the static one reads
    // JobStorage.Current, which the service-based Hangfire.NetCore setup does not populate at
    // startup (it would throw "Current JobStorage instance has not been initialized yet").
    using (var scope = app.Services.CreateScope())
    {
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<HeartbeatJob>(
            HeartbeatJob.RecurringJobId,
            j => j.ExecuteAsync(CancellationToken.None),
            Cron.Minutely());

        // The ranked real-time season calendar (Phase 9.2): each minute, start due seasons, resolve due
        // matchdays, open market windows and close/sort finished seasons. Per-fixture kickoff times gate
        // what actually fires, so minutely polling just asks "is anything due yet".
        recurring.AddOrUpdate<RankedSeasonJob>(
            RankedSeasonJob.RecurringJobId,
            j => j.ExecuteAsync(CancellationToken.None),
            Cron.Minutely());

        // Balance push propagation (Phase 10.3): a push swaps the config on the instance that served it,
        // and this brings every other instance up to the same revision within a minute. Cheap — it reads
        // one row and does nothing when the revision has not moved.
        recurring.AddOrUpdate<BalanceReloadJob>(
            BalanceReloadJob.RecurringJobId,
            j => j.ExecuteAsync(CancellationToken.None),
            Cron.Minutely());
    }

    if (!app.Environment.IsProduction()
        && app.Configuration.GetValue("Jobs:ExposeDashboard", true))
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
        });
    }

    if (!app.Environment.IsProduction()
        && app.Configuration.GetValue("Jobs:ExposeTestEndpoint", true))
    {
        app.MapJobEndpoints();
    }
}

app.Run();

// Exposed for WebApplicationFactory in Api.Tests.
public partial class Program;
