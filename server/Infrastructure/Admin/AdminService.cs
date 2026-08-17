using Fts.Application.Admin;
using Fts.Application.Balance;
using Fts.Application.Integrity;
using Fts.Application.Ranked;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Balance;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sim.Core.Config;

namespace Fts.Infrastructure.Admin;

/// <summary>
/// <see cref="IAdminService"/> implementation (Phase 10.3) — the live-ops surface: metrics, worlds,
/// accounts and the balance push, each of them a projection over state the server already owns.
///
/// Two rules run through the whole class:
/// <list type="number">
/// <item><b>Read-mostly.</b> Almost everything here is a query. The writes (lock/unlock, grant/revoke,
/// world open/close, balance push/rollback) are deliberately the SMALLEST set that covers a live incident:
/// stop an abuser, let a colleague in, stop new players walking into a broken world, and change the
/// numbers. Anything that would let an operator edit a coach's rating or a match result is absent on
/// purpose — a ladder whose results an admin can rewrite is not a ladder.</item>
/// <item><b>Every write is audited.</b> The audit row is added to the same change tracker and committed by
/// the same <c>SaveChangesAsync</c> as the action, so there is no path that mutates without a line.</item>
/// </list>
///
/// Every query in here is written to translate on BOTH providers — PostgreSQL in production and the
/// in-memory SQLite the Api.Tests host runs on. That rules out Npgsql-only helpers (<c>ILike</c>) and the
/// grouped-aggregate shapes EF cannot translate (<c>group.Where(...).Min(...)</c>), which is why the
/// counts below are written as conditional <c>Sum</c>s.
/// </summary>
public sealed class AdminService : IAdminService
{
    /// <summary>Identity's "locked forever" sentinel. Identity compares <c>LockoutEnd</c> to now, so a
    /// far-future date is a permanent lock and clearing it is the unlock.</summary>
    private static readonly DateTimeOffset LockedForever = DateTimeOffset.MaxValue;

    public const string AdminRole = "admin";

    private readonly FtsDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IBalanceProvider _balance;
    private readonly AdminRuntimeInfo _runtime;
    private readonly IServiceProvider _services;
    private readonly ILogger<AdminService> _log;

