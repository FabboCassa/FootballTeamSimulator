using Fts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fts.Api.Tests;

/// <summary>
/// Spins up the real API host but swaps PostgreSQL for a shared in-memory SQLite database so the
/// auth flow (Identity stores + rotating refresh tokens) can be exercised end-to-end without a
/// live DB. Runs under the "Testing" environment so startup skips the Npgsql auto-migration; the
/// schema is created here via <c>EnsureCreated</c>. The Jwt signing key comes from the API's
/// appsettings.json (dev placeholder). The single SQLite connection is kept open for the factory's
/// lifetime so the in-memory database survives between requests.
/// </summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuthTestFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Silence the host's own logging. EF Core logs every SQL statement at Information, which for a
        // suite that generates whole worlds means tens of thousands of lines: locally it is noise, in CI
        // it is worse than noise — GitHub truncates the step and the actual failure becomes unreadable.
        // Warnings and errors still come through, and the tests' own TestContext output is untouched.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.ConfigureTestServices(services =>
        {
            // Drop the Npgsql provider wiring and re-add it on SQLite. Remove every options-related
            // descriptor — DbContextOptions<T>, DbContextOptions, AND IDbContextOptionsConfiguration<T>
            // (EF 10 registers the latter too; leaving it in would keep Npgsql active alongside SQLite
            // → "only a single database provider can be registered").
            var toRemove = services
                .Where(d => d.ServiceType.FullName is { } n && n.Contains("DbContextOptions"))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<FtsDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
