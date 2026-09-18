using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Assigns a club's persistent <see cref="Domain.Club.Stature"/> (task: club stature, R4):
    /// 0-100, correlated with the club's rank within its own league (its generated strength
    /// order) but not equal to it — a second, independent draw on top of the same rank gives two
    /// equally-strong clubs a real chance of ending up with different stature (and so different
    /// revenue, <see cref="Market.FinanceModel.StatureMultiplierPermille"/>).
    ///
    /// PURE and deterministic given its RNG: callers MUST pass a stream derived from (but never
    /// consuming) the caller's own generation stream — see <see cref="WorldGenerator"/> and
    /// <see cref="LeagueGenerator"/>, both of which read <c>IRandomSource.GetState()</c> (a pure
    /// getter) to seed a dedicated sub-stream, so adding stature never perturbs the existing
    /// squad/player draws and every pre-existing golden/determinism test stays byte-identical.
    /// </summary>
    public static class StatureModel
    {
        /// <summary>
        /// <paramref name="clubIndex"/> is 0 for the strongest club in the league, ascending to
        /// <paramref name="clubCount"/> - 1 for the weakest (matching how both generators lay out
        /// their strength hierarchy) — so the rank-derived baseline runs 100 down to 0.
        /// </summary>
        public static int Assign(int clubIndex, int clubCount, IRandomSource rng, GenerationBalance cfg)
        {
            int baseline = clubCount <= 1 ? 50 : (clubCount - 1 - clubIndex) * 100 / (clubCount - 1);

            int noise = cfg.StatureNoisePoints > 0
                ? rng.NextInt(-cfg.StatureNoisePoints, cfg.StatureNoisePoints + 1)
                : 0;

            int stature = baseline + noise;
            if (stature < 0) stature = 0;
            if (stature > 100) stature = 100;
            return stature;
        }
    }
}
