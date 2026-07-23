using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Notifications;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sim.Core.Career;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Match;
using Sim.Core.Tactics;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedSeasonService"/> implementation (Phase 9.2) — the ranked ladder's real-time season
/// engine. Unlike the private-league season (8.3, "advance when all ready"), the ranked calendar is driven
/// by a wall-clock: <see cref="TickAsync"/> (run by a recurring Hangfire job, or a test) starts due seasons,
/// resolves each group's lowest due matchday once its kickoff has passed, announces the market windows and
/// closes finished seasons — sorting a finished placement cohort into divisions.
///
/// Matches resolve exactly like the private league (shared Sim.Core engine, deterministic per-fixture seed,
/// each seat's last submitted lineup with a best-XI AI fallback for vacant/unset seats) so a ranked replay
/// renders identically for everyone. There is NO Sim.Core change here — the Api pulls Sim.Core transitively.
///
/// 9.2a scope: the calendar backbone (schedule, matchday resolution, placement auto-resolution, market
/// windows firing + notifications). The market CONTENT (free-agent auctions + direct coach offers) opens
/// in 9.2b; whole-world condition/development evolution across matchdays is deferred with it.
/// </summary>
public sealed class RankedSeasonService : IRankedSeasonService
{
    private readonly FtsDbContext _db;
    private readonly IRankedService _ranked;
    private readonly INotificationService _notify;
    private readonly RankedOptions _opt;
    private readonly BalanceConfig _config = new();

    public RankedSeasonService(
        FtsDbContext db, IRankedService ranked, INotificationService notify, IOptions<RankedOptions> options)
    {
        _db = db;
        _ranked = ranked;
        _notify = notify;
        _opt = options.Value;
    }

    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- the clock ---------------------------------------------------------------------------------

    public async Task<RankedTickSummary> TickAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        int started = await StartDueSeasonsAsync(now, ct);
        (int matchdays, int fixtures, int placements, int completed) = await ResolveDueMatchdaysAsync(now, ct);
        int windows = await AnnounceMarketWindowsAsync(now, ct);

