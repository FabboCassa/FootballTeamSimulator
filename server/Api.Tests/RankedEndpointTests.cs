using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Ranked;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Fts.Api.Tests;

/// <summary>
/// Shared helpers for the public ranked ladder tests (Phase 9.1). The pyramid is shrunk via config
/// (4-club groups, one group per tier ⇒ 8 placeable seats per world) so spillover across several
/// server-managed worlds can be exercised without generating hundreds of players. Each fixture owns its
/// own <see cref="AuthTestFactory"/> (a private in-memory SQLite DB) so global ladder state — worlds,
/// placement groups — does not leak between fixtures.
/// </summary>
public abstract class RankedTestBase
{
    protected const string ValidPassword = "Password1";

    // 4-club groups, one group per tier ⇒ placeable seats per world = tier2 (4) + tier3 (4) = 8.
    protected const int GroupSize = 4;

    protected AuthTestFactory Factory = null!;
    protected HttpClient Client = null!;

    [OneTimeSetUp]
    public void BaseOneTimeSetUp()
    {
        Factory = new AuthTestFactory();
        var shrunk = Factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Ranked:GroupSize"] = GroupSize.ToString(),
                    ["Ranked:PlacementGroupSize"] = GroupSize.ToString(),
                    ["Ranked:Tier1Groups"] = "1",
                    ["Ranked:Tier2Groups"] = "1",
                    ["Ranked:Tier3Groups"] = "1",
                    ["Ranked:PlacementTopPositionsToUpperTier"] = "2",
                })));
        Client = shrunk.CreateClient();
    }

    [OneTimeTearDown]
    public void BaseOneTimeTearDown()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    protected async Task<(string Token, Guid UserId)> RegisterAccount()
    {
        var resp = await Client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.Profile.UserId);
    }

    protected HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    protected async Task<RankedStateDto> Enrol(string token)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/enrol", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "enrol should succeed");
        return (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
    }

    protected async Task<RankedStateDto> GetMine(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/me", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
    }

    protected async Task<RankedGroupDto> GetGroup(string token, Guid groupId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/groups/{groupId}", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedGroupDto>())!;
    }

    protected async Task<PlacementResultDto> ResolvePlacement(Guid groupId)
    {
        // The internal endpoint is unauthenticated + dev-gated (mapped under the Testing environment).
        var resp = await Client.PostAsJsonAsync(
            $"/internal/ranked/placement/{groupId}/resolve", new ResolvePlacementRequest(null));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "resolve placement should succeed");
        return (await resp.Content.ReadFromJsonAsync<PlacementResultDto>())!;
    }
}

