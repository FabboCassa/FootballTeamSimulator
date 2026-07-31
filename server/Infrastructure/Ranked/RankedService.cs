using System.Security.Cryptography;
using Fts.Application.Integrity;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedService"/> implementation (Phase 9.1) — the server-managed ranked pyramid.
///
/// Three invariants drive the whole file:
/// <list type="number">
/// <item><b>Fixed division sizes.</b> Every group's <see cref="RankedSeat"/> rows are created with the group
/// and are never added to or deleted. Joining only sets <c>UserId</c> on a vacant seat, so a division always
/// has exactly <see cref="RankedOptions.GroupSize"/> clubs; unclaimed seats play as AI.</item>
/// <item><b>Placement first.</b> A newcomer never lands straight in the pyramid: they are seated in a placement
/// group and sorted into a division by where they finish (top finishers into the upper placeable tier, the rest
/// into the lowest). Tier 1 is promotion-only.</item>
/// <item><b>Never blocked.</b> A new placement group is only opened in a world that still has enough free
/// division seats to absorb the whole cohort (counting the seats already reserved by pending placements).
/// When no world qualifies the server opens a brand-new one — the spillover ✅.</item>
/// </list>
///
/// A group's clubs are generated LAZILY (<see cref="MaterializeAsync"/>) the first time the group gets an
/// occupant, so opening a world is cheap and unreached groups cost nothing.
/// </summary>
public sealed class RankedService : IRankedService
{
    private readonly FtsDbContext _db;
    private readonly RankedOptions _opt;
    private readonly IIntegrityService _integrity;

    public RankedService(FtsDbContext db, IOptions<RankedOptions> options, IIntegrityService integrity)
    {
        _db = db;
        _opt = options.Value;
        _integrity = integrity;
    }

    // --- enrolment -----------------------------------------------------------------------------

    public async Task<RankedResult<RankedStateDto>> EnrolAsync(Guid userId, CancellationToken ct = default)
    {
        // Idempotent: an account that already joined just gets its current state back.
        var existing = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is not null)
            return RankedResult<RankedStateDto>.Ok(await BuildStateAsync(existing, ct));

        // MULTI-ACCOUNT GUARD (Phase 9.5): accounts that look like the same person are never seated in the
        // same group. Nothing is refused — the coach still joins the ladder immediately — they simply land
        // in a different cohort, which is where the boosting would otherwise happen.
        var linked = await _integrity.LinkedUserIdsAsync(userId, ct);

        RankedGroup group = await FindOrCreatePlacementGroupAsync(linked, ct);
        if (linked.Count > 0)
        {
            await _integrity.FlagAsync(
                IntegrityFlagKind.LinkedAccounts, severity: 60, userId: userId,
                subjectUserId: linked.First(), rankedGroupId: group.Id, fee: 0, marketValue: 0,
                details: $"{linked.Count} linked account(s) at enrolment; seated in a separate group", ct);
        }

        RankedSeat? seat = await ClaimSeatAsync(group, userId, ct);
        if (seat is null)
            return RankedResult<RankedStateDto>.Fail(RankedError.NoCapacity, "No placement seat available.");

