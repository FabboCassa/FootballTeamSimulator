using System.Text.Json;
using Fts.Application.Balance;
using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sim.Core.Config;
using Sim.Core.Match;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedLiveMatchService"/> (task 12.3): the ladder's live match, built on 8.6's mechanism
/// rather than beside it. Each pause-point appends a <see cref="LiveChange"/> to the session's accumulated
/// plan and the server re-runs the whole 90' from the fixture's seed through the SHARED
/// <see cref="MatchResolver.ResolveLive"/>; the engine is a pure function of (plan, seed), so the minutes
/// before a change stay byte-identical and only the remainder re-rolls.
///
/// THREE THINGS ARE DELIBERATELY DIFFERENT FROM THE PRIVATE-LEAGUE SESSION, and each is the ladder being a
/// competition rather than a lobby:
/// <list type="number">
/// <item><b>The calendar owns kick-off.</b> 8.6 starts the match when both members are present; here the
/// match starts at its scheduled instant — 21:00 of the world's own zone — and presence only decides who
/// gets to make changes. Nobody's absence can move a ranked kick-off, and nobody's presence can bring one
/// forward.</item>
/// <item><b>An AI seat is a legitimate opponent.</b> A ladder group is mostly vacant seats until the pyramid
/// fills; requiring two humans would mean the live mode almost never fires. The AI side plays its stored
/// orders, which is exactly what an absent human's side does — so there is no second code path.</item>
/// <item><b>A change cannot be made in the match's future.</b> The whole 90' is in the report the client
/// holds, so without a check a doctored client could read the ending and then "substitute" at minute 10 with
/// hindsight. The server therefore refuses a minute that runs past what the wall clock says has been played,
/// with a tolerance for latency. A private league is a lobby of friends; this is ranked.</item>
/// </list>
///
/// What is NOT different is the result: with no changes submitted, a live session's report is byte-for-byte
/// the one the headless matchday produces, because both go through the same engine, on the same live squad
/// condition, from the same <see cref="FixtureSeed"/> over the same season seed. Attending changes the match
/// only through the changes you actually make — which is the acceptance test of the whole task.
/// </summary>
public sealed class RankedLiveMatchService : IRankedLiveMatchService
{
    private readonly FtsDbContext _db;
    private readonly IRankedLiveBroadcaster _broadcaster;
    private readonly RankedOptions _opt;
    /// <summary>The server's ACTIVE balance (Phase 10.3), snapshotted for the lifetime of this scoped
    /// service so one live match re-simulates against ONE set of numbers even if an admin pushes a revision
    /// half way through it — a match whose first half ran on different numbers from its second would not be
    /// reproducible from (plan, seed) any more.</summary>
    private readonly BalanceConfig _config;

    public RankedLiveMatchService(
        FtsDbContext db, IRankedLiveBroadcaster broadcaster,
        IOptions<RankedOptions> options, IBalanceProvider balance)
    {
        _db = db;
        _broadcaster = broadcaster;
        _opt = options.Value;
        _config = balance.Current;
    }

    /// <summary>Shared with the rest of the ranked services so a plan written by any of them — including the
    /// 9.4 seeded defaults — reads back identically here.</summary>
    private static readonly JsonSerializerOptions PlanJson = RankedPlanJson.Options;

    // --- open / leave ------------------------------------------------------------------------------

    public async Task<RankedResult<RankedLiveStateDto>> OpenAsync(
        Guid userId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, ctx) = await LoadAsync(userId, fixtureId, ct);
        if (ctx is null) return Fail(message, error);
        if (!ctx.IsParticipant)
            return Fail("Non sei tu a giocare questa partita.", RankedError.NotYourMatch);

        var now = DateTime.UtcNow;

        if (ctx.Fixture.IsPlayed)
            return Fail("Questa giornata è già stata risolta.", RankedError.LiveNotOpen);

        int? currentRound = await CurrentRoundAsync(ctx.Group.Id, ct);
        if (currentRound is null || ctx.Fixture.Round != currentRound)
            return Fail("Si gioca dal vivo solo la partita della giornata in corso.", RankedError.LiveNotOpen);

