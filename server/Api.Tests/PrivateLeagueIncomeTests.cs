using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fts.Application.Auth;
using Fts.Application.Leagues;
using Fts.Infrastructure.Leagues;
using Fts.Infrastructure.Migrations;
using Fts.Infrastructure.Persistence;
using Fts.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Sim.Core.Config;
using Sim.Core.Market;
using EntClub = Fts.Infrastructure.Persistence.Entities.Club;
using SimClub = Sim.Core.Domain.Club;

namespace Fts.Api.Tests;

/// <summary>
/// Task: stature-based income AND wages for private leagues (R12 — spec
/// docs/specs/realistic-club-economy.md:109-112). Three checks: the <c>clubs</c> table gains
/// <see cref="EntClub.Stature"/> additively (a migration test, no DB needed); every private-league club
/// starts the draft with an equal budget regardless of stature (unchanged 8.2 behaviour — pinned here so
/// a future finance change cannot quietly break it); and every resolved round's balance delta equals that
/// round's stature-driven gate+sponsor income minus the squad's demanded wage bill (floored so bankruptcy
/// is impossible), with the season total confirming two differently-statured clubs end up earning
/// different gross income. Reuses <see cref="AuthTestFactory"/>.
/// </summary>
[TestFixture]
public class PrivateLeagueIncomeTests
{
    private AuthTestFactory _factory = null!;
    private HttpClient _client = null!;

    private const string ValidPassword = "Password1";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new AuthTestFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // --- the R12 checks -----------------------------------------------------------------------------

    [Test]
    public void AddClubStature_Migration_AddsAnAdditiveColumn()
    {
        var addColumn = new AddClubStature().UpOperations
            .OfType<AddColumnOperation>()
            .Single(op => op.Table == "clubs" && op.Name == "Stature");

        Assert.Multiple(() =>
        {
            Assert.That(addColumn.ClrType, Is.EqualTo(typeof(int)));
            Assert.That(addColumn.IsNullable, Is.False);
            Assert.That(addColumn.DefaultValue, Is.EqualTo(0),
                "additive: existing rows backfill to a neutral stature instead of requiring a manual fixup");
        });
    }

    [Test]
    public async Task AfterDraft_AllPrivateLeagueBudgets_AreEqual()
    {
        var (leagueId, _) = await CreateActiveLeague(size: 6);
        List<EntClub> clubs = await ClubsOf(leagueId);

        Assert.Multiple(() =>
        {
            Assert.That(clubs, Has.Count.EqualTo(6));
            Assert.That(clubs.Select(c => c.TransferBudget).Distinct().Count(), Is.EqualTo(1),
                "every club starts the draft with the same transfer budget, whatever its stature");
            Assert.That(clubs.Select(c => c.Balance).Distinct().Count(), Is.EqualTo(1),
                "every club starts with the same operating cash, whatever its stature");
            Assert.That(clubs.Select(c => c.Stature).Distinct().Count(), Is.GreaterThan(1),
                "sanity: the generated world actually spreads stature, else the revenue check below is vacuous");
        });
    }

    /// <summary>
    /// R12 end to end, on the real /advance path: every resolved round's balance delta equals that round's
    /// income (gate + sponsor, stature-driven) minus the squad's demanded wage bill
    /// (<see cref="LeagueMarketEngine.DemandedWage(Player,EntClub)"/> summed over the squad — the same
    /// formula R7 established for a real signing), floored so bankruptcy is impossible (mirroring
    /// Sim.Core's <c>FinanceProgressor.AccrueWeek</c>) — checked round by round, not season-aggregate,
    /// because a player's Overall (and so his demanded wage) legitimately drifts with the weekly
    /// development tick. A double round-robin gives every club an EQUAL number of home legs, so summing
    /// each round's income (not the net balance, which the two clubs' own — stature-independent, squad-
    /// strength-driven — wage bills would otherwise contaminate) over the whole season is what makes
    /// "the higher-stature club earns strictly more" deterministic however a single round's fixture list
    /// happens to pair them (one round alone can flip on home/away: a home low-stature gate can beat an
    /// away high-stature sponsor-only week).
    /// </summary>
    [Test]
    public async Task AfterEachResolvedRound_BalanceDelta_EqualsIncomeMinusWageBillFlooredAtMinBalance()
    {
        const int size = 4;
        var (leagueId, accounts) = await CreateActiveLeague(size);
        (Guid richId, Guid poorId) = await PinStatures(leagueId, rich: 90, poor: 10);

        var cfg = new BalanceConfig();
        var (richExt, poorExt) = await ExternalIdsOf(richId, poorId);
        long richBalance = await BalanceOf(richId);
        long poorBalance = await BalanceOf(poorId);
        long richTotalIncome = 0, poorTotalIncome = 0;

        int totalRounds = 2 * (size - 1);
        for (int round = 1; round <= totalRounds; round++)
        {
            LeagueSeasonDto season = await Advance(accounts[0].Tok, leagueId);
            bool richHome = season.Fixtures.Any(f => f.Round == round && f.HomeClubExternalId == richExt);
            bool poorHome = season.Fixtures.Any(f => f.Round == round && f.HomeClubExternalId == poorExt);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
            var engine = new LeagueMarketEngine(db, cfg);
            EntClub richClub = await db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == richId);
            EntClub poorClub = await db.Clubs.Include(c => c.Players).FirstAsync(c => c.Id == poorId);

            // Read the squad's Overall AFTER this round's development tick — the same values the wage
            // debit itself used, since PrivateLeagueFinance runs after that tick is persisted.
            long richWage = richClub.Players.Sum(p => engine.DemandedWage(p, richClub));
            long poorWage = poorClub.Players.Sum(p => engine.DemandedWage(p, poorClub));

            long richIncome = ExpectedRoundIncome(90, richHome, cfg);
            long poorIncome = ExpectedRoundIncome(10, poorHome, cfg);

            long expectedRich = Math.Max(cfg.Finance.MinBalance, richBalance + richIncome - richWage);
            long expectedPoor = Math.Max(cfg.Finance.MinBalance, poorBalance + poorIncome - poorWage);

            Assert.Multiple(() =>
            {
                Assert.That(richClub.Balance, Is.EqualTo(expectedRich), $"round {round}: high-stature club");
                Assert.That(poorClub.Balance, Is.EqualTo(expectedPoor), $"round {round}: low-stature club");
            });

            richBalance = richClub.Balance;
            poorBalance = poorClub.Balance;
            richTotalIncome += richIncome;
            poorTotalIncome += poorIncome;
        }

