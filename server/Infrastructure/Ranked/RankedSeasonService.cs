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
    private readonly IRankedAuctionService _auctions;
    private readonly IRankedRankingService _ranking;
    private readonly IRankedSeasonEndService _seasonEnd;
    private readonly INotificationService _notify;
    private readonly RankedOptions _opt;
    private readonly Fts.Infrastructure.Integrity.IntegrityOptions _integrityOpt;
    private readonly BalanceConfig _config = new();

    public RankedSeasonService(
        FtsDbContext db, IRankedService ranked, IRankedAuctionService auctions,
        IRankedRankingService ranking, IRankedSeasonEndService seasonEnd,
        INotificationService notify, IOptions<RankedOptions> options,
        IOptions<Fts.Infrastructure.Integrity.IntegrityOptions> integrityOptions)
    {
        _db = db;
        _ranked = ranked;
        _auctions = auctions;
        _ranking = ranking;
        _seasonEnd = seasonEnd;
        _notify = notify;
        _opt = options.Value;
        _integrityOpt = integrityOptions.Value;
    }

    /// <summary>Shared with the other ranked services (see <see cref="RankedPlanJson"/>) so a plan written by
    /// any of them — including the 9.4 seeded defaults — reads back identically here.</summary>
    private static readonly JsonSerializerOptions PlanJson = RankedPlanJson.Options;

    // --- the clock ---------------------------------------------------------------------------------

    /// <summary>
    /// One calendar tick at a time in this process (Phase 9.6). The recurring job carries Hangfire's
    /// distributed lock, but the dev/staging endpoints (<c>/internal/ranked/tick</c> and the fast-forward)
    /// are a different path — during the load test they ran straight into the minutely job, two ticks
    /// resolving the same due matchdays and contending on the same rows. The gate is per process, which is
    /// exactly the scope the job's own lock does not cover; across instances the Hangfire lock still rules.
    /// </summary>
    private static readonly SemaphoreSlim TickGate = new(1, 1);

    public async Task<RankedTickSummary> TickAsync(CancellationToken ct = default)
    {
        await TickGate.WaitAsync(ct);
        try
        {
            return await RunTickAsync(ct);
        }
        finally
        {
            TickGate.Release();
        }
    }

    private async Task<RankedTickSummary> RunTickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        int started = await StartDueSeasonsAsync(now, ct);
        (int matchdays, int fixtures, int placements, int completed) = await ResolveDueMatchdaysAsync(now, ct);
        int windows = await AnnounceMarketWindowsAsync(now, ct);

        // A group whose between-seasons break has elapsed gets its promotions/relegations applied and its
        // squads rebuilt, then reopens for the next season (9.3).
        int resets = await ApplyDueSeasonResetsAsync(now, ct);

        // Settle any free-agent auction lot whose window has closed (9.2b). The lots were opened when the
        // window opened (see AnnounceMarketWindows); the winner gets the player + is charged.
        await _auctions.SettleDueAsync(false, ct);

        return new RankedTickSummary(started, matchdays, fixtures, placements, completed, windows, resets);
    }

    /// <summary>
    /// Starts a fresh unit of work for one group and loads it (Phase 9.6).
    ///
    /// A tick walks every group on the ladder, and each group's work is heavy in its own right: a whole
    /// generated world reconstructed, a matchday simulated, every player written back, a full match report
    /// serialized per fixture. Sharing one change tracker across all of them means the entire ladder's graph
    /// — tens of thousands of players plus every stored replay — piles up in memory, and each further
    /// SaveChanges pays to scan it. The 9.6 load test caught exactly that: 44ms per fixture with 25 groups
    /// on the ladder, 1.16s per fixture with 125. The groups are independent (a separate world each) and
    /// every step commits before the next begins, so dropping the tracked graph between them is safe and
    /// keeps a tick linear in the number of groups.
    /// </summary>
    private async Task<RankedGroup?> BeginGroupAsync(Guid groupId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        return await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
    }

    /// <summary>Resets every group whose between-seasons break has run out (Phase 9.3). Until then a finished
    /// group keeps its final table readable — the reset is what applies promotion/relegation and rebuilds
    /// fair squads for the next season.</summary>
    private async Task<int> ApplyDueSeasonResetsAsync(DateTime now, CancellationToken ct)
    {
        var due = await _db.RankedGroups
            .Where(g => g.Kind == RankedGroupKind.Division
                        && g.Status == RankedGroupStatus.Completed
                        && g.SeasonEndedUtc != null)
            .Select(g => new { g.Id, EndedUtc = g.SeasonEndedUtc!.Value })
            .ToListAsync(ct);

        int resets = 0;
        foreach (var g in due)
        {
            if (g.EndedUtc.AddSeconds(_opt.SeasonBreakSeconds) > now) continue;

            // A reset rebuilds a whole world's squads — its own unit of work, like every other per-group step.
            _db.ChangeTracker.Clear();
            await _seasonEnd.ApplySeasonResetAsync(g.Id, ct);

            // Counted from the group's own state (it reopens as Forming) rather than from the summary, so a
            // group with no human coaches — or with squad equalisation switched off — still counts as reset.
            var status = await _db.RankedGroups
                .Where(x => x.Id == g.Id).Select(x => x.Status).FirstOrDefaultAsync(ct);
            if (status == RankedGroupStatus.Forming) resets++;
        }
        return resets;
    }

    /// <summary>DEV/STAGING fast-forward (Phase 9.3 dev-sim tooling): pull every running season's clock back
    /// by one matchday interval and tick, repeatedly. Both <c>SeasonStartedUtc</c> and every kickoff move by
    /// the SAME amount, so matchdays and market windows keep their relative spacing — it is real time travel,
    /// not a bypass of the calendar. With the production interval (1 day) this lets a solo tester run a whole
    /// season, its reset and the season after it in seconds.</summary>
    public async Task<RankedTickSummary> FastForwardAsync(int matchdays, CancellationToken ct = default)
    {
        int rounds = Math.Clamp(matchdays, 1, 400);
        int started = 0, days = 0, fixtures = 0, placements = 0, completed = 0, windows = 0, resets = 0;

        // A zero interval means every matchday is already due — a plain tick is enough (the test calendar).
        int step = Math.Max(_opt.MatchdayIntervalSeconds, 0);

        // Hold the tick gate for the WHOLE fast-forward (Phase 9.6) and drive the tick body directly: moving
        // the clock back and then ticking has to be one atomic move, or the minutely job lands in the middle
        // of it and resolves half a shifted calendar — which is what muddied the first load-test run.
        await TickGate.WaitAsync(ct);
        try
        {
            for (int i = 0; i < rounds; i++)
            {
                if (step > 0) await ShiftClockBackAsync(step, ct);
                // The between-seasons break must run out too, or a fast-forward would stall at the season end.
                await ShiftBreakBackAsync(Math.Max(step, _opt.SeasonBreakSeconds), ct);

                var s = await RunTickAsync(ct);
                started += s.SeasonsStarted;
                days += s.MatchdaysResolved;
                fixtures += s.FixturesResolved;
                placements += s.PlacementsResolved;
                completed += s.DivisionsCompleted;
                windows += s.MarketWindowsOpened;
                resets += s.SeasonsReset;
            }
        }
        finally
        {
            TickGate.Release();
        }

        return new RankedTickSummary(started, days, fixtures, placements, completed, windows, resets);
    }

    /// <summary>Moves every started season (and its pending kickoffs) back by <paramref name="seconds"/>.</summary>
    private async Task ShiftClockBackAsync(int seconds, CancellationToken ct)
    {
        var offset = TimeSpan.FromSeconds(seconds);

        var groups = await _db.RankedGroups
            .Where(g => g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active)
            .ToListAsync(ct);
        if (groups.Count == 0) return;

        var groupIds = groups.Select(g => g.Id).ToList();
        var pending = await _db.RankedFixtures
            .Where(f => groupIds.Contains(f.RankedGroupId) && !f.IsPlayed)
            .ToListAsync(ct);

        foreach (var g in groups) g.SeasonStartedUtc = g.SeasonStartedUtc!.Value - offset;
        foreach (var f in pending) f.KickoffUtc -= offset;

        // Auction lots close with their window, which just moved too.
        var lots = await _db.RankedAuctions
            .Where(a => groupIds.Contains(a.RankedGroupId) && a.Status == RankedAuctionStatus.Open)
            .ToListAsync(ct);
        foreach (var lot in lots) lot.EndsUtc -= offset;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Ages every finished group's break by <paramref name="seconds"/> so a fast-forward walks
    /// through the between-seasons pause instead of parking on it (Phase 9.3 dev tooling).</summary>
    private async Task ShiftBreakBackAsync(int seconds, CancellationToken ct)
    {
        if (seconds <= 0) return;

        var finished = await _db.RankedGroups
            .Where(g => g.SeasonEndedUtc != null && g.Status == RankedGroupStatus.Completed)
            .ToListAsync(ct);
        if (finished.Count == 0) return;

        var offset = TimeSpan.FromSeconds(seconds);
        foreach (var g in finished) g.SeasonEndedUtc = g.SeasonEndedUtc!.Value - offset;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Generates the schedule for any group whose season is due but not yet started: a filled
    /// placement group, or a division that has at least one human coach. Deterministic double round-robin
    /// (<see cref="FixtureScheduler"/>) stamped with real-time kickoffs from <see cref="RankedCalendar"/>.</summary>
    private async Task<int> StartDueSeasonsAsync(DateTime now, CancellationToken ct)
    {
        // Candidate groups: materialised, not completed, with no fixtures yet. Ids only — each group is then
        // loaded inside its own unit of work (see BeginGroupAsync).
        var candidateIds = await _db.RankedGroups
            .Where(g => g.WorldId != null
                        && g.Status != RankedGroupStatus.Completed
                        && !_db.RankedFixtures.Any(f => f.RankedGroupId == g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);

        int started = 0;
        foreach (var groupId in candidateIds)
        {
            var group = await BeginGroupAsync(groupId, ct);
            if (group is null || group.WorldId is null || group.Status == RankedGroupStatus.Completed) continue;
            if (await _db.RankedFixtures.AnyAsync(f => f.RankedGroupId == group.Id, ct)) continue;

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
        // Mix the group's season counter into the seed (Phase 9.3) so the season after a reset is a NEW
        // season — a different fixture order and different match seeds, not a replay of the last one.
        var schedule = FixtureScheduler.Build(externalIds, SeasonSeed(world.Seed, group.SeasonNumber));

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

        // SMART DEFAULTS (Phase 9.4): every human coach starts the season with a stored best-XI lineup and a
        // balanced training plan, so a coach who never opens a screen still fields their best team and trains
        // sensibly — and the daily digest can state truthfully that their inputs are ready. Same XI the engine
        // would have picked as its silent fallback, so nothing about a result changes.
        await RankedInputDefaults.SeedForGroupAsync(_db, group.Id, ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves each active group's lowest still-pending round once its kickoff has passed, then
    /// closes/sorts any group whose schedule is now complete. One matchday per group per tick keeps the
    /// real-time cadence (with a compressed interval a test just calls Tick repeatedly).</summary>
    private async Task<(int matchdays, int fixtures, int placements, int completed)> ResolveDueMatchdaysAsync(
        DateTime now, CancellationToken ct)
    {
        var activeIds = await _db.RankedGroups
            .Where(g => g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active)
            .Select(g => g.Id)
            .ToListAsync(ct);

        int matchdays = 0, fixturesResolved = 0, placements = 0, completed = 0;
        if (activeIds.Count == 0) return (matchdays, fixturesResolved, placements, completed);

        // Groups that still have something to play, MOST OVERDUE FIRST — and, when several are due at the
        // same instant (which is the normal case: a ladder's matchdays land together), the one served least
        // recently goes first. Without that order a capped tick would keep serving the head of an unordered
        // list and starve everyone behind it.
        var due = await (
            from f in _db.RankedFixtures
            join g in _db.RankedGroups on f.RankedGroupId equals g.Id
            where !f.IsPlayed && g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active
            group f by f.RankedGroupId into grp
            select new { GroupId = grp.Key, NextKickoff = grp.Min(x => x.KickoffUtc) })
            .ToListAsync(ct);

        var lastServed = await _db.RankedFixtures
            .Where(f => f.IsPlayed && f.ResolvedUtc != null)
            .GroupBy(f => f.RankedGroupId)
            .Select(g => new { GroupId = g.Key, Last = g.Max(x => x.ResolvedUtc) })
            .ToDictionaryAsync(x => x.GroupId, x => x.Last ?? DateTime.MinValue, ct);

        var order = due
            .OrderBy(d => d.NextKickoff)
            .ThenBy(d => lastServed.TryGetValue(d.GroupId, out var last) ? last : DateTime.MinValue)
            .ToList();

        // Optional safety valve for a big live ladder (Phase 9.6): 0 = no cap, which is the behaviour the
        // calendar has always had. With a cap the leftover groups simply resolve on the next run — kickoffs
        // are a day apart, so a minute's delay costs nothing, and no single run can grow unbounded.
        int cap = _opt.MaxMatchdaysPerTick > 0 ? _opt.MaxMatchdaysPerTick : int.MaxValue;

        // A group whose schedule is fully played only needs closing — cheap, and never held back by the cap.
        var pendingGroups = new HashSet<Guid>(due.Select(d => d.GroupId));
        foreach (var groupId in activeIds)
        {
            if (pendingGroups.Contains(groupId)) continue;

            var group = await BeginGroupAsync(groupId, ct);
            if (group is null || group.Status != RankedGroupStatus.Active) continue;

            var fixtures = await _db.RankedFixtures
                .Where(f => f.RankedGroupId == group.Id)
                .ToListAsync(ct);
            if (fixtures.Count == 0) continue;

            // The season is over — close it (and sort a placement cohort into divisions).
            if (await CompleteSeasonAsync(group, fixtures, ct))
            {
                if (group.Kind == RankedGroupKind.Placement) placements++; else completed++;
            }
        }

        foreach (var candidate in order)
        {
            if (matchdays >= cap) break;
            if (candidate.NextKickoff > now) continue; // not due yet

            var group = await BeginGroupAsync(candidate.GroupId, ct);
            if (group is null || group.SeasonStartedUtc is null
                || group.Status != RankedGroupStatus.Active) continue;

            var fixtures = await _db.RankedFixtures
                .Where(f => f.RankedGroupId == group.Id)
                .ToListAsync(ct);
            var pending = fixtures.Where(f => !f.IsPlayed).ToList();
            if (pending.Count == 0) continue;

            int round = pending.Min(f => f.Round);
            var roundFixtures = pending.Where(f => f.Round == round).OrderBy(f => f.MatchIndex).ToList();

            // Not due yet — every fixture in a round shares one kickoff, so the first tells the time.
            if (roundFixtures[0].KickoffUtc > now) continue;

            fixturesResolved += await ResolveRoundAsync(group, roundFixtures, round, ct);
            matchdays++;

            // If that was the last round, close the season in the same tick.
            if (fixtures.All(f => f.IsPlayed))
            {
                if (await CompleteSeasonAsync(group, fixtures, ct))
                {
                    if (group.Kind == RankedGroupKind.Placement) placements++; else completed++;
                }
            }
        }

        return (matchdays, fixturesResolved, placements, completed);
    }

    private async Task<int> ResolveRoundAsync(
        RankedGroup group, List<RankedFixture> roundFixtures, int round, CancellationToken ct)
    {
        var world = await _db.Worlds.FirstAsync(w => w.Id == group.WorldId, ct);
        // Per-season seed (Phase 9.3): the same world, but each season after a reset rolls its own matches.
        long worldSeed = SeasonSeed(world.Seed, group.SeasonNumber);

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

            // Ladder rating (Phase 9.3): every matchday moves each human coach's Elo.
            await ApplyMatchRatingsAsync(group, humanByClub, f.HomeClubId, f.AwayClubId,
                report.HomeGoals, report.AwayGoals, ct);

            // Notify the two human coaches (best-effort — the sender no-ops when FCM is unconfigured).
            await NotifyMatchdayAsync(humanByClub, f.HomeClubId, entByGuid, report.HomeGoals, report.AwayGoals, ct);
            await NotifyMatchdayAsync(humanByClub, f.AwayClubId, entByGuid, report.AwayGoals, report.HomeGoals, ct);
        }

        // Server-authoritative weekly tick (1 resolved round = 1 week): evolve the WHOLE world's condition +
        // development on the same reconstructed Sim.Core clubs the matches used (9.2b). Since 9.4 a ranked
        // coach's stored training plan drives their club's development week (a club without one — every AI
        // seat — trains the AI default); a client re-running the same deterministic progressors from this
        // state derives the same world-state hash (the 8.4 agreement mechanic).
        var simWorld = new SimLeague { Division = 1 };
        foreach (SimClub sc in simByGuid.Values) simWorld.Clubs.Add(sc);

        IReadOnlyDictionary<int, TrainingPlan> trainingPlans = await LoadTrainingPlansAsync(group.Id, entByGuid, ct);
        OnlineSeasonTick.EvolveWeek(new[] { simWorld }, played, trainingPlans, unchecked((ulong)worldSeed), round, _config);

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

    /// <summary>The stored training plans keyed by club external id (= the Sim.Core club id), for the weekly
    /// development tick (Phase 9.4). A club without a row is simply absent → the tick trains it the AI
    /// default, which is what every ranked club did before 9.4 and what every AI seat still does. Mirrors
    /// <c>LeagueSeasonService.LoadTrainingPlansAsync</c> (8.4).</summary>
    private async Task<IReadOnlyDictionary<int, TrainingPlan>> LoadTrainingPlansAsync(
        Guid groupId, IReadOnlyDictionary<Guid, Club> entByGuid, CancellationToken ct)
    {
        var rows = await _db.RankedTrainings.Where(x => x.RankedGroupId == groupId).ToListAsync(ct);
        var plans = new Dictionary<int, TrainingPlan>();
        foreach (var row in rows)
        {
            if (!entByGuid.TryGetValue(row.ClubId, out var club)) continue;
            TrainingPlan? plan = null;
            try { plan = JsonSerializer.Deserialize<TrainingPlan>(row.TrainingJson, PlanJson); }
            catch (JsonException) { }
            if (plan != null) plans[club.ExternalId] = plan;
        }
        return plans;
    }

    /// <summary>Closes a group whose schedule is fully played: a placement group is sorted into divisions
    /// (top finishers up, the rest down) via <see cref="IRankedService.ResolvePlacementAsync"/> and its
    /// coaches get their first palmarès line; a division season is handed to
    /// <see cref="IRankedSeasonEndService"/>, which rates + rewards its coaches and puts the group into the
    /// between-seasons break (the promotion/relegation + squad reset then fire when the break elapses).
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
            {
                string placementWorld = await _db.RankedWorlds
                    .Where(w => w.Id == group.RankedWorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

                foreach (var a in result.Value.Assignments)
                {
                    // The first line of every coach's palmarès: they entered the pyramid (Phase 9.3).
                    await _ranking.GrantAwardAsync(
                        a.UserId, RankedAwardKind.PlacementCompleted, group.Id, placementWorld, group.Name,
                        tier: 0, position: a.Position, seasonNumber: group.SeasonNumber,
                        ratingAfter: a.Rating, ratingDelta: 0, ct);

                    await SafeSend(a.UserId, "Piazzamento completato",
                        $"Sei stato assegnato a {a.GroupName} ({a.WorldName}) con il club {a.ClubName}.",
                        new Dictionary<string, string> { ["kind"] = "ranked_placed", ["groupId"] = a.GroupId.ToString() }, ct);
                }
            }
            return result.Success;
        }

        // A division season closes with the whole 9.3 season end: rating, awards, promotion/relegation and
        // the squad reset that reopens the group for the next season (with a fresh free-agent auction).
        await _seasonEnd.CompleteDivisionSeasonAsync(group.Id, standings, ct);
        return true;
    }

    /// <summary>Moves the human coaches' ladder rating after one resolved fixture (Phase 9.3). A vacant (AI)
    /// seat plays at a tier-derived baseline, so a division with a single human coach still rates its matches;
    /// placement seasons are skipped because placement re-seeds the rating from the final position anyway.
    /// Both ratings are read BEFORE either is updated, so a human-vs-human match is symmetric.</summary>
    private async Task ApplyMatchRatingsAsync(
        RankedGroup group, IReadOnlyDictionary<Guid, Guid> humanByClub,
        Guid homeClubId, Guid awayClubId, int homeGoals, int awayGoals, CancellationToken ct)
    {
        if (group.Kind != RankedGroupKind.Division) return;

        Guid? homeUser = humanByClub.TryGetValue(homeClubId, out var hu) ? hu : null;
        Guid? awayUser = humanByClub.TryGetValue(awayClubId, out var au) ? au : null;
        if (homeUser is null && awayUser is null) return;

        int aiRating = _opt.AiRatingForTier(group.Tier);
        int homeRating = homeUser is { } h ? await _ranking.RatingOfAsync(h, ct) : aiRating;
        int awayRating = awayUser is { } a ? await _ranking.RatingOfAsync(a, ct) : aiRating;

        if (homeUser is { } homeId)
            await _ranking.ApplyMatchAsync(homeId, awayRating, EloModel.OutcomeOf(homeGoals, awayGoals), ct);
        if (awayUser is { } awayId)
            await _ranking.ApplyMatchAsync(awayId, homeRating, EloModel.OutcomeOf(awayGoals, homeGoals), ct);
    }

    /// <summary>The seed a given season of a group runs on: the generated world's seed mixed (splitmix-style)
    /// with the group's season counter, so every season after a reset gets its own schedule and match seeds
    /// while staying fully reproducible from the world's root seed.</summary>
    private static long SeasonSeed(long worldSeed, int seasonNumber)
    {
        unchecked
        {
            ulong mixed = (ulong)worldSeed ^ ((ulong)(uint)Math.Max(1, seasonNumber) * 0x9E3779B97F4A7C15UL);
            mixed ^= mixed >> 29;
            mixed *= 0xBF58476D1CE4E5B9UL;
            mixed ^= mixed >> 32;
            return (long)mixed;
        }
    }

    /// <summary>Announces the market window that just opened for each running season (once each, at season
    /// start and around the midpoint). 9.2a fires the signal + notification; the market content is 9.2b.</summary>
    private async Task<int> AnnounceMarketWindowsAsync(DateTime now, CancellationToken ct)
    {
        var groupIds = await _db.RankedGroups
            .Where(g => g.SeasonStartedUtc != null && g.Status == RankedGroupStatus.Active)
            .Select(g => g.Id)
            .ToListAsync(ct);

        int opened = 0;
        foreach (var groupId in groupIds)
        {
            var group = await BeginGroupAsync(groupId, ct);
            if (group is null || group.SeasonStartedUtc is null
                || group.Status != RankedGroupStatus.Active) continue;

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

            // Open a free-agent auction lot per unattached player, ending when the window closes (9.2b).
            await _auctions.OpenWindowLotsAsync(group.Id, w.Index, w.ClosesUtc, ct);

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

        // INPUT DEADLINE (Phase 9.5): once the next matchday's kickoff has arrived, the team that plays is
        // the one already stored. The calendar is polled by a minutely job, so without this guard a coach
        // could keep resubmitting during the gap between kickoff and resolution — picking their side after
        // seeing how the rest of the day is going. Rejecting costs nobody anything: a seeded default lineup
        // is always on file (9.4), so a missed deadline never means fielding no team.
        if (_integrityOpt.EnforceLineupDeadline
            && await LineupDeadlinePassedAsync(group.Id, DateTime.UtcNow, ct))
        {
            return RankedResult<RankedSeasonDto>.Fail(
                RankedError.DeadlinePassed,
                "The next matchday has kicked off — your stored lineup is the one that plays.");
        }

        string lineupJson = JsonSerializer.Serialize(request.Lineup, PlanJson);
        string? tacticJson = request.Tactic != null ? JsonSerializer.Serialize(request.Tactic, PlanJson) : null;
        string? planJson = request.Plan != null ? JsonSerializer.Serialize(request.Plan, PlanJson) : null;

        // Submitting a lineup IS a confirmation of the upcoming matchday (Phase 9.4): a coach who went to the
        // trouble of picking a team should not then be nagged to confirm it on the digest.
        int confirmedRound = await NextRoundAsync(group.Id, ct) ?? 0;

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
                ConfirmedRound = confirmedRound,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.LineupJson = lineupJson;
            existing.TacticJson = tacticJson;
            existing.PrematchPlanJson = planJson;
            existing.ConfirmedRound = Math.Max(existing.ConfirmedRound, confirmedRound);
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

    public async Task<RankedResult<RankedSeasonDto>> SubmitTrainingAsync(
        Guid userId, SubmitRankedTrainingRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");
        if (request.Training is null)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.ValidationFailed, "A training plan is required.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked club.");

        bool started = await _db.RankedFixtures.AnyAsync(f => f.RankedGroupId == group.Id, ct);
        if (!started || group.Status == RankedGroupStatus.Completed)
            return RankedResult<RankedSeasonDto>.Fail(RankedError.WrongPhase, "Your ranked season is not under way.");

        string trainingJson = JsonSerializer.Serialize(request.Training, PlanJson);

        var existing = await _db.RankedTrainings.FirstOrDefaultAsync(
            x => x.RankedGroupId == group.Id && x.ClubId == clubId, ct);
        if (existing is null)
        {
            _db.RankedTrainings.Add(new RankedTraining
            {
                Id = Guid.NewGuid(),
                RankedGroupId = group.Id,
                UserId = userId,
                ClubId = clubId,
                TrainingJson = trainingJson,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.TrainingJson = trainingJson;
            existing.UpdatedUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return RankedResult<RankedSeasonDto>.Ok(await BuildSeasonAsync(group, seat, userId, ct));
    }

    public async Task<RankedResult<string>> GetMyTrainingAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<string>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<string>.Ok(string.Empty);

        var row = await _db.RankedTrainings.FirstOrDefaultAsync(
            x => x.RankedGroupId == group.Id && x.ClubId == clubId, ct);
        return RankedResult<string>.Ok(row?.TrainingJson ?? string.Empty);
    }

    public async Task<RankedResult<string>> GetMyLineupAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<string>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<string>.Ok(string.Empty);

        var row = await _db.RankedLineups.FirstOrDefaultAsync(
            x => x.RankedGroupId == group.Id && x.ClubId == clubId, ct);
        return RankedResult<string>.Ok(row?.LineupJson ?? string.Empty);
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

    /// <summary>The group's lowest still-unplayed round — the matchday a submission/confirmation applies to,
    /// or null when the season is complete (Phase 9.4).</summary>
    /// <summary>Whether the upcoming matchday is already locked (Phase 9.5): its kickoff is at or before
    /// now plus the configured lock-out. A season with no pending round left is never locked — there is
    /// nothing to influence.</summary>
    private async Task<bool> LineupDeadlinePassedAsync(Guid groupId, DateTime now, CancellationToken ct)
    {
        var nextKickoff = await _db.RankedFixtures
            .Where(f => f.RankedGroupId == groupId && !f.IsPlayed)
            .OrderBy(f => f.Round)
            .Select(f => (DateTime?)f.KickoffUtc)
            .FirstOrDefaultAsync(ct);

        if (nextKickoff is not { } kickoff) return false;
        return kickoff <= now.AddSeconds(_integrityOpt.LineupLockSeconds);
    }

    private async Task<int?> NextRoundAsync(Guid groupId, CancellationToken ct) =>
        await _db.RankedFixtures
            .Where(f => f.RankedGroupId == groupId && !f.IsPlayed)
            .Select(f => (int?)f.Round)
            .MinAsync(ct);

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

    /// <summary>The group's table — delegated to the shared <see cref="RankedStandings"/> calculator so the
    /// order a coach sees is byte-for-byte the order the season end rewards (Phase 9.3).</summary>
    private IReadOnlyList<RankedStandingDto> ComputeStandings(
        List<Club> clubs, List<RankedFixture> fixtures, int? youExternal = null) =>
        RankedStandings.Compute(clubs, fixtures, _config.Season.PointsForWin, _config.Season.PointsForDraw, youExternal);

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

}
