using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Balance;
using Fts.Application.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Sim.Core.Config;
using Sim.Core.Match;
using Sim.Core.Tactics;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// <see cref="ILiveMatchService"/> implementation (Phase 8.6): a live-controlled session for one
/// human-vs-human fixture, all state authoritative in Postgres/EF (a <c>LiveMatch</c> row). The model is
/// "deterministic re-sim + broadcast": each pause-point (sub / instruction change) appends a
/// <see cref="LiveChange"/> to the accumulated plan and the server re-runs the whole 90' from the fixture
/// seed via <see cref="MatchResolver.ResolveLive"/>, stores the new full report and broadcasts the state.
/// The engine is a pure function of (plan, seed), so the minutes before a change stay byte-identical and
/// only the remainder re-rolls — both connected clients render the same match in sync. Disconnect is
/// graceful by construction: an absent member simply stops sending changes, so the match plays out on
/// their last-submitted lineup + pre-match plan (the engine fires those rules automatically). Only the
/// current round's fixture between two members is eligible; every other fixture stays instant (8.3).
/// </summary>
public sealed class LiveMatchService : ILiveMatchService
{
    private readonly FtsDbContext _db;
    private readonly ILiveMatchBroadcaster _broadcaster;
    /// <summary>The server's ACTIVE balance (Phase 10.3), snapshotted for the lifetime of this scoped
    /// service so one request - or one calendar tick - resolves against ONE set of numbers even if an
    /// admin pushes a revision half way through it. Before 10.3 this was `new BalanceConfig()`, i.e. the
    /// balance embedded in the build; with nothing ever pushed it still is, byte for byte.</summary>
    private readonly BalanceConfig _config;

    public LiveMatchService(
        FtsDbContext db, ILiveMatchBroadcaster broadcaster, IBalanceProvider balance)
    {
        _db = db;
        _broadcaster = broadcaster;
        _config = balance.Current;
    }

    /// <summary>Tolerant of string- or number-valued enums and of casing, like the season service.</summary>
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- open / join / leave -----------------------------------------------------------------------

    public async Task<LeagueResult<LiveMatchStateDto>> OpenAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return Fail("League not found.", LeagueError.NotFound);
        if (league.Status != LeagueStatus.Active)
            return Fail("The season is not under way.", LeagueError.WrongPhase);

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return Fail("You are not a member of this league.", LeagueError.Forbidden);

        var fixture = await _db.LeagueFixtures.FirstOrDefaultAsync(
            f => f.Id == fixtureId && f.PrivateLeagueId == leagueId, ct);
        if (fixture is null)
            return Fail("No such fixture in this league.", LeagueError.FixtureNotFound);
        if (fixture.IsPlayed)
            return Fail("That fixture has already been played.", LeagueError.LiveMatchNotJoinable);

        int? currentRound = await CurrentRoundAsync(leagueId, ct);
        if (currentRound is null || fixture.Round != currentRound)
            return Fail("Only the current round's fixture can be played live.", LeagueError.LiveMatchNotJoinable);

        // The fixture must be between two human members (both clubs claimed).
        var members = await _db.LeagueMembers.Where(m => m.PrivateLeagueId == leagueId).ToListAsync(ct);
        var homeOwner = members.FirstOrDefault(m => m.ClubId == fixture.HomeClubId);
        var awayOwner = members.FirstOrDefault(m => m.ClubId == fixture.AwayClubId);
        if (homeOwner is null || awayOwner is null)
            return Fail("This fixture has an AI side and cannot be played live.", LeagueError.LiveMatchNotJoinable);
        if (homeOwner.UserId != userId && awayOwner.UserId != userId)
            return Fail("You are not playing in this fixture.", LeagueError.LiveMatchNotJoinable);

        var live = await _db.LiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        var now = DateTime.UtcNow;
        if (live is null)
        {
            long worldSeed = await _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);
            int homeExt = await _db.Clubs.Where(c => c.Id == fixture.HomeClubId).Select(c => c.ExternalId).FirstAsync(ct);
            int awayExt = await _db.Clubs.Where(c => c.Id == fixture.AwayClubId).Select(c => c.ExternalId).FirstAsync(ct);

