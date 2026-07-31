using System.Security.Cryptography;
using System.Text;
using Fts.Application.Integrity;
using Fts.Application.Ranked;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fts.Infrastructure.Integrity;

/// <summary>
/// <see cref="IIntegrityService"/> implementation (Phase 9.5) — the ladder's audit and multi-account layer.
///
/// Everything here is bookkeeping over the existing schema plus two new append/upsert tables; the actual
/// DECISIONS (refuse this transfer, do not seat these two together) are taken by the ranked use cases using
/// the pure models in <c>Fts.Application.Integrity</c>. Keeping it that way means a guard can never
/// half-apply: the use case that owns the transaction owns the verdict, and this service only records it.
///
/// Privacy: addresses and device ids are salted-SHA-256 hashed here and never stored raw — see
/// <see cref="AccountSignal"/>.
/// </summary>
public sealed class IntegrityService : IIntegrityService
{
    private readonly FtsDbContext _db;
    private readonly IntegrityOptions _opt;

    public IntegrityService(FtsDbContext db, IOptions<IntegrityOptions> options)
    {
        _db = db;
        _opt = options.Value;
    }

    // --- account signals ------------------------------------------------------------------------

    public async Task RecordAccountSignalAsync(
        Guid userId, string? ipAddress, string? deviceId, CancellationToken ct = default)
    {
        if (!_opt.EnableMultiAccountHeuristics) return;
        if (string.IsNullOrWhiteSpace(ipAddress) && string.IsNullOrWhiteSpace(deviceId)) return;

        string address = Hash(ipAddress);
        string device = Hash(deviceId);
        var now = DateTime.UtcNow;

        var existing = await _db.AccountSignals.FirstOrDefaultAsync(
            s => s.UserId == userId && s.AddressHash == address && s.DeviceHash == device, ct);

        if (existing is null)
        {
            _db.AccountSignals.Add(new AccountSignal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AddressHash = address,
                DeviceHash = device,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                SeenCount = 1,
            });
        }
        else
        {
            // Cooldown: this runs on every ranked request, and "seen again 20 seconds later" is not news.
            if (existing.LastSeenUtc.AddMinutes(_opt.SignalRefreshMinutes) > now) return;
            existing.LastSeenUtc = now;
            existing.SeenCount++;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyCollection<Guid>> LinkedUserIdsAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_opt.EnableMultiAccountHeuristics) return Array.Empty<Guid>();

        var mine = await _db.AccountSignals.Where(s => s.UserId == userId).ToListAsync(ct);
        if (mine.Count == 0) return Array.Empty<Guid>();

        var myAddresses = mine.Select(s => s.AddressHash).Where(h => h.Length > 0).Distinct().ToList();
        var myDevices = mine.Select(s => s.DeviceHash).Where(h => h.Length > 0).Distinct().ToList();

        // Everyone else ever seen from one of my addresses or devices — the candidate set.
        var candidates = await _db.AccountSignals
            .Where(s => s.UserId != userId
                        && (myAddresses.Contains(s.AddressHash) || myDevices.Contains(s.DeviceHash)))
            .ToListAsync(ct);
        if (candidates.Count == 0) return Array.Empty<Guid>();

        var candidateIds = candidates.Select(s => s.UserId).Distinct().ToList();
        var registered = await _db.Users
            .Where(u => candidateIds.Contains(u.Id) || u.Id == userId)
            .Select(u => new { u.Id, u.CreatedUtc })
            .ToDictionaryAsync(x => x.Id, x => x.CreatedUtc, ct);

        var weights = _opt.Weights();
        var mineRegistered = registered.TryGetValue(userId, out var myCreated) ? myCreated : DateTime.UtcNow;

        var linked = new HashSet<Guid>();
        foreach (var group in candidates.GroupBy(s => s.UserId))
        {
            bool sharedAddress = group.Any(s => s.AddressHash.Length > 0 && myAddresses.Contains(s.AddressHash));
            bool sharedDevice = group.Any(s => s.DeviceHash.Length > 0 && myDevices.Contains(s.DeviceHash));
            double minutesApart = registered.TryGetValue(group.Key, out var theirCreated)
                ? Math.Abs((theirCreated - mineRegistered).TotalMinutes)
                : double.MaxValue;

            if (LinkHeuristics.AreLinked(new LinkEvidence(sharedAddress, sharedDevice, minutesApart), weights))
                linked.Add(group.Key);
        }

