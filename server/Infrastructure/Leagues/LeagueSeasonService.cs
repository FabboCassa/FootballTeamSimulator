using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Balance;
using Fts.Application.Leagues;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Sim.Core.Career;
using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Match;
using Sim.Core.Tactics;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// <see cref="ILeagueSeasonService"/> implementation (Phase 8.3): the "advance when all ready" season.
/// Members submit their club's lineup/tactic/plan (stored and reused each matchday); the next round
/// resolves when every member is ready (or the creator forces it), running the shared Sim.Core engine
/// per fixture with a deterministic seed and a best-XI AI fallback for any club without a submission.
/// The full <c>MatchReport</c> is stored on the fixture and served verbatim to every member so a replay
/// renders identically for everyone. Resolution is synchronous here (a friend-league matchday is a
/// handful of instant sims, and the all-ready trigger is a member action, not a clock); a Hangfire
/// kickoff job wrapping this same resolution path is the RealTime-mode concern of 8.4.
/// </summary>
public sealed class LeagueSeasonService : ILeagueSeasonService
{
    private readonly FtsDbContext _db;
    /// <summary>The server's ACTIVE balance (Phase 10.3), snapshotted for the lifetime of this scoped
    /// service so one request - or one calendar tick - resolves against ONE set of numbers even if an
    /// admin pushes a revision half way through it. Before 10.3 this was `new BalanceConfig()`, i.e. the
    /// balance embedded in the build; with nothing ever pushed it still is, byte for byte.</summary>
    private readonly BalanceConfig _config;

    public LeagueSeasonService(FtsDbContext db, IBalanceProvider balance)
    {
        _db = db;
        _config = balance.Current;
    }

    /// <summary>Options for the stored plan JSON: tolerant of string- or number-valued enums and of
    /// property-name casing, so the round-trip is robust regardless of how the client serialised.</summary>
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- submit inputs -----------------------------------------------------------------------------

    public async Task<LeagueResult<SeasonStateDto>> SubmitLineupAsync(
        Guid userId, Guid leagueId, SubmitLineupRequest request, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.NotFound, "League not found.");

