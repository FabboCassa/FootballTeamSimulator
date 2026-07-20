using System;
using System.Collections.Generic;

namespace Fts.Services.Online
{
    // Wire DTOs mirroring the server's /leagues payloads (Phase 8.1). Public fields named to match
    // the server's camelCase JSON so Newtonsoft round-trips them exactly. Enums (status/mode) arrive
    // as ints — the client interprets them via LeagueStatus/LeagueMode below.

    [Serializable]
    public sealed class CreateLeagueBody
    {
        public string name;
        public int size;
        public int mode; // LeagueMode
    }

    [Serializable]
    public sealed class JoinLeagueBody
    {
        public string inviteCode;
    }

    [Serializable]
    public sealed class LeagueSummaryDto
    {
        public string id;
        public string name;
        public string inviteCode;
        public int size;
        public int memberCount;
        public int status; // LeagueStatus
        public int mode;   // LeagueMode
        public bool isCreator;
    }

    [Serializable]
    public sealed class LeagueMemberDto
    {
        public string userId;
        public string displayName;
        public int? clubExternalId;
        public string clubName;
        public bool isCreator;
        public bool isReady;
    }

    [Serializable]
    public sealed class LeaguePlayerDto
    {
        public int externalId;
        public string name;
        public int age;
        public int role; // Sim.Core PositionRole (0=GK … 7=ST)
        public int overall;
    }

    [Serializable]
    public sealed class LeagueClubDto
    {
        public int externalId;
        public string name;
        public string shortName;
        public int strength;
        public long transferBudget; // 0 until the draft, then equal for every club (8.2)
        public List<LeaguePlayerDto> players = new List<LeaguePlayerDto>();
    }

    /// <summary>Mirrors the server's DraftStateDto (8.2). While <see cref="inProgress"/>, the member whose
    /// turn it is (<see cref="currentPickUserId"/>) picks one of the still-unclaimed clubs.</summary>
    [Serializable]
    public sealed class DraftStateDto
    {
        public bool inProgress;
        public string currentPickUserId;
        public int picksMade;
        public int totalPicks;
    }

    [Serializable]
    public sealed class LeagueDetailDto
    {
        public LeagueSummaryDto league;
        public List<LeagueMemberDto> members = new List<LeagueMemberDto>();
        public List<LeagueClubDto> clubs = new List<LeagueClubDto>();
        public DraftStateDto draft;
    }

    /// <summary>Body for POST /leagues/{id}/draft/pick (8.2).</summary>
    [Serializable]
    public sealed class PickClubBody
    {
        public int clubExternalId;
    }

    // --- Season (Phase 8.3b) -------------------------------------------------------------------

    /// <summary>Body for POST /leagues/{id}/ready.</summary>
    [Serializable]
    public sealed class SetReadyBody
    {
        public bool ready;
    }

    /// <summary>Mirrors the server's LeagueFixtureDto (8.3). <see cref="id"/> is the fixture Guid (for
    /// the replay endpoint); club names/ids let the schedule render without extra lookups.</summary>
    [Serializable]
    public sealed class LeagueFixtureDto
    {
        public string id;
        public int round;
        public int day;
        public int homeClubExternalId;
        public string homeClubName;
        public int awayClubExternalId;
        public string awayClubName;
        public bool played;
        public int homeGoals;
        public int awayGoals;
    }

