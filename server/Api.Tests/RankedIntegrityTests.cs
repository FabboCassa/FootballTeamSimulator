using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fts.Application.Auth;
using Fts.Application.Integrity;
using Fts.Application.Ranked;
using NUnit.Framework;
using Sim.Core.Match;

namespace Fts.Api.Tests;

/// <summary>
/// The collusion price bands (Phase 9.5) as pure maths — no host, no database. These are the rules the
/// market guard enforces, so they are pinned here where a failure points straight at the model instead of
/// at a transfer flow.
/// </summary>
[TestFixture]
public class TransferIntegrityTests
{
    private static readonly TransferBands Bands = new(
        MinPercent: 40, MaxPercent: 250, SuspiciousLowPercent: 65, SuspiciousHighPercent: 160,
        MinValueChecked: 250_000);

    [Test]
    public void AFairFee_Passes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TransferIntegrity.Assess(10_000_000, 10_000_000, Bands).Verdict,
                Is.EqualTo(TransferVerdict.Fair), "market value itself is obviously fair");
            Assert.That(TransferIntegrity.Assess(7_000_000, 10_000_000, Bands).Verdict,
                Is.EqualTo(TransferVerdict.Fair), "a 30% discount is a negotiation, not collusion");
            Assert.That(TransferIntegrity.Assess(15_000_000, 10_000_000, Bands).Verdict,
                Is.EqualTo(TransferVerdict.Fair), "a 50% premium for a wanted player is normal");
        });
    }

    [Test]
    public void AGiftedStar_IsBlocked()
    {
        var verdict = TransferIntegrity.Assess(100_000, 40_000_000, Bands);
        Assert.Multiple(() =>
        {
            Assert.That(verdict.Verdict, Is.EqualTo(TransferVerdict.Blocked));
            Assert.That(verdict.FeePercentOfValue, Is.EqualTo(0), "0% of value — a gift, not a transfer");
            Assert.That(TransferIntegrity.Assess(0, 40_000_000, Bands).IsBlocked, Is.True, "and so is a free one");
        });
    }

    [Test]
    public void AWildOverpay_IsBlocked()
    {
        var verdict = TransferIntegrity.Assess(25_000_000, 1_000_000, Bands);
        Assert.Multiple(() =>
        {
            Assert.That(verdict.Verdict, Is.EqualTo(TransferVerdict.Blocked));
            Assert.That(verdict.FeePercentOfValue, Is.EqualTo(2500), "2500% of value — a budget transfer");
        });
    }

    [Test]
    public void TheGreyBand_IsFlaggedButAllowed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TransferIntegrity.Assess(5_000_000, 10_000_000, Bands).Verdict,
                Is.EqualTo(TransferVerdict.Suspicious), "half price is allowed but worth logging");
            Assert.That(TransferIntegrity.Assess(20_000_000, 10_000_000, Bands).Verdict,
                Is.EqualTo(TransferVerdict.Suspicious), "double price likewise");
        });
    }

    [Test]
    public void CheapPlayers_AreNotPoliced()
    {
        // Below the valuation floor the percentages swing wildly and there is nothing worth farming.
        Assert.That(TransferIntegrity.Assess(25_000, 100_000, Bands).Verdict, Is.EqualTo(TransferVerdict.Fair));
        Assert.That(TransferIntegrity.Assess(1, 0, Bands).Verdict, Is.EqualTo(TransferVerdict.Fair),
            "an unpriced player cannot be judged, so he is not judged");
    }
}

/// <summary>
/// The multi-account scoring rules (Phase 9.5) as pure maths. The important one is the NEGATIVE case: a
/// shared address on its own must not clear the threshold, because a household, a student flat and a mobile
/// carrier's NAT all look exactly like that.
/// </summary>
[TestFixture]
public class LinkHeuristicsTests
{
    private static readonly LinkWeights Weights = LinkHeuristics.Default;

    [Test]
    public void ASharedAddressAlone_IsNotEnough()
    {
        var evidence = new LinkEvidence(SharedAddress: true, SharedDevice: false, MinutesApartAtRegistration: 5_000);
        Assert.Multiple(() =>
        {
            Assert.That(LinkHeuristics.Score(evidence, Weights), Is.EqualTo(50));
            Assert.That(LinkHeuristics.AreLinked(evidence, Weights), Is.False,
                "two flatmates are not one cheater");
        });
    }

    [Test]
    public void ASharedDevice_IsEnoughOnItsOwn()
    {
        var evidence = new LinkEvidence(SharedAddress: false, SharedDevice: true, MinutesApartAtRegistration: 5_000);
        Assert.That(LinkHeuristics.AreLinked(evidence, Weights), Is.True);
    }

