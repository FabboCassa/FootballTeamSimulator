using System.Text;
using Fts.Api.Auctions;
using Fts.Api.Matches;
using Fts.Api.Auth;
using Fts.Api.Dev;
using Fts.Api.Jobs;
using Fts.Api.Leagues;
using Fts.Api.Notifications;
using Fts.Api.Ranked;
using Fts.Api.Simulation;
using Fts.Application.Leagues;
using Fts.Application.Simulation;
using Fts.Infrastructure;
using Fts.Infrastructure.Jobs;
using Fts.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

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

// Liveness - also proves the server runs the same Sim.Core DLL as the client.
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
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
        scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().AddOrUpdate<HeartbeatJob>(
            HeartbeatJob.RecurringJobId,
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
