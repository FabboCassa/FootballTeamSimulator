using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One competition group (girone) inside a <see cref="RankedWorld"/> — either a permanent division at
/// some <see cref="Tier"/> or a transient placement season (Phase 9.1). Its <see cref="Capacity"/>
/// <see cref="RankedSeat"/> rows are created with the group and never added to, which is what structurally
/// guarantees "divisions stay at fixed size": a seat is either held by a coach or played by AI.
///
/// The group's clubs live in their own generated <see cref="World"/> (<see cref="WorldId"/>), materialised
/// LAZILY the first time the group gets an occupant — so opening a fresh ranked world is cheap (rows only)
/// and no players are generated for groups nobody has reached yet.
/// </summary>
public sealed class RankedGroup
{
    public Guid Id { get; set; }

    public Guid RankedWorldId { get; set; }
    public RankedWorld? RankedWorld { get; set; }

    public RankedGroupKind Kind { get; set; }

    /// <summary>1 = top division. Placement groups carry 0 (they are outside the pyramid).</summary>
    public int Tier { get; set; }

    /// <summary>0-based index within the tier (tier 2 group 0 = "2A", group 1 = "2B", …). Placement
    /// groups use a running counter within the world.</summary>
    public int GroupIndex { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Number of clubs — fixed for the group's whole life.</summary>
    public int Capacity { get; set; }

    /// <summary>The generated world holding this group's clubs/players. Null until the group is
    /// materialised (see the type remarks).</summary>
    public Guid? WorldId { get; set; }
    public World? World { get; set; }

    public RankedGroupStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>When this group's real-time season started (its fixtures were generated), or null while it
    /// has not begun yet (Phase 9.2). The whole matchday/market-window calendar is anchored to this instant;
    /// the presence of fixtures is what marks a season "started".</summary>
    public DateTime? SeasonStartedUtc { get; set; }

    /// <summary>Highest market-window index already opened + announced this season (Phase 9.2). Starts at -1;
    /// used so the season job announces each window (season start, midpoint) exactly once.</summary>
    public int LastMarketWindowOpened { get; set; } = -1;

    /// <summary>When this group's season finished (its coaches were rated + rewarded), or null while one is
    /// under way (Phase 9.3). The group then sits in the between-seasons BREAK — the final table stays
    /// readable — until <c>RankedOptions.SeasonBreakSeconds</c> elapse and the reset reopens it.</summary>
    public DateTime? SeasonEndedUtc { get; set; }

    /// <summary>How many seasons this group has run (1 = the first). Bumped by the seasonal reset (Phase 9.3)
    /// and mixed into the schedule/match seeds so consecutive seasons are not a replay of each other; also
    /// stamped onto the awards a season hands out.</summary>
    public int SeasonNumber { get; set; } = 1;

    public ICollection<RankedSeat> Seats { get; set; } = new List<RankedSeat>();
}
