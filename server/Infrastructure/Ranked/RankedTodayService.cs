using System.Text.Json;
using Fts.Application.Balance;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sim.Core.Config;
using Sim.Core.Development;

namespace Fts.Infrastructure.Ranked;

/// <summary>
/// <see cref="IRankedTodayService"/> implementation (Phase 9.4) — the daily digest, plus the one-tap
/// confirmation that closes an uneventful day.
///
/// It is a pure PROJECTION service: it reads the coach's seat, their group's calendar and table, the market
/// window with its pending offers and open lots, and the stored match inputs, and turns them into one answer
/// plus a short prioritised to-do list. The only write it performs is
/// <see cref="ConfirmMatchdayAsync"/> (seed/repair the inputs + stamp the confirmed round) — no calendar, no
/// rating, no market state is touched here, so the digest can never change the outcome of anything. NO
/// Sim.Core change; the balance config is only read for the points-per-win used by the shared table
/// calculator.
///
/// Deliberately NOT included (and why): a "you were outbid" item — a ranked auction lot stores only its
/// current leader, with no bid history in v1, so the server cannot tell a coach who never bid from one who
/// was outbid. The digest reports the lots the coach is currently LEADING instead, which is honest with the
/// data we keep.
/// </summary>
public sealed class RankedTodayService : IRankedTodayService
{
    private readonly FtsDbContext _db;
    private readonly RankedOptions _opt;
    /// <summary>The server's ACTIVE balance (Phase 10.3), snapshotted for the lifetime of this scoped
    /// service so one request - or one calendar tick - resolves against ONE set of numbers even if an
    /// admin pushes a revision half way through it. Before 10.3 this was `new BalanceConfig()`, i.e. the
    /// balance embedded in the build; with nothing ever pushed it still is, byte for byte.</summary>
    private readonly BalanceConfig _config;

    public RankedTodayService(
        FtsDbContext db, IOptions<RankedOptions> options, IBalanceProvider balance)
    {
        _db = db;
        _opt = options.Value;
        _config = balance.Current;
    }

    // --- read --------------------------------------------------------------------------------------

    public async Task<RankedResult<RankedTodayDto>> GetTodayAsync(Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null) return RankedResult<RankedTodayDto>.Ok(RankedTodayDto.NotEnrolled());