        return linked;
    }

    // --- flags ---------------------------------------------------------------------------------

    public async Task<Guid> FlagAsync(
        IntegrityFlagKind kind,
        int severity,
        Guid? userId,
        Guid? subjectUserId,
        Guid? rankedGroupId,
        long fee,
        long marketValue,
        string details,
        CancellationToken ct = default)
    {
        var flag = new IntegrityFlag
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Status = IntegrityFlagStatus.Open,
            Severity = Math.Clamp(severity, 0, 100),
            UserId = userId,
            SubjectUserId = subjectUserId,
            RankedGroupId = rankedGroupId,
            Fee = fee,
            MarketValue = marketValue,
            Details = Truncate(details, 512),
            CreatedUtc = DateTime.UtcNow,
        };
        _db.IntegrityFlags.Add(flag);
        await _db.SaveChangesAsync(ct);
        return flag.Id;
    }

    public Task<int> CompletedTradesBetweenAsync(Guid groupId, Guid a, Guid b, CancellationToken ct = default) =>
        _db.RankedOffers.CountAsync(
            o => o.RankedGroupId == groupId
                 && o.Status == RankedOfferStatus.Accepted
                 && ((o.BuyerUserId == a && o.SellerUserId == b) || (o.BuyerUserId == b && o.SellerUserId == a)),
            ct);

    public async Task<IntegrityFlagsDto> GetFlagsAsync(
        IntegrityFlagStatus? status, int take, CancellationToken ct = default)
    {
        int limit = take <= 0 ? 100 : Math.Min(take, 500);
        var query = _db.IntegrityFlags.AsQueryable();
        if (status is { } s) query = query.Where(f => f.Status == s);

        int total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(f => f.CreatedUtc)
            .Take(limit)
            .ToListAsync(ct);

        return new IntegrityFlagsDto(total, rows.Select(f => new IntegrityFlagDto(
            f.Id, f.Kind, f.Status, f.Severity, f.UserId, f.SubjectUserId, f.RankedGroupId,
            f.Fee, f.MarketValue, f.Details, f.CreatedUtc)).ToList());
    }

    // --- player reports -------------------------------------------------------------------------

    public async Task<RankedResult<RankedReportDto>> ReportAsync(
        Guid userId, SubmitRankedReportRequest request, CancellationToken ct = default)
    {
        var coach = await _db.RankedCoaches.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (coach is null)
            return RankedResult<RankedReportDto>.Fail(RankedError.NotEnrolled, "You have not joined the ranked ladder.");
        if (coach.SeatId is not { } seatId)
            return RankedResult<RankedReportDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked seat.");

        var seat = await _db.RankedSeats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null)
            return RankedResult<RankedReportDto>.Fail(RankedError.WrongPhase, "You do not currently hold a ranked seat.");
        Guid groupId = seat.RankedGroupId;

        // Daily cap — a report button is itself a harassment vector.
        var since = DateTime.UtcNow.AddDays(-1);
        int filedToday = await _db.IntegrityFlags.CountAsync(
            f => f.Kind == IntegrityFlagKind.PlayerReport && f.UserId == userId && f.CreatedUtc >= since, ct);
        if (filedToday >= _opt.MaxReportsPerDay)
            return RankedResult<RankedReportDto>.Fail(
                RankedError.RateLimited, "You have filed too many reports today.");

        // Resolve the subject: by their club (what the client shows) or directly by account id.
        Guid? subjectUserId = request.SubjectUserId;
        if (subjectUserId is null && request.SubjectClubExternalId is { } clubExternalId)
        {
            var group = await _db.RankedGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
            var club = group?.WorldId is { } worldId
                ? await _db.Clubs.FirstOrDefaultAsync(c => c.WorldId == worldId && c.ExternalId == clubExternalId, ct)
                : null;
            if (club is not null)
            {
                subjectUserId = await _db.RankedSeats
                    .Where(s => s.RankedGroupId == groupId && s.ClubId == club.Id && s.UserId != null)
                    .Select(s => s.UserId)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (subjectUserId is null)
            return RankedResult<RankedReportDto>.Fail(
                RankedError.NotFound, "No such coach in your group.");
        if (subjectUserId == userId)
            return RankedResult<RankedReportDto>.Fail(
                RankedError.ValidationFailed, "You cannot report yourself.");

        // The reported coach must actually be in the caller's group: a report is about someone you play,
        // not a way to reach across the ladder.
        bool sameGroup = await _db.RankedSeats.AnyAsync(
            s => s.RankedGroupId == groupId && s.UserId == subjectUserId, ct);
        if (!sameGroup)
            return RankedResult<RankedReportDto>.Fail(RankedError.NotFound, "No such coach in your group.");

        string details = $"reason={request.Reason}; {Truncate(request.Details ?? string.Empty, _opt.MaxReportDetailsLength)}";
        var flagId = await FlagAsync(
            IntegrityFlagKind.PlayerReport, severity: 40, userId: userId, subjectUserId: subjectUserId,
            rankedGroupId: groupId, fee: 0, marketValue: 0, details: details, ct);

        return RankedResult<RankedReportDto>.Ok(new RankedReportDto(flagId, DateTime.UtcNow));
    }

    // --- helpers --------------------------------------------------------------------------------

    /// <summary>Salted SHA-256, hex, lower-case. Empty input → empty string (stored as "unknown device").</summary>
    private string Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(_opt.SignalSalt + "|" + value.Trim());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
