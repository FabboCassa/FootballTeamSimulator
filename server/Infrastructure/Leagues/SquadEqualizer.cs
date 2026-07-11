using Fts.Infrastructure.Persistence.Entities;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Equalises the squads of every club in a world so the online season starts fair (Phase 8.2 —
/// "rose di pari forza"). Pure, deterministic, NO RNG: for each position role it ranks every player of
/// that role across the whole world by overall (descending, id tie-break) and hands them out to the clubs
/// in a <b>serpentine</b> order (0,1,…,N-1,N-1,…,1,0,0,1,…), so each club receives an equal-quality slice
/// of every role. Because the generator gives every club the identical <see cref="Sim.Core.Generation.SquadTemplate"/>
/// composition, each role has exactly (clubs × count) players ⇒ every club ends with the same composition
/// and a near-identical average overall, with the same unique player ids (just re-parented — no player is
/// added, removed or duplicated). <see cref="Club.Strength"/> is recomputed from the new squads.
/// </summary>
public static class SquadEqualizer
{
    /// <summary>
    /// Re-parents every player to balance strength across the clubs and recomputes each club's
    /// <see cref="Club.Strength"/>. Mutates <see cref="Player.ClubId"/> and <see cref="Club.Strength"/> in
    /// place (the caller persists via SaveChanges). Deterministic for a given set of clubs/players.
    /// </summary>
    public static void Equalize(IReadOnlyList<Club> clubs, IReadOnlyList<Player> players)
    {
        if (clubs.Count == 0) return;

        // Stable club order so the assignment is deterministic and reproducible.
        var orderedClubs = clubs.OrderBy(c => c.ExternalId).ToList();
        int n = orderedClubs.Count;

        // Reset each club's roster; we rebuild them from the redistribution.
        foreach (var club in orderedClubs) club.Players.Clear();

        foreach (var roleGroup in players.GroupBy(p => p.Role))
        {
            // Rank this role's players best-first; ExternalId breaks ties so it never depends on load order.
            var ranked = roleGroup
                .OrderByDescending(p => p.Overall)
                .ThenBy(p => p.ExternalId)
                .ToList();

            for (int rank = 0; rank < ranked.Count; rank++)
            {
                var club = orderedClubs[SerpentineIndex(rank, n)];
                var player = ranked[rank];
                player.ClubId = club.Id;
                player.Club = club;
                club.Players.Add(player);
            }
        }

        foreach (var club in orderedClubs)
            club.Strength = AverageOverall(club.Players);
    }

    /// <summary>Maps a global rank (0-based) to a club index using a serpentine (boustrophedon) walk over
    /// <paramref name="clubCount"/> clubs, so consecutive "rows" alternate direction and the summed ranks
    /// per club stay as even as possible: row 0 → 0,1,…,N-1; row 1 → N-1,…,1,0; row 2 → 0,1,…; etc.</summary>
    public static int SerpentineIndex(int rank, int clubCount)
    {
        int row = rank / clubCount;
        int pos = rank % clubCount;
        return (row % 2 == 0) ? pos : clubCount - 1 - pos;
    }

    private static int AverageOverall(ICollection<Player> squad)
    {
        if (squad.Count == 0) return 0;
        long sum = 0;
        foreach (var p in squad) sum += p.Overall;
        return (int)(sum / squad.Count);
    }
}
