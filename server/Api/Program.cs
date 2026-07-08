using Fts.Infrastructure;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Sim.Core;

var builder = WebApplication.CreateBuilder(args);

// EF Core (PostgreSQL) + Redis + readiness health checks.
builder.Services.AddFtsInfrastructure(builder.Configuration);

var app = builder.Build();

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

app.Run();

// Exposed for WebApplicationFactory in Api.Tests.
public partial class Program;
