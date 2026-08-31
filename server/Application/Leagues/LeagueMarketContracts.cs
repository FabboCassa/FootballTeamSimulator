namespace Fts.Application.Leagues;

/// <summary>
/// The private-league transfer market (Phase 12.1). A private league now trades like the single-player
/// career: you browse another club's squad, offer a fee, and the owner accepts / counters / rejects; you
/// put your own players on the transfer list and field the offers that come in; you sign free agents by
/// agreeing terms with the PLAYER (first come, first served — no auction, the 8.5 free-agent auction is
/// retired by this task). The AI world keeps trading between rounds so the league still moves.
///
/// Everything is gated to an OPEN MARKET WINDOW. A private league runs on rounds, not on a clock, so the
/// windows are round-based (decision taken with the user, 2026-08-25): window 0 before the first round,
/// window 1 at the mid-season round. A window closes when the next round resolves.
///
/// The absent-friend rule (decision taken with the user, 2026-08-25): a private league is played together,
/// so NO AI ever answers in a human's place. An offer left unanswered when the round resolves simply
/// expires — "no answer" means "not accepted" — and the pending count is surfaced on the league summary so
/// the client can shout about it on the home screen.
/// </summary>

/// <summary>Lifecycle of a league transfer offer. Stable numeric values (stored + on the wire).</summary>
public enum LeagueOfferStatus
{
    /// <summary>On the table, waiting for the side that did NOT put the current figure there.</summary>
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    /// <summary>Pulled by the buyer while it was still pending.</summary>
    Withdrawn = 3,
    /// <summary>Never answered before the round resolved — counts as "not accepted".</summary>
    Expired = 4,
}

/// <summary>Which side of a negotiation put the current figure on the table.</summary>
public enum LeagueOfferParty
{
    Buyer = 0,
    Seller = 1,
}

/// <summary>What a coach does with an offer that is waiting on him.</summary>
public enum LeagueOfferAction
{
    Accept = 0,
    Reject = 1,
    /// <summary>Put a different figure on the table; the other side answers (an AI answers at once).</summary>
    Counter = 2,
    /// <summary>Buyer only: pull an offer that has not been answered yet.</summary>
    Withdraw = 3,
}

/// <summary>Whether the market is open, and when it opens/closes in ROUNDS (a private league has no clock).</summary>
/// <param name="Open">True while trading is allowed right now.</param>
/// <param name="WindowIndex">0 = pre-season, 1 = mid-season, -1 = no window open.</param>
/// <param name="RoundsPlayed">Rounds fully resolved so far.</param>
/// <param name="TotalRounds">Rounds in the season's schedule.</param>
/// <param name="ClosesAfterRound">The round whose resolution shuts the current window (0 = none open).</param>
/// <param name="NextOpensAfterRound">The round after which the next window opens (0 = none left).</param>
public sealed record LeagueMarketWindowDto(
    bool Open,
    int WindowIndex,
    int RoundsPlayed,
    int TotalRounds,
    int ClosesAfterRound,
    int NextOpensAfterRound);

/// <summary>One player as the market shows him. <see cref="Role"/> is the numeric
/// <c>Sim.Core.Domain.PositionRole</c> (0 = GK … 7 = Striker); the client maps it to a label.</summary>
public sealed record LeagueMarketPlayerDto(
    int ExternalId,
    string Name,
    int Age,
    int Role,
    int Overall,
    long MarketValue,
    long WeeklyWage,
    int ContractSeasonsRemaining,
    // True when his club has put him on the transfer list.
    bool Listed,
    // His club's asking price when listed, otherwise 0.
    long AskingPrice);

/// <summary>A club's squad, for deciding who to bid on. <see cref="IsHuman"/> is false for a club no
/// member holds — you can offer for a bot's player exactly as for a friend's, the bot just answers at once.</summary>
public sealed record LeagueMarketSquadDto(
    int ClubExternalId,
    string ClubName,
    bool IsHuman,
    long TransferBudget,
    IReadOnlyList<LeagueMarketPlayerDto> Players);

/// <summary>An unattached player and the terms he is asking for (Phase 12.1 — no auction: you agree terms
/// and sign, and the first coach to agree gets him).</summary>
public sealed record LeagueFreeAgentDto(
    int ExternalId,
    string Name,
    int Age,
    int Role,
    int Overall,
    long MarketValue,
    // The weekly wage he wants. Offer at least this and he signs.
    long DemandedWeeklyWage,
    // The shortest contract he will sign, in seasons.
    int MinSeasons,
    // The longest contract he will sign, in seasons.
    int MaxSeasons,
    // What signing him costs the club's transfer budget up front at the demanded wage: the first
    // season's wages, prepaid — the limiter that stops a club hoovering up the whole free-agent pool.
    long SigningCostAtDemand);