        DateTime opens = OpensAt(ctx.Fixture);
        DateTime closes = ClosesAt(ctx.Fixture);
        if (now < opens)
            return Fail("La partita non è ancora aperta: si entra poco prima del fischio d'inizio.",
                RankedError.LiveNotOpen);
        if (now >= closes)
            return Fail("La finestra dal vivo è chiusa: la giornata viene risolta dal calendario.",
                RankedError.LiveNotOpen);

        var live = await _db.RankedLiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        if (live is null)
        {
            long seasonSeed = RankedSeeds.Season(ctx.World.Seed, ctx.Group.SeasonNumber);
            ulong seed = FixtureSeed.For(seasonSeed, ctx.Fixture.Round, ctx.Home.ExternalId, ctx.Away.ExternalId);

            live = new RankedLiveMatch
            {
                Id = Guid.NewGuid(),
                RankedGroupId = ctx.Group.Id,
                FixtureId = fixtureId,
                Round = ctx.Fixture.Round,
                Status = LiveMatchStatus.Pending,
                Seed = unchecked((long)seed),
                HomeClubId = ctx.Fixture.HomeClubId,
                AwayClubId = ctx.Fixture.AwayClubId,
                HomeUserId = ctx.HomeUserId,
                AwayUserId = ctx.AwayUserId,
                // The SCHEDULE's instant, not "now" — this is the whole difference from 8.6.
                KickoffUtc = ctx.Fixture.KickoffUtc,
                ChangesJson = "[]",
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            _db.RankedLiveMatches.Add(live);
        }

        MarkPresent(live, userId);
        await MaybeKickOffAsync(live, ctx, now, ct);
        live.UpdatedUtc = now;
        await _db.SaveChangesAsync(ct);

        var state = BuildState(live, ctx, userId);
        await SafeBroadcastAsync(fixtureId, state, ct);
        return RankedResult<RankedLiveStateDto>.Ok(state);
    }

    public async Task<RankedResult<RankedLiveStateDto>> LeaveAsync(
        Guid userId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, ctx) = await LoadAsync(userId, fixtureId, ct);
        if (ctx is null) return Fail(message, error);
        if (!ctx.IsParticipant)
            return Fail("Non sei tu a giocare questa partita.", RankedError.NotYourMatch);

        var live = await _db.RankedLiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        if (live is null) return Fail("Nessuna partita dal vivo per questo incontro.", RankedError.LiveNotFound);

