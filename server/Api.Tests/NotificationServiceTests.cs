using Fts.Application.Notifications;
using Fts.Infrastructure.Auth;
using Fts.Infrastructure.Jobs;
using Fts.Infrastructure.Notifications;
using Fts.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Unit tests for the notification pieces (Phase 7.4) that don't need the HTTP host: the FCM sender's
/// unconfigured (dev/CI) behaviour — it must never throw, it skips when FCM is off and reports no-devices
/// when the account has none — plus the device store upsert and the heartbeat job being invocable. Each
/// test uses its own in-memory SQLite database.
/// </summary>
[TestFixture]
public class NotificationServiceTests
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<FtsDbContext> _options = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FtsDbContext>().UseSqlite(_connection).Options;

        using var db = new FtsDbContext(_options);
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private FtsDbContext NewContext() => new(_options);

    private static FirebaseNotificationService NewDisabledService(FtsDbContext db) =>
        new(db, Options.Create(new FcmOptions { Enabled = false }),
            NullLogger<FirebaseNotificationService>.Instance);

    private async Task<Guid> SeedUserAsync(FtsDbContext db)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = userId,
            UserName = $"u{userId:N}",
            NormalizedUserName = $"U{userId:N}".ToUpperInvariant(),
            Email = $"{userId:N}@example.com",
            NormalizedEmail = $"{userId:N}@EXAMPLE.COM".ToUpperInvariant(),
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Test]
    public async Task Send_WithNoDevices_ReturnsNoDevices()
    {
        await using var db = NewContext();
        var userId = await SeedUserAsync(db);
        var service = NewDisabledService(db);

        var result = await service.SendToUserAsync(userId, new PushMessage("Hi", "Body"));

        Assert.That(result.Outcome, Is.EqualTo(PushOutcome.NoDevices));
        Assert.That(service.IsConfigured, Is.False);
    }

    [Test]
    public async Task Send_WithDevicesButFcmDisabled_SkipsWithoutThrowing()
    {
        await using var db = NewContext();
        var userId = await SeedUserAsync(db);
        var devices = new DeviceRegistrationService(db);
        await devices.RegisterAsync(userId, new RegisterDeviceRequest("token-abc", DevicePlatform.Android));

        var service = NewDisabledService(db);
        var result = await service.SendToUserAsync(userId, new PushMessage("Hi", "Body"));

        Assert.That(result.Outcome, Is.EqualTo(PushOutcome.Skipped));
    }

    [Test]
    public async Task RegisterDevice_SameToken_ReOwnsAndDoesNotDuplicate()
    {
        await using var db = NewContext();
        var userA = await SeedUserAsync(db);
        var userB = await SeedUserAsync(db);
        var devices = new DeviceRegistrationService(db);

        await devices.RegisterAsync(userA, new RegisterDeviceRequest("shared-token", DevicePlatform.Android));
        await devices.RegisterAsync(userB, new RegisterDeviceRequest("shared-token", DevicePlatform.Ios));

        Assert.That(await db.DeviceRegistrations.CountAsync(d => d.Token == "shared-token"), Is.EqualTo(1));
        Assert.That(await devices.ListAsync(userA), Is.Empty, "the token was re-owned by user B");
        var forB = await devices.ListAsync(userB);
        Assert.That(forB.Count, Is.EqualTo(1));
        Assert.That(forB[0].Platform, Is.EqualTo(DevicePlatform.Ios));
    }

    [Test]
    public async Task HeartbeatJob_Executes_WithoutThrowing()
    {
        var job = new HeartbeatJob(NullLogger<HeartbeatJob>.Instance);
        Assert.DoesNotThrowAsync(() => job.ExecuteAsync());
        await Task.CompletedTask;
    }
}