        return new RankedTickSummary(started, matchdays, fixtures, placements, completed, windows);
    }

    /// <summary>Generates the schedule for any group whose season is due but not yet started: a filled
    /// placement group, or a division that has at least one human coach. Deterministic double round-robin
    /// (<see cref="FixtureScheduler"/>) stamped with real-time kickoffs from <see cref="RankedCalendar"/>.</summary>
    private async Task<int> StartDueSeasonsAsync(DateTime now, CancellationToken ct)
    {
        // Candidate groups: materialised, not completed, with no fixtures yet.
        var candidates = await _db.RankedGroups
            .Where(g => g.WorldId != null
                        && g.Status != RankedGroupStatus.Completed
                        && !_db.RankedFixtures.Any(f => f.RankedGroupId == g.Id))
            .ToListAsync(ct);

        int started = 0;
        foreach (var group in candidates)
        {
            int humans = await _db.RankedSeats.CountAsync(
                s => s.RankedGroupId == group.Id && s.UserId != null, ct);

            bool due = group.Kind == RankedGroupKind.Placement
                ? group.Status == RankedGroupStatus.Active   // a placement season starts once its cohort is full
                : humans > 0;                                 // a division starts once a real coach holds a seat

            if (!due) continue;

            await GenerateScheduleAsync(group, now, ct);
            started++;
        }
        return started;
    }

    private async Task GenerateScheduleAsync(RankedGroup group, DateTime now, CancellationToken ct)
    {
        var world = await _db.Worlds.FirstAsync(w => w.Id == group.WorldId, ct);

        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id && s.ClubId != null)
            .ToListAsync(ct);

        var clubIds = seats.Select(s => s.ClubId!.Value).ToList();
        var clubs = await _db.Clubs.Where(c => clubIds.Contains(c.Id)).ToListAsync(ct);
        var clubByExternal = clubs.ToDictionary(c => c.ExternalId, c => c.Id);

        var externalIds = clubs.Select(c => c.ExternalId).OrderBy(x => x).ToList();
        var schedule = FixtureScheduler.Build(externalIds, world.Seed);

        foreach (var s in schedule)
        {
            if (!clubByExternal.TryGetValue(s.HomeExternalId, out var homeGuid)) continue;
            if (!clubByExternal.TryGetValue(s.AwayExternalId, out var awayGuid)) continue;

            _db.RankedFixtures.Add(new RankedFixture
            {
                Id = Guid.NewGuid(),
                RankedGroupId = group.Id,
                Round = s.Round,
                MatchIndex = s.MatchIndex,
                Day = s.Day,
                KickoffUtc = RankedCalendar.KickoffOf(now, s.Round, _opt.MatchdayIntervalSeconds),
                HomeClubId = homeGuid,
                AwayClubId = awayGuid,
                IsPlayed = false,
            });
        }

        // Seed every club a transfer budget for the season's market windows (Phase 9.2b): the money a coach
        // spends on direct offers to other coaches. Flat + equal — a fair ranked start (like the 8.2 draft).
        foreach (var c in clubs) c.TransferBudget = _opt.StartingTransferBudget;

        group.SeasonStartedUtc = now;
        group.LastMarketWindowOpened = -1;
        group.Status = RankedGroupStatus.Active;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves each active group's lowest still-pending round once its kickoff has passed, then
    /// closes/sorts any group whose schedule is now complete. One matchday per group per tick keeps the
    /// real-time cadence (with a compressed interval a test just calls Tick repeatedly).</summary>
    private async Task<(int matchdays, int fixtures, int placements, int completed)> ResolveDueMatchdaysAsync(
        DateTime now, CancellationToken ct)
    {
        var groups = await _db.RankedGroups
            .Where(g => g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active)
            .ToListAsync(ct);

        int matchdays = 0, fixturesResolved = 0, placements = 0, completed = 0;

        foreach (var group in groups)
        {
            var fixtures = await _db.RankedFixtures
                .Where(f => f.RankedGroupId == group.Id)
                .ToListAsync(ct);
            if (fixtures.Count == 0) continue;

            var pending = fixtures.Where(f => !f.IsPlayed).ToList();
            if (pending.Count == 0)
            {
                // The season is over — close it (and sort a placement cohort into divisions).
                if (await CompleteSeasonAsync(group, fixtures, ct)) { if (group.Kind == RankedGroupKind.Placement) placements++; else completed++; }
                continue;
            }

            int round = pending.Min(f => f.Round);
            var roundFixtures = pending.Where(f => f.Round == round).OrderBy(f => f.MatchIndex).ToList();

            // Not due yet — every fixture in a round shares one kickoff, so the first tells the time.
            if (roundFixtures[0].KickoffUtc > now) continue;

            fixturesResolved += await ResolveRoundAsync(group, roundFixtures, round, ct);
            matchdays++;

            // If that was the last round, close the season in the same tick.
            if (fixtures.All(f => f.IsPlayed))
                if (await CompleteSeasonAsync(group, fixtures, ct)) { if (group.Kind == RankedGroupKind.Placement) placements++; else completed++; }
        }

        return (matchdays, fixturesResolved, placements, completed);
    }

    private async Task<int> ResolveRoundAsync(
        RankedGroup group, List<RankedFixture> roundFixtures, int round, CancellationToken ct)
    {
        var world = await _db.Worlds.FirstAsync(w => w.Id == group.WorldId, ct);
        long worldSeed = world.Seed;

        // Reconstruct the WHOLE group's world so the weekly tick evolves every club (idle ones rest too),
        // exactly like the private-league season (8.4). Matches resolve on the clubs' live condition.
        var clubs = await _db.Clubs.Where(c => c.WorldId == group.WorldId).Include(c => c.Players).ToListAsync(ct);
        var entByGuid = clubs.ToDictionary(c => c.Id);
        var simByGuid = clubs.ToDictionary(c => c.Id, WorldSquadReader.ToSimClub);

        var lineups = await _db.RankedLineups.Where(x => x.RankedGroupId == group.Id).ToListAsync(ct);
        var inputsByClub = lineups.ToDictionary(x => x.ClubId, DeserializeInputs);

        // The human coaches to notify of their matchday result, keyed by their club id.
        var humanByClub = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id && s.UserId != null && s.ClubId != null)
            .ToDictionaryAsync(s => s.ClubId!.Value, s => s.UserId!.Value, ct);

        // The clubs that played this round (kickoff XI + result) — the input to the weekly condition tick.
        var played = new Dictionary<int, ConditionProgressor.Participation>();

        var now = DateTime.UtcNow;
        int resolved = 0;
        foreach (var f in roundFixtures)
        {
            SimClub home = simByGuid[f.HomeClubId];
            SimClub away = simByGuid[f.AwayClubId];
            inputsByClub.TryGetValue(f.HomeClubId, out var homeInputs);
            inputsByClub.TryGetValue(f.AwayClubId, out var awayInputs);

            int homeExt = entByGuid[f.HomeClubId].ExternalId;
            int awayExt = entByGuid[f.AwayClubId].ExternalId;

            ulong seed = FixtureSeed.For(worldSeed, f.Round, homeExt, awayExt);
            MatchResolver.ResolveResult r = MatchResolver.Resolve(home, away, homeInputs, awayInputs, seed, _config);
            MatchReport report = r.Report;

            f.HomeGoals = report.HomeGoals;
            f.AwayGoals = report.AwayGoals;
            f.IsPlayed = true;
            f.MatchSeed = unchecked((long)seed);
            f.ResolvedUtc = now;
            f.ReplayJson = JsonSerializer.Serialize(report);
            resolved++;

            played[homeExt] = new ConditionProgressor.Participation(
                r.HomeStarterIds, ResultFor(report.HomeGoals, report.AwayGoals));
            played[awayExt] = new ConditionProgressor.Participation(
                r.AwayStarterIds, ResultFor(report.AwayGoals, report.HomeGoals));

            // Notify the two human coaches (best-effort — the sender no-ops when FCM is unconfigured).
            await NotifyMatchdayAsync(humanByClub, f.HomeClubId, entByGuid, report.HomeGoals, report.AwayGoals, ct);
            await NotifyMatchdayAsync(humanByClub, f.AwayClubId, entByGuid, report.AwayGoals, report.HomeGoals, ct);
        }

        // Server-authoritative weekly tick (1 resolved round = 1 week): evolve the WHOLE world's condition +
        // development on the same reconstructed Sim.Core clubs the matches used (9.2b). Ranked coaches do not
        // submit training yet, so every club trains the AI default; a client re-running the same deterministic
        // progressors from this state derives the same world-state hash (the 8.4 agreement mechanic).
        var simWorld = new SimLeague { Division = 1 };
        foreach (SimClub sc in simByGuid.Values) simWorld.Clubs.Add(sc);
        OnlineSeasonTick.EvolveWeek(new[] { simWorld }, played, NoTraining, unchecked((ulong)worldSeed), round, _config);

        // Persist the evolved condition + attributes back onto every player.
        foreach (var (guid, ent) in entByGuid)
        {
            var simByExternal = simByGuid[guid].Squad.Players.ToDictionary(p => p.Id);
            foreach (Player entPlayer in ent.Players)
            {
                if (!simByExternal.TryGetValue(entPlayer.ExternalId, out var sp)) continue;
                entPlayer.Form = sp.Condition.Form;
                entPlayer.Morale = sp.Condition.Morale;
                entPlayer.Fitness = sp.Condition.Fitness;
                entPlayer.AttributesJson = WorldSquadReader.SerializeAttributes(sp.Attributes);
                entPlayer.Overall = Sim.Core.Domain.PlayerRating.Overall(sp);
            }
        }

        await _db.SaveChangesAsync(ct);
        return resolved;
    }

    /// <summary>A team's result from its own goals vs the goals conceded (for the condition tick).</summary>
    private static TeamResult ResultFor(int goalsFor, int goalsAgainst) =>
        goalsFor > goalsAgainst ? TeamResult.Win
        : goalsFor < goalsAgainst ? TeamResult.Loss
        : TeamResult.Draw;

    /// <summary>Ranked coaches do not submit training yet — every club trains the AI default each week.</summary>
    private static readonly IReadOnlyDictionary<int, TrainingPlan> NoTraining = new Dictionary<int, TrainingPlan>();

    /// <summary>Closes a group whose schedule is fully played: a placement group is sorted into divisions
    /// (top finishers up, the rest down) via <see cref="IRankedService.ResolvePlacementAsync"/>; a division
    /// is just marked Completed and its humans told the final table (promotion/relegation + reset is 9.3).
    /// Returns true when it actually closed something (idempotent — a re-entry is a no-op).</summary>
    private async Task<bool> CompleteSeasonAsync(RankedGroup group, List<RankedFixture> fixtures, CancellationToken ct)
    {
        if (group.Status == RankedGroupStatus.Completed) return false;

        var clubs = await _db.Clubs
            .Where(c => c.WorldId == group.WorldId)
            .ToListAsync(ct);
        var standings = ComputeStandings(clubs, fixtures);

        if (group.Kind == RankedGroupKind.Placement)
        {
            // Human coaches keyed by their club's external id, best finish first — the final order the
            // placement resolver sorts by. Built in memory (no EF join on a nullable key).
            var humanSeats = await _db.RankedSeats
                .Where(s => s.RankedGroupId == group.Id && s.UserId != null && s.ClubId != null)
                .Select(s => new { ClubId = s.ClubId!.Value, UserId = s.UserId!.Value })
                .ToListAsync(ct);
            var externalByClubId = clubs.ToDictionary(c => c.Id, c => c.ExternalId);
            var humanByClubExternal = new Dictionary<int, Guid>();
            foreach (var hs in humanSeats)
                if (externalByClubId.TryGetValue(hs.ClubId, out var ext)) humanByClubExternal[ext] = hs.UserId;

            var order = standings
                .Where(s => humanByClubExternal.ContainsKey(s.ClubExternalId))
                .Select(s => humanByClubExternal[s.ClubExternalId])
                .ToList();

            var result = await _ranked.ResolvePlacementAsync(group.Id, order, ct);
            if (result.Success && result.Value is not null)
                foreach (var a in result.Value.Assignments)
                    await SafeSend(a.UserId, "Piazzamento completato",
                        $"Sei stato assegnato a {a.GroupName} ({a.WorldName}) con il club {a.ClubName}.",
                        new Dictionary<string, string> { ["kind"] = "ranked_placed", ["groupId"] = a.GroupId.ToString() }, ct);
            return result.Success;
        }

        group.Status = RankedGroupStatus.Completed;
        await _db.SaveChangesAsync(ct);

        var humans = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id && s.UserId != null)
            .Select(s => s.UserId!.Value)
            .ToListAsync(ct);
        foreach (var uid in humans)
            await SafeSend(uid, "Stagione conclusa",
                $"La stagione del girone {group.Name} è terminata. Controlla la classifica finale.",
                new Dictionary<string, string> { ["kind"] = "ranked_season_end", ["groupId"] = group.Id.ToString() }, ct);
        return true;
    }

    /// <summary>Announces the market window that just opened for each running season (once each, at season
    /// start and around the midpoint). 9.2a fires the signal + notification; the market content is 9.2b.</summary>
    private async Task<int> AnnounceMarketWindowsAsync(DateTime now, CancellationToken ct)
    {
        var groups = await _db.RankedGroups
            .Where(g => g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active)
            .ToListAsync(ct);

        int opened = 0;
        foreach (var group in groups)
        {
            int totalRounds = (await _db.RankedFixtures
                .Where(f => f.RankedGroupId == group.Id)
                .Select(f => (int?)f.Round)
                .MaxAsync(ct)) ?? 0;
            if (totalRounds == 0) continue;

            var window = RankedCalendar.CurrentWindow(
                group.SeasonStartedUtc!.Value, totalRounds,
                _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, now);
            if (window is not { } w || w.Index <= group.LastMarketWindowOpened) continue;

            group.LastMarketWindowOpened = w.Index;
            await _db.SaveChangesAsync(ct);
            opened++;

            var humans = await _db.RankedSeats
                .Where(s => s.RankedGroupId == group.Id && s.UserId != null)
                .Select(s => s.UserId!.Value)
                .ToListAsync(ct);
            foreach (var uid in humans)
                await SafeSend(uid, "Finestra di mercato aperta",
                    $"La finestra di mercato del girone {group.Name} è aperta.",
                    new Dictionary<string, string> { ["kind"] = "ranked_market", ["groupId"] = group.Id.ToString() }, ct);
        }
        return opened;
    }

    // --- coach reads / submit ----------------------------------------------------------------------

    public async Task<RankedResult<RankedSeasonDto>> SubmitLineupAsync(
        Guid userId, SubmitRankedLineupRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");
        if (request.Lineup is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.ValidationFailed, "A lineup is required.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked club.");

        bool started = await _db.RankedFixtures.AnyAsync(f => f.RankedGroupId == group.Id, ct);
        if (!started || group.Status == RankedGroupStatus.Completed)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.WrongPhase, "Your ranked season is not under way.");

        var club = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.WrongPhase, "Your club no longer exists.");

        SimClub sim = WorldSquadReader.ToSimClub(club);
        if (!request.Lineup.TryMaterialize(sim, out _))
            return RankedResult<RankedSeasonDto>.Fail(
                RankedError.ValidationFailed, "The lineup is not valid for your current squad.");

        string lineupJson = JsonSerializer.Serialize(request.Lineup, PlanJson);
        string? tacticJson = request.Tactic != null ? JsonSerializer.Serialize(request.Tactic, PlanJson) : null;
        string? planJson = request.Plan != null ? JsonSerializer.Serialize(request.Plan, PlanJson) : null;

        var existing = await _db.RankedLineups.FirstOrDefaultAsync(
            x => x.RankedGroupId == group.Id && x.ClubId == clubId, ct);
        if (existing is null)
        {
            _db.RankedLineups.Add(new RankedLineup
            {
                Id = Guid.NewGuid(),
                RankedGroupId = group.Id,
                UserId = userId,
                ClubId = clubId,
                LineupJson = lineupJson,
                TacticJson = tacticJson,
                PrematchPlanJson = planJson,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.LineupJson = lineupJson;
            existing.TacticJson = tacticJson;
            existing.PrematchPlanJson = planJson;
            existing.UpdatedUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return RankedResult<RankedSeasonDto>.Ok(await BuildSeasonAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<RankedSeasonDto>> GetMySeasonAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat is null)
            return RankedResult<RankedSeasonDto>.Ok(RankedSeasonDto.NotInSeason());

        bool started = await _db.RankedFixtures.AnyAsync(f => f.RankedGroupId == group.Id, ct);
        if (!started)
            return RankedResult<RankedSeasonDto>.Ok(RankedSeasonDto.NotInSeason());

        return RankedResult<RankedSeasonDto>.Ok(await BuildSeasonAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<string>> GetReplayAsync(Guid userId, Guid fixtureId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<string>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, _) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null)
            return RankedResult<string>.Fail(RankedError.FixtureNotFound, "No such fixture in your group.");

        var fixture = await _db.RankedFixtures.FirstOrDefaultAsync(
            f => f.Id == fixtureId && f.RankedGroupId == group.Id, ct);
        if (fixture is null)
            return RankedResult<string>.Fail(RankedError.FixtureNotFound, "No such fixture in your group.");
        if (!fixture.IsPlayed || string.IsNullOrEmpty(fixture.ReplayJson))
            return RankedResult<string>.Fail(RankedError.ReplayNotReady, "This match has not been played yet.");

        return RankedResult<string>.Ok(fixture.ReplayJson);
    }

    // --- projections -------------------------------------------------------------------------------

    private async Task<(RankedGroup? group, RankedSeat? seat)> CurrentGroupSeatAsync(
        RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is not { } seatId) return (null, null);
        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return (null, null);
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct);
        return (group, seat);
    }

    private async Task<RankedSeasonDto> BuildSeasonAsync(
        RankedGroup group, RankedSeat mySeat, Guid callerId, CancellationToken ct)
    {
        var clubs = await _db.Clubs.Where(c => c.WorldId == group.WorldId).Include(c => c.Players).ToListAsync(ct);
        var entByGuid = clubs.ToDictionary(c => c.Id);
        int? yourExternal = mySeat.ClubId is { } cid && entByGuid.TryGetValue(cid, out var mine) ? mine.ExternalId : null;

        // The canonical hash of the whole group's mutable player state (condition + attributes), so a client
        // can re-run the deterministic Sim.Core progressors and confirm it agrees (the 8.4 agreement mechanic).
        SimLeague simWorldHash = WorldSquadReader.ToSimLeague(clubs);
        string stateHash = Sim.Core.Domain.WorldStateHasher.ToHex(
            Sim.Core.Domain.WorldStateHasher.Hash(new[] { simWorldHash }));

        var fixtures = await _db.RankedFixtures
            .Where(f => f.RankedGroupId == group.Id)
            .OrderBy(f => f.Round).ThenBy(f => f.MatchIndex)
            .ToListAsync(ct);

        var fixtureDtos = fixtures.Select(f => new RankedFixtureDto(
            Id: f.Id,
            Round: f.Round,
            Day: f.Day,
            KickoffUtc: f.KickoffUtc,
            HomeClubExternalId: entByGuid[f.HomeClubId].ExternalId,
            HomeClubName: entByGuid[f.HomeClubId].Name,
            AwayClubExternalId: entByGuid[f.AwayClubId].ExternalId,
            AwayClubName: entByGuid[f.AwayClubId].Name,
            Played: f.IsPlayed,
            HomeGoals: f.HomeGoals,
            AwayGoals: f.AwayGoals,
            IsYours: yourExternal is { } ye
                     && (entByGuid[f.HomeClubId].ExternalId == ye || entByGuid[f.AwayClubId].ExternalId == ye)))
            .ToList();

        var standings = ComputeStandings(clubs, fixtures, yourExternal);

        int totalRounds = fixtures.Count > 0 ? fixtures.Max(f => f.Round) : 0;
        int roundsPlayed = 0;
        int? nextRound = null;
        for (int r = 1; r <= totalRounds; r++)
        {
            bool allPlayed = fixtures.Where(f => f.Round == r).All(f => f.IsPlayed);
            if (allPlayed) roundsPlayed++;
            else if (nextRound is null) nextRound = r;
        }
        bool seasonComplete = totalRounds > 0 && fixtures.All(f => f.IsPlayed);
        DateTime? nextKickoff = nextRound is { } nr
            ? fixtures.Where(f => f.Round == nr).Select(f => f.KickoffUtc).DefaultIfEmpty().Min()
            : null;

        RankedMarketWindowDto? currentWindow = null;
        if (group.SeasonStartedUtc is { } start && totalRounds > 0)
        {
            var w = RankedCalendar.CurrentWindow(
                start, totalRounds, _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, DateTime.UtcNow);
            if (w is { } win) currentWindow = new RankedMarketWindowDto(win.Index, win.OpensUtc, win.ClosesUtc, true);
        }

        bool youSubmitted = mySeat.ClubId is { } myClub
            && await _db.RankedLineups.AnyAsync(x => x.RankedGroupId == group.Id && x.ClubId == myClub, ct);

        var state = new RankedSeasonStateDto(
            GroupId: group.Id,
            GroupName: group.Name,
            Kind: group.Kind,
            Tier: group.Tier,
            Started: true,
            TotalRounds: totalRounds,
            RoundsPlayed: roundsPlayed,
            NextRound: nextRound,
            NextKickoffUtc: nextKickoff,
            SeasonComplete: seasonComplete,
            YourClubExternalId: yourExternal,
            YouSubmittedLineup: youSubmitted,
            CurrentWindow: currentWindow,
            StateHashHex: stateHash);

        return new RankedSeasonDto(true, state, fixtureDtos, standings);
    }

    private IReadOnlyList<RankedStandingDto> ComputeStandings(
        List<Club> clubs, List<RankedFixture> fixtures, int? youExternal = null)
    {
        int win = _config.Season.PointsForWin;
        int draw = _config.Season.PointsForDraw;

        var table = clubs.ToDictionary(c => c.Id, c => new Row { ExternalId = c.ExternalId, Name = c.Name });

        foreach (var f in fixtures)
        {
            if (!f.IsPlayed) continue;
            if (!table.TryGetValue(f.HomeClubId, out var home) || !table.TryGetValue(f.AwayClubId, out var away))
                continue;

            home.Played++; away.Played++;
            home.GoalsFor += f.HomeGoals; home.GoalsAgainst += f.AwayGoals;
            away.GoalsFor += f.AwayGoals; away.GoalsAgainst += f.HomeGoals;

            if (f.HomeGoals > f.AwayGoals) { home.Won++; away.Lost++; home.Points += win; }
            else if (f.HomeGoals < f.AwayGoals) { away.Won++; home.Lost++; away.Points += win; }
            else { home.Drawn++; away.Drawn++; home.Points += draw; away.Points += draw; }
        }

        return table.Values
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.GoalsFor - r.GoalsAgainst)
            .ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new RankedStandingDto(
                ClubExternalId: r.ExternalId,
                ClubName: r.Name,
                Played: r.Played,
                Won: r.Won,
                Drawn: r.Drawn,
                Lost: r.Lost,
                GoalsFor: r.GoalsFor,
                GoalsAgainst: r.GoalsAgainst,
                GoalDifference: r.GoalsFor - r.GoalsAgainst,
                Points: r.Points,
                IsYou: youExternal == r.ExternalId))
            .ToList();
    }

    // --- helpers -----------------------------------------------------------------------------------

    private static MatchResolver.SideInputs DeserializeInputs(RankedLineup x)
    {
        LineupPlan? lineup = null;
        TacticPlan? tactic = null;
        PrematchPlan? plan = null;
        try { lineup = JsonSerializer.Deserialize<LineupPlan>(x.LineupJson, PlanJson); } catch (JsonException) { }
        if (!string.IsNullOrEmpty(x.TacticJson))
            try { tactic = JsonSerializer.Deserialize<TacticPlan>(x.TacticJson, PlanJson); } catch (JsonException) { }
        if (!string.IsNullOrEmpty(x.PrematchPlanJson))
            try { plan = JsonSerializer.Deserialize<PrematchPlan>(x.PrematchPlanJson, PlanJson); } catch (JsonException) { }
        return new MatchResolver.SideInputs { Lineup = lineup, Tactic = tactic, Plan = plan };
    }

    private async Task NotifyMatchdayAsync(
        IReadOnlyDictionary<Guid, Guid> humanByClub, Guid clubId,
        IReadOnlyDictionary<Guid, Club> entByGuid, int goalsFor, int goalsAgainst, CancellationToken ct)
    {
        if (!humanByClub.TryGetValue(clubId, out var userId)) return;
        string club = entByGuid.TryGetValue(clubId, out var c) ? c.Name : "il tuo club";
        string verdict = goalsFor > goalsAgainst ? "Vittoria" : goalsFor < goalsAgainst ? "Sconfitta" : "Pareggio";
        await SafeSend(userId, "Giornata giocata",
            $"{club}: {verdict} {goalsFor}-{goalsAgainst}.",
            new Dictionary<string, string> { ["kind"] = "ranked_matchday" }, ct);
    }

    private async Task SafeSend(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        try { await _notify.SendToUserAsync(userId, new PushMessage(title, body, data), ct); }
        catch { /* notifications are best-effort — never let a push failure break the calendar. */ }
    }

    private sealed class Row
    {
        public int ExternalId;
        public string Name = string.Empty;
        public int Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points;
    }
}