        // A coach who left the ladder (opted out and was released at a reset) holds no seat, so the projection
        // below naturally answers "enrolled, Retired, nothing to do today". Re-joining after retiring is not a
        // ladder capability yet (9.1 enrolment is once per account) — the digest does not invent a chore for it.
        return RankedResult<RankedTodayDto>.Ok(await BuildAsync(coach, ct));
    }

    // --- the one-tap confirm ------------------------------------------------------------------------

    public async Task<RankedResult<RankedTodayDto>> ConfirmMatchdayAsync(
        Guid userId, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedTodayDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");

        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        if (group is null || seat?.ClubId is not { } clubId)
            return RankedResult<RankedTodayDto>.Fail(
                RankedError.WrongPhase, "You do not currently hold a ranked club.");

        var fixtures = await FixturesAsync(group.Id, ct);
        if (fixtures.Count == 0 || group.Status == RankedGroupStatus.Completed)
            return RankedResult<RankedTodayDto>.Fail(
                RankedError.WrongPhase, "Your ranked season is not under way.");

        // Make the inputs real before claiming they are confirmed: seed a best XI when there is none, repair
        // one a transfer broke, and seed the balanced training plan if the coach never set one.
        await RankedInputDefaults.EnsureLineupAsync(_db, group.Id, clubId, coach.UserId, ct);
        await RankedInputDefaults.EnsureTrainingAsync(_db, group.Id, clubId, coach.UserId, ct);
        await _db.SaveChangesAsync(ct);

        int? nextRound = NextRoundOf(fixtures);
        var row = await _db.RankedLineups.FirstOrDefaultAsync(
            x => x.RankedGroupId == group.Id && x.ClubId == clubId, ct);
        if (row is not null && nextRound is { } nr && row.ConfirmedRound < nr)
        {
            row.ConfirmedRound = nr;
            row.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return RankedResult<RankedTodayDto>.Ok(await BuildAsync(coach, ct));
    }

    // --- projection --------------------------------------------------------------------------------

    private async Task<RankedTodayDto> BuildAsync(RankedCoach coach, CancellationToken ct)
    {
        var (group, seat) = await CurrentGroupSeatAsync(coach, ct);
        var todo = new List<RankedTodoDto>();

        if (group is null || seat is null)
        {
            // Enrolled, but between seats (waiting for a placement cohort to fill, or just promoted/relegated
            // and not yet materialised). Nothing to do but wait — an empty to-do list is the right answer.
            return Base(coach, group: null, seat: null, clubName: null, clubExternal: null, todo);
        }

        // The group's world, once: the same rows feed the opponent names, the budget and the table.
        var clubs = await _db.Clubs.Where(c => c.WorldId == group.WorldId).ToListAsync(ct);
        var clubById = clubs.ToDictionary(c => c.Id);

        Club? myClub = seat.ClubId is { } cid && clubById.TryGetValue(cid, out var mc) ? mc : null;
        var fixtures = await FixturesAsync(group.Id, ct);

        if (fixtures.Count == 0)
        {
            // The group has not kicked off yet: no calendar, no inputs to confirm.
            return Base(coach, group, seat, myClub?.Name, myClub?.ExternalId, todo);
        }

        int totalRounds = fixtures.Max(f => f.Round);
        int roundsPlayed = 0;
        for (int r = 1; r <= totalRounds; r++)
            if (fixtures.Where(f => f.Round == r).All(f => f.IsPlayed)) roundsPlayed++;
        int? nextRound = NextRoundOf(fixtures);
        bool seasonComplete = fixtures.All(f => f.IsPlayed);

        // --- your matches -------------------------------------------------------------------
        RankedTodayNextMatchDto? nextMatch = null;
        RankedTodayLastResultDto? lastResult = null;
        var now = DateTime.UtcNow;

        if (seat.ClubId is { } myClubId)
        {
            var mine = fixtures
                .Where(f => f.HomeClubId == myClubId || f.AwayClubId == myClubId)
                .OrderBy(f => f.Round).ToList();

            var upcoming = mine.FirstOrDefault(f => !f.IsPlayed);
            if (upcoming is not null)
            {
                bool home = upcoming.HomeClubId == myClubId;
                Guid oppId = home ? upcoming.AwayClubId : upcoming.HomeClubId;
                var opp = clubById.TryGetValue(oppId, out var o) ? o : null;
                nextMatch = new RankedTodayNextMatchDto(
                    FixtureId: upcoming.Id,
                    Round: upcoming.Round,
                    KickoffUtc: upcoming.KickoffUtc,
                    SecondsToKickoff: (int)Math.Max(0, Math.Round((upcoming.KickoffUtc - now).TotalSeconds)),
                    YouAreHome: home,
                    OpponentClubExternalId: opp?.ExternalId ?? 0,
                    OpponentClubName: opp?.Name ?? string.Empty);
            }

            var played = mine.LastOrDefault(f => f.IsPlayed);
            if (played is not null)
            {
                bool home = played.HomeClubId == myClubId;
                Guid oppId = home ? played.AwayClubId : played.HomeClubId;
                var opp = clubById.TryGetValue(oppId, out var o2) ? o2 : null;
                lastResult = new RankedTodayLastResultDto(
                    FixtureId: played.Id,
                    Round: played.Round,
                    YouAreHome: home,
                    OpponentClubExternalId: opp?.ExternalId ?? 0,
                    OpponentClubName: opp?.Name ?? string.Empty,
                    GoalsFor: home ? played.HomeGoals : played.AwayGoals,
                    GoalsAgainst: home ? played.AwayGoals : played.HomeGoals);
            }
        }

        // --- where you stand ----------------------------------------------------------------
        int? position = null, points = null;
        if (myClub is not null)
        {
            var standings = RankedStandings.Compute(
                clubs, fixtures,
                _config.Season.PointsForWin, _config.Season.PointsForDraw, myClub.ExternalId);
            for (int i = 0; i < standings.Count; i++)
            {
                if (standings[i].ClubExternalId != myClub.ExternalId) continue;
                position = i + 1;
                points = standings[i].Points;
                break;
            }
        }

        // --- your stored inputs (the smart defaults) ----------------------------------------
        bool lineupReady = false, lineupConfirmed = false, trainingSet = false;
        int? trainingFocus = null;

        if (seat.ClubId is { } inputClubId)
        {
            var lineup = await _db.RankedLineups.FirstOrDefaultAsync(
                x => x.RankedGroupId == group.Id && x.ClubId == inputClubId, ct);
            lineupReady = lineup is not null && !string.IsNullOrWhiteSpace(lineup.LineupJson);
            lineupConfirmed = lineup is not null && nextRound is { } nr && lineup.ConfirmedRound >= nr;

            var training = await _db.RankedTrainings.FirstOrDefaultAsync(
                x => x.RankedGroupId == group.Id && x.ClubId == inputClubId, ct);
            trainingSet = training is not null;
            trainingFocus = TeamFocusOf(training?.TrainingJson);
        }

        // --- the market ---------------------------------------------------------------------
        RankedMarketWindowDto? window = null;
        if (group.SeasonStartedUtc is { } start && totalRounds > 0)
        {
            var w = RankedCalendar.CurrentWindow(
                start, totalRounds, _opt.MatchdayIntervalSeconds, _opt.MarketWindowDurationSeconds, now);
            if (w is { } win) window = new RankedMarketWindowDto(win.Index, win.OpensUtc, win.ClosesUtc, true);
        }

        int incoming = await _db.RankedOffers.CountAsync(
            o => o.RankedGroupId == group.Id && o.SellerUserId == coach.UserId
                 && o.Status == RankedOfferStatus.Pending, ct);
        int outgoing = await _db.RankedOffers.CountAsync(
            o => o.RankedGroupId == group.Id && o.BuyerUserId == coach.UserId
                 && o.Status == RankedOfferStatus.Pending, ct);
        int openLots = await _db.RankedAuctions.CountAsync(
            a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open, ct);
        int leading = await _db.RankedAuctions.CountAsync(
            a => a.RankedGroupId == group.Id && a.Status == RankedAuctionStatus.Open
                 && a.HighBidUserId == coach.UserId, ct);

        // --- the to-do list (prioritised: answer people first, then your own inputs) --------
        if (incoming > 0) todo.Add(new RankedTodoDto(RankedTodoKind.RespondOffer, incoming, 1));
        if (!seasonComplete && nextRound is not null && !lineupConfirmed)
            todo.Add(new RankedTodoDto(RankedTodoKind.ConfirmMatchday, 1, 2));
        if (window is not null && openLots > 0)
            todo.Add(new RankedTodoDto(RankedTodoKind.MarketWindow, openLots, 3));
        if (seasonComplete)
            todo.Add(new RankedTodoDto(RankedTodoKind.SeasonSummary, 1, 4));

        return new RankedTodayDto(
            Enrolled: true,
            Status: coach.Status,
            Rating: coach.Rating,
            AutoEnrol: coach.AutoEnrol,
            GroupId: group.Id,
            GroupName: group.Name,
            Kind: group.Kind,
            Tier: group.Tier,
            ClubExternalId: myClub?.ExternalId,
            ClubName: myClub?.Name,
            InSeason: true,
            SeasonComplete: seasonComplete,
            TotalRounds: totalRounds,
            RoundsPlayed: roundsPlayed,
            NextRound: nextRound,
            YourPosition: position,
            YourPoints: points,
            NextMatch: nextMatch,
            LastResult: lastResult,
            LineupReady: lineupReady,
            LineupConfirmed: lineupConfirmed,
            TrainingSet: trainingSet,
            TrainingTeamFocus: trainingFocus,
            MarketWindow: window,
            Budget: myClub?.TransferBudget ?? 0,
            IncomingOffers: incoming,
            OutgoingOffers: outgoing,
            OpenLots: openLots,
            LotsYouLead: leading,
            ActionCount: todo.Count,
            Todo: todo.OrderBy(t => t.Priority).ToList());
    }

    /// <summary>The digest for a coach who is enrolled but has no calendar to look at yet (no seat, or a
    /// group that has not kicked off).</summary>
    private static RankedTodayDto Base(
        RankedCoach coach, RankedGroup? group, RankedSeat? seat,
        string? clubName, int? clubExternal, List<RankedTodoDto> todo) => new(
        Enrolled: true,
        Status: coach.Status,
        Rating: coach.Rating,
        AutoEnrol: coach.AutoEnrol,
        GroupId: group?.Id,
        GroupName: group?.Name,
        Kind: group?.Kind,
        Tier: group?.Tier,
        ClubExternalId: clubExternal,
        ClubName: clubName,
        InSeason: false,
        SeasonComplete: false,
        TotalRounds: 0,
        RoundsPlayed: 0,
        NextRound: null,
        YourPosition: null,
        YourPoints: null,
        NextMatch: null,
        LastResult: null,
        LineupReady: false,
        LineupConfirmed: false,
        TrainingSet: false,
        TrainingTeamFocus: null,
        MarketWindow: null,
        Budget: 0,
        IncomingOffers: 0,
        OutgoingOffers: 0,
        OpenLots: 0,
        LotsYouLead: 0,
        ActionCount: todo.Count,
        Todo: todo);

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<(RankedGroup? group, RankedSeat? seat)> CurrentGroupSeatAsync(
        RankedCoach coach, CancellationToken ct)
    {
        if (coach.SeatId is not { } seatId) return (null, null);
        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return (null, null);
        var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == seat.RankedGroupId, ct);
        return (group, seat);
    }

    private async Task<List<RankedFixture>> FixturesAsync(Guid groupId, CancellationToken ct) =>
        await _db.RankedFixtures
            .Where(f => f.RankedGroupId == groupId)
            .OrderBy(f => f.Round).ThenBy(f => f.MatchIndex)
            .ToListAsync(ct);

    /// <summary>The lowest round that still has an unplayed fixture — the matchday a confirmation applies to.</summary>
    private static int? NextRoundOf(List<RankedFixture> fixtures)
    {
        int? next = null;
        foreach (var f in fixtures)
            if (!f.IsPlayed && (next is null || f.Round < next)) next = f.Round;
        return next;
    }

    /// <summary>The stored plan's team focus as its numeric enum value, or null when nothing is stored.</summary>
    private static int? TeamFocusOf(string? trainingJson)
    {
        if (string.IsNullOrWhiteSpace(trainingJson)) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<TrainingPlan>(trainingJson, RankedPlanJson.Options);
            return plan is null ? null : (int)plan.TeamFocus;
        }
        catch (JsonException) { return null; }
    }
}