        var member = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (member is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.Status != LeagueStatus.Active)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.WrongPhase, "The season is not under way.");
        if (member.ClubId is not { } clubId)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.NotAssignedClub, "You have no club in this league.");
        if (request.Lineup is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.ValidationFailed, "A lineup is required.");

        var club = await _db.Clubs.Include(c => c.Players).FirstOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.NotAssignedClub, "Your club no longer exists.");

        SimClub sim = WorldSquadReader.ToSimClub(club);
        if (!request.Lineup.TryMaterialize(sim, out _))
            return LeagueResult<SeasonStateDto>.Fail(
                LeagueError.ValidationFailed, "The lineup is not valid for your current squad.");

        var existing = await _db.LeagueLineups.FirstOrDefaultAsync(
            x => x.PrivateLeagueId == leagueId && x.ClubId == clubId, ct);

        string lineupJson = JsonSerializer.Serialize(request.Lineup, PlanJson);
        string? tacticJson = request.Tactic != null ? JsonSerializer.Serialize(request.Tactic, PlanJson) : null;
        string? planJson = request.Plan != null ? JsonSerializer.Serialize(request.Plan, PlanJson) : null;

        if (existing is null)
        {
            _db.LeagueLineups.Add(new LeagueLineup
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = leagueId,
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

        var state = await BuildSeasonStateAsync(league, userId, ct);
        return LeagueResult<SeasonStateDto>.Ok(state);
    }

    public async Task<LeagueResult<SeasonStateDto>> SubmitTrainingAsync(
        Guid userId, Guid leagueId, SubmitTrainingRequest request, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.NotFound, "League not found.");

        var member = await _db.LeagueMembers.FirstOrDefaultAsync(
            m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (member is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.Status != LeagueStatus.Active)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.WrongPhase, "The season is not under way.");
        if (member.ClubId is not { } clubId)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.NotAssignedClub, "You have no club in this league.");
        if (request.Training is null)
            return LeagueResult<SeasonStateDto>.Fail(LeagueError.ValidationFailed, "A training plan is required.");

        string trainingJson = JsonSerializer.Serialize(request.Training, PlanJson);

        var existing = await _db.LeagueTrainings.FirstOrDefaultAsync(
            x => x.PrivateLeagueId == leagueId && x.ClubId == clubId, ct);
        if (existing is null)
        {
            _db.LeagueTrainings.Add(new LeagueTraining
            {
                Id = Guid.NewGuid(),
                PrivateLeagueId = leagueId,
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

        var state = await BuildSeasonStateAsync(league, userId, ct);
        return LeagueResult<SeasonStateDto>.Ok(state);
    }

    // --- ready / advance ---------------------------------------------------------------------------

    public async Task<LeagueResult<LeagueSeasonDto>> SetReadyAsync(
        Guid userId, Guid leagueId, SetReadyRequest request, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.NotFound, "League not found.");

        var members = await _db.LeagueMembers.Where(m => m.PrivateLeagueId == leagueId).ToListAsync(ct);
        var me = members.FirstOrDefault(m => m.UserId == userId);
        if (me is null)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.Status != LeagueStatus.Active)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.WrongPhase, "The season is not under way.");

        me.IsReady = request.Ready;
        await _db.SaveChangesAsync(ct);

        // Everyone ready → resolve the next round (which also clears the ready flags).
        if (members.All(m => m.IsReady))
            await ResolveNextRoundAsync(league, members, ct);

        var season = await BuildSeasonAsync(league, userId, ct);
        return LeagueResult<LeagueSeasonDto>.Ok(season);
    }

    public async Task<LeagueResult<LeagueSeasonDto>> AdvanceAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.NotFound, "League not found.");

        var members = await _db.LeagueMembers.Where(m => m.PrivateLeagueId == leagueId).ToListAsync(ct);
        if (members.All(m => m.UserId != userId))
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");
        if (league.CreatorUserId != userId)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.Forbidden, "Only the league owner can force an advance.");
        if (league.Status != LeagueStatus.Active)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.WrongPhase, "The season is not under way.");

        int resolved = await ResolveNextRoundAsync(league, members, ct);
        if (resolved < 0)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.NothingToResolve, "The season is already complete.");

        var season = await BuildSeasonAsync(league, userId, ct);
        return LeagueResult<LeagueSeasonDto>.Ok(season);
    }

    // --- reads -------------------------------------------------------------------------------------

    public async Task<LeagueResult<LeagueSeasonDto>> GetSeasonAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<LeagueSeasonDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        var season = await BuildSeasonAsync(league, userId, ct);
        return LeagueResult<LeagueSeasonDto>.Ok(season);
    }

    public async Task<LeagueResult<string>> GetReplayAsync(
        Guid userId, Guid leagueId, Guid fixtureId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<string>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<string>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        var fixture = await _db.LeagueFixtures.FirstOrDefaultAsync(
            f => f.Id == fixtureId && f.PrivateLeagueId == leagueId, ct);
        if (fixture is null)
            return LeagueResult<string>.Fail(LeagueError.FixtureNotFound, "No such fixture in this league.");
        if (!fixture.IsPlayed || string.IsNullOrEmpty(fixture.ReplayJson))
            return LeagueResult<string>.Fail(LeagueError.ReplayNotReady, "This match has not been played yet.");

        return LeagueResult<string>.Ok(fixture.ReplayJson);
    }

    // --- season end (8.7) --------------------------------------------------------------------------

    public async Task<LeagueResult<SeasonSummaryDto>> GetSeasonSummaryAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<SeasonSummaryDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<SeasonSummaryDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);

        var fixtures = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == league.Id)
            .ToListAsync(ct);

        var standings = ComputeStandings(clubs, fixtures);
        var playedFixtures = fixtures.Where(f => f.IsPlayed).ToList();
        int matchesPlayed = playedFixtures.Count;
        int totalGoals = playedFixtures.Sum(f => f.HomeGoals + f.AwayGoals);
        bool seasonComplete = league.Status == LeagueStatus.Completed
            || (fixtures.Count > 0 && fixtures.All(f => f.IsPlayed));

        SeasonAwardDto? champion = null, bestDefence = null, woodenSpoon = null;
        if (matchesPlayed > 0 && standings.Count > 0)
        {
            var top = standings[0];
            champion = new SeasonAwardDto(top.ClubExternalId, top.ClubName, top.Points);

            var last = standings[^1];
            woodenSpoon = new SeasonAwardDto(last.ClubExternalId, last.ClubName, last.Points);

            // Fewest goals conceded, breaking ties by points then goal difference (a good defence that also
            // wins beats one that just parks the bus).
            var def = standings
                .Where(s => s.Played > 0)
                .OrderBy(s => s.GoalsAgainst)
                .ThenByDescending(s => s.Points)
                .ThenByDescending(s => s.GoalDifference)
                .First();
            bestDefence = new SeasonAwardDto(def.ClubExternalId, def.ClubName, def.GoalsAgainst);
        }

        TopScorerDto? topScorer = ComputeTopScorer(clubs, playedFixtures);

        return LeagueResult<SeasonSummaryDto>.Ok(new SeasonSummaryDto(
            SeasonComplete: seasonComplete,
            FinalStandings: standings,
            Champion: champion,
            BestDefence: bestDefence,
            WoodenSpoon: woodenSpoon,
            TopScorer: topScorer,
            MatchesPlayed: matchesPlayed,
            TotalGoals: totalGoals));
    }

    /// <summary>Aggregates goals per scorer across every played fixture's stored MatchReport (Goal events
    /// keyed by the scorer's world-unique player id — the Sim.Core club id equals the persisted ExternalId
    /// in the online world) and returns the leader, or null when no goals were scored. A report that fails to
    /// deserialize is skipped, so a single bad row never breaks the summary.</summary>
    private static TopScorerDto? ComputeTopScorer(List<Club> clubs, List<LeagueFixture> playedFixtures)
    {
        var goalsByPlayer = new Dictionary<int, int>();
        foreach (var f in playedFixtures)
        {
            if (string.IsNullOrEmpty(f.ReplayJson)) continue;
            MatchReport? report = null;
            try { report = JsonSerializer.Deserialize<MatchReport>(f.ReplayJson); }
            catch (JsonException) { }
            if (report is null) continue;
            foreach (MatchEvent e in report.Events)
            {
                if (e.Type != MatchEventType.Goal) continue;
                goalsByPlayer.TryGetValue(e.PlayerId, out int g);
                goalsByPlayer[e.PlayerId] = g + 1;
            }
        }
        if (goalsByPlayer.Count == 0) return null;

        var playerIndex = clubs
            .SelectMany(c => c.Players.Select(p => (Player: p, Club: c)))
            .GroupBy(x => x.Player.ExternalId)
            .ToDictionary(g => g.Key, g => g.First());

        var best = goalsByPlayer
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .First();

        if (!playerIndex.TryGetValue(best.Key, out var found)) return null;
        string name = string.IsNullOrEmpty(found.Player.FirstName)
            ? found.Player.LastName
            : $"{found.Player.FirstName} {found.Player.LastName}";
        return new TopScorerDto(
            PlayerExternalId: found.Player.ExternalId,
            PlayerName: name,
            ClubExternalId: found.Club.ExternalId,
            ClubName: found.Club.Name,
            Goals: best.Value);
    }

    public async Task<LeagueResult<StateHashDto>> GetStateHashAsync(
        Guid userId, Guid leagueId, CancellationToken ct = default)
    {
        var league = await _db.PrivateLeagues.FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null)
            return LeagueResult<StateHashDto>.Fail(LeagueError.NotFound, "League not found.");

        var isMember = await _db.LeagueMembers.AnyAsync(m => m.PrivateLeagueId == leagueId && m.UserId == userId, ct);
        if (!isMember)
            return LeagueResult<StateHashDto>.Fail(LeagueError.Forbidden, "You are not a member of this league.");

        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);

        SimLeague simWorld = WorldSquadReader.ToSimLeague(clubs);
        ulong hash = Sim.Core.Domain.WorldStateHasher.Hash(new[] { simWorld });
        int playerCount = clubs.Sum(c => c.Players.Count);
        int roundsPlayed = await CountRoundsPlayedAsync(leagueId, ct);

        return LeagueResult<StateHashDto>.Ok(new StateHashDto(
            Sim.Core.Domain.WorldStateHasher.ToHex(hash), playerCount, roundsPlayed));
    }

    /// <summary>Number of fully-played rounds (a round counts only when all its fixtures are played).</summary>
    private async Task<int> CountRoundsPlayedAsync(Guid leagueId, CancellationToken ct)
    {
        var fixtures = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == leagueId)
            .Select(f => new { f.Round, f.IsPlayed })
            .ToListAsync(ct);
        if (fixtures.Count == 0) return 0;

        int totalRounds = fixtures.Max(f => f.Round);
        int played = 0;
        for (int r = 1; r <= totalRounds; r++)
            if (fixtures.Where(f => f.Round == r).All(f => f.IsPlayed)) played++;
        return played;
    }

    // --- round resolution --------------------------------------------------------------------------

    /// <summary>Resolves the lowest round that still has unplayed fixtures. Returns the round number, or
    /// -1 if the season is already complete. Runs each fixture through the shared engine, stores the score
    /// + full report, and clears every member's ready flag. Deterministic per (world seed, round, clubs).</summary>
    private async Task<int> ResolveNextRoundAsync(
        PrivateLeague league, List<LeagueMember> members, CancellationToken ct)
    {
        var fixtures = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == league.Id)
            .ToListAsync(ct);

        var pending = fixtures.Where(f => !f.IsPlayed).ToList();
        if (pending.Count == 0) return -1;

        int round = pending.Min(f => f.Round);
        var roundFixtures = pending.Where(f => f.Round == round).OrderBy(f => f.MatchIndex).ToList();

        long worldSeed = await _db.Worlds.Where(w => w.Id == league.WorldId).Select(w => w.Seed).FirstAsync(ct);

        // Reconstruct every club (squads) once and index the submitted inputs by club.
        var clubs = await _db.Clubs
            .Where(c => c.WorldId == league.WorldId)
            .Include(c => c.Players)
            .ToListAsync(ct);
        var entByGuid = clubs.ToDictionary(c => c.Id);
        var simByGuid = clubs.ToDictionary(c => c.Id, WorldSquadReader.ToSimClub);

        var lineups = await _db.LeagueLineups.Where(x => x.PrivateLeagueId == league.Id).ToListAsync(ct);
        var inputsByClub = lineups.ToDictionary(x => x.ClubId, DeserializeInputs);

        // A fixture played LIVE (8.6) carries its result on a Finished LiveMatch — consume it verbatim
        // instead of re-simulating, so the live-played score feeds standings and the weekly tick coherently.
        var roundFixtureIds = roundFixtures.Select(f => f.Id).ToList();
        var liveByFixture = await _db.LiveMatches
            .Where(l => l.PrivateLeagueId == league.Id
                        && l.Status == LiveMatchStatus.Finished
                        && roundFixtureIds.Contains(l.FixtureId))
            .ToListAsync(ct);
        var liveResult = liveByFixture
            .Where(l => !string.IsNullOrEmpty(l.ReportJson))
            .ToDictionary(l => l.FixtureId);

        // The clubs that played this round, keyed by their world-unique external id (= Sim.Core club id),
        // with the kickoff XI + result — the input to the weekly condition tick.
        var played = new Dictionary<int, ConditionProgressor.Participation>();

        var now = DateTime.UtcNow;
        foreach (var f in roundFixtures)
        {
            SimClub home = simByGuid[f.HomeClubId];
            SimClub away = simByGuid[f.AwayClubId];
            inputsByClub.TryGetValue(f.HomeClubId, out var homeInputs);
            inputsByClub.TryGetValue(f.AwayClubId, out var awayInputs);

            int homeExt = entByGuid[f.HomeClubId].ExternalId;
            int awayExt = entByGuid[f.AwayClubId].ExternalId;

            // A live-played fixture: use the stored live report + its denormalised score; the kickoff XI
            // (before any live sub) credits the weekly condition tick, per the 8.4 convention.
            if (liveResult.TryGetValue(f.Id, out var live))
            {
                f.HomeGoals = live.HomeGoals;
                f.AwayGoals = live.AwayGoals;
                f.IsPlayed = true;
                f.MatchSeed = live.Seed;
                f.ResolvedUtc = now;
                f.ReplayJson = live.ReportJson;

                (HashSet<int> homeStarters, HashSet<int> awayStarters) =
                    MatchResolver.KickoffElevenIds(home, away, homeInputs, awayInputs);
                played[homeExt] = new ConditionProgressor.Participation(
                    homeStarters, ResultFor(live.HomeGoals, live.AwayGoals));
                played[awayExt] = new ConditionProgressor.Participation(
                    awayStarters, ResultFor(live.AwayGoals, live.HomeGoals));
                continue;
            }

            ulong seed = MatchSeed(worldSeed, f.Round, homeExt, awayExt);

            // Resolve on the clubs' live condition (server-authoritative from 8.4).
            MatchResolver.ResolveResult r = MatchResolver.Resolve(home, away, homeInputs, awayInputs, seed, _config);
            MatchReport report = r.Report;

            f.HomeGoals = report.HomeGoals;
            f.AwayGoals = report.AwayGoals;
            f.IsPlayed = true;
            f.MatchSeed = unchecked((long)seed);
            f.ResolvedUtc = now;
            f.ReplayJson = JsonSerializer.Serialize(report);

            played[homeExt] = new ConditionProgressor.Participation(
                r.HomeStarterIds, ResultFor(report.HomeGoals, report.AwayGoals));
            played[awayExt] = new ConditionProgressor.Participation(
                r.AwayStarterIds, ResultFor(report.AwayGoals, report.HomeGoals));
        }

        // The server-authoritative weekly tick: evolve the WHOLE world's condition + development for this
        // round-week (1 resolved round = 1 week). It mutates the same reconstructed Sim.Core clubs used by
        // the matches, driven by who played and by the submitted training plans; a client re-running the
        // identical Sim.Core progressors derives the same state (the 8.4 ✅, verified via the state hash).
        var simWorld = new SimLeague { Division = 1 };
        foreach (SimClub sc in simByGuid.Values) simWorld.Clubs.Add(sc);

        IReadOnlyDictionary<int, TrainingPlan> trainingPlans = await LoadTrainingPlansAsync(league.Id, entByGuid, ct);

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

        // A fresh matchday — everyone must ready up again for the next one.
        foreach (var m in members) m.IsReady = false;

        // Season end (8.7): once every fixture in the schedule is played the league is Completed. The table
        // freezes at the final standings; members read the summary + awards via GetSeasonSummaryAsync and the
        // creator can start a fresh season (full reset → draft) via LeagueService.StartNewSeasonAsync.
        if (fixtures.All(f => f.IsPlayed))
            league.Status = LeagueStatus.Completed;

        await _db.SaveChangesAsync(ct);
        return round;
    }

    /// <summary>The submitted training plans keyed by club external id (Sim.Core club id); a club without
    /// a submission is simply absent → the tick trains it the AI default.</summary>
    private async Task<IReadOnlyDictionary<int, TrainingPlan>> LoadTrainingPlansAsync(
        Guid leagueId, IReadOnlyDictionary<Guid, Club> entByGuid, CancellationToken ct)
    {
        var rows = await _db.LeagueTrainings.Where(x => x.PrivateLeagueId == leagueId).ToListAsync(ct);
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

    /// <summary>A team's result from its own goals vs the goals conceded.</summary>
    private static TeamResult ResultFor(int goalsFor, int goalsAgainst) =>
        goalsFor > goalsAgainst ? TeamResult.Win
        : goalsFor < goalsAgainst ? TeamResult.Loss
        : TeamResult.Draw;

    private static MatchResolver.SideInputs DeserializeInputs(LeagueLineup x)
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

    /// <summary>Deterministic per-fixture seed from (world seed, round, home, away). Shared with the live
    /// session (8.6) via <see cref="FixtureSeed"/> so a live-played fixture uses the identical seed.</summary>
    private static ulong MatchSeed(long worldSeed, int round, int homeExternalId, int awayExternalId) =>
        FixtureSeed.For(worldSeed, round, homeExternalId, awayExternalId);

    // --- season view helpers -----------------------------------------------------------------------

    private async Task<LeagueSeasonDto> BuildSeasonAsync(PrivateLeague league, Guid callerId, CancellationToken ct)
    {
        var clubs = await _db.Clubs.Where(c => c.WorldId == league.WorldId).ToListAsync(ct);
        var entByGuid = clubs.ToDictionary(c => c.Id);

        var fixtures = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == league.Id)
            .OrderBy(f => f.Round).ThenBy(f => f.MatchIndex)
            .ToListAsync(ct);

        var fixtureDtos = fixtures.Select(f => new LeagueFixtureDto(
            Id: f.Id,
            Round: f.Round,
            Day: f.Day,
            HomeClubExternalId: entByGuid[f.HomeClubId].ExternalId,
            HomeClubName: entByGuid[f.HomeClubId].Name,
            AwayClubExternalId: entByGuid[f.AwayClubId].ExternalId,
            AwayClubName: entByGuid[f.AwayClubId].Name,
            Played: f.IsPlayed,
            HomeGoals: f.HomeGoals,
            AwayGoals: f.AwayGoals)).ToList();

        var standings = ComputeStandings(clubs, fixtures);
        var state = await BuildSeasonStateAsync(league, callerId, ct);

        return new LeagueSeasonDto(state, fixtureDtos, standings);
    }

    private async Task<SeasonStateDto> BuildSeasonStateAsync(PrivateLeague league, Guid callerId, CancellationToken ct)
    {
        var members = await _db.LeagueMembers.Where(m => m.PrivateLeagueId == league.Id).ToListAsync(ct);
        var me = members.FirstOrDefault(m => m.UserId == callerId);

        var fixtures = await _db.LeagueFixtures
            .Where(f => f.PrivateLeagueId == league.Id)
            .Select(f => new { f.Round, f.IsPlayed })
            .ToListAsync(ct);

        bool started = fixtures.Count > 0;
        int totalRounds = started ? fixtures.Max(f => f.Round) : 0;

        // A round counts as played only when all of its fixtures are played.
        int roundsPlayed = 0;
        int? nextRound = null;
        for (int r = 1; r <= totalRounds; r++)
        {
            bool allPlayed = fixtures.Where(f => f.Round == r).All(f => f.IsPlayed);
            if (allPlayed) roundsPlayed++;
            else if (nextRound is null) nextRound = r;
        }
        bool seasonComplete = started && fixtures.All(f => f.IsPlayed);

        int? yourClubExternalId = null;
        bool youSubmitted = false;
        if (me?.ClubId is { } clubId)
        {
            yourClubExternalId = await _db.Clubs.Where(c => c.Id == clubId).Select(c => (int?)c.ExternalId).FirstOrDefaultAsync(ct);
            youSubmitted = await _db.LeagueLineups.AnyAsync(x => x.PrivateLeagueId == league.Id && x.ClubId == clubId, ct);
        }

        return new SeasonStateDto(
            Started: started,
            TotalRounds: totalRounds,
            RoundsPlayed: roundsPlayed,
            NextRound: nextRound,
            SeasonComplete: seasonComplete,
            MembersTotal: members.Count,
            MembersReady: members.Count(m => m.IsReady),
            YouAreReady: me?.IsReady ?? false,
            YourClubExternalId: yourClubExternalId,
            YouSubmittedLineup: youSubmitted);
    }

    private IReadOnlyList<LeagueStandingDto> ComputeStandings(List<Club> clubs, List<LeagueFixture> fixtures)
    {
        int win = _config.Season.PointsForWin;
        int draw = _config.Season.PointsForDraw;

        var table = clubs.ToDictionary(
            c => c.Id,
            c => new Row { ExternalId = c.ExternalId, Name = c.Name });

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
            .Select(r => new LeagueStandingDto(
                ClubExternalId: r.ExternalId,
                ClubName: r.Name,
                Played: r.Played,
                Won: r.Won,
                Drawn: r.Drawn,
                Lost: r.Lost,
                GoalsFor: r.GoalsFor,
                GoalsAgainst: r.GoalsAgainst,
                GoalDifference: r.GoalsFor - r.GoalsAgainst,
                Points: r.Points))
            .ToList();
    }

    private sealed class Row
    {
        public int ExternalId;
        public string Name = string.Empty;
        public int Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points;
    }
}