        // Mark absent. The match keeps running on the accumulated plan: the ladder's clock does not care
        // who is watching, and the side plays on its stored orders exactly as an AI seat does.
        if (live.HomeUserId == userId) live.HomePresent = false;
        else if (live.AwayUserId == userId) live.AwayPresent = false;
        live.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var state = BuildState(live, ctx, userId);
        await SafeBroadcastAsync(fixtureId, state, ct);
        return RankedResult<RankedLiveStateDto>.Ok(state);
    }

    // --- read --------------------------------------------------------------------------------------

    public async Task<RankedResult<RankedLiveStateDto>> GetAsync(
        Guid userId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, ctx) = await LoadAsync(userId, fixtureId, ct);
        if (ctx is null) return Fail(message, error);

        var live = await _db.RankedLiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        if (live is null) return Fail("Nessuna partita dal vivo per questo incontro.", RankedError.LiveNotFound);

        // A session opened before kickoff sits Pending; the moment the appointment arrives it goes Live on
        // whoever reads it next, so a client that simply polls sees the match start on time.
        var now = DateTime.UtcNow;
        if (await MaybeKickOffAsync(live, ctx, now, ct))
        {
            live.UpdatedUtc = now;
            await _db.SaveChangesAsync(ct);
        }

        return RankedResult<RankedLiveStateDto>.Ok(BuildState(live, ctx, userId));
    }

    // --- pause-point change ------------------------------------------------------------------------

    public async Task<RankedResult<RankedLiveStateDto>> SubmitChangeAsync(
        Guid userId, Guid fixtureId, SubmitRankedLiveChangeRequest request, CancellationToken ct = default)
    {
        var (error, message, ctx) = await LoadAsync(userId, fixtureId, ct);
        if (ctx is null) return Fail(message, error);
        if (!ctx.IsParticipant)
            return Fail("Non sei tu a giocare questa partita.", RankedError.NotYourMatch);

        var live = await _db.RankedLiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        if (live is null) return Fail("Nessuna partita dal vivo per questo incontro.", RankedError.LiveNotFound);

        var now = DateTime.UtcNow;
        await MaybeKickOffAsync(live, ctx, now, ct);

        if (live.Status == LiveMatchStatus.Finished)
            return Fail("La partita è finita.", RankedError.LiveAlreadyFinished);
        if (live.Status != LiveMatchStatus.Live)
            return Fail("Il fischio d'inizio non è ancora arrivato.", RankedError.LiveNotOpen);
        if (now >= ClosesAt(ctx.Fixture))
            return Fail("La finestra dal vivo è chiusa: la giornata viene risolta dal calendario.",
                RankedError.LiveNotOpen);

        LiveSide side = live.HomeUserId == userId ? LiveSide.Home : LiveSide.Away;

        if (request is null || (request.Lineup is null && request.Tactic is null))
            return Fail("Un cambio deve portare una formazione e/o una tattica.", RankedError.InvalidLiveChange);
        if (request.FromMinute < 1 || request.FromMinute > 90)
            return Fail("Il minuto del cambio deve essere fra 1 e 90.", RankedError.InvalidLiveChange);

        var changes = DeserializeChanges(live.ChangesJson);
        int lastMinute = changes.Count > 0 ? changes[^1].FromMinute : 0;
        if (request.FromMinute < lastMinute)
            return Fail("Un cambio non può tornare indietro rispetto a uno già applicato.",
                RankedError.InvalidLiveChange);

        // THE RANKED GUARD (task 12.3): the client holds the whole 90' — refuse a change made in the
        // match's future. `LiveChangeMinuteTolerance` absorbs latency and clock skew; 0 disables the check.
        if (_opt.LiveChangeMinuteTolerance > 0)
        {
            int playable = PlayedMinuteAt(live.KickoffUtc, now) + _opt.LiveChangeMinuteTolerance;
            if (request.FromMinute > playable)
                return Fail($"Quel minuto non è ancora stato giocato (siamo al {PlayedMinuteAt(live.KickoffUtc, now)}').",
                    RankedError.InvalidLiveChange);
        }

        var sides = await LoadSidesAsync(live, ct);
        if (sides is null)
            return Fail("I club di questo incontro non esistono più.", RankedError.FixtureNotFound);

        // The submitted XI must materialise against the caller's OWN current squad.
        if (request.Lineup is not null)
        {
            SimClub own = side == LiveSide.Home ? sides.Value.Home : sides.Value.Away;
            if (!request.Lineup.TryMaterialize(own, out _))
                return Fail("La formazione non è valida per la tua rosa attuale.", RankedError.InvalidLiveChange);
        }

        changes.Add(new LiveChange(request.FromMinute, side, request.Lineup, request.Tactic));

        MatchResolver.ResolveResult r = MatchResolver.ResolveLive(
            sides.Value.Home, sides.Value.Away, sides.Value.HomeInputs, sides.Value.AwayInputs,
            changes, unchecked((ulong)live.Seed), _config);

        live.ChangesJson = JsonSerializer.Serialize(changes, PlanJson);
        live.ReportJson = ReplayStore.Write(r.Report);
        live.HomeGoals = r.Report.HomeGoals;
        live.AwayGoals = r.Report.AwayGoals;
        live.UpdatedUtc = now;
        await _db.SaveChangesAsync(ct);

        var state = BuildState(live, ctx, userId);
        await SafeBroadcastAsync(fixtureId, state, ct);
        return RankedResult<RankedLiveStateDto>.Ok(state);
    }

    // --- finish ------------------------------------------------------------------------------------

    public async Task<RankedResult<RankedLiveStateDto>> FinishAsync(
        Guid userId, Guid fixtureId, CancellationToken ct = default)
    {
        var (error, message, ctx) = await LoadAsync(userId, fixtureId, ct);
        if (ctx is null) return Fail(message, error);
        if (!ctx.IsParticipant)
            return Fail("Non sei tu a giocare questa partita.", RankedError.NotYourMatch);

        var live = await _db.RankedLiveMatches.FirstOrDefaultAsync(l => l.FixtureId == fixtureId, ct);
        if (live is null) return Fail("Nessuna partita dal vivo per questo incontro.", RankedError.LiveNotFound);

        var now = DateTime.UtcNow;
        if (live.Status == LiveMatchStatus.Finished)
            return RankedResult<RankedLiveStateDto>.Ok(BuildState(live, ctx, userId));

        await MaybeKickOffAsync(live, ctx, now, ct);
        if (live.Status != LiveMatchStatus.Live)
            return Fail("Il fischio d'inizio non è ancora arrivato.", RankedError.LiveNotOpen);

        // Make sure a report exists (a session finished with zero changes right after kickoff).
        if (string.IsNullOrEmpty(live.ReportJson))
            await ResimBaselineAsync(live, ct);

        live.Status = LiveMatchStatus.Finished;
        live.FinishedUtc = now;
        live.UpdatedUtc = now;
        await _db.SaveChangesAsync(ct);

        var state = BuildState(live, ctx, userId);
        await SafeBroadcastAsync(fixtureId, state, ct);
        return RankedResult<RankedLiveStateDto>.Ok(state);
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>Everything one call needs: the fixture, its group and generated world, both clubs, and who
    /// (if anyone) holds each side. Loaded once so the guards and the state builder read one snapshot.</summary>
    private sealed record LiveContext(
        RankedFixture Fixture, RankedGroup Group, World World, Club Home, Club Away,
        Guid? HomeUserId, Guid? AwayUserId, Guid CallerId)
    {
        public bool IsParticipant => HomeUserId == CallerId || AwayUserId == CallerId;
    }

    private async Task<(RankedError Error, string? Message, LiveContext? Ctx)> LoadAsync(
        Guid userId, Guid fixtureId, CancellationToken ct)
    {
        if (!_opt.LiveMatchesEnabled)
            return (RankedError.LiveNotOpen, "Le partite dal vivo non sono attive.", null);

        var fixture = await _db.RankedFixtures.FirstOrDefaultAsync(f => f.Id == fixtureId, ct);
        if (fixture is null)
            return (RankedError.FixtureNotFound, "Incontro inesistente.", null);

        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == fixture.RankedGroupId, ct);
        if (group is null || group.WorldId is null)
            return (RankedError.NotFound, "Girone inesistente.", null);
        if (group.Status != RankedGroupStatus.Active || group.SeasonStartedUtc is null)
            return (RankedError.LiveNotOpen, "La stagione di questo girone non è in corso.", null);

        // Watching is a privilege of the group; changing is a privilege of the two sides (checked by the
        // callers). A coach outside the group learns nothing here, not even that the fixture exists.
        var seats = await _db.RankedSeats
            .Where(s => s.RankedGroupId == group.Id && s.ClubId != null)
            .ToListAsync(ct);
        if (!seats.Any(s => s.UserId == userId))
            return (RankedError.Forbidden, "Non sei in questo girone.", null);

        var world = await _db.Worlds.FirstOrDefaultAsync(w => w.Id == group.WorldId, ct);
        var home = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == fixture.HomeClubId, ct);
        var away = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == fixture.AwayClubId, ct);
        if (world is null || home is null || away is null)
            return (RankedError.NotFound, "Il mondo di questo girone non è disponibile.", null);

        Guid? homeUser = seats.FirstOrDefault(s => s.ClubId == fixture.HomeClubId)?.UserId;
        Guid? awayUser = seats.FirstOrDefault(s => s.ClubId == fixture.AwayClubId)?.UserId;

        return (RankedError.None, null,
            new LiveContext(fixture, group, world, home, away, homeUser, awayUser, userId));
    }

    /// <summary>When the door opens: shortly before the appointment, which is also when the reminder push
    /// fires — the notification and the lobby are the same event.</summary>
    private DateTime OpensAt(RankedFixture f) => f.KickoffUtc.AddSeconds(-Math.Max(0, _opt.LiveOpensBeforeSeconds));

    /// <summary>When the door shuts: the calendar will not wait past this, so nothing a client does (or
    /// fails to do) can hold a whole group's matchday hostage.</summary>
    private DateTime ClosesAt(RankedFixture f) => f.KickoffUtc.AddSeconds(Math.Max(0, _opt.LiveGraceSeconds));

    /// <summary>The match minute the wall clock says has been played, clamped to the 90'.</summary>
    private int PlayedMinuteAt(DateTime kickoffUtc, DateTime now)
    {
        int perMinute = Math.Max(1, _opt.LiveSecondsPerMatchMinute);
        double elapsed = (now - kickoffUtc).TotalSeconds;
        if (elapsed <= 0) return 0;
        return (int)Math.Min(90, Math.Floor(elapsed / perMinute));
    }

    private static void MarkPresent(RankedLiveMatch live, Guid userId)
    {
        if (live.HomeUserId == userId) live.HomePresent = true;
        else if (live.AwayUserId == userId) live.AwayPresent = true;
    }

    /// <summary>The appointment arrived ⇒ the match starts, whoever is (or is not) watching. Returns true
    /// when this call is what flipped it, so the caller knows to save.</summary>
    private async Task<bool> MaybeKickOffAsync(
        RankedLiveMatch live, LiveContext ctx, DateTime now, CancellationToken ct)
    {
        if (live.Status != LiveMatchStatus.Pending) return false;
        if (now < live.KickoffUtc) return false;

        live.Status = LiveMatchStatus.Live;
        await ResimBaselineAsync(live, ct);
        return true;
    }

    /// <summary>Re-simulate with the currently-accumulated changes and store the report + score. With no
    /// changes this is byte-for-byte the headless matchday's result.</summary>
    private async Task ResimBaselineAsync(RankedLiveMatch live, CancellationToken ct)
    {
        var sides = await LoadSidesAsync(live, ct);
        if (sides is null) return;
        var changes = DeserializeChanges(live.ChangesJson);
        MatchResolver.ResolveResult r = MatchResolver.ResolveLive(
            sides.Value.Home, sides.Value.Away, sides.Value.HomeInputs, sides.Value.AwayInputs,
            changes, unchecked((ulong)live.Seed), _config);
        live.ReportJson = ReplayStore.Write(r.Report);
        live.HomeGoals = r.Report.HomeGoals;
        live.AwayGoals = r.Report.AwayGoals;
    }

    /// <summary>Reconstruct both clubs as live Sim.Core clubs + their stored inputs — the same reconstruction
    /// the matchday does, which is what keeps the two paths one match.</summary>
    private async Task<(SimClub Home, SimClub Away,
        MatchResolver.SideInputs HomeInputs, MatchResolver.SideInputs AwayInputs)?> LoadSidesAsync(
        RankedLiveMatch live, CancellationToken ct)
    {
        var home = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == live.HomeClubId, ct);
        var away = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == live.AwayClubId, ct);
        if (home is null || away is null) return null;

        var lineups = await _db.RankedLineups
            .Where(x => x.RankedGroupId == live.RankedGroupId
                        && (x.ClubId == live.HomeClubId || x.ClubId == live.AwayClubId))
            .ToListAsync(ct);

        return (WorldSquadReader.ToSimClub(home), WorldSquadReader.ToSimClub(away),
            RankedLiveInputs.For(lineups, live.HomeClubId), RankedLiveInputs.For(lineups, live.AwayClubId));
    }

    private static List<LiveChange> DeserializeChanges(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<LiveChange>();
        try { return JsonSerializer.Deserialize<List<LiveChange>>(json, PlanJson) ?? new List<LiveChange>(); }
        catch (JsonException) { return new List<LiveChange>(); }
    }

    /// <summary>The lowest round that still has unplayed fixtures — the one that may be played live.</summary>
    private async Task<int?> CurrentRoundAsync(Guid groupId, CancellationToken ct)
    {
        var pending = await _db.RankedFixtures
            .Where(f => f.RankedGroupId == groupId && !f.IsPlayed)
            .Select(f => f.Round)
            .ToListAsync(ct);
        return pending.Count == 0 ? null : pending.Min();
    }

    private RankedLiveStateDto BuildState(RankedLiveMatch live, LiveContext ctx, Guid callerId)
    {
        LiveSide? yourSide =
            live.HomeUserId == callerId ? LiveSide.Home
            : live.AwayUserId == callerId ? LiveSide.Away
            : (LiveSide?)null;

        var changes = DeserializeChanges(live.ChangesJson)
            .Select(c => new RankedLiveChangeDto(c.FromMinute, c.Side))
            .ToList();

        return new RankedLiveStateDto(
            FixtureId: live.FixtureId,
            GroupId: live.RankedGroupId,
            Round: live.Round,
            HomeClubExternalId: ctx.Home.ExternalId,
            HomeClubName: ctx.Home.Name,
            AwayClubExternalId: ctx.Away.ExternalId,
            AwayClubName: ctx.Away.Name,
            Status: live.Status,
            YourSide: yourSide,
            HomeIsAi: live.HomeUserId is null,
            AwayIsAi: live.AwayUserId is null,
            HomePresent: live.HomePresent,
            AwayPresent: live.AwayPresent,
            KickoffUtc: live.KickoffUtc,
            OpensUtc: OpensAt(ctx.Fixture),
            ClosesUtc: ClosesAt(ctx.Fixture),
            ServerUtc: DateTime.UtcNow,
            SecondsPerMatchMinute: Math.Max(1, _opt.LiveSecondsPerMatchMinute),
            HomeGoals: live.HomeGoals,
            AwayGoals: live.AwayGoals,
            Changes: changes,
            ReportJson: live.ReportJson);
    }

    private async Task SafeBroadcastAsync(Guid fixtureId, RankedLiveStateDto state, CancellationToken ct)
    {
        try { await _broadcaster.RankedMatchChangedAsync(fixtureId, state, ct); }
        catch { /* a broadcast failure must not fail the change that already committed */ }
    }

    private static RankedResult<RankedLiveStateDto> Fail(string? message, RankedError error) =>
        RankedResult<RankedLiveStateDto>.Fail(error, message);
}