        Assert.That(richTotalIncome, Is.GreaterThan(poorTotalIncome),
            "over the whole season (equal home legs for both) the higher-stature club earns strictly more gross income");
    }

    private async Task<(Guid RichId, Guid PoorId)> PinStatures(Guid leagueId, int rich, int poor)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        var worldId = await db.PrivateLeagues.Where(l => l.Id == leagueId).Select(l => l.WorldId).FirstAsync();
        var clubs = await db.Clubs.Where(c => c.WorldId == worldId).OrderBy(c => c.ExternalId).ToListAsync();

        clubs[0].Stature = rich;
        clubs[1].Stature = poor;
        await db.SaveChangesAsync();
        return (clubs[0].Id, clubs[1].Id);
    }

    private async Task<(int Rich, int Poor)> ExternalIdsOf(Guid richId, Guid poorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        int rich = await db.Clubs.Where(c => c.Id == richId).Select(c => c.ExternalId).FirstAsync();
        int poor = await db.Clubs.Where(c => c.Id == poorId).Select(c => c.ExternalId).FirstAsync();
        return (rich, poor);
    }

    private async Task<long> BalanceOf(Guid clubId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        return await db.Clubs.Where(c => c.Id == clubId).Select(c => c.Balance).FirstAsync();
    }

    private static long ExpectedRoundIncome(int stature, bool isHome, BalanceConfig cfg)
    {
        var simClub = new SimClub { Stature = stature };
        long income = FinanceModel.WeeklySponsor(simClub, leagueLevel: 1, economicReputation: 100, cfg);
        if (isHome) income += FinanceModel.GateReceipts(simClub, leagueLevel: 1, economicReputation: 100, cfg);
        return income;
    }

    private async Task<List<EntClub>> ClubsOf(Guid leagueId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FtsDbContext>();
        var worldId = await db.PrivateLeagues.Where(l => l.Id == leagueId).Select(l => l.WorldId).FirstAsync();
        return await db.Clubs.Where(c => c.WorldId == worldId).ToListAsync();
    }

    // --- HTTP harness (trimmed copy of LeagueSeasonEndpointTests' helpers) -------------------------

    private static string UniqueEmail() => $"coach_{Guid.NewGuid():N}@example.com";

    private async Task<(string Tok, Guid Id)> RegisterAccount()
    {
        var resp = await _client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(UniqueEmail(), ValidPassword, "Mister"));
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.Profile.UserId);
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<LeagueDetailDto> CreateLeague(string accessToken, int size, string name = "Amici FC")
    {
        using var req = Authed(HttpMethod.Post, "/leagues", accessToken,
            new CreateLeagueRequest(name, size, LeagueMode.AllReady));
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "create league should succeed");
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<HttpResponseMessage> Join(string accessToken, string inviteCode)
    {
        using var req = Authed(HttpMethod.Post, "/leagues/join", accessToken, new JoinLeagueRequest(inviteCode));
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> StartDraft(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/draft/start", accessToken);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> Pick(string accessToken, Guid leagueId, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/draft/pick", accessToken,
            new PickClubRequest(clubExternalId));
        return await _client.SendAsync(req);
    }

    private async Task<LeagueDetailDto> GetDetail(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Get, $"/leagues/{leagueId}", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<LeagueDetailDto>())!;
    }

    private async Task<LeagueSeasonDto> Advance(string accessToken, Guid leagueId)
    {
        using var req = Authed(HttpMethod.Post, $"/leagues/{leagueId}/advance", accessToken);
        var resp = await _client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "advance should resolve a round");
        return (await resp.Content.ReadFromJsonAsync<LeagueSeasonDto>())!;
    }

    private async Task<(Guid LeagueId, List<(string Tok, Guid Id)> Accounts)> CreateActiveLeague(int size)
    {
        var (creatorTok, creatorId) = await RegisterAccount();
        var created = await CreateLeague(creatorTok, size);
        var accounts = new List<(string Tok, Guid Id)> { (creatorTok, creatorId) };
        for (int i = 0; i < size - 1; i++)
        {
            var acc = await RegisterAccount();
            Assert.That((await Join(acc.Tok, created.League.InviteCode)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            accounts.Add(acc);
        }

        Assert.That((await StartDraft(creatorTok, created.League.Id)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var taken = new HashSet<int>();
        for (int pick = 0; pick < size; pick++)
        {
            var state = await GetDetail(creatorTok, created.League.Id);
            var picker = accounts.First(a => a.Id == state.Draft.CurrentPickUserId!.Value);
            int club = state.Clubs.First(c => !taken.Contains(c.ExternalId)).ExternalId;
            Assert.That((await Pick(picker.Tok, created.League.Id, club)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            taken.Add(club);
        }

        return (created.League.Id, accounts);
    }
}
