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

    /// <summary>The caller's ranked season: state + schedule + standings (InSeason false before kickoff).</summary>
    [Serializable]
    public sealed class RankedSeasonDto
    {
        public bool inSeason;
        public RankedSeasonStateDto state;
        public List<RankedFixtureDto> fixtures = new List<RankedFixtureDto>();
        public List<RankedStandingDto> standings = new List<RankedStandingDto>();
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