            live = new LiveMatch
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = leagueId,
                FixtureId = fixtureId,
                Round = fixture.Round,
                Status = LiveMatchStatus.Pending,
                Seed = unchecked((long)FixtureSeed.For(worldSeed, fixture.Round, homeExt, awayExt)),
                HomeClubId = fixture.HomeClubId,
                AwayClubId = fixture.AwayClubId,
                HomeUserId = homeOwner.UserId,
                AwayUserId = awayOwner.UserId,
                ChangesJson = "[]",
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            _db.LiveMatches.Add(live);
        }

        MarkPresent(live, userId);
        await MaybeGoLiveAsync(live, ct);
        live.UpdatedUtc = now;
        await _db.SaveChangesAsync(ct);

        return LeagueResult<LiveMatchStateDto>.Ok(await BuildStateAsync(live, userId, ct));
    }

    public async Task<LeagueResult<LiveMatchStateDto>> JoinAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, live) = await LoadParticipantAsync(userId, leagueId, fixtureId, ct);
        if (live is null) return Fail(message, error);

        MarkPresent(live, userId);
        await MaybeGoLiveAsync(live, ct);
        live.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return LeagueResult<LiveMatchStateDto>.Ok(await BuildStateAsync(live, userId, ct));
    }

    public async Task<LeagueResult<LiveMatchStateDto>> LeaveAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, live) = await LoadParticipantAsync(userId, leagueId, fixtureId, ct);
        if (live is null) return Fail(message, error);

        // Mark absent — the match keeps running on the accumulated plan (graceful fallback).
        if (live.HomeUserId == userId) live.HomePresent = false;
        else if (live.AwayUserId == userId) live.AwayPresent = false;
        live.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var state = await BuildStateAsync(live, userId, ct);
        await SafeBroadcastAsync(live.FixtureId, state, ct);
        return LeagueResult<LiveMatchStateDto>.Ok(state);
    }

    // --- read --------------------------------------------------------------------------------------

    public async Task<LeagueResult<LiveMatchStateDto>> GetAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return Fail("League not found.", LeagueError.NotFound);

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return Fail("You are not a member of this league.", LeagueError.Forbidden);

        var live = await _db.LiveMatches.FirstOrDefaultAsync(
            l => l.FixtureId == fixtureId && l.PrivateLeagueId == leagueId, ct);
        if (live is null)
            return Fail("No live session for that fixture.", LeagueError.LiveMatchNotFound);

        return LeagueResult<LiveMatchStateDto>.Ok(await BuildStateAsync(live, userId, ct));
    }

    // --- pause-point change ------------------------------------------------------------------------

    public async Task<LeagueResult<LiveMatchStateDto>> SubmitChangeAsync(
        Guid userId, Guid leagueId, Guid fixtureId, SubmitLiveChangeRequest request, CancellationToken ct = default)
    {
        var (error, message, live) = await LoadParticipantAsync(userId, leagueId, fixtureId, ct);
        if (live is null) return Fail(message, error);

        if (live.Status == LiveMatchStatus.Finished)
            return Fail("This live match has already finished.", LeagueError.LiveMatchAlreadyFinished);
        if (live.Status != LiveMatchStatus.Live)
            return Fail("The live match has not kicked off yet.", LeagueError.LiveMatchNotLive);

        LiveSide side = live.HomeUserId == userId ? LiveSide.Home : LiveSide.Away;

        if (request is null || (request.Lineup is null && request.Tactic is null))
            return Fail("A change must carry a lineup and/or a tactic.", LeagueError.InvalidLiveChange);
        if (request.FromMinute < 1 || request.FromMinute > 90)
            return Fail("The change minute must be between 1 and 90.", LeagueError.InvalidLiveChange);

        var changes = DeserializeChanges(live.ChangesJson);
        int lastMinute = changes.Count > 0 ? changes[^1].FromMinute : 0;
        if (request.FromMinute < lastMinute)
            return Fail("A change cannot move behind an already-applied one.", LeagueError.InvalidLiveChange);

        var sides = await LoadSidesAsync(live, ct);
        if (sides is null)
            return Fail("The clubs for this fixture no longer exist.", LeagueError.FixtureNotFound);

        // Validate the submitted lineup materialises against the caller's own current squad.
        if (request.Lineup is not null)
        {
            SimClub own = side == LiveSide.Home ? sides.Value.Home : sides.Value.Away;
            if (!request.Lineup.TryMaterialize(own, out _))
                return Fail("The lineup is not valid for your current squad.", LeagueError.InvalidLiveChange);
        }

        changes.Add(new LiveChange(request.FromMinute, side, request.Lineup, request.Tactic));

        MatchResolver.ResolveResult r = MatchResolver.ResolveLive(
            sides.Value.Home, sides.Value.Away, sides.Value.HomeInputs, sides.Value.AwayInputs,
            changes, unchecked((ulong)live.Seed), _config);

        live.ChangesJson = JsonSerializer.Serialize(changes, PlanJson);
        live.ReportJson = JsonSerializer.Serialize(r.Report);
        live.HomeGoals = r.Report.HomeGoals;
        live.AwayGoals = r.Report.AwayGoals;
        live.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var state = await BuildStateAsync(live, userId, ct);
        await SafeBroadcastAsync(live.FixtureId, state, ct);
        return LeagueResult<LiveMatchStateDto>.Ok(state);
    }

    // --- finish ------------------------------------------------------------------------------------

    public async Task<LeagueResult<LiveMatchStateDto>> FinishAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, live) = await LoadParticipantAsync(userId, leagueId, fixtureId, ct);
        if (live is null) return Fail(message, error);

        if (live.Status == LiveMatchStatus.Finished)
            return LeagueResult<LiveMatchStateDto>.Ok(await BuildStateAsync(live, userId, ct));
        if (live.Status != LiveMatchStatus.Live)
            return Fail("The live match has not kicked off yet.", LeagueError.LiveMatchNotLive);

        // Make sure a report exists (e.g. finished with zero changes right after kickoff).
        if (string.IsNullOrEmpty(live.ReportJson))
            await ResimBaselineAsync(live, ct);

        live.Status = LiveMatchStatus.Finished;
        live.FinishedUtc = DateTime.UtcNow;
        live.UpdatedUtc = live.FinishedUtc.Value;
        await _db.SaveChangesAsync(ct);

        var state = await BuildStateAsync(live, userId, ct);
        await SafeBroadcastAsync(live.FixtureId, state, ct);
        return LeagueResult<LiveMatchStateDto>.Ok(state);
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>Load the session and check the caller is one of the two side owners. Returns the live row
    /// on success, else the error to surface.</summary>
    private async Task<(LeagueError Error, string? Message, LiveMatch? Live)> LoadParticipantAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return (LeagueError.NotFound, "League not found.", null);

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return (LeagueError.Forbidden, "You are not a member of this league.", null);

        var live = await _db.LiveMatches.FirstOrDefaultAsync(
            l => l.FixtureId == fixtureId && l.PrivateLeagueId == leagueId, ct);
        if (live is null)
            return (LeagueError.LiveMatchNotFound, "No live session for that fixture.", null);

        if (live.HomeUserId != userId && live.AwayUserId != userId)
            return (LeagueError.NotYourSide, "You are not playing in this fixture.", null);

        return (LeagueError.None, null, live);
    }

    private static void MarkPresent(LiveMatch live, Guid userId)
    {
        if (live.HomeUserId == userId) live.HomePresent = true;
        else if (live.AwayUserId == userId) live.AwayPresent = true;
    }

    /// <summary>Both present and still Pending ⇒ kickoff: go Live, stamp the time, seed a baseline report.</summary>
    private async Task MaybeGoLiveAsync(LiveMatch live, CancellationToken ct)
    {
        if (live.Status == LiveMatchStatus.Pending && live.HomePresent && live.AwayPresent)
        {
            live.Status = LiveMatchStatus.Live;
            live.KickoffUtc = DateTime.UtcNow;
            await ResimBaselineAsync(live, ct);
        }
    }

    /// <summary>Re-simulate with the currently-accumulated changes and store the report + score.</summary>
    private async Task ResimBaselineAsync(LiveMatch live, CancellationToken ct)
    {
        var sides = await LoadSidesAsync(live, ct);
        if (sides is null) return;
        var changes = DeserializeChanges(live.ChangesJson);
        MatchResolver.ResolveResult r = MatchResolver.ResolveLive(
            sides.Value.Home, sides.Value.Away, sides.Value.HomeInputs, sides.Value.AwayInputs,
            changes, unchecked((ulong)live.Seed), _config);
        live.ReportJson = JsonSerializer.Serialize(r.Report);
        live.HomeGoals = r.Report.HomeGoals;
        live.AwayGoals = r.Report.AwayGoals;
    }

    /// <summary>Reconstruct both clubs (squads) as live Sim.Core clubs + their submitted inputs.</summary>
    private async Task<(SimClub Home, SimClub Away,
        MatchResolver.SideInputs HomeInputs, MatchResolver.SideInputs AwayInputs)?> LoadSidesAsync(
        LiveMatch live, CancellationToken ct)
    {
        var home = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == live.HomeClubId, ct);
        var away = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == live.AwayClubId, ct);
        if (home is null || away is null) return null;

        var lineups = await _db.LeagueLineups
            .Where(x => x.PrivateLeagueId == live.PrivateLeagueId
                        && (x.ClubId == live.HomeClubId || x.ClubId == live.AwayClubId))
            .ToListAsync(ct);
        MatchResolver.SideInputs homeInputs = InputsFor(lineups, live.HomeClubId);
        MatchResolver.SideInputs awayInputs = InputsFor(lineups, live.AwayClubId);

        return (WorldSquadReader.ToSimClub(home), WorldSquadReader.ToSimClub(away), homeInputs, awayInputs);
    }

    private static MatchResolver.SideInputs InputsFor(List<LeagueLineup> lineups, Guid clubId)
    {
        var row = lineups.FirstOrDefault(x => x.ClubId == clubId);
        if (row is null) return new MatchResolver.SideInputs();

        LineupPlan? lineup = null;
        TacticPlan? tactic = null;
        PrematchPlan? plan = null;
        try { lineup = JsonSerializer.Deserialize<LineupPlan>(row.LineupJson, PlanJson); } catch (JsonException) { }
        if (!string.IsNullOrEmpty(row.TacticJson))
            try { tactic = JsonSerializer.Deserialize<TacticPlan>(row.TacticJson, PlanJson); } catch (JsonException) { }
        if (!string.IsNullOrEmpty(row.PrematchPlanJson))
            try { plan = JsonSerializer.Deserialize<PrematchPlan>(row.PrematchPlanJson, PlanJson); } catch (JsonException) { }
        return new MatchResolver.SideInputs { Lineup = lineup, Tactic = tactic, Plan = plan };
    }

    private static List<LiveChange> DeserializeChanges(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<LiveChange>();
        try { return JsonSerializer.Deserialize<List<LiveChange>>(json, PlanJson) ?? new List<LiveChange>(); }
        catch (JsonException) { return new List<LiveChange>(); }
    }

    /// <summary>The lowest round that still has unplayed fixtures (the round eligible to be played live),
    /// or null if the season is complete / not started.</summary>
    private async Task<int?> CurrentRoundAsync(Guid leagueId, CancellationToken ct)
    {
        var pending = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == leagueId && !f.IsPlayed)
            .Select(f => f.Round)
            .ToListAsync(ct);
        return pending.Count == 0 ? null : pending.Min();
    }

    private async Task<LiveMatchStateDto> BuildStateAsync(LiveMatch live, Guid callerId, CancellationToken ct)
    {
        var home = await _db.Clubs.Where(c => c.Id == live.HomeClubId)
            .Select(c => new { c.ExternalId, c.Name }).FirstAsync(ct);
        var away = await _db.Clubs.Where(c => c.Id == live.AwayClubId)
            .Select(c => new { c.ExternalId, c.Name }).FirstAsync(ct);

        LiveSide? yourSide =
            live.HomeUserId == callerId ? LiveSide.Home
            : live.AwayUserId == callerId ? LiveSide.Away
            : (LiveSide?)null;

        var changes = DeserializeChanges(live.ChangesJson)
            .Select(c => new LiveChangeDto(c.FromMinute, c.Side))
            .ToList();

        return new LiveMatchStateDto(
            FixtureId: live.FixtureId,
            Round: live.Round,
            HomeClubExternalId: home.ExternalId,
            HomeClubName: home.Name,
            AwayClubExternalId: away.ExternalId,
            AwayClubName: away.Name,
            Status: live.Status,
            YourSide: yourSide,
            HomePresent: live.HomePresent,
            AwayPresent: live.AwayPresent,
            KickoffUtc: live.KickoffUtc,
            HomeGoals: live.HomeGoals,
            AwayGoals: live.AwayGoals,
            Changes: changes,
            ReportJson: live.ReportJson);
    }

    private async Task SafeBroadcastAsync(Guid fixtureId, LiveMatchStateDto state, CancellationToken ct)
    {
        try { await _broadcaster.MatchChangedAsync(fixtureId, state, ct); }
        catch { /* a broadcast failure must not fail the committed change */ }
    }

    private static LeagueResult<LiveMatchStateDto> Fail(string? message, LeagueError error) =>
        LeagueResult<LiveMatchStateDto>.Fail(error, message);
}
