using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fts.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add …</c> can build the context without
/// starting the API host. No live database connection is needed to scaffold a migration —
/// the connection string is only used at runtime. Override with the
/// <c>FTS_MIGRATIONS_CONNECTION</c> env var if desired.
/// </summary>
public sealed class FtsDbContextFactory : IDesignTimeDbContextFactory<FtsDbContext>
{
    public FtsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("FTS_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=fts;Username=fts;Password=fts_dev_password";

        var options = new DbContextOptionsBuilder<FtsDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(FtsDbContextFactory).Assembly.FullName))
            .Options;

        return new FtsDbContext(options);
    }
}
