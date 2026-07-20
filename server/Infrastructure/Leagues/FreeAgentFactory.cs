using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Random;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// Generates the pool of unattached free agents a private-league world starts with (Phase 8.5). These
/// are the lots the season-start (and mid-season) auctions run on: with the draft (8.2) equalising every
/// club's squad, the market's spice comes from a shared pool of buyable players. The quality curve is
/// realistic — a couple of phenoms, a handful of very good players, and a long tail of medium/low ones —
/// so the auctions have a few genuinely contested stars and plenty of squad filler.
///
/// Deterministic: everything flows through a <see cref="Pcg32"/> derived from the world seed on a
/// distinct sub-stream, so it does not perturb the club generation (existing worlds/tests are unaffected)
/// and the same seed rebuilds the same pool. Composes the shared Sim.Core <see cref="PlayerGenerator"/> —
/// the free agents are ordinary players, just clubless.
/// </summary>
public static class FreeAgentFactory
{
    /// <summary>Free-agent Sim.Core ids start here so they never collide with a club player's id
    /// (club players are numbered from <see cref="LeagueGenerationOptions.FirstPlayerId"/> = 1 upward).</summary>
    public const int ExternalIdBase = 900_000;

    /// <summary>A distinct RNG sub-stream mix so the free-agent draws don't shift the club generation.</summary>
    private const ulong SeedMix = 0x9E3779B97F4A7C15UL;

    /// <summary>Quality tiers: (how many, target-overall range). A world tops out around the mid-70s, so a
    /// target in the high 70s / low 80s is a genuine phenom. Counts sum to <c>PoolSize</c> (~52).</summary>
    private static readonly (int Count, int MinTarget, int MaxTarget)[] Tiers =
    {
        (2,  78, 84),   // phenoms — one or two, the marquee lots
        (10, 70, 77),   // very good — the contested first-team upgrades
        (18, 60, 69),   // solid squad players
        (22, 46, 59),   // depth / rotation
    };

    /// <summary>The realistic role mix (mirrors the 22-man squad template), cycled across the pool so every
    /// position is represented in proportion.</summary>
    private static readonly PositionRole[] RoleCycle =
    {
        PositionRole.Goalkeeper, PositionRole.Goalkeeper, PositionRole.Goalkeeper,
        PositionRole.CentreBack, PositionRole.CentreBack, PositionRole.CentreBack, PositionRole.CentreBack,
        PositionRole.FullBack, PositionRole.FullBack, PositionRole.FullBack,
        PositionRole.DefensiveMidfielder, PositionRole.DefensiveMidfielder,
        PositionRole.CentralMidfielder, PositionRole.CentralMidfielder, PositionRole.CentralMidfielder,
        PositionRole.AttackingMidfielder, PositionRole.AttackingMidfielder,
        PositionRole.Winger, PositionRole.Winger, PositionRole.Winger,
        PositionRole.Striker, PositionRole.Striker,
    };

    /// <summary>Generates the free-agent pool for a world. Deterministic per <paramref name="seed"/>.</summary>
    public static List<Player> Generate(long seed, BalanceConfig cfg)
    {
        var rng = new Pcg32(unchecked((ulong)seed ^ SeedMix));
        var gen = new PlayerGenerator(cfg.Generation);

        var pool = new List<Player>();
        int index = 0;
        foreach (var (count, min, max) in Tiers)
        {
            for (int i = 0; i < count; i++)
            {
                PositionRole role = RoleCycle[index % RoleCycle.Length];
                int target = rng.NextInt(min, max + 1);
                int id = ExternalIdBase + index;
                pool.Add(gen.Generate(id, role, target, rng));
                index++;
            }
        }

        return pool;
    }
}
