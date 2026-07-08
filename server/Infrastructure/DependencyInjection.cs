using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Fts.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: EF Core (PostgreSQL), Redis, and the
/// readiness health checks. The API calls <see cref="AddFtsInfrastructure"/> once at startup.
/// </summary>
public static class DependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddFtsInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres.");
        var redis = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis.");

        services.AddDbContext<FtsDbContext>(options =>
            options.UseNpgsql(postgres, npgsql =>
                npgsql.MigrationsAssembly(typeof(FtsDbContext).Assembly.FullName)));

        // Lazy, resilient multiplexer: AbortOnConnectFail=false lets the API start even if
        // Redis is briefly unavailable; the health check surfaces the real state.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var opts = ConfigurationOptions.Parse(redis);
            opts.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(opts);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<FtsDbContext>("postgres", tags: new[] { ReadyTag })
            .AddCheck<RedisHealthCheck>("redis", tags: new[] { ReadyTag });

        return services;
    }
}