/// <summary>
/// Deserializing a ranked club's stored match inputs — pulled out of <see cref="RankedSeasonService"/> in
/// task 12.3 so the live session and the headless matchday read a coach's orders through ONE function. Two
/// copies of this would be two ways for a live result and a scheduled one to disagree about what a coach
/// actually submitted, which is precisely the disagreement the task's determinism test exists to catch.
/// </summary>
internal static class RankedLiveInputs
{
    private static readonly JsonSerializerOptions PlanJson = RankedPlanJson.Options;

    public static MatchResolver.SideInputs For(List<RankedLineup> lineups, Guid clubId)
    {
        var row = lineups.FirstOrDefault(x => x.ClubId == clubId);
        return row is null ? new MatchResolver.SideInputs() : Of(row);
    }

    public static MatchResolver.SideInputs Of(RankedLineup x)
    {
        Sim.Core.Match.LineupPlan? lineup = null;
        Sim.Core.Tactics.TacticPlan? tactic = null;
        Sim.Core.Match.PrematchPlan? plan = null;
        try { lineup = JsonSerializer.Deserialize<Sim.Core.Match.LineupPlan>(x.LineupJson, PlanJson); }
        catch (JsonException) { }
        if (!string.IsNullOrEmpty(x.TacticJson))
            try { tactic = JsonSerializer.Deserialize<Sim.Core.Tactics.TacticPlan>(x.TacticJson, PlanJson); }
            catch (JsonException) { }
        if (!string.IsNullOrEmpty(x.PrematchPlanJson))
            try { plan = JsonSerializer.Deserialize<Sim.Core.Match.PrematchPlan>(x.PrematchPlanJson, PlanJson); }
            catch (JsonException) { }
        return new MatchResolver.SideInputs { Lineup = lineup, Tactic = tactic, Plan = plan };
    }
}

/// <summary>Default <see cref="IRankedLiveBroadcaster"/> that pushes nothing (task 12.3). Registered in
/// Infrastructure so the ranked live service resolves everywhere — including the unit tests, which prove the
/// state through the REST reads; the Api replaces it with the SignalR implementation when the real host
/// runs.</summary>
public sealed class NoOpRankedLiveBroadcaster : IRankedLiveBroadcaster
{
    public Task RankedMatchChangedAsync(
        Guid fixtureId, RankedLiveStateDto state, CancellationToken ct = default) => Task.CompletedTask;
}
