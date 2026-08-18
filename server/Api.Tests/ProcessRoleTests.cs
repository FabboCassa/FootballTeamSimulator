using System.Net;
using Fts.Infrastructure.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// The api/worker process split (Roadmap 10.4). Two things are worth a test and nothing else is:
/// that the DEFAULT is still "one process does everything" (so nothing about a dev run or the other
/// ~182 tests changed), and that a worker-role process really does refuse to be an API — the whole
/// point of splitting the scheduler out is that it stops competing with player traffic, and a worker
/// that quietly still served /auth would not be split at all.
///
/// Everything runs under the Testing environment: no live PostgreSQL, so the Hangfire server is off in
/// every role anyway and what is under test here is purely the MAPPING decision.
/// </summary>
[TestFixture]
public class ProcessRoleTests
{
    private static WebApplicationFactory<Program> CreateFactory(string? role) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            if (role is not null) b.UseSetting("Jobs:Role", role);
        });

    // ---- the reader itself: a pure function, so test it directly rather than through a host --------

    [Test]
    public void Role_DefaultsToBoth_WhenUnset()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.That(JobsRoleReader.Read(config), Is.EqualTo(ProcessRole.Both));
    }

    [TestCase("api", ProcessRole.Api)]
    [TestCase("API", ProcessRole.Api)]
    [TestCase(" worker ", ProcessRole.Worker)]
    [TestCase("both", ProcessRole.Both)]
    [TestCase("", ProcessRole.Both)]
    public void Role_IsParsedCaseAndWhitespaceInsensitively(string configured, ProcessRole expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Role"] = configured })
            .Build();

        Assert.That(JobsRoleReader.Read(config), Is.EqualTo(expected));
    }

    /// <summary>A typo must NOT degrade to "both": that would put a second scheduler on the ladder,
    /// racing the minutely calendar tick — the exact failure this setting exists to prevent.</summary>
    [Test]
    public void Role_Unrecognised_ThrowsAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Role"] = "wroker" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => JobsRoleReader.Read(config));
        Assert.That(ex!.Message, Does.Contain("Jobs:Role"));
        Assert.That(ex.Message, Does.Contain("wroker"));
    }

    // ---- what each role actually maps --------------------------------------------------------------

    [Test]
    public async Task Default_ServesThePlayerSurface_AndReportsRoleBoth()
    {
        await using var factory = CreateFactory(null);
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await health.Content.ReadAsStringAsync(), Does.Contain("\"role\":\"both\""));

        // Mapped and JWT-protected ⇒ 401, which is the proof that it IS mapped (an unmapped route is 404).
        var me = await client.GetAsync("/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ApiRole_ServesThePlayerSurface()
    {
        await using var factory = CreateFactory("api");
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await health.Content.ReadAsStringAsync(), Does.Contain("\"role\":\"api\""));

        var me = await client.GetAsync("/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>THE ✅: a worker answers the probes and NOTHING else.</summary>
    [Test]
    public async Task WorkerRole_MapsHealthOnly()
    {
        await using var factory = CreateFactory("worker");
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await health.Content.ReadAsStringAsync(), Does.Contain("\"role\":\"worker\""));

        foreach (var path in new[] { "/auth/me", "/leagues", "/ranked/me", "/admin/metrics" })
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                $"{path} must not be mapped on a worker-role process.");
        }
    }
}