        var now = DateTime.UtcNow;
        var coach = new RankedCoach
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RankedWorldId = group.RankedWorldId,
            Rating = _opt.StartingRating,
            Status = RankedCoachStatus.Placement,
            SeatId = seat.Id,
            PlacementGroupId = group.Id,
            PlacementPosition = null,
            AutoEnrol = true,
            EnrolledUtc = now,
        };
        _db.RankedCoaches.Add(coach);

        // A full cohort can play its placement season (the real-time calendar that runs it is 9.2).
        int occupied = await _db.RankedSeats.CountAsync(s => s.RankedGroupId == group.Id && s.UserId != null, ct);
        if (occupied >= group.Capacity) group.Status = RankedGroupStatus.Active;

        await _db.SaveChangesAsync(ct);
        await RefreshWorldStatusAsync(group.RankedWorldId, ct);

        return RankedResult<RankedStateDto>.Ok(await BuildStateAsync(coach, ct));
    }

    public async Task<RankedResult<RankedStateDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return coach is null
            ? RankedResult<RankedStateDto>.Ok(RankedStateDto.NotEnrolled())
            : RankedResult<RankedStateDto>.Ok(await BuildStateAsync(coach, ct));
    }

    public async Task<RankedResult<RankedStateDto>> SetAutoEnrolAsync(
        Guid userId, bool autoEnrol, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedStateDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        coach.AutoEnrol = autoEnrol;
        await _db.SaveChangesAsync(ct);
        return RankedResult<RankedStateDto>.Ok(await BuildStateAsync(coach, ct));
    }

    public async Task<RankedResult<RankedGroupDto>> GetGroupAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
            return RankedResult<RankedGroupDto>.Fail(RankedError.NotFound, "Group not found.");

        return RankedResult<RankedGroupDto>.Ok(await BuildGroupAsync(group, userId, ct));
    }

    // --- placement resolution ------------------------------------------------------------------

    public async Task<RankedResult<PlacementResultDto>> ResolvePlacementAsync(
        Guid groupId, IReadOnlyList<Guid>? finalOrder, CancellationToken ct = default)
    {
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
            return RankedResult<PlacementResultDto>.Fail(RankedError.NotFound, "Group not found.");
        if (group.Kind != RankedGroupKind.Placement)
            return RankedResult<PlacementResultDto>.Fail(RankedError.WrongPhase, "That group is not a placement group.");
        if (group.Status == RankedGroupStatus.Completed)
            return RankedResult<PlacementResultDto>.Fail(RankedError.WrongPhase, "This placement season is already resolved.");

        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == groupId)
            .OrderBy(s => s.SeatIndex)
            .ToListAsync(ct);
        var occupied = seats.Where(s => s.UserId is not null).ToList();

        if (occupied.Count == 0)
        {
            group.Status = RankedGroupStatus.Completed;
            await _db.SaveChangesAsync(ct);
            return RankedResult<PlacementResultDto>.Ok(new PlacementResultDto(groupId, Array.Empty<PlacementAssignmentDto>()));
        }

        var order = await ResolveOrderAsync(occupied, finalOrder, ct);
        if (order is null)
            return RankedResult<PlacementResultDto>.Fail(
                RankedError.ValidationFailed, "The final order must list every coach in this placement group exactly once.");

        var userIds = order.ToList();
        var coaches = await _db.RankedCoaches.Where(c => userIds.Contains(c.UserId)).ToListAsync(ct);
        var coachByUser = coaches.ToDictionary(c => c.UserId);

        var assignments = new List<PlacementAssignmentDto>();
        var touchedWorlds = new HashSet<Guid> { group.RankedWorldId };
        var now = DateTime.UtcNow;

        for (int i = 0; i < order.Count; i++)
        {
            int position = i + 1;
            Guid userId = order[i];
            if (!coachByUser.TryGetValue(userId, out var coach)) continue;

            int wantedTier = position <= _opt.PlacementTopPositionsToUpperTier
                ? _opt.UpperPlacementTier()
                : _opt.LowerPlacementTier();

            RankedGroup? target =
                await FindGroupWithFreeSeatAsync(group.RankedWorldId, wantedTier, ct)
                ?? await FindGroupWithFreeSeatAsync(group.RankedWorldId, _opt.LowerPlacementTier(), ct);

            if (target is null)
            {
                // This world is full: spill the coach into another open world (opening one if needed).
                RankedWorld spill = await FindOrCreateWorldWithRoomAsync(ct);
                target = await FindGroupWithFreeSeatAsync(spill.Id, wantedTier, ct)
                      ?? await FindGroupWithFreeSeatAsync(spill.Id, _opt.LowerPlacementTier(), ct);
            }

            if (target is null)
                return RankedResult<PlacementResultDto>.Fail(RankedError.NoCapacity, "No division seat available.");

            await MaterializeAsync(target, ct);

            var targetSeats = await _db.RankedSeats
                .Where(s => s.RankedGroupId == target.Id)
                .OrderBy(s => s.SeatIndex)
                .ToListAsync(ct);
            var freeSeat = targetSeats.FirstOrDefault(s => s.UserId is null);
            if (freeSeat is null)
                return RankedResult<PlacementResultDto>.Fail(RankedError.NoCapacity, "No division seat available.");

            freeSeat.UserId = userId;
            freeSeat.OccupiedUtc = now;

            coach.SeatId = freeSeat.Id;
            coach.RankedWorldId = target.RankedWorldId;
            coach.Status = RankedCoachStatus.Placed;
            coach.PlacementPosition = position;
            coach.Rating = SeedRating(position, order.Count);
            // Career-best tracking starts here (Phase 9.3): placement is the coach's first real rating.
            if (coach.Rating > coach.PeakRating) coach.PeakRating = coach.Rating;
            coach.PlacedUtc = now;

            // Save each assignment so the next iteration's free-seat lookup sees it.
            await _db.SaveChangesAsync(ct);
            touchedWorlds.Add(target.RankedWorldId);

            var club = freeSeat.ClubId is { } cid
                ? await _db.Clubs.FirstOrDefaultAsync(c => c.Id == cid, ct)
                : null;
            var targetWorldName = await _db.RankedWorlds
                .Where(w => w.Id == target.RankedWorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

            assignments.Add(new PlacementAssignmentDto(
                UserId: userId,
                Position: position,
                RankedWorldId: target.RankedWorldId,
                WorldName: targetWorldName,
                GroupId: target.Id,
                GroupName: target.Name,
                Tier: target.Tier,
                SeatIndex: freeSeat.SeatIndex,
                ClubExternalId: club?.ExternalId ?? 0,
                ClubName: club?.Name ?? string.Empty,
                Rating: coach.Rating));
        }

        // The placement cohort has moved on: free its seats (they become AI clubs) and close the group.
        foreach (var s in occupied) { s.UserId = null; s.OccupiedUtc = null; }
        group.Status = RankedGroupStatus.Completed;
        await _db.SaveChangesAsync(ct);

        foreach (var worldId in touchedWorlds) await RefreshWorldStatusAsync(worldId, ct);

        return RankedResult<PlacementResultDto>.Ok(new PlacementResultDto(groupId, assignments));
    }

    // --- promotion / relegation (Phase 9.3) ----------------------------------------------------

    public async Task<RankedResult<PlacementAssignmentDto>> MoveToTierAsync(
        Guid userId, int targetTier, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<PlacementAssignmentDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");
        if (coach.SeatId is not { } currentSeatId)
            return RankedResult<PlacementAssignmentDto>.Fail(RankedError.WrongPhase, "You do not currently hold a seat.");

        var currentSeat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == currentSeatId, ct);
        if (currentSeat is null)
            return RankedResult<PlacementAssignmentDto>.Fail(RankedError.WrongPhase, "You do not currently hold a seat.");

        var currentGroup = await _db.RankedGroups.FirstAsync(g => g.Id == currentSeat.RankedGroupId, ct);

        // Out of the pyramid's range (promotion out of tier 1, relegation out of the bottom) → stay put.
        bool inRange = targetTier >= 1 && targetTier <= _opt.TierCount() && targetTier != currentGroup.Tier;
        RankedGroup? target = inRange
            ? await FindGroupWithFreeSeatAsync(currentGroup.RankedWorldId, targetTier, ct)
            : null;

        if (target is null) return await DescribeSeatAsync(coach, currentGroup, currentSeat, ct);

        await MaterializeAsync(target, ct);

        var free = await _db.RankedSeats
            .Where(s => s.RankedGroupId == target.Id && s.UserId == null)
            .OrderBy(s => s.SeatIndex)
            .FirstOrDefaultAsync(ct);
        if (free is null) return await DescribeSeatAsync(coach, currentGroup, currentSeat, ct);

        // Leave the old seat behind (it plays as AI again → the group keeps its fixed size) and take the new one.
        currentSeat.UserId = null;
        currentSeat.OccupiedUtc = null;

        var now = DateTime.UtcNow;
        free.UserId = userId;
        free.OccupiedUtc = now;

        coach.SeatId = free.Id;
        coach.RankedWorldId = target.RankedWorldId;
        coach.Status = RankedCoachStatus.Placed;
        coach.PlacedUtc = now;
        await _db.SaveChangesAsync(ct);

        // SMART DEFAULTS (Phase 9.4): a coach who lands on a seat whose season is already under way (the
        // documented v1 quirk — groups run independent clocks) gets a stored best-XI lineup and a balanced
        // training plan straight away, so their new club is never fielded by a silent fallback. A group that
        // has not kicked off yet is seeded when its season starts instead.
        if (free.ClubId is { } newClubId
            && await _db.RankedFixtures.AnyAsync(f => f.RankedGroupId == target.Id, ct))
        {
            bool wrote = await RankedInputDefaults.EnsureLineupAsync(_db, target.Id, newClubId, userId, ct);
            wrote |= await RankedInputDefaults.EnsureTrainingAsync(_db, target.Id, newClubId, userId, ct);
            if (wrote) await _db.SaveChangesAsync(ct);
        }

        await RefreshWorldStatusAsync(target.RankedWorldId, ct);
        if (currentGroup.RankedWorldId != target.RankedWorldId)
            await RefreshWorldStatusAsync(currentGroup.RankedWorldId, ct);

        return await DescribeSeatAsync(coach, target, free, ct);
    }

    /// <summary>Reports where a coach now sits (reused by <see cref="MoveToTierAsync"/> for both the moved
    /// and the stayed-put case).</summary>
    private async Task<RankedResult<PlacementAssignmentDto>> DescribeSeatAsync(
        RankedCoach coach, RankedGroup group, RankedSeat seat, CancellationToken ct)
    {
        var club = seat.ClubId is { } cid
            ? await _db.Clubs.FirstOrDefaultAsync(c => c.Id == cid, ct)
            : null;
        var worldName = await _db.RankedWorlds
            .Where(w => w.Id == group.RankedWorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        return RankedResult<PlacementAssignmentDto>.Ok(new PlacementAssignmentDto(
            UserId: coach.UserId,
            Position: coach.PlacementPosition ?? 0,
            RankedWorldId: group.RankedWorldId,
            WorldName: worldName,
            GroupId: group.Id,
            GroupName: group.Name,
            Tier: group.Tier,
            SeatIndex: seat.SeatIndex,
            ClubExternalId: club?.ExternalId ?? 0,
            ClubName: club?.Name ?? string.Empty,
            Rating: coach.Rating));
    }

    /// <summary>Validates a supplied final order, or derives a deterministic one (strongest squad first,
    /// seat index as tie-break) so a placement can always be closed even before the 9.2 season engine runs.
    /// Returns null when the supplied order does not match the group's occupants exactly.</summary>
    private async Task<List<Guid>?> ResolveOrderAsync(
        List<RankedSeat> occupied, IReadOnlyList<Guid>? finalOrder, CancellationToken ct)
    {
        var present = occupied.Select(s => s.UserId!.Value).ToHashSet();

        if (finalOrder is { Count: > 0 })
        {
            if (finalOrder.Count != present.Count) return null;
            if (finalOrder.Distinct().Count() != finalOrder.Count) return null;
            if (finalOrder.Any(u => !present.Contains(u))) return null;
            return finalOrder.ToList();
        }

        var clubIds = occupied.Where(s => s.ClubId is not null).Select(s => s.ClubId!.Value).ToList();
        var strengths = await _db.Clubs
            .Where(c => clubIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Strength })
            .ToDictionaryAsync(x => x.Id, x => x.Strength, ct);

        return occupied
            .OrderByDescending(s => s.ClubId is { } cid && strengths.TryGetValue(cid, out var st) ? st : 0)
            .ThenBy(s => s.SeatIndex)
            .Select(s => s.UserId!.Value)
            .ToList();
    }

    private int SeedRating(int position, int cohortSize) =>
        _opt.StartingRating + (cohortSize - position) * _opt.RatingPerPlacementPosition;

    // --- seats, groups, worlds -----------------------------------------------------------------

    /// <summary>An existing placement group still taking coaches, or a new one in a world that can absorb
    /// a whole cohort — opening a new ranked world when none can.
    ///
    /// <paramref name="avoidUserIds"/> (Phase 9.5) are accounts the caller must not share a group with:
    /// a forming group holding one of them is skipped as if it were full. When every candidate is blocked
    /// the normal "open a fresh placement group" path runs, so the guard can never leave a coach unable to
    /// enrol — it only ever changes WHICH cohort they land in.</summary>
    private async Task<RankedGroup> FindOrCreatePlacementGroupAsync(
        IReadOnlyCollection<Guid> avoidUserIds, CancellationToken ct)
    {
        var forming = await _db.RankedGroups
            .Where(g => g.Kind == RankedGroupKind.Placement && g.Status == RankedGroupStatus.Forming)
            .OrderBy(g => g.CreatedUtc).ThenBy(g => g.Id)
            .ToListAsync(ct);

        foreach (var g in forming)
        {
            // One read serves both questions (a group is only GroupSize seats): is there room, and is one
            // of the seats held by an account linked to the caller?
            var occupants = (await _db.RankedSeats
                    .Where(s => s.RankedGroupId == g.Id && s.UserId != null)
                    .Select(s => s.UserId)
                    .ToListAsync(ct))
                .Where(u => u.HasValue).Select(u => u!.Value).ToList();

            if (occupants.Count >= g.Capacity) continue;
            if (avoidUserIds.Count > 0 && occupants.Any(u => avoidUserIds.Contains(u))) continue;

            return g;
        }

        var worlds = await _db.RankedWorlds
            .Where(w => w.Status == RankedWorldStatus.Open)
            .OrderBy(w => w.CreatedUtc).ThenBy(w => w.Id)
            .ToListAsync(ct);

        foreach (var w in worlds)
        {
            if (await PlaceableCapacityAsync(w.Id, ct) >= _opt.PlacementGroupSize)
                return await CreatePlacementGroupAsync(w, ct);
        }

        RankedWorld created = await CreateRankedWorldAsync(ct);
        return await CreatePlacementGroupAsync(created, ct);
    }

    /// <summary>Free placeable division seats in a world, minus the seats already spoken for by coaches
    /// sitting in an unresolved placement group there. Tier 1 is excluded (promotion-only).</summary>
    private async Task<int> PlaceableCapacityAsync(Guid rankedWorldId, CancellationToken ct)
    {
        int minTier = _opt.UpperPlacementTier();

        int free = await (from s in _db.RankedSeats
                          join g in _db.RankedGroups on s.RankedGroupId equals g.Id
                          where g.RankedWorldId == rankedWorldId
                                && g.Kind == RankedGroupKind.Division
                                && g.Tier >= minTier
                                && s.UserId == null
                          select s.Id).CountAsync(ct);

        int reserved = await (from s in _db.RankedSeats
                              join g in _db.RankedGroups on s.RankedGroupId equals g.Id
                              where g.RankedWorldId == rankedWorldId
                                    && g.Kind == RankedGroupKind.Placement
                                    && g.Status != RankedGroupStatus.Completed
                                    && s.UserId != null
                              select s.Id).CountAsync(ct);

        return free - reserved;
    }

    /// <summary>An open world with at least one placeable seat left, or a brand-new one.</summary>
    private async Task<RankedWorld> FindOrCreateWorldWithRoomAsync(CancellationToken ct)
    {
        var worlds = await _db.RankedWorlds
            .Where(w => w.Status == RankedWorldStatus.Open)
            .OrderBy(w => w.CreatedUtc).ThenBy(w => w.Id)
            .ToListAsync(ct);

        foreach (var w in worlds)
        {
            int minTier = _opt.UpperPlacementTier();
            int free = await (from s in _db.RankedSeats
                              join g in _db.RankedGroups on s.RankedGroupId equals g.Id
                              where g.RankedWorldId == w.Id
                                    && g.Kind == RankedGroupKind.Division
                                    && g.Tier >= minTier
                                    && s.UserId == null
                              select s.Id).CountAsync(ct);
            if (free > 0) return w;
        }

        return await CreateRankedWorldAsync(ct);
    }

    /// <summary>Opens a whole pyramid: every division group and every seat, all vacant. Cheap — the clubs
    /// are only generated when a group is first reached (<see cref="MaterializeAsync"/>).</summary>
    private async Task<RankedWorld> CreateRankedWorldAsync(CancellationToken ct)
    {
        int index = await _db.RankedWorlds.CountAsync(ct);
        var now = DateTime.UtcNow;

        var world = new RankedWorld
        {
            Id = Guid.NewGuid(),
            Name = $"Mondo {index + 1}",
            Seed = NewSeed(),
            SeasonNumber = 1,
            Status = RankedWorldStatus.Open,
            CreatedUtc = now,
        };

        var tiers = _opt.GroupsPerTier();
        for (int t = 0; t < tiers.Count; t++)
        {
            int tier = t + 1;
            for (int i = 0; i < tiers[t]; i++)
            {
                var group = new RankedGroup
                {
                    Id = Guid.NewGuid(),
                    RankedWorldId = world.Id,
                    Kind = RankedGroupKind.Division,
                    Tier = tier,
                    GroupIndex = i,
                    Name = tiers[t] > 1 ? $"Divisione {tier}{Letter(i)}" : $"Divisione {tier}",
                    Capacity = _opt.GroupSize,
                    WorldId = null,
                    Status = RankedGroupStatus.Forming,
                    CreatedUtc = now,
                };
                AddSeats(group);
                world.Groups.Add(group);
            }
        }

        _db.RankedWorlds.Add(world);
        await _db.SaveChangesAsync(ct);
        return world;
    }

    private async Task<RankedGroup> CreatePlacementGroupAsync(RankedWorld world, CancellationToken ct)
    {
        int index = await _db.RankedGroups.CountAsync(
            g => g.RankedWorldId == world.Id && g.Kind == RankedGroupKind.Placement, ct);
        var now = DateTime.UtcNow;

        var group = new RankedGroup
        {
            Id = Guid.NewGuid(),
            RankedWorldId = world.Id,
            Kind = RankedGroupKind.Placement,
            Tier = 0,
            GroupIndex = index,
            Name = $"Placement {index + 1}",
            Capacity = _opt.PlacementGroupSize,
            WorldId = null,
            Status = RankedGroupStatus.Forming,
            CreatedUtc = now,
        };
        AddSeats(group);

        _db.RankedGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return group;
    }

    private static void AddSeats(RankedGroup group)
    {
        for (int i = 0; i < group.Capacity; i++)
        {
            group.Seats.Add(new RankedSeat
            {
                Id = Guid.NewGuid(),
                RankedGroupId = group.Id,
                SeatIndex = i,
                ClubId = null,
                UserId = null,
                OccupiedUtc = null,
            });
        }
    }

    /// <summary>Claims the lowest free seat in a group for a coach (materialising the group's clubs first).</summary>
    private async Task<RankedSeat?> ClaimSeatAsync(RankedGroup group, Guid userId, CancellationToken ct)
    {
        await MaterializeAsync(group, ct);

        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id)
            .OrderBy(s => s.SeatIndex)
            .ToListAsync(ct);

        var seat = seats.FirstOrDefault(s => s.UserId is null);
        if (seat is null) return null;

        seat.UserId = userId;
        seat.OccupiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return seat;
    }

    /// <summary>Generates the group's world (its <see cref="RankedGroup.Capacity"/> clubs + squads) and pins
    /// one club to each seat. Idempotent — a materialised group is left alone. The seed is derived from the
    /// ranked world's root seed, so the whole pyramid is reproducible from one number.</summary>
    private async Task MaterializeAsync(RankedGroup group, CancellationToken ct)
    {
        if (group.WorldId is not null) return;

        long rootSeed = await _db.RankedWorlds
            .Where(w => w.Id == group.RankedWorldId)
            .Select(w => w.Seed)
            .FirstAsync(ct);

        World world = WorldFactory.Build(DeriveSeed(rootSeed, group), group.Capacity, group.Name);
        _db.Worlds.Add(world);
        await _db.SaveChangesAsync(ct);

        var clubs = world.Clubs.OrderBy(c => c.ExternalId).ToList();
        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id)
            .OrderBy(s => s.SeatIndex)
            .ToListAsync(ct);

        for (int i = 0; i < seats.Count && i < clubs.Count; i++) seats[i].ClubId = clubs[i].Id;
        group.WorldId = world.Id;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>The tier's group with the most room (fewest occupants), or null when the tier is full.</summary>
    private async Task<RankedGroup?> FindGroupWithFreeSeatAsync(Guid rankedWorldId, int tier, CancellationToken ct)
    {
        var groups = await _db.RankedGroups
            .Where(g => g.RankedWorldId == rankedWorldId && g.Kind == RankedGroupKind.Division && g.Tier == tier)
            .ToListAsync(ct);
        if (groups.Count == 0) return null;

        var ids = groups.Select(g => g.Id).ToList();
        var occupancy = await _db.RankedSeats
            .Where(s => ids.Contains(s.RankedGroupId) && s.UserId != null)
            .GroupBy(s => s.RankedGroupId)
            .Select(x => new { GroupId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        return groups
            .Select(g => new { Group = g, Taken = occupancy.TryGetValue(g.Id, out var n) ? n : 0 })
            .Where(x => x.Taken < x.Group.Capacity)
            .OrderBy(x => x.Taken).ThenBy(x => x.Group.GroupIndex)
            .Select(x => x.Group)
            .FirstOrDefault();
    }

    /// <summary>Flips a world to <see cref="RankedWorldStatus.Full"/> once it can no longer absorb a
    /// placement cohort (and back to Open if seats free up).</summary>
    private async Task RefreshWorldStatusAsync(Guid rankedWorldId, CancellationToken ct)
    {
        var world = await _db.RankedWorlds.FirstOrDefaultAsync(w => w.Id == rankedWorldId, ct);
        if (world is null || world.Status == RankedWorldStatus.Closed) return;

        int capacity = await PlaceableCapacityAsync(rankedWorldId, ct);
        var wanted = capacity >= _opt.PlacementGroupSize ? RankedWorldStatus.Open : RankedWorldStatus.Full;
        if (world.Status == wanted) return;

        world.Status = wanted;
        await _db.SaveChangesAsync(ct);
    }

    // --- projections ---------------------------------------------------------------------------

    private async Task<RankedStateDto> BuildStateAsync(RankedCoach coach, CancellationToken ct)
    {
        var world = await _db.RankedWorlds.FirstOrDefaultAsync(w => w.Id == coach.RankedWorldId, ct);

        RankedSeat? seat = coach.SeatId is { } seatId
            ? await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct)
            : null;

        RankedGroup? group = seat is not null
            ? await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct)
            : null;

        Club? club = seat?.ClubId is { } clubId
            ? await _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId, ct)
            : null;

        return new RankedStateDto(
            Enrolled: true,
            RankedWorldId: world?.Id,
            WorldName: world?.Name,
            GroupId: group?.Id,
            GroupName: group?.Name,
            Kind: group?.Kind,
            Tier: group?.Tier,
            SeatIndex: seat?.SeatIndex,
            ClubExternalId: club?.ExternalId,
            ClubName: club?.Name,
            Rating: coach.Rating,
            Status: coach.Status,
            PlacementPosition: coach.PlacementPosition,
            AutoEnrol: coach.AutoEnrol);
    }

    private async Task<RankedGroupDto> BuildGroupAsync(RankedGroup group, Guid callerId, CancellationToken ct)
    {
        var worldName = await _db.RankedWorlds
            .Where(w => w.Id == group.RankedWorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id)
            .OrderBy(s => s.SeatIndex)
            .ToListAsync(ct);

        var clubIds = seats.Where(s => s.ClubId is not null).Select(s => s.ClubId!.Value).ToList();
        var clubs = await _db.Clubs
            .Where(c => clubIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var userIds = seats.Where(s => s.UserId is not null).Select(s => s.UserId!.Value).Distinct().ToList();
        var displayNames = await _db.CoachProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);

        var seatDtos = seats.Select(s =>
        {
            Club? club = s.ClubId is { } cid && clubs.TryGetValue(cid, out var c) ? c : null;
            string? name = s.UserId is { } uid && displayNames.TryGetValue(uid, out var n) ? n : null;
            return new RankedSeatDto(
                SeatIndex: s.SeatIndex,
                UserId: s.UserId,
                DisplayName: name,
                ClubExternalId: club?.ExternalId,
                ClubName: club?.Name,
                ClubStrength: club?.Strength ?? 0,
                IsYou: s.UserId == callerId);
        }).ToList();

        return new RankedGroupDto(
            Id: group.Id,
            RankedWorldId: group.RankedWorldId,
            WorldName: worldName,
            Kind: group.Kind,
            Tier: group.Tier,
            GroupIndex: group.GroupIndex,
            Name: group.Name,
            Capacity: group.Capacity,
            Occupied: seatDtos.Count(s => s.UserId is not null),
            Status: group.Status,
            Seats: seatDtos);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static string Letter(int index) =>
        index >= 0 && index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();

    /// <summary>Per-group generation seed: a deterministic mix of the ranked world's root seed and the
    /// group's identity, so no two groups share a squad set and the world is reproducible.</summary>
    private static long DeriveSeed(long rootSeed, RankedGroup group)
    {
        unchecked
        {
            ulong mixed = (ulong)rootSeed;
            ulong key = (ulong)(((int)group.Kind + 1) * 1_000_003 + group.Tier * 1009 + group.GroupIndex + 1);
            mixed ^= key * 0x9E3779B97F4A7C15UL;
            mixed ^= mixed >> 29;
            mixed *= 0xBF58476D1CE4E5B9UL;
            mixed ^= mixed >> 32;
            return (long)mixed;
        }
    }

    private static long NewSeed()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }
}