    [Test]
    public void SharedAddress_PlusAJointSignup_Links()
    {
        var evidence = new LinkEvidence(SharedAddress: true, SharedDevice: false, MinutesApartAtRegistration: 3);
        Assert.Multiple(() =>
        {
            Assert.That(LinkHeuristics.Score(evidence, Weights), Is.EqualTo(70));
            Assert.That(LinkHeuristics.AreLinked(evidence, Weights), Is.True);
        });
    }

    [Test]
    public void NoEvidence_ScoresZero()
    {
        var evidence = new LinkEvidence(SharedAddress: false, SharedDevice: false, MinutesApartAtRegistration: 0);
        Assert.That(LinkHeuristics.Score(evidence, Weights), Is.EqualTo(0),
            "registering at the same moment from different places is a coincidence");
    }
}

/// <summary>
/// THE 9.5 ✅, part one: scripted abuse against the live ladder is blocked or flagged. A full placement
/// cohort fills one all-human group (so any two coaches can trade), then we try the things a pair of
/// friends would actually try.
/// </summary>
[TestFixture]
public class RankedIntegrityTests : RankedSeasonTestBase
{
    protected async Task<RankedSquadDto> Squad(string token, int clubExternalId)
    {
        using var req = Authed(HttpMethod.Get, $"/ranked/clubs/{clubExternalId}/squad", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedSquadDto>())!;
    }

