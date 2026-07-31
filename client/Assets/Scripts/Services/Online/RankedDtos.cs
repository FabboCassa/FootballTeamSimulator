using System;
using System.Collections.Generic;

namespace Fts.Services.Online
{
    // Wire DTOs mirroring the server's /ranked payloads (Phase 9.2). Public fields named to match the
    // server's camelCase JSON so Newtonsoft round-trips them exactly. Nullable enums/ids arrive as
    // nullable ints; Guids as strings; timestamps as ISO strings.

    /// <summary>Mirrors the server RankedGroupKind (0 placement season, 1 permanent division).</summary>
    public enum RankedGroupKind { Placement = 0, Division = 1 }

    /// <summary>Mirrors the server RankedCoachStatus (0 in placement, 1 placed in a division, 2 retired).</summary>
    public enum RankedCoachStatus { Placement = 0, Placed = 1, Retired = 2 }

    /// <summary>The caller's ladder state (GET /ranked/me, POST /ranked/enrol). Everything but
    /// <see cref="enrolled"/> is default/null for an account that never joined.</summary>
    [Serializable]
    public sealed class RankedStateDto
    {
        public bool enrolled;
        public string rankedWorldId;
        public string worldName;
        public string groupId;
        public string groupName;
        public int? kind;          // RankedGroupKind
        public int? tier;
        public int? seatIndex;
        public int? clubExternalId;
        public string clubName;
        public int rating;
        public int status;         // RankedCoachStatus
        public int? placementPosition;
        public bool autoEnrol;
    }

    /// <summary>A market window on the season calendar (season start + midpoint).</summary>
    [Serializable]
    public sealed class RankedMarketWindowDto
    {
        public int index;
        public string opensUtc;
        public string closesUtc;
        public bool isOpen;
    }

    /// <summary>Where the caller's ranked season is + their own state.</summary>
    [Serializable]
    public sealed class RankedSeasonStateDto
    {
        public string groupId;
        public string groupName;
        public int kind;           // RankedGroupKind
        public int tier;
        public bool started;
        public int totalRounds;
        public int roundsPlayed;
        public int? nextRound;
        public string nextKickoffUtc;
        public bool seasonComplete;
        public int? yourClubExternalId;
        public bool youSubmittedLineup;
        public RankedMarketWindowDto currentWindow;
        public string stateHashHex;
    }

    /// <summary>One scheduled/played match with its real-time kickoff.</summary>
    [Serializable]
    public sealed class RankedFixtureDto
    {
        public string id;
        public int round;
        public int day;
        public string kickoffUtc;
        public int homeClubExternalId;
        public string homeClubName;
        public int awayClubExternalId;
        public string awayClubName;
        public bool played;
        public int homeGoals;
        public int awayGoals;
        public bool isYours;
    }

    /// <summary>A club's standing in the group.</summary>
    [Serializable]
    public sealed class RankedStandingDto
    {
        public int clubExternalId;
        public string clubName;
        public int played;
        public int won;
        public int drawn;
        public int lost;
        public int goalsFor;
        public int goalsAgainst;
        public int goalDifference;
        public int points;
        public bool isYou;
    }

    /// <summary>One player in a browsed ranked squad (<see cref="RankedSquadDto"/>). <see cref="role"/> is
    /// the numeric Sim.Core PositionRole (0 = GK … 7 = ST).</summary>
    [Serializable]
    public sealed class RankedPlayerDto
    {
        public int externalId;
        public string name;
        public int age;
        public int role;
        public int overall;
        public long marketValue;
    }

    /// <summary>A club's squad (GET /ranked/clubs/{ext}/squad) — for the lineup editor + market browsing.</summary>
    [Serializable]
    public sealed class RankedSquadDto
    {
        public int clubExternalId;
        public string clubName;
        public bool isHuman;
        public List<RankedPlayerDto> players = new List<RankedPlayerDto>();
    }

    /// <summary>The caller's ranked season: state + schedule + standings (InSeason false before kickoff).</summary>
    [Serializable]
    public sealed class RankedSeasonDto
    {
        public bool inSeason;
        public RankedSeasonStateDto state;
        public List<RankedFixtureDto> fixtures = new List<RankedFixtureDto>();
        public List<RankedStandingDto> standings = new List<RankedStandingDto>();
    }

    // --- market: direct offers + free-agent auctions (Phase 9.2b) ---------------------------------

    /// <summary>Mirrors the server RankedOfferStatus.</summary>
    public enum RankedOfferStatus { Pending = 0, Accepted = 1, Rejected = 2, Withdrawn = 3 }

    /// <summary>Mirrors the server RankedAuctionStatus.</summary>
    public enum RankedAuctionStatus { Open = 0, Settled = 1, Unsold = 2 }

