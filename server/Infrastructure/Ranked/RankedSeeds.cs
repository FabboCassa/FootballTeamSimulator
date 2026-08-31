namespace Fts.Infrastructure.Ranked;

/// <summary>
/// The ladder's deterministic seeds, in one place (extracted in task 12.3). The season seed used to be a
/// private helper inside <see cref="RankedSeasonService"/>, which was fine while only the calendar tick
/// resolved a match. A LIVE session now resolves the same fixture from the same seed — that identity is the
/// acceptance test of 12.3, "a fixture nobody attends resolves on the tick with the identical scoreline and
/// replay" — so the two callers must derive it from one function rather than from two copies that can drift.
/// </summary>
public static class RankedSeeds
{
    /// <summary>The seed a given season of a group runs on: the generated world's seed mixed (splitmix-style)
    /// with the group's season counter, so every season after a reset gets its own schedule and match seeds
    /// while staying fully reproducible from the world's root seed.</summary>
    public static long Season(long worldSeed, int seasonNumber)
    {
        unchecked
        {
            ulong mixed = (ulong)worldSeed ^ ((ulong)(uint)Math.Max(1, seasonNumber) * 0x9E3779B97F4A7C15UL);
            mixed ^= mixed >> 29;
            mixed *= 0xBF58476D1CE4E5B9UL;
            mixed ^= mixed >> 32;
            return (long)mixed;
        }
    }
}