    protected async Task<RankedOffersDto> Offers(string token)
    {
        using var req = Authed(HttpMethod.Get, "/ranked/offers", token);
        var resp = await Client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await resp.Content.ReadFromJsonAsync<RankedOffersDto>())!;
    }

    protected async Task<HttpResponseMessage> MakeOffer(string token, int playerExternalId, long fee)
    {
        using var req = Authed(HttpMethod.Post, "/ranked/offers", token,
            new MakeRankedOfferRequest(playerExternalId, fee));
        return await Client.SendAsync(req);
    }

    protected async Task<IntegrityFlagsDto> Flags()
    {
        var resp = await Client.GetAsync("/internal/ranked/integrity/flags");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the review queue is dev-mapped under Testing");
        return (await resp.Content.ReadFromJsonAsync<IntegrityFlagsDto>())!;
    }

    /// <summary>Enrol a full cohort and tick once: the season starts, budgets are seeded and a window opens.</summary>
    protected async Task<List<string>> StartMarket()
    {
        var tokens = await EnrolCohort();
        await Tick();
        Assert.That((await Offers(tokens[0])).MarketOpen, Is.True, "the market window opens when the season starts");
        return tokens;
    }

    /// <summary>The seller's most valuable player — the one a friend would want gifted.</summary>
    private static RankedPlayerDto Star(RankedSquadDto squad) =>
        squad.Players.OrderByDescending(p => p.MarketValue).ThenBy(p => p.ExternalId).First();

    [Test]
    public async Task LopsidedTransfers_AreRefused_AndFlaggedForReview()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1];
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;
        var star = Star(await Squad(buyer, sellerClub));

        Assert.That(star.MarketValue, Is.GreaterThan(1_000_000),
            "the guard needs a properly valued player to police — a ranked club's best is worth millions");

        // 1) The gift: a star for pocket change.
        var gift = await MakeOffer(buyer, star.ExternalId, star.MarketValue / 100);
        // 2) The laundered budget: a wild overpay for someone cheap (still inside the 25M budget, so the
        //    budget guard cannot be what refuses it).
        var cheap = (await Squad(buyer, sellerClub)).Players
            .Where(p => p.MarketValue > 250_000 && p.MarketValue * 4 < 25_000_000)
            .OrderBy(p => p.MarketValue)
            .FirstOrDefault();
        Assert.That(cheap, Is.Not.Null, "a squad should have a policed player cheap enough to overpay for");
        var overpay = await MakeOffer(buyer, cheap!.ExternalId, cheap.MarketValue * 4);

        var flags = await Flags();
        var blocked = flags.Flags.Where(f => f.Kind == IntegrityFlagKind.BlockedTransfer).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(gift.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "a gifted star is refused");
            Assert.That(overpay.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "so is a wild overpay");
            Assert.That(blocked, Has.Count.EqualTo(2), "both refusals are on the review queue");
            Assert.That(blocked.All(f => f.Severity == 100), Is.True);
            Assert.That(blocked.All(f => f.MarketValue > 0), Is.True, "the reviewer sees what the player was worth");
        });

        // And nothing moved: the seller still owns his star.
        var sellerSquadAfter = await Squad(buyer, sellerClub);
        Assert.That(sellerSquadAfter.Players.Any(p => p.ExternalId == star.ExternalId), Is.True,
            "the refused deal did not quietly go through");
    }

    [Test]
    public async Task AGreyBandTransfer_GoesThrough_ButIsFlagged()
    {
        var tokens = await StartMarket();
        string buyer = tokens[0], seller = tokens[1];
        int sellerClub = (await GetMine(seller)).ClubExternalId!.Value;

        // A player worth policing whose half-price fee still fits the budget.
        var target = (await Squad(buyer, sellerClub)).Players
            .Where(p => p.MarketValue >= 500_000 && p.MarketValue <= 20_000_000)
            .OrderByDescending(p => p.MarketValue)
            .FirstOrDefault();
        Assert.That(target, Is.Not.Null, "a generated squad should have a mid-priced player to trade");

        long halfPrice = target!.MarketValue / 2; // ~50% → inside the hard band, inside the grey band
        var made = await MakeOffer(buyer, target.ExternalId, halfPrice);
        Assert.That(made.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a bargain is a bargain, not a crime");

        var offerId = (await Offers(seller)).Incoming.Single(o => o.Status == RankedOfferStatus.Pending).Id;
        using var acceptReq = Authed(HttpMethod.Post, $"/ranked/offers/{offerId}/accept", seller);
        var accepted = await Client.SendAsync(acceptReq);

        var flags = await Flags();
        Assert.Multiple(() =>
        {
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the deal completes");
            Assert.That(flags.Flags.Any(f => f.Kind == IntegrityFlagKind.SuspiciousTransfer), Is.True,
                "…and lands on the review queue");
            Assert.That(flags.Flags.Any(f => f.Kind == IntegrityFlagKind.BlockedTransfer), Is.False,
                "nothing was refused");
        });
    }

    [Test]
    public async Task AReport_IsFiled_AndCannotTargetYourself()
    {
        var tokens = await StartMarket();
        string reporter = tokens[0], subject = tokens[1];
        int subjectClub = (await GetMine(subject)).ClubExternalId!.Value;
        int ownClub = (await GetMine(reporter)).ClubExternalId!.Value;

        using var reqSelf = Authed(HttpMethod.Post, "/ranked/report", reporter,
            new SubmitRankedReportRequest(ownClub, null, RankedReportReason.Collusion, "me"));
        var self = await Client.SendAsync(reqSelf);

        using var reqOk = Authed(HttpMethod.Post, "/ranked/report", reporter,
            new SubmitRankedReportRequest(subjectClub, null, RankedReportReason.Collusion, "gifted a star away"));
        var filed = await Client.SendAsync(reqOk);

        var flags = await Flags();
        Assert.Multiple(() =>
        {
            Assert.That(self.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "you cannot report yourself");
            Assert.That(filed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(flags.Flags.Count(f => f.Kind == IntegrityFlagKind.PlayerReport), Is.EqualTo(1),
                "exactly the one real report is queued");
        });
    }

    [Test]
    public async Task ALineupSubmittedAfterKickoff_IsRefused()
    {
        var tokens = await StartMarket(); // compressed calendar → the next matchday is always already due
        string coach = tokens[0];

        var body = await StoredLineupBody(Client, Authed, coach);
        using var req = Authed(HttpMethod.Post, "/ranked/lineup", coach, body);
        var resp = await Client.SendAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "kickoff has passed, so the stored lineup is the one that plays");
    }

    /// <summary>Reads the coach's stored (seeded) lineup and hands it back as a submit body. Sim.Core writes
    /// enums as names in the stored JSON, the Api binds them as numbers — so the plan is round-tripped
    /// through <see cref="LineupPlan"/> rather than pasted as raw JSON.</summary>
    internal static async Task<object> StoredLineupBody(
        HttpClient client, Func<HttpMethod, string, string, object?, HttpRequestMessage> authed, string token)
    {
        using var req = authed(HttpMethod.Get, "/ranked/lineup", token, null);
        var resp = await client.SendAsync(req);
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string json = await resp.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var plan = JsonSerializer.Deserialize<LineupPlan>(json, options);
        Assert.That(plan?.Slots, Is.Not.Null.And.Not.Empty,
            "the season start seeds every human seat a best-XI lineup (9.4)");

        return new { lineup = plan, tactic = (object?)null, plan = (object?)null };
    }
}

/// <summary>
/// The same deadline, from the other side: with a REAL gap between matchdays the next kickoff is still in
/// the future, so submitting a lineup is exactly as allowed as it was before 9.5. Without this the guard
/// could be "working" simply by refusing everything.
/// </summary>
[TestFixture]
public class RankedLineupDeadlineOpenTests : RankedSeasonTestBase
{
    protected override int MatchdayIntervalSeconds => 3600; // matchday 2 kicks off in an hour

    [Test]
    public async Task ALineupSubmittedBeforeKickoff_IsAccepted()
    {
        var tokens = await EnrolCohort();
        await Tick(); // the season starts and matchday 1 (due at once) resolves; matchday 2 is an hour away

        var body = await RankedIntegrityTests.StoredLineupBody(Client, Authed, tokens[0]);
        using var req = Authed(HttpMethod.Post, "/ranked/lineup", tokens[0], body);
        var resp = await Client.SendAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "there is still time before the next kickoff, so the coach may pick his team");
    }
}

