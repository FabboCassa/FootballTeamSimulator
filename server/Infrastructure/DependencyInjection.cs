using Fts.Application.Auth;
using Fts.Application.Leagues;
using Fts.Application.Notifications;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Jobs;
using Fts.Infrastructure.Notifications;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Redis;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        this IServiceCollection services, IConfiguration config, bool enableBackgroundJobs = true)
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
        AddNotifications(services, config);
        AddBackgroundJobs(services, postgres, enableBackgroundJobs);

        // Private-league lifecycle (Phase 8.1): create/join/leave/list, backed by the shared
        // Sim.Core world generation. Scoped (it uses the request-scoped FtsDbContext).
        services.AddScoped<ILeagueService, LeagueService>();

        // Private-league season (Phase 8.3): submit inputs, ready/advance a round (all-ready mode),
        // read schedule/standings + replay. Scoped (request-scoped FtsDbContext + the shared engine).
        services.AddScoped<ILeagueSeasonService, LeagueSeasonService>();

        // Online auctions (Phase 8.5): authoritative bid/settlement engine over Postgres/EF. The
        // broadcaster + scheduler are no-op DEFAULTS (TryAdd) — the Api replaces the broadcaster with the
        // SignalR one, and AddBackgroundJobs (above) already registered the real Hangfire scheduler when
        // jobs are enabled, so TryAdd here leaves that in place and only fills the gap when jobs are off.
        services.AddScoped<IAuctionService, AuctionService>();
        services.TryAddScoped<IAuctionBroadcaster, NoOpAuctionBroadcaster>();
        services.TryAddScoped<IAuctionScheduler, NoOpAuctionScheduler>();

        // Dev-only test-league seeding (dev tooling). Always registered (harmless); the Api maps the
        // endpoints only outside Production and behind a config flag.
        services.AddScoped<Fts.Application.Dev.IDevSeedService, Fts.Infrastructure.Dev.DevSeedService>();

        return services;
    }

    /// <summary>Push notifications (Phase 7.4): the EF device-token store + the config-gated FCM sender.
    /// Both are scoped (they use the request-scoped <see cref="FtsDbContext"/>). Registered
    /// unconditionally — device registration works everywhere; the sender no-ops when FCM is unconfigured.</summary>
    private static void AddNotifications(IServiceCollection services, IConfiguration config)
    {
        // Bind FcmOptions by hand (no config-binder dependency in this class library), like JwtOptions.
        var fcm = config.GetSection(FcmOptions.SectionName);
        services.Configure<FcmOptions>(o =>
        {
            o.Enabled = bool.TryParse(fcm["Enabled"], out var enabled) && enabled;
            o.ProjectId = fcm["ProjectId"];
            o.CredentialsPath = fcm["CredentialsPath"];
            o.CredentialsJson = fcm["CredentialsJson"];
        });

        services.AddScoped<IDeviceRegistrationService, DeviceRegistrationService>();
        services.AddScoped<INotificationService, FirebaseNotificationService>();
    }

    /// <summary>Hangfire scheduler (Phase 7.4) with durable PostgreSQL storage (its own "hangfire"
    /// schema, created on startup). Skipped when <paramref name="enable"/> is false (unit tests run
    /// under the Testing environment with SQLite and no live Postgres) so the host starts without a
    /// storage connection. Jobs are activated from DI, so the job classes are registered too.</summary>
    private static void AddBackgroundJobs(IServiceCollection services, string postgres, bool enable)
    {
        // The recurring job type is resolvable regardless (so a direct unit test can new/inject it).
        services.AddScoped<HeartbeatJob>();

        if (!enable) return;

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // Hangfire.PostgreSql 1.20+: options-based connection setup; creates its schema if missing.
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(postgres)));

        services.AddHangfireServer();

        // Online-auction settlement (Phase 8.5): the DI-activated per-lot job + the real scheduler that
        // enqueues it (replacing the no-op default). Only when jobs are enabled — the Hangfire client the
        // scheduler uses does not exist otherwise, and the unit tests settle via the "close window" action.
        services.AddScoped<AuctionSettlementJob>();
        services.AddScoped<IAuctionScheduler, HangfireAuctionScheduler>();
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