/// <summary>
/// Behaviour of the ranked endpoints (Phase 9.1): auth, the not-enrolled state, idempotent enrol, the
/// fixed-size seat list, auto-enrol toggle and the resolve-twice guard.
/// </summary>
[TestFixture]
public class RankedEndpointTests : RankedTestBase
{
    [Test]
    public async Task Enrol_WithoutToken_ReturnsUnauthorized()
    {
        var resp = await Client.PostAsync("/ranked/enrol", content: null);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetMine_BeforeEnrolling_ReportsNotEnrolled()
    {
        var (token, _) = await RegisterAccount();
        var state = await GetMine(token);
        Assert.That(state.Enrolled, Is.False, "a fresh account is not on the ladder");
    }

    [Test]
    public async Task Enrol_SeatsInAPlacementGroup_AndIsIdempotent()
    {
        var (token, _) = await RegisterAccount();

        var first = await Enrol(token);
        Assert.Multiple(() =>
        {
            Assert.That(first.Enrolled, Is.True);
            Assert.That(first.Status, Is.EqualTo(RankedCoachStatus.Placement), "newcomers start in placement");
            Assert.That(first.Kind, Is.EqualTo(RankedGroupKind.Placement));
            Assert.That(first.GroupId, Is.Not.Null);
            Assert.That(first.ClubExternalId, Is.Not.Null, "the placement group is materialised with clubs");
        });

        var again = await Enrol(token);
        Assert.That(again.GroupId, Is.EqualTo(first.GroupId), "enrolling twice is idempotent");

        var grp = await GetGroup(token, first.GroupId!.Value);
        Assert.That(grp.Capacity, Is.EqualTo(GroupSize));
        Assert.That(grp.Seats, Has.Count.EqualTo(GroupSize), "seats are created up front and fixed");
        Assert.That(grp.Seats.Count(s => s.IsYou), Is.EqualTo(1));
    }

    [Test]
    public async Task SetAutoEnrol_TogglesTheFlag()
    {
        var (token, _) = await RegisterAccount();
        await Enrol(token);

        using var req = Authed(HttpMethod.Post, "/ranked/auto-enrol", token, new SetAutoEnrolRequest(false));
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var state = (await resp.Content.ReadFromJsonAsync<RankedStateDto>())!;
        Assert.That(state.AutoEnrol, Is.False);
    }

    [Test]
    public async Task GetGroup_UnknownId_ReturnsNotFound()
    {
        var (token, _) = await RegisterAccount();
        using var req = Authed(HttpMethod.Get, $"/ranked/groups/{Guid.NewGuid()}", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ResolvePlacement_Twice_IsRejected()
    {
        var (token, _) = await RegisterAccount();
        var state = await Enrol(token);
        var groupId = state.GroupId!.Value;

        // Resolving is idempotent-safe only once: the group's occupants are sorted, then it is Completed.
        var occupiedBefore = (await GetGroup(token, groupId)).Occupied;
        var first = await ResolvePlacement(groupId);
        Assert.That(first.Assignments, Has.Count.EqualTo(occupiedBefore));

        var resp = await Client.PostAsJsonAsync(
            $"/internal/ranked/placement/{groupId}/resolve", new ResolvePlacementRequest(null));
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "a resolved placement cannot be resolved again");
    }
}

/// <summary>
/// THE 9.1 ✅ in its own fixture (private DB): 40 accounts enrol, spill across multiple server-managed
/// worlds, and once their placement seasons resolve every one is sorted into a fixed-size division.
/// </summary>
[TestFixture]
public class RankedPlacementSpilloverTests : RankedTestBase
{
    [Test]
    public async Task FortyAccounts_AllGetPlaced_DivisionsStayFixedSize_AndSpillOverIntoNewWorlds()
    {
        const int accountCount = 40;
        var tokens = new List<string>(accountCount);
        var placementGroups = new HashSet<Guid>();
        var placementWorlds = new HashSet<Guid>();

        for (int i = 0; i < accountCount; i++)
        {
            var (token, _) = await RegisterAccount();
            tokens.Add(token);
            var state = await Enrol(token);

            Assert.That(state.Enrolled, Is.True);
            Assert.That(state.Status, Is.EqualTo(RankedCoachStatus.Placement));
            Assert.That(state.GroupId, Is.Not.Null);
            Assert.That(state.RankedWorldId, Is.Not.Null);
            placementGroups.Add(state.GroupId!.Value);
            placementWorlds.Add(state.RankedWorldId!.Value);
        }

        // Spillover: 8 placeable seats per world ⇒ 40 accounts cannot fit in one world.
        Assert.That(placementWorlds.Count, Is.GreaterThan(1),
            "enrolment spills across multiple server-managed worlds");
        Assert.That(placementGroups.Count, Is.EqualTo(accountCount / GroupSize),
            "each placement group holds exactly one cohort");

        foreach (var groupId in placementGroups)
        {
            var grp = await GetGroup(tokens[0], groupId);
            Assert.That(grp.Occupied, Is.EqualTo(GroupSize), "a placement group fills before a new one opens");
            Assert.That(grp.Status, Is.EqualTo(RankedGroupStatus.Active));
        }

        // Resolve every placement season → sort coaches into divisions.
        var divisionGroups = new HashSet<Guid>();
        foreach (var groupId in placementGroups)
        {
            var result = await ResolvePlacement(groupId);
            Assert.That(result.Assignments, Has.Count.EqualTo(GroupSize));
            foreach (var a in result.Assignments)
            {
                Assert.That(a.Tier, Is.AnyOf(2, 3), "placement sorts into tier 2 or tier 3, never tier 1");
                Assert.That(a.ClubExternalId, Is.GreaterThan(0), "a placed coach takes over a real club");
                divisionGroups.Add(a.GroupId);
            }
        }

        // Every one of the 40 is now Placed with a division seat and a seeded rating.
        foreach (var token in tokens)
        {
            var state = await GetMine(token);
            Assert.That(state.Status, Is.EqualTo(RankedCoachStatus.Placed), "everyone is placed");
            Assert.That(state.Tier, Is.AnyOf(2, 3));
            Assert.That(state.ClubExternalId, Is.Not.Null);
            Assert.That(state.Rating, Is.GreaterThan(0));
        }

        // Divisions stay at fixed size: exactly their seats, never over-filled.
        foreach (var groupId in divisionGroups)
        {
            var grp = await GetGroup(tokens[0], groupId);
            Assert.That(grp.Capacity, Is.EqualTo(GroupSize));
            Assert.That(grp.Seats, Has.Count.EqualTo(GroupSize), "the division always has exactly its seats");
            Assert.That(grp.Occupied, Is.LessThanOrEqualTo(GroupSize), "never over-filled");
            Assert.That(grp.Seats.Select(s => s.SeatIndex).Distinct().Count(), Is.EqualTo(GroupSize));
        }
    }
}
