using Fts.Application.Auth;
using Fts.Application.Integrity;
using Fts.Application.Leagues;
using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Integrity;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Jobs;
using Fts.Infrastructure.Notifications;
using Fts.Infrastructure.Ranked;
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

        // Live match control (Phase 8.6): authoritative live session over Postgres/EF (deterministic
        // re-sim on each pause-point input). The broadcaster is a no-op DEFAULT (TryAdd) — the Api
        // replaces it with the SignalR MatchHub one; the unit tests keep the no-op and prove state via REST.
        services.AddScoped<ILiveMatchService, LiveMatchService>();
        services.TryAddScoped<ILiveMatchBroadcaster, NoOpLiveMatchBroadcaster>();

        // Public ranked ladder (Phase 9.1): server-managed pyramid worlds, enrolment, placement seasons
        // and division placement. Scoped (request-scoped FtsDbContext + Sim.Core world generation).
        // `enableBackgroundJobs` doubles as "this environment has the real infrastructure" — it is false
        // only under Testing (SQLite, no Redis container), which decides the leaderboard cache below.
        AddRanked(services, config, enableBackgroundJobs);

        // Abuse & integrity (Phase 9.5): price-band guards, multi-account heuristics, player reports and
        // the review queue. Registered ALWAYS — the ranked services depend on it, and the guards must be
        // live in every environment (a test that could silently run without them would prove nothing).
        AddIntegrity(services, config);

        // Dev-only test-league seeding (dev tooling). Always registered (harmless); the Api maps the
        // endpoints only outside Production and behind a config flag.
        services.AddScoped<Fts.Application.Dev.IDevSeedService, Fts.Infrastructure.Dev.DevSeedService>();

        return services;
    }

    /// <summary>Abuse &amp; integrity (Phase 9.5). Knobs bound by hand from the "Integrity" section, same
    /// convention as <see cref="RankedOptions"/>, so a live ladder can be tightened (or a guard switched
    /// off in an emergency) without a redeploy.</summary>
    private static void AddIntegrity(IServiceCollection services, IConfiguration config)
    {
        var integrity = config.GetSection(IntegrityOptions.SectionName);
        services.Configure<IntegrityOptions>(o =>
        {
            // Transfer price bands.
            if (int.TryParse(integrity["MinFeePercentOfValue"], out var minPct) && minPct >= 0)
                o.MinFeePercentOfValue = minPct;
            if (int.TryParse(integrity["MaxFeePercentOfValue"], out var maxPct) && maxPct > 0)
                o.MaxFeePercentOfValue = maxPct;
            if (int.TryParse(integrity["SuspiciousLowPercentOfValue"], out var susLow) && susLow >= 0)
                o.SuspiciousLowPercentOfValue = susLow;
            if (int.TryParse(integrity["SuspiciousHighPercentOfValue"], out var susHigh) && susHigh > 0)
                o.SuspiciousHighPercentOfValue = susHigh;
            if (long.TryParse(integrity["MinPlayerValueChecked"], out var minValue) && minValue >= 0)
                o.MinPlayerValueChecked = minValue;
            if (int.TryParse(integrity["RepeatedTradesPerPairThreshold"], out var pairs) && pairs > 0)
                o.RepeatedTradesPerPairThreshold = pairs;

            // Multi-account heuristics.
            if (bool.TryParse(integrity["EnableMultiAccountHeuristics"], out var heuristics))
                o.EnableMultiAccountHeuristics = heuristics;
            if (int.TryParse(integrity["SharedAddressPoints"], out var addrPts) && addrPts >= 0)
                o.SharedAddressPoints = addrPts;
            if (int.TryParse(integrity["SharedDevicePoints"], out var devPts) && devPts >= 0)
                o.SharedDevicePoints = devPts;
            if (int.TryParse(integrity["CreatedTogetherPoints"], out var togetherPts) && togetherPts >= 0)
                o.CreatedTogetherPoints = togetherPts;
            if (int.TryParse(integrity["CreatedTogetherMinutes"], out var togetherMins) && togetherMins >= 0)
                o.CreatedTogetherMinutes = togetherMins;
            if (int.TryParse(integrity["LinkScoreThreshold"], out var threshold) && threshold > 0)
                o.LinkScoreThreshold = threshold;
            if (!string.IsNullOrWhiteSpace(integrity["SignalSalt"]))
                o.SignalSalt = integrity["SignalSalt"]!;
            if (int.TryParse(integrity["SignalRefreshMinutes"], out var refresh) && refresh >= 0)
                o.SignalRefreshMinutes = refresh;

            // Reports.
            if (int.TryParse(integrity["MaxReportsPerDay"], out var maxReports) && maxReports > 0)
                o.MaxReportsPerDay = maxReports;
            if (int.TryParse(integrity["MaxReportDetailsLength"], out var detailLen) && detailLen > 0)
                o.MaxReportDetailsLength = detailLen;

            // Input deadlines.
            if (bool.TryParse(integrity["EnforceLineupDeadline"], out var deadline))
                o.EnforceLineupDeadline = deadline;
            if (int.TryParse(integrity["LineupLockSeconds"], out var lockSecs) && lockSecs >= 0)
                o.LineupLockSeconds = lockSecs;

            // Rate limits.
            if (bool.TryParse(integrity["EnableRateLimiting"], out var limiting))
                o.EnableRateLimiting = limiting;
            if (int.TryParse(integrity["RateWindowSeconds"], out var window) && window > 0)
                o.RateWindowSeconds = window;
            if (int.TryParse(integrity["WritesPerWindow"], out var writes) && writes > 0)
                o.WritesPerWindow = writes;
            if (int.TryParse(integrity["BidsPerWindow"], out var bids) && bids > 0)
                o.BidsPerWindow = bids;
            if (int.TryParse(integrity["ReportsPerWindow"], out var reports) && reports > 0)
                o.ReportsPerWindow = reports;
        });

        services.AddScoped<IIntegrityService, IntegrityService>();
    }

    /// <summary>Public ranked ladder (Phase 9.1). The pyramid shape lives in <see cref="RankedOptions"/>,
    /// bound by hand from the "Ranked" section (no config-binder dependency in this class library, same as
    /// JwtOptions/FcmOptions) so a deployment — or a test — can reshape the pyramid without a code change.</summary>
    private static void AddRanked(IServiceCollection services, IConfiguration config, bool redisAvailable)
    {
        var ranked = config.GetSection(RankedOptions.SectionName);
        services.Configure<RankedOptions>(o =>
        {
            if (int.TryParse(ranked["GroupSize"], out var groupSize) && groupSize > 1) o.GroupSize = groupSize;
            if (int.TryParse(ranked["PlacementGroupSize"], out var placementSize) && placementSize > 1)
                o.PlacementGroupSize = placementSize;
            if (int.TryParse(ranked["Tier1Groups"], out var t1) && t1 >= 0) o.Tier1Groups = t1;
            if (int.TryParse(ranked["Tier2Groups"], out var t2) && t2 >= 0) o.Tier2Groups = t2;
            if (int.TryParse(ranked["Tier3Groups"], out var t3) && t3 >= 0) o.Tier3Groups = t3;
            if (int.TryParse(ranked["PlacementTopPositionsToUpperTier"], out var top) && top >= 0)
                o.PlacementTopPositionsToUpperTier = top;
            if (int.TryParse(ranked["StartingRating"], out var rating) && rating > 0) o.StartingRating = rating;
            if (int.TryParse(ranked["RatingPerPlacementPosition"], out var step) && step >= 0)
                o.RatingPerPlacementPosition = step;
            // Real-time calendar knobs (Phase 9.2) — allow 0 (tests compress the calendar).
            if (int.TryParse(ranked["MatchdayIntervalSeconds"], out var interval) && interval >= 0)
                o.MatchdayIntervalSeconds = interval;
            if (int.TryParse(ranked["MarketWindowDurationSeconds"], out var windowDur) && windowDur >= 0)
                o.MarketWindowDurationSeconds = windowDur;
            if (int.TryParse(ranked["MaxMatchdaysPerTick"], out var tickCap) && tickCap >= 0)
                o.MaxMatchdaysPerTick = tickCap;
            if (long.TryParse(ranked["StartingTransferBudget"], out var budget) && budget >= 0)
                o.StartingTransferBudget = budget;
            if (int.TryParse(ranked["MinSquadSizeForSale"], out var minSquad) && minSquad >= 0)
                o.MinSquadSizeForSale = minSquad;
            if (long.TryParse(ranked["AuctionFlatStartPrice"], out var flatStart) && flatStart >= 0)
                o.AuctionFlatStartPrice = flatStart;
            // Ranking + seasonal reset knobs (Phase 9.3) — allow 0 (tests remove the between-seasons break).
            if (int.TryParse(ranked["EloKFactor"], out var k) && k > 0) o.EloKFactor = k;
            if (int.TryParse(ranked["AiRatingTopTier"], out var aiTop) && aiTop > 0) o.AiRatingTopTier = aiTop;
            if (int.TryParse(ranked["AiRatingPerTierStep"], out var aiStep) && aiStep >= 0)
                o.AiRatingPerTierStep = aiStep;
            if (int.TryParse(ranked["SeasonEndPositionSwing"], out var swing) && swing >= 0)
                o.SeasonEndPositionSwing = swing;
            if (int.TryParse(ranked["PromotionRatingBonus"], out var promoBonus) && promoBonus >= 0)
                o.PromotionRatingBonus = promoBonus;
            if (int.TryParse(ranked["PromotionSlots"], out var promoSlots) && promoSlots >= 0)
                o.PromotionSlots = promoSlots;
            if (int.TryParse(ranked["RelegationSlots"], out var relSlots) && relSlots >= 0)
                o.RelegationSlots = relSlots;
            if (int.TryParse(ranked["MinRating"], out var minRating) && minRating >= 0) o.MinRating = minRating;
            if (int.TryParse(ranked["SeasonBreakSeconds"], out var breakSecs) && breakSecs >= 0)
                o.SeasonBreakSeconds = breakSecs;
            if (bool.TryParse(ranked["ResetSquadsBetweenSeasons"], out var resetSquads))
                o.ResetSquadsBetweenSeasons = resetSquads;
            if (int.TryParse(ranked["SeasonResetSquadSize"], out var squadSize) && squadSize >= 0)
                o.SeasonResetSquadSize = squadSize;
            if (int.TryParse(ranked["LeaderboardTopCount"], out var topCount) && topCount > 0)
                o.LeaderboardTopCount = topCount;
        });

        services.AddScoped<IRankedService, RankedService>();
        // The real-time season engine (Phase 9.2). Registered always so unit tests can drive TickAsync
        // directly (the recurring Hangfire job that calls it is registered only when jobs are enabled).
        services.AddScoped<IRankedSeasonService, RankedSeasonService>();
        // Direct coach-to-coach market (Phase 9.2b): squad browse + offers during the season's windows.
        services.AddScoped<IRankedMarketService, RankedMarketService>();
        // Free-agent auctions in the season's windows (Phase 9.2b): lots opened/settled by the season tick.
        services.AddScoped<IRankedAuctionService, RankedAuctionService>();

        // Coach ranking + seasonal reset (Phase 9.3). PostgreSQL is authoritative for both the rating and
        // the palmarès; the leaderboard cache is a REBUILDABLE Redis sorted set, so environments without a
        // Redis container (notably Testing, which runs on in-memory SQLite) get the no-op and simply read
        // the database. Nothing about a coach's progression depends on the cache being there.
        services.AddScoped<IRankedRankingService, RankedRankingService>();
        services.AddScoped<IRankedSeasonEndService, RankedSeasonEndService>();

        // The daily digest (Phase 9.4): a read-only projection over the ladder's own state + the one-tap
        // matchday confirmation. Scoped like the rest (request-scoped FtsDbContext).
        services.AddScoped<IRankedTodayService, RankedTodayService>();
        if (redisAvailable) services.AddScoped<IRankedLeaderboardCache, RedisRankedLeaderboardCache>();
        else services.AddScoped<IRankedLeaderboardCache, NoOpRankedLeaderboardCache>();
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
        // The recurring job types are resolvable regardless (so a direct unit test can new/inject them).
        services.AddScoped<HeartbeatJob>();
        services.AddScoped<RankedSeasonJob>();

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