    /// <summary>A direct coach-to-coach offer as seen by the caller.</summary>
    [Serializable]
    public sealed class RankedOfferDto
    {
        public string id;
        public int windowIndex;
        public int playerExternalId;
        public string playerName;
        public int buyerClubExternalId;
        public string buyerClubName;
        public int sellerClubExternalId;
        public string sellerClubName;
        public long fee;
        public int status; // RankedOfferStatus
        public bool youAreBuyer;
        public bool youAreSeller;
    }

    /// <summary>The caller's market view: budget + incoming/outgoing offers + whether a window is open.</summary>
    [Serializable]
    public sealed class RankedOffersDto
    {
        public long yourBudget;
        public bool marketOpen;
        public List<RankedOfferDto> incoming = new List<RankedOfferDto>();
        public List<RankedOfferDto> outgoing = new List<RankedOfferDto>();
    }

    /// <summary>Body for POST /ranked/offers.</summary>
    [Serializable]
    public sealed class MakeRankedOfferBody
    {
        public int playerExternalId;
        public long fee;
    }

    /// <summary>One free-agent auction lot.</summary>
    [Serializable]
    public sealed class RankedAuctionLotDto
    {
        public string id;
        public int playerExternalId;
        public string playerName;
        public int age;
        public int role;
        public int overall;
        public long marketValue;
        public long startPrice;
        public long highBid;
        public int? highBidClubExternalId;
        public bool youAreLeading;
        public long minNextBid;
        public int status; // RankedAuctionStatus
        public string endsUtc;
        public int secondsRemaining;
    }

    /// <summary>The caller's auction view: open lots + budget picture + window state.</summary>
    [Serializable]
    public sealed class RankedAuctionsDto
    {
        public long budget;
        public long committed;
        public long available;
        public bool windowOpen;
        public List<RankedAuctionLotDto> lots = new List<RankedAuctionLotDto>();
    }

    /// <summary>Body for POST /ranked/auctions/{id}/bid.</summary>
    [Serializable]
    public sealed class PlaceRankedBidBody { public long amount; }

    // --- ranking: global leaderboard + palmarès (Phase 9.3) ---------------------------------------

    /// <summary>Mirrors the server RankedAwardKind — what a palmarès line records.</summary>
    public enum RankedAwardKind
    {
        SeasonPlayed = 0,
        Champion = 1,
        Promotion = 2,
        Relegation = 3,
        PlacementCompleted = 4,
        TopFlightTitle = 5,
    }

    /// <summary>One row of the global ladder (GET /ranked/leaderboard).</summary>
    [Serializable]
    public sealed class RankedLeaderboardEntryDto
    {
        public int rank;
        public string userId;
        public string displayName;
        public int rating;
        public int peakRating;
        public int? tier;
        public string groupName;
        public int seasonsPlayed;
        public int titles;
        public bool isYou;
    }

    /// <summary>The ladder's top slice + the caller's own row (present even when outside the slice).</summary>
    [Serializable]
    public sealed class RankedLeaderboardDto
    {
        public List<RankedLeaderboardEntryDto> entries = new List<RankedLeaderboardEntryDto>();
        public RankedLeaderboardEntryDto you;
        public int totalCoaches;
    }

    /// <summary>One line of a coach's permanent record (a title, a promotion, a season played…).</summary>
    [Serializable]
    public sealed class RankedAwardDto
    {
        public string id;
        public int kind;           // RankedAwardKind
        public string worldName;
        public string groupName;
        public int tier;
        public int position;
        public int seasonNumber;
        public int ratingAfter;
        public int ratingDelta;
        public string awardedUtc;
    }

    /// <summary>The caller's persistent record (GET /ranked/palmares): rating, counters, award history.</summary>
    [Serializable]
    public sealed class RankedPalmaresDto
    {
        public string userId;
        public string displayName;
        public int rating;
        public int peakRating;
        public int seasonsPlayed;
        public int titles;
        public int promotions;
        public int relegations;
        public List<RankedAwardDto> awards = new List<RankedAwardDto>();
    }

    // --- Daily digest (Phase 9.4) --------------------------------------------------------------

    /// <summary>Mirrors the server RankedTodoKind. The wire carries only the kind + a count — the client
    /// renders the localised sentence (`ranked.todo.*`), so no server string is ever shown to the user.</summary>
    public enum RankedTodoKind
    {
        Enrol = 0,
        ConfirmMatchday = 1,
        RespondOffer = 2,
        MarketWindow = 3,
        SeasonSummary = 4,
    }

    /// <summary>One thing to do today. Lower <see cref="priority"/> = more urgent (the server sorts).</summary>
    [Serializable]
    public sealed class RankedTodoDto
    {
        public int kind;      // RankedTodoKind
        public int count;
        public int priority;
    }