/// <summary>
/// THE 9.5 ✅, part two: bid spam. The limits are tightened to 3 bids per minute for this fixture so the
/// assertion is deterministic instead of racing a real 10-second window.
/// </summary>
[TestFixture]
public class RankedBidSpamTests : RankedSeasonTestBase
{
    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        settings["Integrity:BidsPerWindow"] = "3";
        settings["Integrity:RateWindowSeconds"] = "60";
    }

    [Test]
    public async Task BidSpam_IsRateLimited()
    {
        var tokens = await EnrolCohort();
        await Tick(); // season starts → market window opens → free-agent lots are created

        using var lotsReq = Authed(HttpMethod.Get, "/ranked/auctions", tokens[0]);
        var lots = (await (await Client.SendAsync(lotsReq)).Content.ReadFromJsonAsync<RankedAuctionsDto>())!;
        Assert.That(lots.Lots, Is.Not.Empty, "a window opening seeds free-agent lots");
        var lot = lots.Lots.OrderBy(l => l.StartPrice).First(); // the cheapest lot is affordable

        int rejected = 0, served = 0;
        for (int i = 0; i < 10; i++)
        {
            using var bid = Authed(HttpMethod.Post, $"/ranked/auctions/{lot.Id}/bid", tokens[0],
                new PlaceRankedBidRequest(lot.MinNextBid));
            var resp = await Client.SendAsync(bid);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) rejected++; else served++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(served, Is.LessThanOrEqualTo(3), "only the permitted bids reach the auction service");
            Assert.That(rejected, Is.GreaterThanOrEqualTo(7), "the rest are refused by the rate limiter");
        });
    }
}

/// <summary>
/// THE 9.5 ✅, part three: two accounts that look like the same person are not seated together. They both
/// enrol normally — nobody is refused — but the ladder puts them in different cohorts, which is where the
/// mutual point-feeding would otherwise happen.
/// </summary>
[TestFixture]
public class RankedMultiAccountTests : RankedSeasonTestBase
{
    private const string SharedDevice = "device-shared-by-both-accounts";

    /// <summary>The one fixture that runs WITH the heuristics on (the shared base disables them so the
    /// other fixtures' cohorts are not scattered — see the base for why).</summary>
    protected override void ConfigureExtra(IDictionary<string, string?> settings)
    {
        settings["Integrity:EnableMultiAccountHeuristics"] = "true";
    }

    /// <summary>Registers an account, then walks the ladder the way the real client does: the ranked home
    /// reads <c>/ranked/me</c> (which is what records the device fingerprint) before the coach presses
    /// "join".</summary>
    private async Task<(string Token, RankedStateDto State)> EnrolFrom(string? deviceId)
    {
        var registered = await Client.PostAsJsonAsync("/auth/register",
            new RegisterRequest($"coach_{Guid.NewGuid():N}@example.com", ValidPassword, "Mister"));
        string token = (await registered.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;

        using (var me = Authed(HttpMethod.Get, "/ranked/me", token))
        {
            if (deviceId is not null) me.Headers.Add("X-Fts-Device", deviceId);
            var resp = await Client.SendAsync(me);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var enrol = Authed(HttpMethod.Post, "/ranked/enrol", token);
        if (deviceId is not null) enrol.Headers.Add("X-Fts-Device", deviceId);
        var enrolled = await Client.SendAsync(enrol);
        Assert.That(enrolled.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (token, (await enrolled.Content.ReadFromJsonAsync<RankedStateDto>())!);
    }

    [Test]
    public async Task LinkedAccounts_JoinTheLadder_ButNotTheSameGroup()
    {
        var first = await EnrolFrom(SharedDevice);
        var second = await EnrolFrom(SharedDevice);
        var unrelated = await EnrolFrom(deviceId: null);

        var flagsResp = await Client.GetAsync("/internal/ranked/integrity/flags");
        var flags = (await flagsResp.Content.ReadFromJsonAsync<IntegrityFlagsDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(first.State.GroupId, Is.Not.Null);
            Assert.That(second.State.GroupId, Is.Not.Null);
            Assert.That(second.State.GroupId, Is.Not.EqualTo(first.State.GroupId),
                "the second account is seated in a different cohort");
            Assert.That(unrelated.State.GroupId, Is.EqualTo(first.State.GroupId),
                "an unrelated newcomer still fills the earliest forming group — matchmaking is not disturbed");
            Assert.That(flags.Flags.Any(f => f.Kind == IntegrityFlagKind.LinkedAccounts), Is.True,
                "the link is on the review queue");
        });
    }
}
