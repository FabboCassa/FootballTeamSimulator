using Sim.Core.Config;
using Sim.Core.Random;

namespace Sim.Core.Generation
{
    /// <summary>
    /// Assigns a club's persistent <see cref="Domain.Club.Stature"/> (task: club stature, R4):
    /// 0-100, correlated with the club's rank within its own league (its generated strength
    /// order) but not equal to it. Two forces are balanced here, deliberately NOT via one small
    /// global noise term (that made stature a near-rank-preserving function of strength — 0 rank
    /// inversions across 1024 sampled club slots at the old StatureNoisePoints=3):
    ///
    ///  - the league's strongest club (index 0) is ANCHORED close to the stature ceiling (only
    ///    ever nudged down, never up) so the R4 richest-club revenue band keeps landing — the
    ///    revenue curve (<see cref="Market.FinanceModel.StatureMultiplierPermille"/>) is convex
    ///    and only pulls away from the floor in the last few stature points, so *someone* in the
    ///    league needs to reliably sit near 100;
    ///  - every other club draws large, independent noise on top of its rank-derived baseline —
    ///    large enough (relative to the ~6-7 point gap between adjacent ranks in a 16-20 club
    ///    league) that adjacent ranks genuinely cross, so stature is correlated with strength on
    ///    average but is a real, decorrelated second draw for any two individual clubs (and so
    ///    different revenue, same <see cref="Market.FinanceModel.StatureMultiplierPermille"/>).
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
            if (clubIndex == 0)
            {
                int topNoise = cfg.StatureTopAnchorNoisePoints > 0
                    ? rng.NextInt(-cfg.StatureTopAnchorNoisePoints, 1) // [-N, 0]: only ever pulls down from 100
                    : 0;
                return Clamp(100 + topNoise);
            }

            int baseline = clubCount <= 1 ? 50 : (clubCount - 1 - clubIndex) * 100 / (clubCount - 1);

            int noise = cfg.StatureNoisePoints > 0
                ? rng.NextInt(-cfg.StatureNoisePoints, cfg.StatureNoisePoints + 1)
                : 0;

            return Clamp(baseline + noise);
        }

        private static int Clamp(int stature) => stature < 0 ? 0 : stature > 100 ? 100 : stature;
    }
}