    /// <summary>The caller's next scheduled match.</summary>
    [Serializable]
    public sealed class RankedTodayNextMatchDto
    {
        public string fixtureId;
        public int round;
        public string kickoffUtc;
        public int secondsToKickoff;
        public bool youAreHome;
        public int opponentClubExternalId;
        public string opponentClubName;
    }

    /// <summary>The caller's most recent result, from their own point of view.</summary>
    [Serializable]
    public sealed class RankedTodayLastResultDto
    {
        public string fixtureId;
        public int round;
        public bool youAreHome;
        public int opponentClubExternalId;
        public string opponentClubName;
        public int goalsFor;
        public int goalsAgainst;
    }

    /// <summary>The whole daily loop in one payload (GET /ranked/today, POST /ranked/today/confirm). A
    /// signed-in account always gets a valid answer: someone who never joined comes back with
    /// <see cref="enrolled"/> false and a single "enrol" to-do.</summary>
    [Serializable]
    public sealed class RankedTodayDto
    {
        public bool enrolled;
        public int status;              // RankedCoachStatus
        public int rating;
        public bool autoEnrol;
        public string groupId;
        public string groupName;
        public int? kind;               // RankedGroupKind
        public int? tier;
        public int? clubExternalId;
        public string clubName;

        public bool inSeason;
        public bool seasonComplete;
        public int totalRounds;
        public int roundsPlayed;
        public int? nextRound;
        public int? yourPosition;
        public int? yourPoints;
        public RankedTodayNextMatchDto nextMatch;
        public RankedTodayLastResultDto lastResult;

        public bool lineupReady;
        public bool lineupConfirmed;
        public bool trainingSet;
        public int? trainingTeamFocus;  // Sim.Core TeamTrainingFocus

        public RankedMarketWindowDto marketWindow;
        public long budget;
        public int incomingOffers;
        public int outgoingOffers;
        public int openLots;
        public int lotsYouLead;

        public int actionCount;
        public List<RankedTodoDto> todo = new List<RankedTodoDto>();
    }

    // --- abuse & integrity (Phase 9.5) --------------------------------------------------------

    /// <summary>Why a coach is reporting another coach — mirrors the server enum, sent as its integer
    /// value (the Api binds enums numerically). The client localises the label per value.</summary>
    public enum RankedReportReason
    {
        Collusion = 0,
        Inactivity = 1,
        OffensiveName = 2,
        Cheating = 3,
        Other = 4,
    }

    /// <summary>POST /ranked/report — identify the reported coach by the club external id the UI shows.</summary>
    [Serializable]
    public sealed class SubmitRankedReportBody
    {
        public int subjectClubExternalId;
        public int reason;
        public string details;
    }

    /// <summary>The server's acknowledgement. Deliberately thin — a reporter is told the report was filed
    /// and nothing else, so the endpoint cannot be used to probe other accounts.</summary>
    [Serializable]
    public sealed class RankedReportDto
    {
        public string flagId;
        public string createdUtc;
    }

    /// <summary>Why a ranked API call failed — mapped from the server's HTTP status (Phase 9.2).</summary>
    public enum RankedApiError
    {
        None = 0,
        NotSignedIn,      // no valid token locally
        Network,          // server unreachable
        Server,           // 5xx / bad payload
        Validation,       // 400
        NotEnrolled,      // 404 not_enrolled
        NotFound,         // 404 not_found
        Forbidden,        // 403
        WrongPhase,       // 409 wrong_phase
        NoCapacity,       // 409 no_capacity
        FixtureNotFound,  // 404 fixture_not_found
        ReplayNotReady,   // 409 replay_not_ready
        InsufficientBudget, // 400 insufficient_budget
        AuctionNotFound,  // 404 auction_not_found
        AuctionClosed,    // 409 auction_closed
        BidTooLow,        // 400 bid_too_low
        IntegrityBlocked, // 409 integrity_blocked — the fee is far outside the band around market value
        DeadlinePassed,   // 409 deadline_passed  — the next matchday has kicked off
        RateLimited,      // 429 rate_limited     — too many requests in the window
    }

    /// <summary>Result wrapper mirroring <see cref="LeagueApiResult{T}"/> — the presenter maps the error to loc.</summary>
    public readonly struct RankedApiResult<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public RankedApiError Error { get; }

        private RankedApiResult(bool success, T value, RankedApiError error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static RankedApiResult<T> Ok(T value) => new RankedApiResult<T>(true, value, RankedApiError.None);
        public static RankedApiResult<T> Fail(RankedApiError error) => new RankedApiResult<T>(false, default, error);
    }
}
