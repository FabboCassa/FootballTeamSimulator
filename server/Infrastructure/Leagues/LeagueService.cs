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

    /// <summary>At least two clubs must be human-controlled to play a season (8.2). Any unclaimed clubs
    /// (members &lt; size) stay AI-controlled.</summary>
    public const int MinDraftMembers = 2;

    // Equal starting economy for every club when the draft runs — "pari budget a tutti" (8.2). Online
    // fairness: the market choices, not the starting kitty, decide the squads. First-pass tunable knobs.
    public const long DraftTransferBudget = 25_000_000;
    public const long DraftStartingBalance = 25_000_000;

    // Neutral starting condition, matching the Player entity defaults — reapplied when a new season resets
    // the world (8.7) so every squad begins fresh and fair.
    private const int NeutralForm = 50;
    private const int NeutralMorale = 50;
    private const int FullFitness = 100;

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

    public async Task<LeagueResult<LeagueDetailDto>> StartDraftAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.CreatorUserId != userId)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "Only the league owner can start the draft.");
        if (league.Status != LeagueStatus.Forming)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.WrongPhase, "The draft has already started.");

        var memberCount = await _db.LeagueMembers.CountAsync(m => m.PrivateLeagueId == leagueId, ct);
        if (memberCount < MinDraftMembers)
            return LeagueResult<LeagueDetailDto>.Fail(
                LeagueError.TooFewMembers, $"At least {MinDraftMembers} members are needed to start the season.");

        // Equalise every club's squad to the same strength (fair start) and give every club the same
        // budget. Player ids are only re-parented — no player added/removed/duplicated.
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);
        var players = clubs.SelectMany(c => c.Players).ToList();
        SquadEqualizer.Equalize(clubs, players);
        foreach (var club in clubs)
        {
            club.TransferBudget = DraftTransferBudget;
            club.Balance = DraftStartingBalance;
        }

        league.Status = LeagueStatus.Drafting;
        await _db.SaveChangesAsync(ct);

        var detail = await BuildDetailAsync(league, userId, ct);
        return LeagueResult<LeagueDetailDto>.Ok(detail);
    }

    public async Task<LeagueResult<LeagueDetailDto>> PickClubAsync(
        Guid userId, Guid leagueId, PickClubRequest request, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotFound, "League not found.");
        if (league.Status != LeagueStatus.Drafting)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.WrongPhase, "The draft is not currently running.");

        var members = await _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == leagueId)
            .OrderBy(m => m.JoinedUtc).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var me = members.FirstOrDefault(m => m.UserId == userId);
        if (me is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        // Turn = the earliest-joined member who has not yet picked (one club per member → snake reversal is
        // a no-op with single picks, but the turn is still strictly ordered).
        var current = members.FirstOrDefault(m => m.ClubId is null);
        if (current is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.WrongPhase, "The draft is already complete.");
        if (current.UserId != userId)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotYourTurn, "It is not your turn to pick.");

        var club = await _db.Clubs.FirstOrDefaultAsync(
            c => c.WorldId == league.WorldId && c.ExternalId == request.ClubExternalId, ct);
        if (club is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.ClubUnavailable, "No such club in this league.");

        if (members.Any(m => m.ClubId == club.Id))
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.ClubUnavailable, "That club has already been taken.");

        me.ClubId = club.Id;

        // Once every member holds a club the season is set: the league goes Active and its double
        // round-robin schedule is generated (8.3). Fixtures cover ALL world clubs — unclaimed clubs
        // (members < size) play as AI. Deterministic from the world seed.
        if (members.All(m => m.ClubId is not null))
        {
            league.Status = LeagueStatus.Active;
            await GenerateSeasonFixturesAsync(league, ct);
        }

        await _db.SaveChangesAsync(ct);

        var detail = await BuildDetailAsync(league, userId, ct);
        return LeagueResult<LeagueDetailDto>.Ok(detail);
    }

    public async Task<LeagueResult<LeagueDetailDto>> StartNewSeasonAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.CreatorUserId != userId)
            return LeagueResult<LeagueDetailDto>.Fail(LeagueError.Forbidden, "Only the league owner can start a new season.");
        if (league.Status != LeagueStatus.Completed)
            return LeagueResult<LeagueDetailDto>.Fail(
                LeagueError.WrongPhase, "A new season can only start once the current one has finished.");

        // Tear down the finished season's rows (keep the world + clubs + players — the reset re-drafts the same
        // evolving player pool). Bids/auctions reference players via a Restrict FK; live sessions reference
        // fixtures by a plain column — delete them first. Ordered ExecuteDelete stays portable across
        // PostgreSQL and the SQLite test provider.
        await _db.LiveMatches.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
        await _db.LeagueFixtures.Where(f => f.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
        await _db.LeagueLineups.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
        await _db.LeagueTrainings.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
        await _db.Bids.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
        await _db.Auctions.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);

        // Un-assign every member's club and clear ready flags for the fresh draft.
        var members = await _db.LeagueMembers.Where(m => m.PrivateLeagueId == leagueId).ToListAsync(ct);
        foreach (var m in members) { m.ClubId = null; m.IsReady = false; }

        // Re-equalise the (now-developed) squads to equal strength, re-seed equal budgets, and reset condition
        // to neutral — the same fair, fresh starting state as StartDraft. Players keep the ability they
        // developed over the season; only the squads are reshuffled equal and the draft reopens.
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);
        var players = clubs.SelectMany(c => c.Players).ToList();
        SquadEqualizer.Equalize(clubs, players);
        foreach (var club in clubs)
        {
            club.TransferBudget = DraftTransferBudget;
            club.Balance = DraftStartingBalance;
        }
        foreach (var p in players) { p.Form = NeutralForm; p.Morale = NeutralMorale; p.Fitness = FullFitness; }

        league.Status = LeagueStatus.Drafting;
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
            // Season rows first: league_fixtures/league_lineups Restrict-reference clubs, so they must
            // go before the clubs delete below (they also cascade from the private league, but explicit
            // ordered deletes keep the teardown portable across PostgreSQL and the SQLite test provider).
            // Live sessions (8.6) reference fixtures by a plain column — delete them before the fixtures.
            await _db.LiveMatches.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            await _db.LeagueFixtures.Where(f => f.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            await _db.LeagueLineups.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            await _db.LeagueTrainings.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            // Auctions/bids (8.5) reference players via a Restrict FK — delete them before the players.
            await _db.Bids.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
            await _db.Auctions.Where(x => x.PrivateLeagueId == leagueId).ExecuteDeleteAsync(ct);
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

    /// <summary>Generates the season's double round-robin over every club in the world (8.3), keyed to
    /// the persisted club rows, and adds the fixtures to the change tracker (saved by the caller). The
    /// schedule is deterministic from the world seed via <see cref="FixtureScheduler"/>.</summary>
    private async Task GenerateSeasonFixturesAsync(PrivateLeague league, CancellationToken ct)
    {
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Select(c => new { c.Id, c.ExternalId })
            .ToListAsync(ct);
        long seed = await _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);

        var guidByExternal = clubs.ToDictionary(c => c.ExternalId, c => c.Id);
        var externalIds = clubs.Select(c => c.ExternalId).ToList();

        foreach (var s in FixtureScheduler.Build(externalIds, seed))
        {
            _db.LeagueFixtures.Add(new LeagueFixture
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = league.Id,
                Round = s.Round,
                MatchIndex = s.MatchIndex,
                Day = s.Day,
                HomeClubId = guidByExternal[s.HomeExternalId],
                AwayClubId = guidByExternal[s.AwayExternalId],
                IsPlayed = false,
            });
        }
    }

    private async Task<LeagueDetailDto> BuildDetailAsync(PrivateLeague league, Guid callerId, CancellationToken ct)
    {
        var members = await _db.LeagueMembers
            .Where(m => m.PrivateLeagueId == league.Id)
            .OrderBy(m => m.JoinedUtc).ThenBy(m => m.Id)
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
                TransferBudget: c.TransferBudget,
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

        var picksMade = members.Count(m => m.ClubId is not null);
        var inProgress = league.Status == LeagueStatus.Drafting;
        Guid? currentPickUserId = inProgress
            ? members.FirstOrDefault(m => m.ClubId is null)?.UserId
            : null;
        var draft = new DraftStateDto(inProgress, currentPickUserId, picksMade, members.Count);

        return new LeagueDetailDto(Summary(league, members.Count, callerId), memberDtos, clubDtos, draft);
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
