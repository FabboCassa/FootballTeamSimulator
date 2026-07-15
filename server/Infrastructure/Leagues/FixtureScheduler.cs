using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;
using SimClub = Sim.Core.Domain.Club;
using SimLeague = Sim.Core.Domain.League;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Builds a private league's double round-robin schedule (Phase 8.3) by reusing the shared, tested
/// Sim.Core <see cref="FixtureGenerator"/> (circle method + seeded shuffle) so the server schedule uses
/// the exact same algorithm as the single-player game. The generator needs an even club count; for an
/// odd number of clubs a phantom "bye" id is added and the fixtures involving it are dropped (that club
/// simply rests that round). Deterministic: same clubs + same world seed ⇒ identical schedule.
/// </summary>
public static class FixtureScheduler
{
    /// <summary>A phantom club id that never collides with a real Sim.Core club id (which are small
    /// positive ints), used to pad an odd club count to even.</summary>
    private const int ByeClubId = int.MinValue;

    /// <summary>One scheduled meeting by club <c>ExternalId</c> (Sim.Core id).</summary>
    public readonly record struct ScheduledFixture(int Round, int MatchIndex, int Day, int HomeExternalId, int AwayExternalId);

    public static IReadOnlyList<ScheduledFixture> Build(IReadOnlyList<int> clubExternalIds, long worldSeed)
    {
        var ids = new List<int>(clubExternalIds);
        if (ids.Count % 2 != 0) ids.Add(ByeClubId); // pad odd → even; its matches are dropped below.

        var simLeague = new SimLeague { Name = string.Empty, Division = 1 };
        foreach (int id in ids)
            simLeague.Clubs.Add(new SimClub { Id = id });

        List<Fixture> generated = new FixtureGenerator().Generate(
            simLeague, new Pcg32(unchecked((ulong)worldSeed)));

        var result = new List<ScheduledFixture>(generated.Count);
        int lastRound = -1;
        int matchIndex = 0;
        foreach (Fixture f in generated) // already ordered round-by-round by the generator
        {
            if (f.Round != lastRound)
            {
                lastRound = f.Round;
                matchIndex = 0;
            }
            if (f.HomeClubId == ByeClubId || f.AwayClubId == ByeClubId)
                continue; // the resting club's "bye" — no match this round.

            result.Add(new ScheduledFixture(f.Round, matchIndex++, f.Day, f.HomeClubId, f.AwayClubId));
        }

        return result;
    }
}
