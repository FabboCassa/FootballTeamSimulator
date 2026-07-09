using Fts.Application.Auth;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Redis;
using Microsoft.AspNetCore.Identity;
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

        AddAuth(services, config);

        return services;
    }

    /// <summary>ASP.NET Core Identity (users/passwords) + the JWT/refresh-token services (Phase 7.2).
    /// The Api adds the JwtBearer authentication middleware; here we own the stores and the use cases.</summary>
    private static void AddAuth(IServiceCollection services, IConfiguration config)
    {
        // Bind JwtOptions from the "Jwt" section without taking a dependency on the config binder
        // package (this is a plain class library) — parse the primitives by hand.
        var jwt = config.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(o =>
        {
            o.Issuer = jwt["Issuer"] ?? o.Issuer;
            o.Audience = jwt["Audience"] ?? o.Audience;
            o.SigningKey = jwt["SigningKey"] ?? o.SigningKey;
            if (int.TryParse(jwt["AccessTokenMinutes"], out var m)) o.AccessTokenMinutes = m;
            if (int.TryParse(jwt["RefreshTokenDays"], out var d)) o.RefreshTokenDays = d;
        });

        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FtsDbContext>();

        services.AddScoped<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
    }
}
