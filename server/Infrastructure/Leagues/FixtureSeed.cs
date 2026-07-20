namespace Fts.Infrastructure.Leagues;

/// <summary>
/// The deterministic per-fixture match seed (Phase 8.3/8.6). Extracted so the scheduled round resolution
/// (<see cref="LeagueSeasonService"/>) and a live session (<see cref="LiveMatchService"/>) derive the
/// SAME seed for a fixture — a live-played result is then byte-for-byte what the schedule would have
/// produced for the same inputs, and a re-sim is reproducible. FNV-style mix of (world seed, round, home,
/// away external ids).
/// </summary>
public static class FixtureSeed
{
    public static ulong For(long worldSeed, int round, int homeExternalId, int awayExternalId)
    {
        unchecked
        {
            const ulong prime = 0x100000001B3UL;
            ulong h = (ulong)worldSeed;
            h = (h ^ (uint)round) * prime;
            h = (h ^ (uint)homeExternalId) * prime;
            h = (h ^ (uint)awayExternalId) * prime;
            return h;
        }
    }
}
