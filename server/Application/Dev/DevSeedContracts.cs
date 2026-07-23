namespace Fts.Application.Dev;

/// <summary>Dev-only seeding DTOs (dev tooling — see <see cref="IDevSeedService"/>). Let a developer spin
/// up a fully-formed online test league (bot members + completed draft) in one call, so online features
/// (auctions, season) can be exercised without manually creating accounts and running the draft each time.
/// Never exposed in Production.</summary>

/// <summary>Seed a test league. <paramref name="Size"/> is the world/club count; <paramref name="Bots"/>
/// is how many seats to fill (2..Size). <paramref name="ToStatus"/> = Forming | Drafting | Active. If
/// <paramref name="CreatorUserId"/> is set (the signed-in human, for the client dev button), that account
/// is the creator and bots fill the remaining seats; otherwise a deterministic bot is the creator.</summary>
public sealed record DevSeedRequest(
    int Size = 4, int Bots = 4, string ToStatus = "Active", Guid? CreatorUserId = null);

/// <summary>One seeded member. The bot access tokens let a script act as any member; the human creator's
/// token is null (the caller already holds it).</summary>
public sealed record DevSeedMemberDto(
    Guid UserId, string Email, string? AccessToken, int? ClubExternalId, bool IsCreator, bool IsBot);

/// <summary>The seeded league + its members (with tokens for the bots).</summary>
public sealed record DevSeedResult(
    Guid LeagueId, string InviteCode, string Status, IReadOnlyList<DevSeedMemberDto> Members);

/// <summary>Have the league's bot members place a round (or several) of legal ascending bids on every
/// open auction lot — so a single human can watch live outbidding / anti-snipe / settlement.</summary>
public sealed record DevBotBidRequest(int Rounds = 1);

public sealed record DevBotBidResult(int BidsPlaced);

/// <summary>Result of marking every bot member ready (advances the all-ready season one round).</summary>
public sealed record DevBotReadyResult(int BotsReadied);

/// <summary>Simulate the fixture's bot opponent in a live match (Phase 8.6 dev tooling): the bot joins the
/// live session — so once the human has opened it too the match goes Live — and, if <paramref name="Sub"/>,
/// makes a legal substitution at <paramref name="Minute"/>, so a single human can see the opponent's change
/// reflected without a second account/device.</summary>
public sealed record DevBotLiveRequest(bool Sub = false, int Minute = 45);

/// <summary>Outcome of the bot live autopilot: the reported status, whether the match kicked off (both
/// present), whether the bot made its substitution, and the minute it used.</summary>
public sealed record DevBotLiveResult(string Status, bool WentLive, bool SubMade, int Minute);

/// <summary>Result of the cleanup: how many league memberships the bots left.</summary>
public sealed record DevResetResult(int LeaguesLeft, int BotsChecked);

/// <summary>Fill the caller's forming ranked placement group with bot coaches so a solo human can test the
/// ranked flow (Phase 9.2 dev tooling). <paramref name="Count"/> overrides how many bots to enrol; when
/// omitted the server enrols exactly enough to fill the earliest forming placement group. Fresh bot
/// accounts (<c>rankedbot_{batch}_{i}@dev.local</c>) each call — a ranked coach is one row per account
/// ever, so bots can't be reused across ranked runs.</summary>
public sealed record DevRankedFillRequest(int? Count = null);

/// <summary>How many bots were enrolled + the placement group's occupancy afterwards.</summary>
public sealed record DevRankedFillResult(int Enrolled, int Occupied, int Capacity);
