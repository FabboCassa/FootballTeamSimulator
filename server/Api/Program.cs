using System.Text;
using Fts.Api.Auth;
using Fts.Infrastructure;
using Fts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sim.Core;

var builder = WebApplication.CreateBuilder(args);

// EF Core (PostgreSQL) + Redis + readiness health checks + Identity/auth services (Phase 7.2).
builder.Services.AddFtsInfrastructure(builder.Configuration);

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
    });
builder.Services.AddAuthorization();

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

app.Run();

// Exposed for WebApplicationFactory in Api.Tests.
public partial class Program;
