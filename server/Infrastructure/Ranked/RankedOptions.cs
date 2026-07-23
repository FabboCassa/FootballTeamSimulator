namespace Fts.Infrastructure.Ranked;

/// <summary>
/// Shape of a public-ranked world (Phase 9.1), bound by hand from the "Ranked" configuration section
/// (like <c>JwtOptions</c>/<c>FcmOptions</c> — this is a plain class library, no config-binder dependency).
///
/// The defaults are the agreed pyramid: <b>3 tiers, 8 clubs per group</b> — tier 1 one group, tier 2 two
/// groups, tier 3 four groups = 56 seats per world, of which 48 are placeable (tier 1 is reached by
/// promotion only). Small groups keep a season short (14 matchdays), which suits the ≤10-minute daily
/// loop the ladder is designed around.
///
/// Tests shrink these so spillover to a second world can be exercised without hundreds of accounts.
/// </summary>
public sealed class RankedOptions
{
    public const string SectionName = "Ranked";

    /// <summary>Clubs per group — also the number of clubs generated for the group's world.</summary>
    public int GroupSize { get; set; } = 8;

    /// <summary>Coaches per placement season (kept equal to <see cref="GroupSize"/> by default so a
    /// placement cohort maps 1:1 onto a group's worth of seats).</summary>
    public int PlacementGroupSize { get; set; } = 8;

    /// <summary>Groups in the top tier.</summary>
    public int Tier1Groups { get; set; } = 1;

    /// <summary>Groups in the second tier.</summary>
    public int Tier2Groups { get; set; } = 2;

    /// <summary>Groups in the third tier.</summary>
    public int Tier3Groups { get; set; } = 4;

    /// <summary>How many placement finishers earn the higher placeable tier; everyone below drops to the
    /// lowest tier. Tier 1 is never handed out by placement — it is earned by promotion.</summary>
    public int PlacementTopPositionsToUpperTier { get; set; } = 2;

    /// <summary>Ladder rating a freshly placed coach starts from.</summary>
    public int StartingRating { get; set; } = 1000;

    /// <summary>Rating spread per placement position (1st in a group of 8 starts this × 7 above last).</summary>
    public int RatingPerPlacementPosition { get; set; } = 15;

    /// <summary>Group counts per tier, index 0 = tier 1. Only non-empty tiers are kept.</summary>
    public IReadOnlyList<int> GroupsPerTier()
    {
        var tiers = new List<int>();
        if (Tier1Groups > 0) tiers.Add(Tier1Groups);
        if (Tier2Groups > 0) tiers.Add(Tier2Groups);
        if (Tier3Groups > 0) tiers.Add(Tier3Groups);
        if (tiers.Count == 0) tiers.Add(1);
        return tiers;
    }

    /// <summary>Number of tiers in the pyramid.</summary>
    public int TierCount() => GroupsPerTier().Count;

    /// <summary>The best tier a placement season can hand out: the second tier when the pyramid has one
    /// (tier 1 stays promotion-only), otherwise the only tier there is.</summary>
    public int UpperPlacementTier() => TierCount() > 1 ? 2 : 1;

    /// <summary>The tier everyone who did not make <see cref="UpperPlacementTier"/> is sorted into.</summary>
    public int LowerPlacementTier() => TierCount();
}
