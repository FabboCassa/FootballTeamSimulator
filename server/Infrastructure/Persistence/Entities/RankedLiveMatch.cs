using Fts.Application.Leagues;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// A live-controlled session for one RANKED fixture (task 12.3) — the ladder's half of the 8.6 live stack.
/// Deliberately a table of its own rather than a nullable column on <c>live_matches</c>: the migration stays
/// purely additive, the cascade path is single (<c>ranked_live_matches → ranked_groups</c>) exactly like
/// <see cref="RankedFixture"/>, and the private-league sessions that were verified in 8.6 are not touched.
/// The mechanism itself is shared, not copied: the accumulated pause-point inputs are the same
/// <see cref="LiveChange"/> list, replayed through the same
/// <c>MatchResolver.ResolveLive</c> as the private leagues, on the same deterministic per-fixture seed the
/// headless matchday would have used. That is what makes the acceptance test possible: a fixture nobody
/// attends resolves on the tick to the identical scoreline and replay.
///
/// Two things differ from <see cref="LiveMatch"/>, and both come from the ladder being a real-time
/// competition rather than a lobby of friends:
/// <list type="number">
/// <item><b><see cref="KickoffUtc"/> is the SCHEDULE's instant</b>, copied from the fixture — 21:00 of the
/// world's own time zone — not "the moment both players happened to be present". Nobody's absence can move
/// a ranked kick-off, so the match starts at its appointment whether one, two or no coaches turn up.</item>
/// <item><b>A side may have no account behind it</b> (<see cref="HomeUserId"/>/<see cref="AwayUserId"/> are
/// nullable): most ladder seats are AI early on, and a coach must be able to attend his match anyway. The
/// AI side simply plays its stored orders — which is exactly what an absent human's side does too.</item>
/// </list>
/// </summary>
public sealed class RankedLiveMatch
{
    public Guid Id { get; set; }

    /// <summary>The group this session belongs to. Cascade: retiring a group removes its sessions.</summary>
    public Guid RankedGroupId { get; set; }
    public RankedGroup? RankedGroup { get; set; }

    /// <summary>The fixture being played live (one session per fixture — unique index). Plain denormalised
    /// column, no relationship, so the single cascade path back to <c>ranked_groups</c> is preserved.</summary>
    public Guid FixtureId { get; set; }

    /// <summary>The fixture's round (1-based matchday) — the calendar reads it to know whether a matchday
    /// is still being played before it resolves the round.</summary>
    public int Round { get; set; }

    public LiveMatchStatus Status { get; set; } = LiveMatchStatus.Pending;

    /// <summary>The deterministic per-fixture seed (season seed, round, home, away) — byte-for-byte the one
    /// <c>RankedSeasonService.ResolveRoundAsync</c> would use, which is what makes a live result and a
    /// headless one the same match.</summary>
    public long Seed { get; set; }

    public Guid HomeClubId { get; set; }
    public Guid AwayClubId { get; set; }

    /// <summary>The coaches holding each side, or null for an AI seat (which plays its stored orders).</summary>
    public Guid? HomeUserId { get; set; }
    public Guid? AwayUserId { get; set; }

    /// <summary>Presence flags — who is actually watching. They drive the "il tuo avversario è collegato"
    /// line; unlike 8.6 they do NOT decide when the match starts (the calendar does).</summary>
    public bool HomePresent { get; set; }
    public bool AwayPresent { get; set; }

    /// <summary>The fixture's SCHEDULED kickoff, copied at open. Clients sync playback to it, so two
    /// devices in different countries render the same minute at the same instant.</summary>
    public DateTime KickoffUtc { get; set; }

    /// <summary>The serialized accumulated <c>LiveChange</c> list (JSON array), "[]" while none.</summary>
    public string ChangesJson { get; set; } = "[]";

    /// <summary>The latest full serialized <c>Sim.Core.Match.MatchReport</c> after the most recent re-sim.
    /// With no changes accumulated this is byte-identical to what the headless matchday produces.</summary>
    public string? ReportJson { get; set; }

    /// <summary>The latest re-simmed score, denormalised so the state view avoids parsing the big report.</summary>
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