/// <summary>A live negotiation as the caller sees it.</summary>
public sealed record LeagueOfferDto(
    Guid Id,
    int WindowIndex,
    int PlayerExternalId,
    string PlayerName,
    int PlayerRole,
    int PlayerOverall,
    long PlayerMarketValue,
    int BuyerClubExternalId,
    string BuyerClubName,
    int SellerClubExternalId,
    string SellerClubName,
    // The figure currently on the table.
    long Amount,
    // Who put it there — the OTHER side is the one who must answer.
    LeagueOfferParty ProposedBy,
    int Rounds,
    LeagueOfferStatus Status,
    bool YouAreBuyer,
    bool YouAreSeller,
    // True when the ball is in the caller's court right now.
    bool AwaitingYou,
    DateTime UpdatedUtc);

/// <summary>A completed deal in this league, newest first — the transfer news feed.</summary>
public sealed record LeagueTransferNewsDto(
    int PlayerExternalId,
    string PlayerName,
    string FromClubName,
    string ToClubName,
    long Fee,
    bool InvolvesYou,
    DateTime CreatedUtc);

/// <summary>The caller's whole market view in one payload.</summary>
public sealed record LeagueMarketDto(
    LeagueMarketWindowDto Window,
    int? YourClubExternalId,
    long YourBudget,
    int YourSquadSize,
    // Your own squad, so the client can list/unlist from the same screen.
    IReadOnlyList<LeagueMarketPlayerDto> YourSquad,
    // Every other club, human or bot, with its squad.
    IReadOnlyList<LeagueMarketSquadDto> Clubs,
    IReadOnlyList<LeagueFreeAgentDto> FreeAgents,
    // Negotiations where someone is waiting on YOU (the home-screen badge counts these).
    IReadOnlyList<LeagueOfferDto> AwaitingYou,
    IReadOnlyList<LeagueOfferDto> Incoming,
    IReadOnlyList<LeagueOfferDto> Outgoing,
    IReadOnlyList<LeagueTransferNewsDto> News);

/// <summary>Offer a fee for a player on another club (found by his world-unique external id).</summary>
public sealed record MakeLeagueOfferRequest(int PlayerExternalId, long Fee);

/// <summary>Answer an offer that is waiting on you. <see cref="Amount"/> is required for
/// <see cref="LeagueOfferAction.Counter"/> and ignored otherwise.</summary>
public sealed record RespondLeagueOfferRequest(LeagueOfferAction Action, long Amount);

/// <summary>Put one of your players on the transfer list at an asking price (or take him off it with
/// <see cref="Listed"/> = false).</summary>
public sealed record ListPlayerRequest(int PlayerExternalId, bool Listed, long AskingPrice);

/// <summary>Agree terms with a free agent. Meet his demanded wage and a contract length in his range and
/// he signs — if nobody beat you to him.</summary>
public sealed record SignFreeAgentRequest(int PlayerExternalId, long WeeklyWage, int Seasons);

/// <summary>The outcome of a free-agent approach. A refusal is NOT an error: the player simply says what
/// he wants, so the client can bump the offer and try again.</summary>
public sealed record FreeAgentSigningDto(
    bool Signed,
    int PlayerExternalId,
    string PlayerName,
    long DemandedWeeklyWage,
    int MinSeasons,
    int MaxSeasons,
    long SigningCost,
    string? Message,
    LeagueMarketDto Market);

/// <summary>
/// Private-league market use cases (Phase 12.1). The account id is always passed in by the Api from the
/// access token; expected failures come back as <see cref="LeagueResult{T}"/>.
/// </summary>
public interface ILeagueMarketService
{
    /// <summary>The caller's whole market view: window state, squads, free agents, live negotiations, news.</summary>
    Task<LeagueResult<LeagueMarketDto>> GetMarketAsync(Guid userId, Guid leagueId, CancellationToken ct = default);

    /// <summary>Offer a fee for another club's player. A bot seller answers immediately (accept / counter /
    /// reject); a human seller gets a pending offer and a notification.</summary>
    Task<LeagueResult<LeagueMarketDto>> MakeOfferAsync(
        Guid userId, Guid leagueId, MakeLeagueOfferRequest request, CancellationToken ct = default);

    /// <summary>Accept / reject / counter / withdraw a negotiation that is waiting on the caller. A counter
    /// against a bot is answered inside the same call.</summary>
    Task<LeagueResult<LeagueMarketDto>> RespondAsync(
        Guid userId, Guid leagueId, Guid offerId, RespondLeagueOfferRequest request, CancellationToken ct = default);

    /// <summary>List (or unlist) one of your own players. Listing during an open window brings bot offers in
    /// straight away, so selling never depends on a friend being online.</summary>
    Task<LeagueResult<LeagueMarketDto>> SetListingAsync(
        Guid userId, Guid leagueId, ListPlayerRequest request, CancellationToken ct = default);

    /// <summary>Agree terms with a free agent. One transaction against the player row: the first coach to
    /// agree signs him, everyone else is told he is gone.</summary>
    Task<LeagueResult<FreeAgentSigningDto>> SignFreeAgentAsync(
        Guid userId, Guid leagueId, SignFreeAgentRequest request, CancellationToken ct = default);
}
