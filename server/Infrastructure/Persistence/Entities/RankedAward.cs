using Fts.Application.Ranked;

namespace Fts.Infrastructure.Persistence.Entities;

/// <summary>
/// One line of a coach's permanent palmarès (Phase 9.3): a title, a promotion, a relegation or simply a
/// season completed. Append-only — nothing ever updates or deletes an award row.
///
/// Deliberately FK-free (plain denormalised ids + the names captured at the time): a coach's history must
/// OUTLIVE the world, group and clubs it happened in, so a world teardown or a seasonal reset can never
/// erase what they won. Same "plain column" convention as <c>bids</c> / <c>ranked_offers</c>, taken one
/// step further because here the independence is the point.
/// </summary>
public sealed class RankedAward
{
    public Guid Id { get; set; }

    /// <summary>The account that earned it.</summary>
    public Guid UserId { get; set; }

    public RankedAwardKind Kind { get; set; }

    /// <summary>Where it happened (ids kept for diagnostics; the display names are captured below so the
    /// award still reads correctly after the group has been reset or the world retired).</summary>
    public Guid RankedWorldId { get; set; }
    public Guid RankedGroupId { get; set; }

    public string WorldName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Pyramid tier the award was earned in (0 for a placement season).</summary>
    public int Tier { get; set; }

    /// <summary>Final position in the group (1-based), or 0 when the award is not position-based.</summary>
    public int Position { get; set; }

    /// <summary>The group's season counter at the time.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>The coach's rating right after the season-end adjustment, and how much it moved.</summary>
    public int RatingAfter { get; set; }
    public int RatingDelta { get; set; }

    public DateTime AwardedUtc { get; set; }
}
