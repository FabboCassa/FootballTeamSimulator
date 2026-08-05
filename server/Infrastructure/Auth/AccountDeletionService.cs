using Fts.Application.Auth;
using Fts.Application.Leagues;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Auth;

/// <summary>
/// <see cref="IAccountDeletionService"/> implementation (Phase 10.2a).
///
/// Two rules decided with the user shape everything here:
///
/// 1. <b>Personal data goes, shared history stays — anonymised.</b> Anything that identifies the person
///    (login, profile, sessions, devices, the hashed address/device signals, integrity flags) is deleted.
///    The ranked ladder's own record (<c>ranked_coaches</c> + the append-only <c>ranked_awards</c>) is
///    NOT deleted: it is re-stamped with a fresh random id that belongs to nobody, so a finished season's
///    table and the palmarès of the coaches who played against them stay coherent while ceasing to be
///    about anyone. The ladder seat is freed and plays on as AI, exactly as a retirement does — a season
///    in progress must not lose a club mid-way.
///
/// 2. <b>Order matters, so a half-finished deletion is harmless.</b> The Identity user (the login) is
///    destroyed LAST. If anything fails before that, the account still exists and the very same call can
///    be repeated to finish the job; there is no state in which someone is locked out of a half-deleted
///    account. Every step is written to be a no-op the second time round.
/// </summary>
public sealed class AccountDeletionService : IAccountDeletionService
{
    private readonly UserManager<AppUser> _users;
    private readonly FtsDbContext _db;
    private readonly ILeagueService _leagues;
    private readonly IRankedLeaderboardCache _leaderboard;

    public AccountDeletionService(
        UserManager<AppUser> users,
        FtsDbContext db,
        ILeagueService leagues,
        IRankedLeaderboardCache leaderboard)
    {
        _users = users;
        _db = db;
        _leagues = leagues;
        _leaderboard = leaderboard;
    }

    public async Task<AuthResult<DeleteAccountResult>> DeleteAsync(
        Guid userId, DeleteAccountRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return AuthResult<DeleteAccountResult>.Fail(AuthError.InvalidCredentials, "Invalid password.");

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
            return AuthResult<DeleteAccountResult>.Fail(AuthError.InvalidCredentials, "Invalid password.");

        int leaguesLeft = await LeavePrivateLeaguesAsync(userId, ct);
        await ReleasePrivateLeagueTracesAsync(userId, ct);
        var ranked = await AnonymiseRankedHistoryAsync(userId, ct);
        await ScrubIntegrityAsync(userId, ct);

        int signals = await _db.AccountSignals.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        int devices = await _db.DeviceRegistrations.Where(d => d.UserId == userId).ExecuteDeleteAsync(ct);
        int sessions = await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

        // A generated world's coach row may point back at the account (the 7.1 ownership link). Tracked
        // rather than a bulk update: the set is tiny and this stays portable across the SQLite test provider.
        var ownedCoaches = await _db.Coaches.Where(c => c.OwnerUserId == userId).ToListAsync(ct);
        foreach (var c in ownedCoaches) c.OwnerUserId = null;
        if (ownedCoaches.Count > 0) await _db.SaveChangesAsync(ct);

        // The profile is a shared-primary-key row on the user; deleting it explicitly keeps the teardown
        // portable instead of leaning on the provider's cascade.
        await _db.CoachProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

        // Last: the login itself. Everything above is now unreachable from any credential.
        var deleted = await _users.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            var message = string.Join(" ", deleted.Errors.Select(e => e.Description));
            return AuthResult<DeleteAccountResult>.Fail(AuthError.ValidationFailed, message);
        }