    public AdminService(
        FtsDbContext db,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IBalanceProvider balance,
        AdminRuntimeInfo runtime,
        IServiceProvider services,
        ILogger<AdminService> log)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _balance = balance;
        _runtime = runtime;
        _services = services;
        _log = log;
    }

    // --- monitoring ---------------------------------------------------------------------------------

    public async Task<AdminMetricsDto> GetMetricsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);
        var dayAgo = now.AddDays(-1);

        int accounts = await _db.Users.CountAsync(ct);
        int accounts24h = await _db.Users.CountAsync(u => u.CreatedUtc >= dayAgo, ct);
        // SQLite (the test host) cannot translate a DateTimeOffset COMPARISON - PostgreSQL would have been
        // happy either way, which is exactly the kind of difference that only shows up when you run the
        // tests. The null check does translate on both, and only accounts that have EVER been locked carry a
        // non-null LockoutEnd, so pulling that one short column and comparing in memory costs nothing.
        var lockoutEnds = await _db.Users
            .Where(u => u.LockoutEnd != null)
            .Select(u => u.LockoutEnd!.Value)
            .ToListAsync(ct);
        int locked = lockoutEnds.Count(end => end > nowOffset);

        int worlds = await _db.RankedWorlds.CountAsync(ct);
        int openWorlds = await _db.RankedWorlds.CountAsync(w => w.Status == RankedWorldStatus.Open, ct);
        int groups = await _db.RankedGroups.CountAsync(ct);
        int activeGroups = await _db.RankedGroups.CountAsync(g => g.Status == RankedGroupStatus.Active, ct);
        int coaches = await _db.RankedCoaches.CountAsync(ct);
        int placed = await _db.RankedCoaches.CountAsync(c => c.Status == RankedCoachStatus.Placed, ct);

        int fixtures = await _db.RankedFixtures.CountAsync(ct);
        int played = await _db.RankedFixtures.CountAsync(f => f.IsPlayed, ct);

        // The calendar's health in two numbers: how many kickoffs have passed without a result, and how far
        // behind the worst one is. With a minutely tick a lag under ~120s is normal breathing; a lag that
        // keeps climbing is the scheduler being starved or stopped, which is exactly what the 9.6 load test
        // told us to watch. Two plain queries — the second only runs when there is something to measure.
        var overdueQuery = _db.RankedFixtures.Where(f => !f.IsPlayed && f.KickoffUtc <= now);
        int overdueCount = await overdueQuery.CountAsync(ct);
        int worstLag = 0;
        if (overdueCount > 0)
        {
            var oldest = await overdueQuery.MinAsync(f => f.KickoffUtc, ct);
            worstLag = (int)Math.Max(0, (now - oldest).TotalSeconds);
        }

        DateTime? lastResolved = await _db.RankedFixtures
            .Where(f => f.ResolvedUtc != null)
            .OrderByDescending(f => f.ResolvedUtc)
            .Select(f => f.ResolvedUtc)
            .FirstOrDefaultAsync(ct);

        int openLots = await _db.RankedAuctions.CountAsync(a => a.Status == RankedAuctionStatus.Open, ct);
        int pendingOffers = await _db.RankedOffers.CountAsync(o => o.Status == RankedOfferStatus.Pending, ct);
        int openFlags = await _db.IntegrityFlags.CountAsync(f => f.Status == IntegrityFlagStatus.Open, ct);

        var (failed, enqueued, servers) = ReadJobStats();

        return new AdminMetricsDto(
            Version: _runtime.Version,
            SimCoreVersion: _runtime.SimCoreVersion,
            Environment: _runtime.Environment,
            UtcNow: now,
            UptimeSeconds: Math.Round((now - _runtime.StartedUtc).TotalSeconds, 1),
            Accounts: accounts,
            AccountsCreated24h: accounts24h,
            LockedAccounts: locked,
            RankedWorlds: worlds,
            OpenRankedWorlds: openWorlds,
            RankedGroups: groups,
            ActiveGroups: activeGroups,
            RankedCoaches: coaches,
            PlacedCoaches: placed,
            FixturesTotal: fixtures,
            FixturesPlayed: played,
            OverdueFixtures: overdueCount,
            WorstFixtureLagSeconds: worstLag,
            LastFixtureResolvedUtc: lastResolved,
            OpenAuctionLots: openLots,
            PendingOffers: pendingOffers,
            OpenIntegrityFlags: openFlags,
            FailedJobs: failed,
            EnqueuedJobs: enqueued,
            JobServers: servers,
            BalanceRevision: _balance.Revision,
            BalanceLoadedUtc: _balance.LoadedUtc);
    }

    /// <summary>
    /// Hangfire's counters, or three nulls. <c>JobStorage</c> is only in the container when the scheduler is
    /// enabled (it is not under the Testing environment), and the monitoring API talks to the storage, so it
    /// can throw when Postgres is briefly unreachable. Neither case is worth failing the whole metrics call
    /// for — the dashboard renders "n/a" and every other number still arrives.
    /// </summary>
    private (int? Failed, int? Enqueued, int? Servers) ReadJobStats()
    {
        var storage = _services.GetService<JobStorage>();
        if (storage is null) return (null, null, null);

        try
        {
            IMonitoringApi api = storage.GetMonitoringApi();
            var stats = api.GetStatistics();
            return ((int)stats.Failed, (int)stats.Enqueued, (int)stats.Servers);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read Hangfire statistics for the admin metrics.");
            return (null, null, null);
        }
    }

    // --- worlds -------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminWorldDto>> GetWorldsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var worlds = await _db.RankedWorlds.OrderBy(w => w.CreatedUtc).ToListAsync(ct);
        if (worlds.Count == 0) return Array.Empty<AdminWorldDto>();

        var groups = await _db.RankedGroups.OrderBy(g => g.Tier).ThenBy(g => g.GroupIndex).ToListAsync(ct);

        // Seats and fixtures are aggregated in ONE query each rather than per group in a loop: a full ladder
        // is 125 groups, and the 9.6 load test is a standing reminder that a per-row query in an operator
        // screen is how a dashboard takes the server down with it.
        var seatStats = await _db.RankedSeats
            .GroupBy(s => s.RankedGroupId)
            .Select(g => new
            {
                GroupId = g.Key,
                Total = g.Count(),
                Occupied = g.Sum(s => s.UserId != null ? 1 : 0),
            })
            .ToDictionaryAsync(x => x.GroupId, ct);

        var fixtureStats = await _db.RankedFixtures
            .GroupBy(f => f.RankedGroupId)
            .Select(g => new
            {
                GroupId = g.Key,
                Rounds = g.Max(f => f.Round),
                Total = g.Count(),
                Played = g.Sum(f => f.IsPlayed ? 1 : 0),
                // Min ignores nulls in SQL, so mapping played fixtures to null gives "next kickoff still to
                // come" without a filtered aggregate (which EF cannot translate inside a GroupBy).
                NextKickoff = g.Min(f => f.IsPlayed ? (DateTime?)null : f.KickoffUtc),
                Overdue = g.Sum(f => !f.IsPlayed && f.KickoffUtc <= now ? 1 : 0),
            })
            .ToDictionaryAsync(x => x.GroupId, ct);

        var coachCounts = await _db.RankedCoaches
            .GroupBy(c => c.RankedWorldId)
            .Select(g => new { WorldId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorldId, x => x.Count, ct);

        var result = new List<AdminWorldDto>(worlds.Count);
        foreach (var world in worlds)
        {
            var mine = groups.Where(g => g.RankedWorldId == world.Id).ToList();
            int seats = 0, occupied = 0;
            var groupDtos = new List<AdminGroupDto>(mine.Count);

            foreach (var g in mine)
            {
                seatStats.TryGetValue(g.Id, out var s);
                fixtureStats.TryGetValue(g.Id, out var f);

                int groupSeats = s?.Total ?? g.Capacity;
                int groupOccupied = s?.Occupied ?? 0;
                seats += groupSeats;
                occupied += groupOccupied;

                // RoundsPlayed counts COMPLETED rounds, not played fixtures: a round only means something to
                // an operator once all of its matches are in, and a half-resolved round is precisely the
                // symptom the overdue counter exists to surface.
                int roundsTotal = f?.Rounds ?? 0;
                int perRound = roundsTotal > 0 && f is not null && f.Total > 0 ? f.Total / roundsTotal : 0;
                int roundsPlayed = perRound > 0 ? (f?.Played ?? 0) / perRound : 0;

                groupDtos.Add(new AdminGroupDto(
                    Id: g.Id,
                    Name: g.Name,
                    Kind: g.Kind.ToString(),
                    Tier: g.Tier,
                    Capacity: g.Capacity,
                    Occupied: groupOccupied,
                    Status: g.Status.ToString(),
                    SeasonNumber: g.SeasonNumber,
                    SeasonStartedUtc: g.SeasonStartedUtc,
                    SeasonEndedUtc: g.SeasonEndedUtc,
                    RoundsPlayed: roundsPlayed,
                    RoundsTotal: roundsTotal,
                    NextKickoffUtc: f?.NextKickoff,
                    OverdueFixtures: f?.Overdue ?? 0));
            }

            result.Add(new AdminWorldDto(
                Id: world.Id,
                Name: world.Name,
                Seed: world.Seed,
                Status: world.Status.ToString(),
                SeasonNumber: world.SeasonNumber,
                Groups: mine.Count,
                Seats: seats,
                OccupiedSeats: occupied,
                HumanCoaches: coachCounts.TryGetValue(world.Id, out var humans) ? humans : 0,
                CreatedUtc: world.CreatedUtc,
                GroupList: groupDtos));
        }

        return result;
    }

    public async Task<AdminResult<AdminWorldDto>> SetWorldOpenAsync(
        Guid actorUserId, Guid worldId, SetWorldOpenRequest request, CancellationToken ct = default)
    {
        var world = await _db.RankedWorlds.FirstOrDefaultAsync(w => w.Id == worldId, ct);
        if (world is null) return AdminResult<AdminWorldDto>.Fail(AdminError.NotFound, "No such ranked world.");

        // Closed is NOT a teardown: seasons under way keep running and the coaches in them keep playing. It
        // only takes the world out of the enrolment path, which is what an operator wants when a world is
        // misbehaving and new players should be steered into a fresh one instead.
        var target = request.Open ? RankedWorldStatus.Open : RankedWorldStatus.Closed;
        if (world.Status != target)
        {
            world.Status = target;
            await AuditAsync(actorUserId,
                request.Open ? AdminAction.WorldReopen : AdminAction.WorldClose,
                worldId.ToString(),
                $"{world.Name}: {request.Reason ?? "(no reason given)"}", ct);
            await _db.SaveChangesAsync(ct);
        }

        var refreshed = (await GetWorldsAsync(ct)).FirstOrDefault(w => w.Id == worldId);
        return refreshed is null
            ? AdminResult<AdminWorldDto>.Fail(AdminError.NotFound, "No such ranked world.")
            : AdminResult<AdminWorldDto>.Ok(refreshed);
    }

    // --- users --------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminUserDto>> SearchUsersAsync(
        string? query, int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        IQueryable<AppUser> q = _db.Users;
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Case-insensitive on BOTH providers without provider-specific SQL: Identity already stores an
            // uppercase NormalizedEmail, and ToUpper() translates to upper() on PostgreSQL and SQLite alike
            // (Npgsql's ILike would not survive the SQLite test host).
            string needle = query.Trim().ToUpperInvariant();
            var matchingProfiles = _db.CoachProfiles
                .Where(p => p.DisplayName.ToUpper().Contains(needle))
                .Select(p => p.UserId);

            q = q.Where(u =>
                (u.NormalizedEmail != null && u.NormalizedEmail.Contains(needle))
                || matchingProfiles.Contains(u.Id));
        }

        var users = await q.OrderByDescending(u => u.CreatedUtc).Take(limit).ToListAsync(ct);
        return await ProjectUsersAsync(users, ct);
    }

    public async Task<AdminResult<AdminUserDto>> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AdminResult<AdminUserDto>.Fail(AdminError.NotFound, "No such account.");

        var projected = await ProjectUsersAsync(new[] { user }, ct);
        return AdminResult<AdminUserDto>.Ok(projected[0]);
    }

    public async Task<AdminResult<AdminUserDto>> SetUserLockAsync(
        Guid actorUserId, Guid userId, SetUserLockRequest request, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return AdminResult<AdminUserDto>.Fail(AdminError.NotFound, "No such account.");

        // Locking yourself out of the dashboard you are standing in is never what anyone meant.
        if (request.Locked && userId == actorUserId)
            return AdminResult<AdminUserDto>.Fail(
                AdminError.ValidationFailed, "You cannot lock your own account.");

        if (request.Locked)
        {
            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, LockedForever);

            // A lock has to end the sessions the account already holds, or a ban only starts when the banned
            // player next signs out. Revoking the refresh tokens closes the long-lived half; the access token
            // they are already carrying stays valid until it expires (≤ 15 minutes by config), which is the
            // documented tail of this action rather than something to pretend away.
            var live = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedUtc == null)
                .ToListAsync(ct);
            var revokedAt = DateTime.UtcNow;
            foreach (var token in live) token.RevokedUtc = revokedAt;
        }
        else
        {
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
        }

        await AuditAsync(actorUserId, request.Locked ? AdminAction.UserLock : AdminAction.UserUnlock,
            userId.ToString(), $"{user.Email}: {request.Reason ?? "(no reason given)"}", ct);
        await _db.SaveChangesAsync(ct);

        return await GetUserAsync(userId, ct);
    }

    public async Task<AdminResult<AdminUserDto>> SetUserAdminAsync(
        Guid actorUserId, Guid userId, SetUserAdminRequest request, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return AdminResult<AdminUserDto>.Fail(AdminError.NotFound, "No such account.");

        // Removing your own admin rights would need a second admin (or a redeploy with the bootstrap config)
        // to undo, so it is refused rather than confirmed — same reasoning as the self-lock.
        if (!request.IsAdmin && userId == actorUserId)
            return AdminResult<AdminUserDto>.Fail(
                AdminError.ValidationFailed, "You cannot remove your own admin role.");

        await EnsureRoleExistsAsync();

        bool isAdmin = await _users.IsInRoleAsync(user, AdminRole);
        if (isAdmin != request.IsAdmin)
        {
            var outcome = request.IsAdmin
                ? await _users.AddToRoleAsync(user, AdminRole)
                : await _users.RemoveFromRoleAsync(user, AdminRole);

            if (!outcome.Succeeded)
                return AdminResult<AdminUserDto>.Fail(
                    AdminError.Conflict, string.Join(" ", outcome.Errors.Select(e => e.Description)));

            await AuditAsync(actorUserId, request.IsAdmin ? AdminAction.AdminGrant : AdminAction.AdminRevoke,
                userId.ToString(), user.Email ?? string.Empty, ct);
            await _db.SaveChangesAsync(ct);
        }

        return await GetUserAsync(userId, ct);
    }

    /// <summary>Creates the admin role if it is not there yet. Idempotent, and cheap enough to call on the
    /// grant path rather than depending on startup having run it (a database restored from a backup has the
    /// users but need not have the role).</summary>
    private async Task EnsureRoleExistsAsync()
    {
        if (await _roles.RoleExistsAsync(AdminRole)) return;
        await _roles.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = AdminRole });
    }

    private async Task<IReadOnlyList<AdminUserDto>> ProjectUsersAsync(
        IReadOnlyList<AppUser> users, CancellationToken ct)
    {
        var ids = users.Select(u => u.Id).ToList();
        var nowOffset = DateTimeOffset.UtcNow;

        var names = await _db.CoachProfiles
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);

        var coaches = await _db.RankedCoaches.Where(c => ids.Contains(c.UserId)).ToListAsync(ct);

        var seatIds = coaches.Where(c => c.SeatId != null).Select(c => c.SeatId!.Value).ToList();
        var seats = await _db.RankedSeats.Where(s => seatIds.Contains(s.Id)).ToListAsync(ct);

        var groupIds = seats.Select(s => s.RankedGroupId)
            .Concat(coaches.Where(c => c.PlacementGroupId != null).Select(c => c.PlacementGroupId!.Value))
            .Distinct().ToList();
        var groups = await _db.RankedGroups.Where(g => groupIds.Contains(g.Id)).ToListAsync(ct);

        var worldIds = coaches.Select(c => c.RankedWorldId).Distinct().ToList();
        var worlds = await _db.RankedWorlds.Where(w => worldIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);

        var flagCounts = await _db.IntegrityFlags
            .Where(f => f.Status == IntegrityFlagStatus.Open
                        && f.SubjectUserId != null && ids.Contains(f.SubjectUserId.Value))
            .GroupBy(f => f.SubjectUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // One round trip for the whole role membership instead of UserManager.IsInRoleAsync per user.
        var admins = (await (
            from ur in _db.UserRoles
            join r in _db.Roles on ur.RoleId equals r.Id
            where r.Name == AdminRole && ids.Contains(ur.UserId)
            select ur.UserId).ToListAsync(ct)).ToHashSet();

        var list = new List<AdminUserDto>(users.Count);
        foreach (var u in users)
        {
            var coach = coaches.FirstOrDefault(c => c.UserId == u.Id);
            RankedGroup? group = null;
            if (coach?.SeatId is { } seatId)
            {
                var seat = seats.FirstOrDefault(s => s.Id == seatId);
                if (seat is not null) group = groups.FirstOrDefault(g => g.Id == seat.RankedGroupId);
            }
            else if (coach?.PlacementGroupId is { } placementId)
            {
                group = groups.FirstOrDefault(g => g.Id == placementId);
            }

            list.Add(new AdminUserDto(
                UserId: u.Id,
                Email: u.Email ?? string.Empty,
                DisplayName: names.TryGetValue(u.Id, out var name) ? name : string.Empty,
                CreatedUtc: u.CreatedUtc,
                IsAdmin: admins.Contains(u.Id),
                IsLocked: u.LockoutEnd is { } end && end > nowOffset,
                LockoutEndUtc: u.LockoutEnd?.UtcDateTime,
                Enrolled: coach is not null,
                RankedStatus: coach?.Status.ToString(),
                Rating: coach?.Rating,
                PeakRating: coach?.PeakRating,
                SeasonsPlayed: coach?.SeasonsPlayed,
                WorldName: coach is not null && worlds.TryGetValue(coach.RankedWorldId, out var w) ? w : null,
                GroupName: group?.Name,
                Tier: group?.Tier,
                OpenFlags: flagCounts.TryGetValue(u.Id, out var flags) ? flags : 0));
        }

        return list;
    }

    // --- balance ------------------------------------------------------------------------------------

    public async Task<AdminBalanceDto> GetBalanceAsync(CancellationToken ct = default)
    {
        var active = await _db.BalanceRevisions.OrderByDescending(r => r.Revision).FirstOrDefaultAsync(ct);

        // Nothing pushed yet: report the balance embedded in this build, serialised from the LIVE object
        // rather than from a stored copy, so what the dashboard shows is literally what the server is
        // simulating with. That document is also the starting point an operator edits.
        if (active is null)
            return new AdminBalanceDto(
                Revision: 0,
                ConfigVersion: _balance.Current.Version,
                Note: "Balance embedded in this build (nothing pushed yet).",
                CreatedByEmail: string.Empty,
                CreatedUtc: null,
                LoadedUtc: _balance.LoadedUtc,
                IsBaseline: true,
                Json: BalanceStore.Serialize(_balance.Current));

        return new AdminBalanceDto(
            Revision: active.Revision,
            ConfigVersion: active.ConfigVersion,
            Note: active.Note,
            CreatedByEmail: active.CreatedByEmail,
            CreatedUtc: active.CreatedUtc,
            LoadedUtc: _balance.LoadedUtc,
            IsBaseline: false,
            Json: active.Json);
    }

    public async Task<IReadOnlyList<AdminBalanceRevisionDto>> GetBalanceHistoryAsync(
        int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var rows = await _db.BalanceRevisions
            .OrderByDescending(r => r.Revision)
            .Take(limit)
            .ToListAsync(ct);

        int top = rows.Count > 0 ? rows[0].Revision : 0;
        return rows.Select(r => new AdminBalanceRevisionDto(
            r.Revision, r.ConfigVersion, r.Note, r.CreatedByEmail, r.CreatedUtc,
            r.RolledBackFrom, r.Revision == top)).ToList();
    }

    public async Task<AdminResult<AdminBalanceDto>> PushBalanceAsync(
        Guid actorUserId, PushBalanceRequest request, CancellationToken ct = default)
    {
        if (!BalanceStore.TryParse(request.Json ?? string.Empty, out var config, out var error))
            return AdminResult<AdminBalanceDto>.Fail(AdminError.ValidationFailed, error);

        var stored = await AppendRevisionAsync(
            actorUserId, config, request.Note, rolledBackFrom: null, AdminAction.BalancePush, ct);

        _balance.Set(config, stored.Revision);
        _log.LogWarning("Balance revision {Revision} pushed by {Actor} ({Note}).",
            stored.Revision, stored.CreatedByEmail, stored.Note);

        return AdminResult<AdminBalanceDto>.Ok(await GetBalanceAsync(ct));
    }

    public async Task<AdminResult<AdminBalanceDto>> RollbackBalanceAsync(
        Guid actorUserId, int revision, CancellationToken ct = default)
    {
        var source = await _db.BalanceRevisions.FirstOrDefaultAsync(r => r.Revision == revision, ct);
        if (source is null)
            return AdminResult<AdminBalanceDto>.Fail(AdminError.NotFound, $"No balance revision {revision}.");

        if (!BalanceStore.TryParse(source.Json, out var config, out var error))
            return AdminResult<AdminBalanceDto>.Fail(
                AdminError.ValidationFailed, $"Revision {revision} cannot be read back: {error}");

        // A rollback moves FORWARD. Reactivating the old row would turn the history into a graph and leave
        // "what was live at 14:05?" ambiguous; copying the payload into a new revision keeps the answer a
        // single ORDER BY.
        var stored = await AppendRevisionAsync(
            actorUserId, config, $"Rollback to revision {revision}.", revision,
            AdminAction.BalanceRollback, ct);

        _balance.Set(config, stored.Revision);
        _log.LogWarning("Balance rolled back to revision {Source} as revision {Revision} by {Actor}.",
            revision, stored.Revision, stored.CreatedByEmail);

        return AdminResult<AdminBalanceDto>.Ok(await GetBalanceAsync(ct));
    }

    private async Task<BalanceRevision> AppendRevisionAsync(
        Guid actorUserId, BalanceConfig config, string? note,
        int? rolledBackFrom, AdminAction action, CancellationToken ct)
    {
        int next = (await _db.BalanceRevisions.MaxAsync(r => (int?)r.Revision, ct) ?? 0) + 1;

        var row = new BalanceRevision
        {
            Id = Guid.NewGuid(),
            Revision = next,
            // Re-serialised from the PARSED object rather than stored as the operator's text: it normalises
            // formatting, drops anything BalanceConfig does not know about, and guarantees the stored
            // document round-trips — a revision that cannot be read back is worse than a rejected push.
            Json = BalanceStore.Serialize(config),
            ConfigVersion = config.Version,
            Note = Trim(note ?? string.Empty, 280),
            RolledBackFrom = rolledBackFrom,
            CreatedByUserId = actorUserId,
            CreatedByEmail = await ActorEmailAsync(actorUserId, ct),
            CreatedUtc = DateTime.UtcNow,
        };
        _db.BalanceRevisions.Add(row);

        await AuditAsync(actorUserId, action, next.ToString(),
            $"config version {config.Version}, note: {row.Note}", ct);
        await _db.SaveChangesAsync(ct);

        return row;
    }

    // --- audit --------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminAuditDto>> GetAuditAsync(
        int limit = 100, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        return await _db.AdminAudit
            .OrderByDescending(a => a.CreatedUtc)
            .Take(limit)
            .Select(a => new AdminAuditDto(
                a.Id, a.ActorUserId, a.ActorEmail, a.Action, a.Target, a.Details, a.CreatedUtc))
            .ToListAsync(ct);
    }

    /// <summary>Queues the audit row on the SAME change tracker as the action, so the caller's
    /// <c>SaveChangesAsync</c> commits both or neither.</summary>
    private async Task AuditAsync(
        Guid actorUserId, AdminAction action, string target, string details, CancellationToken ct)
    {
        _db.AdminAudit.Add(new AdminAuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActorEmail = await ActorEmailAsync(actorUserId, ct),
            Action = action,
            Target = Trim(target, 120),
            Details = Trim(details, 512),
            CreatedUtc = DateTime.UtcNow,
        });
    }

    private async Task<string> ActorEmailAsync(Guid actorUserId, CancellationToken ct) =>
        await _db.Users.Where(u => u.Id == actorUserId).Select(u => u.Email).FirstOrDefaultAsync(ct)
        ?? string.Empty;

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
