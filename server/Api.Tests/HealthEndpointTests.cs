using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace Fts.Api.Tests;

[TestFixture]
public class HealthEndpointTests
{
    // Run under the Testing environment so startup skips the auto-migration (no live DB in
    // a unit test). The liveness endpoint touches neither PostgreSQL nor Redis.
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Testing"));

    [Test]
    public async Task Health_ReturnsOk()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("\"status\":\"ok\""));
    }
}
