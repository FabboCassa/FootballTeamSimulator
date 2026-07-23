namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A scheduled match in a ranked group's season (Phase 9.2). It mirrors <see cref="LeagueFixture"/> — the
/// double round-robin generated when the group's season starts (see
/// <see cref="Fts.Infrastructure.Leagues.FixtureScheduler"/>), resolved by the shared Sim.Core engine from
/// each seat's submitted lineup/plan (or a best-XI AI fallback for a vacant/unset seat), with the full
/// <c>MatchReport</c> stored verbatim for an identical replay.
///
/// The one thing a ranked fixture adds over a private-league one is <see cref="KickoffUtc"/>: the ranked
/// ladder runs on a REAL-TIME calendar (1 matchday/day at a fixed hour), so each round is stamped with the
/// wall-clock instant it becomes due. The recurring season job resolves a round once its kickoff has passed —
/// there is no "all ready" trigger here (missing a submission just plays your last lineup, then BestEleven).
/// </summary>
public sealed class RankedFixture
{
    public Guid Id { get; set; }

    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>1-based matchday (1..2*(N-1) for a double round-robin).</summary>
    public int Round { get; set; }

    /// <summary>Deterministic ordering of the matches within a round.</summary>
    public int MatchIndex { get; set; }

    /// <summary>The season "day" this round falls on (from the Sim.Core calendar), for display.</summary>
    public int Day { get; set; }

    /// <summary>Wall-clock instant this round becomes due. The season job resolves the round when
    /// <c>DateTime.UtcNow &gt;= KickoffUtc</c> — this is the real-time calendar (Phase 9.2).</summary>
    public DateTime KickoffUtc { get; set; }

    public Guid HomeClubId { get; set; }
    public Club? HomeClub { get; set; }

    public Guid AwayClubId { get; set; }
    public Club? AwayClub { get; set; }

    public bool IsPlayed { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }

    /// <summary>Deterministic seed the engine used, derived from (world seed, round, home, away).</summary>
    public long MatchSeed { get; set; }

    public DateTime? ResolvedUtc { get; set; }

    /// <summary>The full serialized <c>Sim.Core.Match.MatchReport</c> (incl. the position stream) once
    /// played, else null. Served verbatim so a replay renders identically for everyone.</summary>
    public string? ReplayJson { get; set; }
}
