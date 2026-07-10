using System.Security.Cryptography;
using Fts.Application.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// <see cref="ILeagueService"/> implementation (Phase 8.1). Creating a league server-generates a fresh
/// world (unique players — <see cref="WorldFactory"/>) and makes the caller the first member; joining is
/// by invite code while the league is still <see cref="LeagueStatus.Forming"/>. All expected failures
/// come back as <see cref="LeagueResult{T}"/>. The account id is passed in by the Api from the access
/// token — never trusted from the body.
/// </summary>
public sealed class LeagueService : ILeagueService
{
    private readonly FtsDbContext _db;

    public LeagueService(FtsDbContext db) => _db = db;

    public const int MinSize = 2;
    public const int MaxSize = 20;

    // Unambiguous alphabet (no I/L/O/0/1) so invite codes are easy to read out and type.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    public async Task<LeagueResult<LeagueDetailDto>> CreateAsync(
        Guid userId, CreateLeagueRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.ValidationFailed, "A league name is required.");
        if (name.Length > 120) name = name[..120];

        if (request.Size < MinSize || request.Size > MaxSize)
            return LeagueResult<LeagueDetailDto>.Fail(
                LeagueError.ValidationFailed, $"League size must be between {MinSize} and {MaxSize}.");

        if (request.Mode is not (LeagueMode.AllReady or LeagueMode.RealTime))
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.ValidationFailed, "Unknown league mode.");

        var inviteCode = await GenerateUniqueCodeAsync(ct);

        // Generate + persist the world (unique players per world). The graph is inserted with the
        // private league in one SaveChanges.
        World world = WorldFactory.Build(NewSeed(), request.Size, name);

        var league = new PrivateLeague
        {
            Id = Guid.NewGuid(),
            Name = name,
            InviteCode = inviteCode,
            Size = request.Size,
            Mode = request.Mode,
            Status = LeagueStatus.Forming,
            CreatorUserId = userId,
            WorldId = world.Id,
            World = world,
            CreatedUtc = DateTime.UtcNow,
        };

        league.Members.Add(new LeagueMember
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClubId = null,
            IsReady = false,
            JoinedUtc = DateTime.UtcNow,
        });

        _db.PrivateLeagues.Add(league);
        await _db.SaveChangesAsync(ct);

        var detail = await BuildDetailAsync(league, userId, ct);
        return LeagueResult<LeagueDetailDto>.Ok(detail);
    }

    public async Task<LeagueResult<LeagueDetailDto>> JoinAsync(
        Guid userId, JoinLeagueRequest request, CancellationToken ct = default)
    {
        var code = request.InviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.ValidationFailed, "An invite code is required.");

        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.InviteCode == code, ct);
        if (league is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotFound, "No league with that invite code.");

        if (league.Status != LeagueStatus.Forming)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotJoinable, "This league is no longer accepting members.");

        var already = await _db.LeagueMembers.AnyAsync(
            m => m.PrivateLeagueId == league.Id && m.UserId == userId, ct);
        if (already)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.AlreadyMember, "You are already in this league.");

        var memberCount = await _db.LeagueMembers.CountAsync(m => m.PrivateLeagueId == league.Id, ct);
        if (memberCount >= league.Size)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.LeagueFull, "This league is full.");

        _db.LeagueMembers.Add(new LeagueMember
        {
            Id = Guid.NewGuid(),
            PrivateLeagueId = league.Id,
            UserId = userId,
            ClubId = null,
            IsReady = false,
            JoinedUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var detail = await BuildDetailAsync(league, userId, ct);
        return LeagueResult<LeagueDetailDto>.Ok(detail);
    }

    public async Task<LeagueResult<bool>> LeaveAsync(Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var member = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (member is null)
            return LeagueResult<bool>.Fail(LeagueError.NotFound, "You are not a member of this league.");

        var league = await _db.PrivateLeagues.FirstAsync(l => l.Id == leagueId, ct);
        var memberCount = await _db.LeagueMembers.CountAsync(m => m.PrivateLeagueId == leagueId, ct);

        if (memberCount <= 1)
        {
            // Last member out: tear down the whole league + its world. Delete explicitly in
            // dependency order (children first) so the existing clubs→leagues RESTRICT FK can't
            // block a DB cascade — portable across PostgreSQL and the SQLite test provider.
            var worldId = league.WorldId;
            await _db.Players.Where(p => p.WorldId == worldId).ExecuteDeleteAsync(ct);
            await _db.Coaches.Where(c => c.WorldId == worldId).ExecuteDeleteAsync(ct);
            await _db.Clubs.Where(c => c.WorldId == worldId).ExecuteDeleteAsync(ct);
            await _db.Leagues.Where(l => l.WorldId == worldId).ExecuteDeleteAsync(ct);
            await _db.LeagueMembers.Where(m => m.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            await _db.PrivateLeagues.Where(l => l.Id == leagueId).ExecuteDeleteAsync(ct);
            await _db.Worlds.Where(w => w.Id == worldId).ExecuteDeleteAsync(ct);
            return LeagueResult<bool>.Ok(true);
        }

        _db.LeagueMembers.Remove(member);
        if (league.CreatorUserId == userId)
        {
            // Ownership passes to the earliest remaining member.
            var next = await _db.LeagueMembers
                .Where(m => m.PrivateLeagueId == leagueId && m.Id != member.Id)
                .OrderBy(m => m.JoinedUtc)
                .FirstAsync(ct);
            league.CreatorUserId = next.UserId;
        }

        await _db.SaveChangesAsync(ct);
        return LeagueResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<LeagueSummaryDto>> ListMineAsync(Guid userId, CancellationToken ct = default)
    {
        var myLeagueIds = await _db.LeagueMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.PrivateLeagueId)
            .ToListAsync(ct);

        if (myLeagueIds.Count == 0) return Array.Empty<LeagueSummaryDto>();

        var leagues = await _db.PrivateLeagues
            .Where(l => myLeagueIds.Contains(l.Id))
            .ToListAsync(ct);

        var counts = await _db.LeagueMembers
            .Where(m => myLeagueIds.Contains(m.PrivateLeagueId))
            .GroupBy(m => m.PrivateLeagueId)
            .Select(g => new { LeagueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LeagueId, x => x.Count, ct);

        return leagues
            .OrderByDescending(l => l.CreatedUtc)
            .Select(l => Summary(l, counts.TryGetValue(l.Id, out var c) ? c : 0, userId))
            .ToList();
    }

    public async Task<LeagueResult<LeagueDetailDto>> GetAsync(Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        var detail = await BuildDetailAsync(league, userId, ct);
        return LeagueResult<LeagueDetailDto>.Ok(detail);
    }

    // --- helpers -------------------------------------------------------------------------------

    private async Task<LeagueDetailDto> BuildDetailAsync(PrivateLeague league, Guid callerId, CancellationToken ct)
    {
        var members = await _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == league.Id)
            .OrderBy(m => m.JoinedUtc)
            .ToListAsync(ct);

        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var displayNames = await _db.CoachProfiles
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);

        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);

        var clubsById = clubs.ToDictionary(c => c.Id);

        var memberDtos = members.Select(m =>
        {
            Club? club = m.ClubId is { } cid && clubsById.TryGetValue(cid, out var c) ? c : null;
            return new LeagueMemberDto(
                UserId: m.UserId,
                DisplayName: displayNames.TryGetValue(m.UserId, out var n) ? n : "coach",
                ClubExternalId: club?.ExternalId,
                ClubName: club?.Name,
                IsCreator: m.UserId == league.CreatorUserId,
                IsReady: m.IsReady);
        }).ToList();

        var clubDtos = clubs
            .OrderBy(c => c.ExternalId)
            .Select(c => new LeagueClubDto(
                ExternalId: c.ExternalId,
                Name: c.Name,
                ShortName: c.ShortName,
                Strength: c.Strength,
                Players: c.Players
                    .OrderBy(p => p.ExternalId)
                    .Select(p => new LeaguePlayerDto(
                        ExternalId: p.ExternalId,
                        Name: FullName(p),
                        Age: p.Age,
                        Role: p.Role,
                        Overall: p.Overall))
                    .ToList()))
            .ToList();

        return new LeagueDetailDto(Summary(league, members.Count, callerId), memberDtos, clubDtos);
    }

    private static LeagueSummaryDto Summary(PrivateLeague l, int memberCount, Guid callerId) => new(
        Id: l.Id,
        Name: l.Name,
        InviteCode: l.InviteCode,
        Size: l.Size,
        MemberCount: memberCount,
        Status: l.Status,
        Mode: l.Mode,
        IsCreator: l.CreatorUserId == callerId);

    private static string FullName(Player p) =>
        string.IsNullOrEmpty(p.FirstName) ? p.LastName : $"{p.FirstName} {p.LastName}";

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var code = NewCode();
            if (!await _db.PrivateLeagues.AnyAsync(l => l.InviteCode == code, ct))
                return code;
        }
        // Astronomically unlikely; fall back to a longer code that is effectively collision-free.
        return NewCode(CodeLength + 4);
    }

    private static string NewCode(int length = CodeLength)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(chars);
    }

    private static long NewSeed()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }
}
