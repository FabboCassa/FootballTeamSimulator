namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A scheduled match in a private league's season (Phase 8.3). The double round-robin is generated
/// when the draft completes and the league goes <c>Active</c> (see <see cref="Fts.Infrastructure.Leagues.FixtureScheduler"/>);
/// each fixture is resolved by the all-ready round advance, which runs the shared Sim.Core engine
/// from the two clubs' submitted lineups/plans (or an AI fallback) with a deterministic per-fixture
/// seed. The full <c>MatchReport</c> (score + events + replayable position stream) is stored verbatim
/// in <see cref="ReplayJson"/> and served to both members so a replay renders identically for everyone
/// (the 8.3 replay decision). The light score columns support the schedule/standings views without
/// loading the big replay blob.
/// </summary>
public sealed class LeagueFixture
{
    public Guid Id { get; set; }

    public Guid PrivateLeagueId { get; set; }
    public PrivateLeague? PrivateLeague { get; set; }

    /// <summary>1-based matchday (1..2*(N-1) for a double round-robin).</summary>
    public int Round { get; set; }

    /// <summary>Deterministic ordering of the matches within a round.</summary>
    public int MatchIndex { get; set; }

    /// <summary>The season "day" this round falls on (from the Sim.Core calendar), for display.</summary>
    public int Day { get; set; }

    public Guid HomeClubId { get; set; }
    public Club? HomeClub { get; set; }

    public Guid AwayClubId { get; set; }
    public Club? AwayClub { get; set; }

    public bool IsPlayed { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }

    /// <summary>Deterministic seed the engine used, derived from (world seed, round, home, away).
    /// Stored so a resolution is reproducible and auditable.</summary>
    public long MatchSeed { get; set; }

    public DateTime? ResolvedUtc { get; set; }

    /// <summary>The full serialized <c>Sim.Core.Match.MatchReport</c> (incl. the position stream)
    /// once played, else null. Served verbatim to both members for an identical replay.</summary>
    public string? ReplayJson { get; set; }
}