        return AuthResult<DeleteAccountResult>.Ok(new DeleteAccountResult(
            PrivateLeaguesLeft: leaguesLeft,
            SessionsRevoked: sessions,
            DevicesRemoved: devices,
            SignalsRemoved: signals,
            RankedHistoryAnonymised: ranked.Coach,
            RankedAwardsAnonymised: ranked.Awards));
    }

    // --- private leagues ---------------------------------------------------------------------------

    /// <summary>Leaves every private league through the normal 8.1 path, so ownership passes on and the
    /// last member out still tears the whole league + world down.</summary>
    private async Task<int> LeavePrivateLeaguesAsync(Guid userId, CancellationToken ct)
    {
        var leagueIds = await _db.LeagueMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.PrivateLeagueId)
            .ToListAsync(ct);

        int left = 0;
        foreach (var id in leagueIds)
        {
            var result = await _leagues.LeaveAsync(userId, id, ct);
            if (result.Success) left++;
            // A league that vanished under us (another member disbanded it) is not a failure.
        }
        return left;
    }

    /// <summary>Rows the account left behind in leagues that OUTLIVE it: submitted inputs, bids, and any
    /// live session it was a side of. A lot it was currently winning falls back to the best remaining
    /// bid so the auction can still settle honestly.</summary>
    private async Task ReleasePrivateLeagueTracesAsync(Guid userId, CancellationToken ct)
    {
        await _db.LeagueLineups.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.LeagueTrainings.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);

        var leadingLotIds = await _db.Auctions
            .Where(a => a.HighBidUserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);

        await _db.Bids.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);

        if (leadingLotIds.Count > 0)
        {
            var lots = await _db.Auctions.Where(a => leadingLotIds.Contains(a.Id)).ToListAsync(ct);
            foreach (var lot in lots)
            {
                var best = await _db.Bids
                    .Where(b => b.AuctionId == lot.Id)
                    .OrderByDescending(b => b.Amount)
                    .FirstOrDefaultAsync(ct);

                if (lot.Status == AuctionStatus.Open && best is not null)
                {
                    lot.HighBid = best.Amount;
                    lot.HighBidClubId = best.ClubId;
                    lot.HighBidClubExternalId = best.ClubExternalId;
                    lot.HighBidUserId = best.UserId;
                }
                else if (lot.Status == AuctionStatus.Open)
                {
                    // Nobody else ever bid: back to an untouched lot.
                    lot.HighBid = 0;
                    lot.HighBidClubId = null;
                    lot.HighBidClubExternalId = null;
                    lot.HighBidUserId = null;
                }
                else
                {
                    // Already settled: the transfer stands, only the name behind it goes.
                    lot.HighBidUserId = null;
                }
            }
        }

        var live = await _db.LiveMatches
            .Where(m => m.HomeUserId == userId || m.AwayUserId == userId)
            .ToListAsync(ct);
        foreach (var m in live)
        {
            if (m.HomeUserId == userId) { m.HomeUserId = null; m.HomePresent = false; }
            if (m.AwayUserId == userId) { m.AwayUserId = null; m.AwayPresent = false; }
        }

        await _db.SaveChangesAsync(ct);
    }

    // --- ranked ladder -----------------------------------------------------------------------------

    /// <summary>Frees the ladder seat (it plays on as AI) and re-stamps the coach row and its palmarès
    /// with a fresh random id, so the ladder's history survives while belonging to nobody.</summary>
    private async Task<(bool Coach, int Awards)> AnonymiseRankedHistoryAsync(Guid userId, CancellationToken ct)
    {
        await _db.RankedLineups.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.RankedTrainings.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.RankedOffers.Where(o => o.BuyerUserId == userId || o.SellerUserId == userId)
            .ExecuteDeleteAsync(ct);

        // Ranked lots keep only the current leader (no bid history in v1), so an open lot simply reopens.
        var lots = await _db.RankedAuctions.Where(a => a.HighBidUserId == userId).ToListAsync(ct);
        foreach (var lot in lots)
        {
            if (lot.Status == RankedAuctionStatus.Open)
            {
                lot.HighBid = 0;
                lot.HighBidClubId = null;
                lot.HighBidClubExternalId = null;
            }
            lot.HighBidUserId = null;
        }

        var seats = await _db.RankedSeats.Where(s => s.UserId == userId).ToListAsync(ct);
        foreach (var seat in seats) { seat.UserId = null; seat.OccupiedUtc = null; }

        var tombstone = Guid.NewGuid();
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is not null)
        {
            coach.UserId = tombstone;
            coach.SeatId = null;
            coach.AutoEnrol = false;
            coach.Status = RankedCoachStatus.Retired;   // keeps it out of the live leaderboard
        }

        var awards = await _db.RankedAwards.Where(a => a.UserId == userId).ToListAsync(ct);
        foreach (var a in awards) a.UserId = tombstone;

        await _db.SaveChangesAsync(ct);

        // Best-effort by contract; the cache is rebuildable from PostgreSQL anyway.
        await _leaderboard.RemoveAsync(userId, ct);

        return (coach is not null, awards.Count);
    }

    // --- integrity ---------------------------------------------------------------------------------

    /// <summary>Review-queue rows survive as anonymous statistics (a pattern of collusion is about a
    /// transfer, not a person) but stop naming anyone.</summary>
    private async Task ScrubIntegrityAsync(Guid userId, CancellationToken ct)
    {
        var flags = await _db.IntegrityFlags
            .Where(f => f.UserId == userId || f.SubjectUserId == userId)
            .ToListAsync(ct);

        foreach (var f in flags)
        {
            if (f.UserId == userId) f.UserId = null;
            if (f.SubjectUserId == userId) f.SubjectUserId = null;
        }

        if (flags.Count > 0) await _db.SaveChangesAsync(ct);
    }
}