    /// <summary>Mirrors the server's LeagueStandingDto (8.3) — a computed table row.</summary>
    [Serializable]
    public sealed class LeagueStandingDto
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
    }

    /// <summary>Mirrors the server's SeasonStateDto (8.3): where the season is + the caller's own state.</summary>
    [Serializable]
    public sealed class SeasonStateDto
    {
        public bool started;
        public int totalRounds;
        public int roundsPlayed;
        public int? nextRound;
        public bool seasonComplete;
        public int membersTotal;
        public int membersReady;
        public bool youAreReady;
        public int? yourClubExternalId;
        public bool youSubmittedLineup;
    }

    /// <summary>Mirrors the server's LeagueSeasonDto (8.3): state + fixtures + standings.</summary>
    [Serializable]
    public sealed class LeagueSeasonDto
    {
        public SeasonStateDto season;
        public List<LeagueFixtureDto> fixtures = new List<LeagueFixtureDto>();
        public List<LeagueStandingDto> standings = new List<LeagueStandingDto>();
    }

    // --- Daily management (Phase 8.4b) ---------------------------------------------------------

    /// <summary>Mirrors the server's StateHashDto (8.4): the canonical whole-world state hash
    /// (WorldStateHasher over every player's condition + attributes) plus the player count and how
    /// many rounds have resolved. Both server and a client compute the SAME hash, so a match proves
    /// client↔server agreement; the hash CHANGES after a round of play (condition + development evolved).</summary>
    [Serializable]
    public sealed class StateHashDto
    {
        public string hashHex;
        public int playerCount;
        public int roundsPlayed;
    }

    /// <summary>The training plan wire shape (8.4b). The server binds it with the default (numeric)
    /// enum JSON, so <see cref="teamFocus"/> and the <see cref="individualFocuses"/> values are the
    /// integer <c>TeamTrainingFocus</c> / <c>IndividualTrainingFocus</c> members; the dictionary keys
    /// are the stringified player external ids.</summary>
    [Serializable]
    public sealed class TrainingPlanBody
    {
        public int teamFocus;
        public Dictionary<string, int> individualFocuses = new Dictionary<string, int>();
    }

    /// <summary>Body for POST /leagues/{id}/training — wraps the plan under "training" to match the
    /// server's SubmitTrainingRequest(TrainingPlan Training) record (8.4b).</summary>
    [Serializable]
    public sealed class SubmitTrainingBody
    {
        public TrainingPlanBody training;
    }

    // --- Online auctions (Phase 8.5b) ----------------------------------------------------------

    /// <summary>Mirrors the server's AuctionLotDto (8.5): one free-agent lot. <see cref="auctionId"/> is
    /// the lot Guid (for the bid endpoint); <see cref="status"/> is the numeric AuctionStatus; <see
    /// cref="secondsRemaining"/> is the server-computed countdown (0 once elapsed).</summary>
    [Serializable]
    public sealed class AuctionLotDto
    {
        public string auctionId;
        public int playerExternalId;
        public string playerName;
        public int age;
        public int role; // Sim.Core PositionRole
        public int overall;
        public int potential;
        public long marketValue;
        public long startPrice;
        public long highBid;
        public int? highBidClubExternalId;
        public string highBidClubName;
        public int status; // AuctionStatus (0 Open / 1 Settled / 2 Unsold)
        public string endsUtc;
        public int secondsRemaining;
    }

    /// <summary>Mirrors the server's AuctionsDto (8.5): the current window's lots + the caller's budget
    /// picture. <see cref="available"/> = <see cref="budget"/> − <see cref="committed"/> is the ceiling a
    /// new bid may not exceed.</summary>
    [Serializable]
    public sealed class AuctionsDto
    {
        public List<AuctionLotDto> lots = new List<AuctionLotDto>();
        public int? yourClubExternalId;
        public long budget;
        public long committed;
        public long available;
        public bool windowOpen;
    }

    /// <summary>Body for POST /leagues/{id}/auctions/{auctionId}/bid.</summary>
    [Serializable]
    public sealed class PlaceBidBody
    {
        public long amount;
    }

    /// <summary>Mirrors the server's BidResultDto (8.5): the updated lot, whether a previous leader was
    /// displaced (they get an outbid push), whether the timer was extended by the anti-snipe rule, and
    /// the caller's remaining available budget.</summary>
    [Serializable]
    public sealed class BidResultDto
    {
        public AuctionLotDto lot;
        public bool outbidPrevious;
        public int? previousLeaderClubExternalId;
        public bool extended;
        public long available;
    }

    /// <summary>Mirrors the server enum (AuctionStatus): 0 Open / 1 Settled / 2 Unsold.</summary>
    public enum AuctionStatus { Open = 0, Settled = 1, Unsold = 2 }

    // --- Dev tooling (dev-only seeding, gated by DevFlags) --------------------------------------

    /// <summary>Body for POST /internal/dev/test-league. <see cref="creatorUserId"/> = the signed-in human
    /// so the seeded league is owned by (and viewable to) the current account; bots fill the other seats.</summary>
    [Serializable]
    public sealed class DevSeedBody
    {
        public int size = 4;
        public int bots = 4;
        public string toStatus = "Active";
        public string creatorUserId;
    }

    /// <summary>Trimmed mirror of the server's DevSeedResult — the client only needs the league id to open
    /// its lobby.</summary>
    [Serializable]
    public sealed class DevSeedResultDto
    {
        public string leagueId;
        public string inviteCode;
        public string status;
    }

    /// <summary>Body for POST /internal/dev/leagues/{id}/auctions/botbid.</summary>
    [Serializable]
    public sealed class DevBotBidBody
    {
        public int rounds = 1;
    }

    /// <summary>Mirrors the server enum (LeagueStatus): 0 Forming / 1 Active / 2 Completed / 3 Drafting.</summary>
    public enum LeagueStatus { Forming = 0, Active = 1, Completed = 2, Drafting = 3 }

    /// <summary>Mirrors the server enum (LeagueMode): 0 AllReady / 1 RealTime.</summary>
    public enum LeagueMode { AllReady = 0, RealTime = 1 }

    /// <summary>Why an online-league call failed — mapped from the server's HTTP status (Phase 8.1b).</summary>
    public enum LeagueApiError
    {
        None = 0,
        NotSignedIn,   // no valid token locally
        Network,       // server unreachable
        Validation,    // 400
        NotFound,      // 404 (unknown league / invite code)
        AlreadyMember, // 409 already_member
        LeagueFull,    // 409 league_full
        NotJoinable,   // 409 not_joinable
        Forbidden,     // 403
        WrongPhase,    // 409 wrong_phase (8.2 — draft already started / not running; 8.3 — season not active)
        NotYourTurn,   // 409 not_your_turn (8.2)
        ClubUnavailable, // 409 club_unavailable (8.2 — unknown or taken club)
        TooFewMembers, // 400 too_few_members (8.2)
        NotAssignedClub, // 409 not_assigned_club (8.3 — no club to submit for)
        NothingToResolve, // 409 nothing_to_resolve (8.3 — season complete)
        ReplayNotReady, // 409 replay_not_ready (8.3 — fixture not played yet)
        AuctionNotFound,   // 404 auction_not_found (8.5)
        AuctionClosed,     // 409 auction_closed (8.5 — lot no longer open)
        BidTooLow,         // 400 bid_too_low (8.5)
        InsufficientBudget,// 400 insufficient_budget (8.5)
        WindowAlreadyOpen, // 409 window_already_open (8.5)
        NoAuctionsOpen,    // 409 no_auctions_open (8.5 — nothing to close)
        Server,        // 5xx / unexpected
    }

    /// <summary>Result of a league call: the value on success, or an error the presenter maps to loc.</summary>
    public readonly struct LeagueApiResult<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public LeagueApiError Error { get; }

        private LeagueApiResult(bool success, T value, LeagueApiError error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static LeagueApiResult<T> Ok(T value) => new LeagueApiResult<T>(true, value, LeagueApiError.None);
        public static LeagueApiResult<T> Fail(LeagueApiError error) => new LeagueApiResult<T>(false, default, error);
    }
}
