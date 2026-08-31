using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One self-contained public-ranked pyramid (Phase 9.1): a fixed set of <see cref="RankedGroup"/>s
/// (tier 1 → tier N) whose seats are created up front. Distinct from the generated <see cref="World"/>
/// entity — a ranked world is the COMPETITION structure, and each of its groups owns its own generated
/// <see cref="World"/> (its 8 clubs and their players).
///
/// When every placeable seat is taken the world flips to <see cref="RankedWorldStatus.Full"/> and the
/// server opens another one, so enrolment never blocks (the 9.1 spillover ✅).
/// </summary>
public sealed class RankedWorld
{
    public Guid Id { get; set; }

    /// <summary>Display label, e.g. "Mondo 1".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Root seed. Every group's generated world derives its own seed from this + the group's
    /// tier/index, so a ranked world's squads are reproducible from one number.</summary>
    public long Seed { get; set; }

    /// <summary>Current season number (1 for a fresh world). Seasonal reset is 9.3.</summary>
    public int SeasonNumber { get; set; } = 1;

    public RankedWorldStatus Status { get; set; }

    /// <summary>
    /// The IANA time zone this pyramid lives in (task 12.3), e.g. <c>Europe/Rome</c>. Every matchday in
    /// every one of its groups kicks off at the configured local hour of THIS zone, and each client renders
    /// that instant in the device's own local time — so a coach in London reads 20:00 for the Italian
    /// world's 21:00. It is a ZONE and not an offset on purpose: only a zone keeps a season kicking off at
    /// 21:00 across a DST change. Seeded from <c>RankedOptions.WorldTimeZone</c> when the world is opened;
    /// a world created before 12.3 carries an empty string and falls back to the pre-12.3 relative
    /// calendar, so an in-flight season is never rescheduled under its coaches' feet.
    /// </summary>
    public string TimeZoneId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public ICollection<RankedGroup> Groups { get; set; } = new List<RankedGroup>();
}
