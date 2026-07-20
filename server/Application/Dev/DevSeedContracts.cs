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

/// <summary>Result of the cleanup: how many league memberships the bots left.</summary>
public sealed record DevResetResult(int LeaguesLeft, int BotsChecked);
