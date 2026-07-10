using Fts.Application.Notifications;
using Fts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fts.Infrastructure.Notifications;

/// <summary>
/// EF-backed device-token store (Phase 7.4). Registration is an upsert keyed by the globally-unique
/// token: a known token is re-pointed at the current account and its platform/last-seen refreshed,
/// so a reinstalled app or a device that changed hands never leaves a stale row targeting the wrong
/// user. All timestamps are UTC.
/// </summary>
public sealed class DeviceRegistrationService : IDeviceRegistrationService
{
    private readonly FtsDbContext _db;

    public DeviceRegistrationService(FtsDbContext db) => _db = db;

    public async Task<DeviceRegistrationDto> RegisterAsync(
        Guid userId, RegisterDeviceRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ArgumentException("Device token is required.", nameof(request));

        var now = DateTime.UtcNow;
        var existing = await _db.DeviceRegistrations
            .SingleOrDefaultAsync(d => d.Token == request.Token, ct);

        if (existing is null)
        {
            existing = new DeviceRegistration
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                CreatedUtc = now,
                LastSeenUtc = now
            };
            _db.DeviceRegistrations.Add(existing);
        }
        else
        {
            // Re-own + refresh (upsert). The token stays the same physical device.
            existing.UserId = userId;
            existing.Platform = request.Platform;
            existing.LastSeenUtc = now;
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(existing);
    }

    public async Task<bool> UnregisterAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var row = await _db.DeviceRegistrations
            .SingleOrDefaultAsync(d => d.Token == token && d.UserId == userId, ct);
        if (row is null) return false;

        _db.DeviceRegistrations.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DeviceRegistrationDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.DeviceRegistrations
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenUtc)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private static DeviceRegistrationDto ToDto(DeviceRegistration d) =>
        new(d.Id, d.Platform, d.CreatedUtc, d.LastSeenUtc);
}
