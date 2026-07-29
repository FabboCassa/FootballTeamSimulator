using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Sim.Core.Development;
using Sim.Core.Match;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Ranked;

/// <summary>How the ranked services (de)serialize the shared Sim.Core plan types. One instance so the
/// lineup/tactic/training JSON written by any of them reads back identically — enums by name, properties
/// case-insensitive (the client posts camelCase).</summary>
internal static class RankedPlanJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// The 9.4 SMART DEFAULTS for a ranked coach's match inputs — the half of the "≤10-minute daily loop" that
/// happens without the coach doing anything.
///
/// Two guarantees, both server-side and both about never punishing a coach for not visiting a screen:
/// <list type="bullet">
/// <item><b>Seeded</b> — when a season starts, every human seat gets a stored best-XI
/// <c>LineupPlan</c> and a balanced <c>TrainingPlan</c>. Before 9.4 an unset lineup fell back to BestEleven
/// silently at kickoff, which worked but left the client unable to say anything truthful about readiness, and
/// left training permanently on the AI default.</item>
/// <item><b>Repaired</b> — when a stored lineup stops materialising against the club's current squad (a
/// starter was sold during a market window), it is rebuilt from the best available XI instead of quietly
/// falling through at kickoff. The coach's tactic and pre-match plan are preserved.</item>
/// </list>
///
/// Everything here is ordinary server bookkeeping over the persisted world plus the shared, deterministic
/// <c>LineupSelector</c> — NO Sim.Core change, and the resolved XI is the same one the engine would have
/// picked as a fallback, so a seeded default cannot change any result that was going to happen anyway.
/// </summary>
internal static class RankedInputDefaults
{
    /// <summary>Seeds a stored lineup + training plan for every human seat in the group that has none.
    /// Called when a season starts. Does NOT call SaveChanges — the caller commits (the season start writes
    /// the whole schedule in one transaction). Returns how many seats were seeded.</summary>
    public static async Task<int> SeedForGroupAsync(FtsDbContext db, Guid groupId, CancellationToken ct)
    {
        var seats = await db.RankedSeats
            .Where(s => s.RankedGroupId == groupId && s.UserId != null && s.ClubId != null)
            .Select(s => new { ClubId = s.ClubId!.Value, UserId = s.UserId!.Value })
            .ToListAsync(ct);
        if (seats.Count == 0) return 0;

        var clubIds = seats.Select(s => s.ClubId).ToList();
        var clubs = await db.Clubs
            .Where(c => clubIds.Contains(c.Id))
            .Include(c => c.Players)
            .ToListAsync(ct);
        var clubById = clubs.ToDictionary(c => c.Id);

        var existingLineups = await db.RankedLineups
            .Where(x => x.RankedGroupId == groupId)
            .Select(x => x.ClubId)
            .ToListAsync(ct);
        var existingTrainings = await db.RankedTrainings
            .Where(x => x.RankedGroupId == groupId)
            .Select(x => x.ClubId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        int seeded = 0;

        foreach (var seat in seats)
        {
            if (!clubById.TryGetValue(seat.ClubId, out var club)) continue;

            if (!existingLineups.Contains(seat.ClubId) && BestElevenPlanJson(club) is { } lineupJson)
            {
                db.RankedLineups.Add(new RankedLineup
                {
                    Id = Guid.NewGuid(),
                    RankedGroupId = groupId,
                    UserId = seat.UserId,
                    ClubId = seat.ClubId,
                    LineupJson = lineupJson,
                    ConfirmedRound = 0,          // seeded, not confirmed — the coach still gets a "confirm" nudge
                    UpdatedUtc = now,
                });
                seeded++;
            }

            if (!existingTrainings.Contains(seat.ClubId))
            {
                db.RankedTrainings.Add(new RankedTraining
                {
                    Id = Guid.NewGuid(),
                    RankedGroupId = groupId,
                    UserId = seat.UserId,
                    ClubId = seat.ClubId,
                    TrainingJson = JsonSerializer.Serialize(TrainingPlan.Balanced(), RankedPlanJson.Options),
                    UpdatedUtc = now,
                });
            }
        }

        return seeded;
    }

    /// <summary>Makes sure the given seat has a stored lineup that materialises against its CURRENT squad,
    /// seeding or repairing it from the best available XI. Returns true when something was written (the caller
    /// commits). Safe to call whenever a squad changed.</summary>
    public static async Task<bool> EnsureLineupAsync(
        FtsDbContext db, Guid groupId, Guid clubId, Guid userId, CancellationToken ct)
    {
        var club = await db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null) return false;

        var row = await db.RankedLineups.FirstOrDefaultAsync(
            x => x.RankedGroupId == groupId && x.ClubId == clubId, ct);

        SimClub sim = WorldSquadReader.ToSimClub(club);
        if (row is not null && StillValid(row.LineupJson, sim)) return false;

        if (BestElevenPlanJson(club) is not { } json) return false;

        if (row is null)
        {
            db.RankedLineups.Add(new RankedLineup
            {
                Id = Guid.NewGuid(),
                RankedGroupId = groupId,
                UserId = userId,
                ClubId = clubId,
                LineupJson = json,
                ConfirmedRound = 0,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            // Repair in place: the tactic + pre-match plan the coach chose are kept, only the XI is rebuilt.
            row.LineupJson = json;
            row.UpdatedUtc = DateTime.UtcNow;
        }
        return true;
    }

    /// <summary>Repairs the club's stored lineup if a squad change broke it — the market's hook. Looks the
    /// seat up itself (a club whose seat is vacant, or with no stored lineup, is left alone: an AI club has
    /// no inputs to repair). Returns true when the lineup was rebuilt.</summary>
    public static async Task<bool> RepairAfterSquadChangeAsync(
        FtsDbContext db, Guid groupId, Guid clubId, CancellationToken ct)
    {
        var row = await db.RankedLineups.FirstOrDefaultAsync(
            x => x.RankedGroupId == groupId && x.ClubId == clubId, ct);
        if (row is null) return false;

        var club = await db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null) return false;

        SimClub sim = WorldSquadReader.ToSimClub(club);
        if (StillValid(row.LineupJson, sim)) return false;
        if (BestElevenPlanJson(club) is not { } json) return false;

        row.LineupJson = json;
        row.UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Seeds a balanced training plan for the seat when it has none (Phase 9.4 smart default).
    /// Returns true when a row was added (the caller commits).</summary>
    public static async Task<bool> EnsureTrainingAsync(
        FtsDbContext db, Guid groupId, Guid clubId, Guid userId, CancellationToken ct)
    {
        bool exists = await db.RankedTrainings.AnyAsync(
            x => x.RankedGroupId == groupId && x.ClubId == clubId, ct);
        if (exists) return false;

        db.RankedTrainings.Add(new RankedTraining
        {
            Id = Guid.NewGuid(),
            RankedGroupId = groupId,
            UserId = userId,
            ClubId = clubId,
            TrainingJson = JsonSerializer.Serialize(TrainingPlan.Balanced(), RankedPlanJson.Options),
            UpdatedUtc = DateTime.UtcNow,
        });
        return true;
    }

    /// <summary>Whether the stored plan still resolves to a legal XI for the club as it is now.</summary>
    private static bool StillValid(string lineupJson, SimClub sim)
    {
        if (string.IsNullOrWhiteSpace(lineupJson)) return false;
        LineupPlan? plan;
        try { plan = JsonSerializer.Deserialize<LineupPlan>(lineupJson, RankedPlanJson.Options); }
        catch (JsonException) { return false; }
        return plan is not null && plan.Slots.Count > 0 && plan.TryMaterialize(sim, out _);
    }

    /// <summary>The club's best available XI as a serialized <c>LineupPlan</c>, or null when the squad cannot
    /// field one (too few players / no goalkeeper — an edge case the caller simply skips).</summary>
    private static string? BestElevenPlanJson(Club club)
    {
        if (club.Players.Count < 11) return null;
        try
        {
            SimClub sim = WorldSquadReader.ToSimClub(club);
            LineupPlan plan = LineupPlan.From(LineupSelector.BestEleven(sim));
            return JsonSerializer.Serialize(plan, RankedPlanJson.Options);
        }
        catch (InvalidOperationException)
        {
            return null;   // BestEleven could not build a legal XI from this squad.
        }
    }
}
