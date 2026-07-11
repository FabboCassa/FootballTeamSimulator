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
        WrongPhase,    // 409 wrong_phase (8.2 — draft already started / not running)
        NotYourTurn,   // 409 not_your_turn (8.2)
        ClubUnavailable, // 409 club_unavailable (8.2 — unknown or taken club)
        TooFewMembers, // 400 too_few_members (8.2)
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
