using Fts.Application.Leagues;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A live-controlled session for one private-league fixture (Phase 8.6). Chosen store: Postgres/EF (like
/// the auctions, 8.5) — durable and ACID, so a session survives a server restart and the unit tests run on
/// SQLite with no live infrastructure. The session holds the fixture's deterministic <see cref="Seed"/>
/// and the accumulated pause-point inputs (<see cref="ChangesJson"/>): each change appends to the
/// authoritative <c>MatchPlan</c> and the server re-simulates the whole 90', storing the latest full
/// <c>MatchReport</c> in <see cref="ReportJson"/>. When the session is <c>Finished</c>, that report becomes
/// the fixture's official result at round resolution (fed into standings and the 8.4 weekly tick). The
/// fixture / club / user ids are plain denormalised columns (no FK relationship) to keep a single cascade
/// path (live_matches → private_leagues), portable across PostgreSQL and the SQLite test provider — the
/// same approach as <see cref="Bid"/>.
/// </summary>
public sealed class LiveMatch
{
    public Guid Id { get; set; }

    /// <summary>The lobby this session belongs to. Cascade: disbanding the league removes its sessions.</summary>
    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>The fixture being played live (one session per fixture — unique index). Plain column.</summary>
    public Guid FixtureId { get; set; }

    /// <summary>The fixture's round (1-based matchday), denormalised for the "current round" guard.</summary>
    public int Round { get; set; }

    public LiveMatchStatus Status { get; set; } = LiveMatchStatus.Pending;

    /// <summary>The deterministic per-fixture seed (world seed, round, home, away) — the same value the
    /// round resolution would use, so a live result is reproducible and consistent with the schedule.</summary>
    public long Seed { get; set; }

    public Guid HomeClubId { get; set; }
    public Guid AwayClubId { get; set; }

    /// <summary>The human members controlling each side (set at Open from the members' assigned clubs).</summary>
    public Guid? HomeUserId { get; set; }
    public Guid? AwayUserId { get; set; }

    /// <summary>Presence flags — drive the "waiting for opponent" UI. Both present ⇒ the match goes Live.</summary>
    public bool HomePresent { get; set; }
    public bool AwayPresent { get; set; }

    /// <summary>Stamped when both members are present and the match goes Live (clients sync playback to it).</summary>
    public DateTime? KickoffUtc { get; set; }

    /// <summary>The serialized accumulated <c>LiveChange</c> list (JSON array), "[]" while none.</summary>
    public string ChangesJson { get; set; } = "[]";

    /// <summary>The latest full serialized <c>Sim.Core.Match.MatchReport</c> after the most recent re-sim.</summary>
    public string? ReportJson { get; set; }

    /// <summary>The latest re-simmed score, denormalised so the state view avoids parsing the big report.</summary>
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
